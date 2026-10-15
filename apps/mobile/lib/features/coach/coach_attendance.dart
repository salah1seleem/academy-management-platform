import 'package:academy_mobile/core/theme/ui_text.dart';
import 'package:flutter/material.dart';

import '../../core/api/api_client.dart';
import '../../core/auth/auth_controller.dart';
import '../../core/theme/appearance.dart';
import '../../core/widgets/app_widgets.dart';
import '../guardian/guardian_ui.dart';
import 'coach_evaluation.dart';
import 'coach_player.dart';
import '../shared/past_attendance.dart';
import '../shared/attendance_draft.dart';

class CoachAttendance extends StatefulWidget {
  final AuthController auth;
  final String sessionId;
  final String? title;
  const CoachAttendance({
    super.key,
    required this.auth,
    required this.sessionId,
    this.title,
  });
  @override
  State<CoachAttendance> createState() => _CoachAttendanceState();
}

class _CoachAttendanceState extends State<CoachAttendance> {
  String synchronizedTime(Object? value) {
    final date = DateTime.tryParse(textOf(value))?.toLocal();
    if (date == null) return '—';
    final hour = date.hour.toString().padLeft(2, '0');
    final minute = date.minute.toString().padLeft(2, '0');
    return '${date.day}/${date.month}/${date.year} · $hour:$minute (${localized(context, 'بتوقيت الجهاز', 'device time')})';
  }

  Json? data;
  final statuses = <String, String>{};
  String? error, message;
  bool busy = false;
  bool blocked = false;
  AttendanceDraft? draft;
  final conflicts = <String, Json?>{};
  final search = TextEditingController();
  String? identity;
  String? get currentIdentity => coachContext(widget.auth);

  @override
  void initState() {
    super.initState();
    identity = currentIdentity;
    widget.auth.addListener(authChanged);
    load();
  }

  void authChanged() {
    if (identity == currentIdentity || !mounted) return;
    setState(() {
      identity = currentIdentity;
      draft = null;
      conflicts.clear();
      statuses.clear();
      data = null;
      search.clear();
      blocked = true;
      error = 'تغير حساب الدخول؛ أعد فتح كشف الحضور.';
    });
  }

  @override
  void dispose() {
    widget.auth.removeListener(authChanged);
    draft = null;
    conflicts.clear();
    search.dispose();
    super.dispose();
  }

  Future<Json> read() async => obj(
    await widget.auth.request(
      'GET',
      '/api/v1/attendance/sessions/${widget.sessionId}/players',
    ),
  );

  Future<bool> load() async {
    try {
      final next = await read();
      if (!mounted || blocked) return false;
      statuses.clear();
      for (final player in rows(next['players'])) {
        statuses[textOf(player['sportEnrollmentId'])] =
            player['attendanceStatus'] as String? ?? 'NotRecorded';
      }
      if (mounted) {
        setState(() {
          data = next;
          error = null;
        });
      }
      return true;
    } on ApiFailure catch (e) {
      if (mounted) {
        setState(() {
          error = e.message;
          if ([401, 403, 404].contains(e.status)) {
            blocked = true;
            data = null;
            draft = null;
            statuses.clear();
            conflicts.clear();
          }
        });
      }
      return false;
    }
  }

  Future<void> save() async {
    if (data?['canManageAttendance'] != true ||
        busy ||
        blocked ||
        conflicts.isNotEmpty) {
      return;
    }
    final retry = draft != null;
    final current = draft ?? AttendanceDraft(data!, statuses);
    setState(() {
      busy = true;
      error = null;
      message = null;
    });
    try {
      current.validate();
      if (retry) {
        final latest = await read();
        if (!mounted || blocked) return;
        current.validate(latest: latest);
        final changed = current.conflicts(latest);
        if (changed.isNotEmpty) {
          setState(() {
            conflicts.addAll(changed);
            error = 'تغير الكشف على جهاز آخر؛ راجع التعارض قبل إعادة الإرسال.';
          });
          return;
        }
        if (current.confirmed(latest)) {
          if (await load() && mounted) {
            setState(() {
              draft = null;
              message = 'تأكدت المزامنة من الخادم دون إعادة الإرسال.';
            });
          }
          return;
        }
      }
      if (current.changes.isEmpty) {
        if (await load() && mounted) {
          setState(() {
            draft = null;
            message = 'لا توجد تغييرات جديدة للحفظ.';
          });
        }
        return;
      }
      await widget.auth.request(
        'PUT',
        '/api/v1/attendance/sessions/${widget.sessionId}/players',
        body: current.body,
      );
      final synchronized = await load();
      if (mounted && synchronized) {
        setState(() {
          draft = null;
          message = 'تم حفظ الحضور ويمكن تصحيحه بأمان.';
        });
      } else if (mounted && !blocked) {
        setState(() {
          blocked = current.lease == null || current.scope == null;
          draft = blocked ? null : current;
          if (blocked) {
            data = null;
            statuses.clear();
            error = 'افتح كشفًا جديدًا بعد تسجيل الدخول لتأكيد الحفظ.';
          }
        });
      }
    } on ApiFailure catch (e) {
      if (mounted && !blocked) {
        setState(() {
          blocked =
              current.lease == null ||
              current.scope == null ||
              [401, 403, 404, 410].contains(e.status);
          draft = blocked ? null : current;
          if (blocked) {
            data = null;
            statuses.clear();
            conflicts.clear();
          }
          error = e.message;
        });
      }
    } finally {
      if (mounted) setState(() => busy = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    final players = data == null ? <Json>[] : rows(data!['players']);
    final filtered = players
        .where((p) => playerMatches(p, search.text))
        .toList();
    final session = data == null ? <String, dynamic>{} : obj(data!['session']);
    final pending = players
        .where(
          (p) =>
              statuses[textOf(p['sportEnrollmentId'])] !=
              (p['attendanceStatus'] ?? 'NotRecorded'),
        )
        .length;
    return AppScaffold(
      title: widget.title ?? 'تسجيل الحضور',
      bottomBar: data?['canManageAttendance'] != true
          ? null
          : Padding(
              padding: const EdgeInsets.symmetric(horizontal: 18, vertical: 10),
              child: FilledButton.icon(
                icon: busy
                    ? const SizedBox.square(
                        dimension: 18,
                        child: CircularProgressIndicator(strokeWidth: 2),
                      )
                    : const Icon(Icons.cloud_done_outlined),
                onPressed: busy || conflicts.isNotEmpty ? null : save,
                label: UiText(
                  busy
                      ? 'جارٍ الحفظ…'
                      : draft != null
                      ? 'مراجعة وإعادة المزامنة'
                      : 'حفظ الحضور',
                ),
              ),
            ),
      child: data == null
          ? Center(
              child: error == null
                  ? const CircularProgressIndicator()
                  : Column(
                      mainAxisSize: MainAxisSize.min,
                      children: [
                        ErrorNotice(error!),
                        if (!blocked)
                          OutlinedButton.icon(
                            onPressed: load,
                            icon: const Icon(Icons.refresh),
                            label: const UiText('إعادة المحاولة'),
                          ),
                      ],
                    ),
            )
          : ListView(
              padding: const EdgeInsets.all(18),
              children: [
                AppCard(
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      UiText(
                        textOf(session['group']),
                        style: Theme.of(context).textTheme.titleLarge,
                      ),
                      const SizedBox(height: 8),
                      UiText(day(session['sessionDate'])),
                      UiText(
                        '${textOf(session['startTime']).split(':').take(2).join(':')} — ${textOf(session['endTime'] ?? '—').split(':').take(2).join(':')}',
                        textDirection: TextDirection.ltr,
                        style: const TextStyle(
                          fontSize: 22,
                          fontWeight: FontWeight.w700,
                        ),
                      ),
                      if (session['branch'] != null || session['sport'] != null)
                        UiText(
                          [
                            session['branch'],
                            session['sport'],
                          ].whereType<String>().join(' · '),
                        ),
                      if (session['status'] != null)
                        UiText(
                          session['status'] == 'Held'
                              ? localized(
                                  context,
                                  'نُفذت الحصة',
                                  'Session held',
                                )
                              : label(session['status']),
                        ),
                    ],
                  ),
                ),
                Wrap(
                  spacing: 8,
                  runSpacing: 8,
                  children: [
                    for (final state in const [
                      'Present',
                      'Absent',
                      'NotRecorded',
                      'Late',
                      'Excused',
                    ])
                      Chip(
                        key: ValueKey('attendance-count-$state'),
                        avatar: Icon(attendanceIcon(state), size: 18),
                        label: UiText(
                          '${label(state)} · ${players.where((p) => statuses[textOf(p['sportEnrollmentId'])] == state).length}',
                        ),
                      ),
                  ],
                ),
                Semantics(
                  liveRegion: true,
                  child: UiText(
                    localized(
                      context,
                      pending == 0
                          ? 'الحالات المعروضة مؤكدة من الخادم'
                          : '$pending تغييرات لم تُحفظ بعد',
                      pending == 0
                          ? 'Displayed states are confirmed by the server'
                          : '$pending unsaved changes',
                    ),
                  ),
                ),
                if (data!['lastSynchronizedAtUtc'] != null)
                  UiText(
                    '${localized(context, 'آخر مزامنة مؤكدة', 'Last confirmed synchronization')}: ${synchronizedTime(data!['lastSynchronizedAtUtc'])}',
                  ),
                if (message != null)
                  UiText(message!, style: const TextStyle(color: Colors.green)),
                if (error != null) ErrorNotice(error!),
                if (draft != null)
                  AppCard(
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        const UiText(
                          'بانتظار المزامنة',
                          style: TextStyle(fontWeight: FontWeight.bold),
                        ),
                        const UiText(
                          'المسودة في هذه الصفحة فقط لمدة 15 دقيقة؛ لا تغادر قبل المراجعة.',
                        ),
                        TextButton(
                          onPressed: busy
                              ? null
                              : () async {
                                  setState(() {
                                    draft = null;
                                    conflicts.clear();
                                  });
                                  await load();
                                },
                          child: const UiText('تجاهل المسودة وفتح كشف محدث'),
                        ),
                      ],
                    ),
                  ),
                for (final entry in conflicts.entries)
                  AppCard(
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        UiText(
                          'تعارض حضور ${entry.value?['arabicName'] ?? 'عضو لم يعد في الكشف'}',
                        ),
                        UiText(
                          'اختيارك: ${label(draft!.changes[entry.key]?['status'])} · الخادم: ${entry.value == null ? 'لم يعد ضمن الحصة' : label(entry.value!['attendanceStatus'])}',
                        ),
                        TextButton(
                          onPressed: () => setState(() {
                            draft!.resolve(entry.key, entry.value, false);
                            conflicts.remove(entry.key);
                          }),
                          child: const UiText('اعتماد حالة الخادم'),
                        ),
                        if (entry.value != null)
                          TextButton(
                            onPressed: () => setState(() {
                              draft!.resolve(entry.key, entry.value, true);
                              conflicts.remove(entry.key);
                            }),
                            child: const UiText(
                              'الاحتفاظ باختياري بعد المراجعة',
                            ),
                          ),
                      ],
                    ),
                  ),
                if (data!['canManageAttendance'] != true)
                  const UiText(
                    'الحضور للعرض فقط؛ يتولى الإداري المصرح له التسجيل.',
                  ),
                const SizedBox(height: 12),
                TextField(
                  controller: search,
                  onChanged: (_) => setState(() {}),
                  decoration: InputDecoration(
                    labelText: localized(
                      context,
                      'بحث بالاسم أو كود اللاعب',
                      'Search name or player code',
                    ),
                    prefixIcon: const Icon(Icons.search),
                    suffixIcon: search.text.isEmpty
                        ? null
                        : IconButton(
                            tooltip: localized(
                              context,
                              'مسح البحث',
                              'Clear search',
                            ),
                            icon: const Icon(Icons.close),
                            onPressed: () => setState(search.clear),
                          ),
                  ),
                ),
                const SizedBox(height: 8),
                UiText(
                  localized(
                    context,
                    '${filtered.length} من ${players.length} لاعب',
                    '${filtered.length} of ${players.length} players',
                  ),
                ),
                if (filtered.isEmpty)
                  UiText(
                    localized(
                      context,
                      players.isEmpty
                          ? 'لا يوجد لاعبون في هذه الحصة.'
                          : 'لا توجد نتائج لهذا البحث.',
                      players.isEmpty
                          ? 'No players in this session.'
                          : 'No matching players.',
                    ),
                  ),
                for (final player in filtered)
                  Card(
                    child: Padding(
                      padding: const EdgeInsets.all(12),
                      child: Column(
                        crossAxisAlignment: CrossAxisAlignment.start,
                        children: [
                          Row(
                            crossAxisAlignment: CrossAxisAlignment.start,
                            children: [
                              CoachPlayerAvatar(
                                auth: widget.auth,
                                reference: player['photoReference'],
                              ),
                              const SizedBox(width: 12),
                              Expanded(
                                child: Column(
                                  crossAxisAlignment: CrossAxisAlignment.start,
                                  children: [
                                    UiText(
                                      textOf(player['arabicName']),
                                      style: const TextStyle(
                                        fontWeight: FontWeight.bold,
                                        fontSize: 16,
                                      ),
                                    ),
                                    UiText(
                                      textOf(player['playerCode']),
                                      style: const TextStyle(fontSize: 14),
                                    ),
                                  ],
                                ),
                              ),
                            ],
                          ),
                          TextButton.icon(
                            icon: const Icon(Icons.person_outline),
                            label: const UiText('عرض الملف'),
                            onPressed: busy || draft != null
                                ? null
                                : () => openPage(
                                    context,
                                    CoachPlayerPage(
                                      auth: widget.auth,
                                      enrollmentId: textOf(
                                        player['sportEnrollmentId'],
                                      ),
                                      sessionId: widget.sessionId,
                                    ),
                                  ),
                          ),
                          if (data!['canEvaluate'] == true)
                            OutlinedButton.icon(
                              icon: const Icon(Icons.assessment_outlined),
                              label: const UiText('تقييم اللاعب'),
                              onPressed: busy || draft != null
                                  ? null
                                  : () => Navigator.of(context).push(
                                      MaterialPageRoute<void>(
                                        builder: (_) => CoachEvaluation(
                                          auth: widget.auth,
                                          enrollment: {
                                            'id': player['sportEnrollmentId'],
                                            'player': player['arabicName'],
                                            'group': obj(
                                              data!['session'],
                                            )['group'],
                                            'sessionId': widget.sessionId,
                                            'evaluationDate': obj(
                                              data!['session'],
                                            )['sessionDate'],
                                          },
                                        ),
                                      ),
                                    ),
                            ),
                          if (data!['canManageAttendance'] != true)
                            Chip(
                              avatar: Icon(
                                attendanceIcon(player['attendanceStatus']),
                                size: 18,
                              ),
                              label: UiText(
                                label(
                                  player['attendanceStatus'] ?? 'NotRecorded',
                                ),
                              ),
                            )
                          else
                            Wrap(
                              spacing: 6,
                              runSpacing: 6,
                              children: [
                                for (final state in const {
                                  'Present': 'حاضر',
                                  'Absent': 'غائب',
                                }.entries)
                                  FilterChip(
                                    avatar: Icon(
                                      attendanceIcon(state.key),
                                      size: 18,
                                    ),
                                    label: UiText(state.value),
                                    selected:
                                        statuses[textOf(
                                          player['sportEnrollmentId'],
                                        )] ==
                                        state.key,
                                    onSelected: busy || draft != null
                                        ? null
                                        : (_) => setState(
                                            () =>
                                                statuses[textOf(
                                                  player['sportEnrollmentId'],
                                                )] = state
                                                    .key,
                                          ),
                                  ),
                                PopupMenuButton<String>(
                                  enabled: !busy && draft == null,
                                  tooltip: localized(
                                    context,
                                    'حالات أخرى',
                                    'More states',
                                  ),
                                  onSelected: (state) => setState(
                                    () =>
                                        statuses[textOf(
                                              player['sportEnrollmentId'],
                                            )] =
                                            state,
                                  ),
                                  itemBuilder: (_) => [
                                    for (final state in const [
                                      'Late',
                                      'Excused',
                                      'NotRecorded',
                                    ])
                                      CheckedPopupMenuItem(
                                        value: state,
                                        checked:
                                            statuses[textOf(
                                              player['sportEnrollmentId'],
                                            )] ==
                                            state,
                                        child: UiText(label(state)),
                                      ),
                                  ],
                                  child: Padding(
                                    padding: const EdgeInsets.all(10),
                                    child: UiText(
                                      localized(
                                        context,
                                        [
                                              'Late',
                                              'Excused',
                                              'NotRecorded',
                                            ].contains(
                                              statuses[textOf(
                                                player['sportEnrollmentId'],
                                              )],
                                            )
                                            ? '${label(statuses[textOf(player['sportEnrollmentId'])])} ▾'
                                            : 'حالات أخرى ▾',
                                        [
                                              'Late',
                                              'Excused',
                                              'NotRecorded',
                                            ].contains(
                                              statuses[textOf(
                                                player['sportEnrollmentId'],
                                              )],
                                            )
                                            ? '${ui(context, label(statuses[textOf(player['sportEnrollmentId'])]))} ▾'
                                            : 'More states ▾',
                                      ),
                                    ),
                                  ),
                                ),
                              ],
                            ),
                          if (player['warning'] != null)
                            UiText(
                              textOf(player['warning']),
                              style: TextStyle(
                                color: Theme.of(context).colorScheme.error,
                              ),
                            ),
                        ],
                      ),
                    ),
                  ),
                if (data!['canRequestCorrection'] == true)
                  OutlinedButton.icon(
                    icon: const Icon(Icons.history_rounded),
                    label: const UiText('طلب تصحيح يوم سابق'),
                    onPressed: () async {
                      final sent = await Navigator.of(context).push<bool>(
                        MaterialPageRoute(
                          builder: (_) => PastAttendanceRequestPage(
                            auth: widget.auth,
                            sessionId: widget.sessionId,
                            players: rows(data!['players']),
                          ),
                        ),
                      );
                      if (mounted) await load();
                      if (sent == true && mounted) {
                        setState(
                          () => message = 'أُرسل طلب التصحيح لاعتماد المالك؛ لم يتغير الحضور بعد.',
                        );
                      }
                    },
                  ),
              ],
            ),
    );
  }
}

IconData attendanceIcon(Object? state) => switch (state) {
  'Present' => Icons.check_circle_outline,
  'Absent' => Icons.cancel_outlined,
  'Late' => Icons.schedule,
  'Excused' => Icons.event_busy_outlined,
  _ => Icons.radio_button_unchecked,
};
