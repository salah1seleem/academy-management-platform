import 'package:academy_mobile/core/theme/ui_text.dart';
import 'package:flutter/material.dart';
import 'package:intl/intl.dart' show Intl;

import '../../core/api/api_client.dart';
import '../../core/auth/auth_controller.dart';
import '../../core/theme/appearance.dart';
import '../../core/widgets/app_widgets.dart';
import '../guardian/guardian_ui.dart';
import 'coach_evaluation.dart';
import 'coach_player.dart';
import '../shared/past_attendance.dart';
import '../shared/attendance_draft.dart';
import 'session_context.dart';
import '../shared/session_lesson.dart';

class CoachAttendance extends StatefulWidget {
  final AuthController auth;
  final String sessionId;
  final String? title;
  final List<Widget> groupHeader;
  final Json? initialRoster;
  final Set<String> groupEnrollmentIds;
  const CoachAttendance({
    super.key,
    required this.auth,
    required this.sessionId,
    this.title,
    this.groupHeader = const [],
    this.initialRoster,
    this.groupEnrollmentIds = const {},
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
  bool reviewed = false;
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
    final initial = widget.initialRoster;
    if (initial == null) {
      load();
    } else {
      applyRoster(initial);
    }
  }

  void authChanged() {
    if (identity == currentIdentity || !mounted) return;
    setState(() {
      identity = currentIdentity;
      draft = null;
      reviewed = false;
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

  void applyRoster(Json roster) {
    statuses.clear();
    for (final player in rows(roster['players'])) {
      statuses[textOf(player['sportEnrollmentId'])] =
          player['attendanceStatus'] as String? ?? 'NotRecorded';
    }
    data = roster;
    error = null;
  }

  Future<bool> load() async {
    try {
      final next = await read();
      if (!mounted || blocked) return false;
      setState(() => applyRoster(next));
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
    final hasDraft = draft != null;
    final current = draft ?? AttendanceDraft(data!, statuses);
    if (!hasDraft && current.changes.isEmpty) return;
    setState(() {
      busy = true;
      error = null;
      message = null;
    });
    try {
      current.validate();
      Iterable<Json> remaining = current.changes.values;
      if (hasDraft) {
        final latest = await read();
        if (!mounted || blocked) return;
        current.validate(latest: latest);
        final changed = current.conflicts(latest);
        if (changed.isNotEmpty) {
          setState(() {
            reviewed = false;
            conflicts.addAll(changed);
            error = 'تغير كشف الحضور. راجع الحالات المتعارضة.';
          });
          return;
        }
        remaining = current.remaining(latest);
        if (remaining.isEmpty) {
          setState(() {
            applyRoster(latest);
            draft = null;
            reviewed = false;
            message = current.changes.isEmpty
                ? 'تم تحديث الكشف بحالات الخادم.'
                : 'الحضور محفوظ بالفعل دون إعادة الإرسال.';
          });
          return;
        }
        if (!reviewed) {
          setState(() => reviewed = true);
          return;
        }
      }
      await widget.auth.request(
        'PUT',
        '/api/v1/attendance/sessions/${widget.sessionId}/players',
        body: current.bodyFor(remaining),
      );
      if (!mounted || blocked) return;
      final latest = await read();
      if (!mounted || blocked) return;
      current.validateAccess(latest);
      if (!current.confirmed(latest)) {
        throw const ApiFailure(409, 'لم تظهر كل التغييرات في الكشف المحدث.');
      }
      setState(() {
        applyRoster(latest);
        draft = null;
        reviewed = false;
        message = 'تم حفظ الحضور.';
      });
    } on ApiFailure catch (e) {
      if (mounted && !blocked) {
        setState(() {
          blocked =
              current.lease == null ||
              current.scope == null ||
              [401, 403, 404, 410].contains(e.status);
          draft = blocked ? null : current;
          reviewed = false;
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

  Future<void> discardDraft() async {
    if (busy) return;
    setState(() => busy = true);
    final loaded = await load();
    if (!mounted) return;
    setState(() {
      busy = false;
      if (loaded) {
        draft = null;
        reviewed = false;
        conflicts.clear();
      }
    });
  }

  Widget evaluationButton(Json player, Json session) => OutlinedButton.icon(
    icon: const Icon(Icons.assessment_outlined, size: 17),
    label: UiText(widget.groupHeader.isEmpty ? 'تقييم اللاعب' : 'تقييم'),
    onPressed: busy || draft != null
        ? null
        : () => openPage(
            context,
            CoachEvaluation(
              auth: widget.auth,
              enrollment: {
                'id': player['sportEnrollmentId'],
                'playerId': player['playerId'],
                'player': player['arabicName'],
                'group': session['group'],
                'sport': session['sport'],
                'sportId': session['sportId'],
                'criteriaState': session['criteriaState'],
                'criteriaSessionId': widget.sessionId,
                if (!widget.groupEnrollmentIds.contains(
                  textOf(player['sportEnrollmentId']),
                )) ...{
                  'sessionId': widget.sessionId,
                  'evaluationDate': session['sessionDate'],
                },
              },
            ),
          ),
  );

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
    final unsavedLabel = localized(
      context,
      Intl.plural(
        pending,
        locale: 'ar',
        one: 'تغيير واحد لم يُحفظ',
        two: 'تغييران لم يُحفظا',
        few: '$pending تغييرات لم تُحفظ',
        many: '$pending تغييرًا لم يُحفظ',
        other: '$pending تغيير لم يُحفظ',
      ),
      pending == 1 ? '1 unsaved change' : '$pending unsaved changes',
    );
    final canSave = data?['canManageAttendance'] == true;
    final checking = draft != null && !reviewed;
    final actionLabel = busy
        ? (checking ? 'جارٍ التحقق…' : 'جارٍ الحفظ…')
        : draft != null
        ? (reviewed ? 'إعادة إرسال التغييرات' : 'تحقق من الحفظ')
        : 'حفظ الحضور';
    final statusNotice = draft != null
        ? (reviewed ? 'تغييرات غير مسجلة' : 'الحفظ غير مؤكد')
        : pending == 0
        ? message
        : null;
    return AppScaffold(
      title: widget.title ?? 'تسجيل الحضور',
      bottomBar: !canSave
          ? null
          : Padding(
              padding: const EdgeInsets.symmetric(horizontal: 18, vertical: 10),
              child: Column(
                mainAxisSize: MainAxisSize.min,
                crossAxisAlignment: CrossAxisAlignment.stretch,
                children: [
                  if (statusNotice != null)
                    Padding(
                      padding: const EdgeInsets.only(bottom: 4),
                      child: Semantics(
                        liveRegion: true,
                        child: UiText(
                          statusNotice,
                          textAlign: TextAlign.center,
                        ),
                      ),
                    ),
                  FilledButton.icon(
                    icon: busy
                        ? const SizedBox.square(
                            dimension: 18,
                            child: CircularProgressIndicator(strokeWidth: 2),
                          )
                        : Icon(
                            checking
                                ? Icons.fact_check_outlined
                                : Icons.save_outlined,
                          ),
                    onPressed:
                        busy ||
                            conflicts.isNotEmpty ||
                            (draft == null && pending == 0)
                        ? null
                        : save,
                    label: UiText(actionLabel),
                  ),
                ],
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
                ...widget.groupHeader,
                AppCard(
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      UiText(
                        widget.groupHeader.isEmpty
                            ? textOf(session['group'])
                            : 'حضور حصة اليوم',
                        style: Theme.of(context).textTheme.titleLarge,
                      ),
                      const SizedBox(height: 8),
                      SessionContextBadges(session: session, showSource: true),
                      GLink(
                        localized(context, 'خطة الحصة', 'Session plan'),
                        Icons.playlist_add_check,
                        () => openPage(
                          context,
                          SessionLessonPage(
                            auth: widget.auth,
                            sessionId: widget.sessionId,
                          ),
                        ),
                      ),
                      UiText(sessionDateLabel(session['sessionDate'])),
                      UiText(
                        trainingTimeRange(
                          session['startTime'],
                          session['endTime'],
                        ),
                        textDirection: TextDirection.rtl,
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
                if (widget.groupHeader.isEmpty)
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
                if (widget.groupHeader.isEmpty || pending > 0)
                  Semantics(
                    liveRegion: true,
                    child: UiText(
                      localized(
                        context,
                        pending == 0
                            ? 'الحالات المعروضة مؤكدة من الخادم'
                            : unsavedLabel,
                        pending == 0
                            ? 'Displayed states are confirmed by the server'
                            : '$pending unsaved changes',
                      ),
                    ),
                  ),
                if (widget.groupHeader.isEmpty &&
                    data!['lastSynchronizedAtUtc'] != null)
                  UiText(
                    '${localized(context, 'آخر تحديث للكشف', 'Roster last refreshed')}: ${synchronizedTime(data!['lastSynchronizedAtUtc'])}',
                  ),
                if (!canSave && message != null && pending == 0)
                  Semantics(liveRegion: true, child: UiText(message!)),
                if (error != null) ErrorNotice(error!),
                if (draft != null)
                  AppCard(
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        const UiText(
                          'تبقى التغييرات في هذه الصفحة لمدة 15 دقيقة كحد أقصى.',
                        ),
                        TextButton(
                          onPressed: busy ? null : discardDraft,
                          child: const UiText('تجاهل التغييرات وتحديث الكشف'),
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
                  UiText(
                    session['status'] == 'Cancelled'
                        ? 'أُلغيت الحصة.'
                        : data!['isPast'] == true
                        ? 'كشف حضور يوم سابق.'
                        : 'كشف الحضور للعرض فقط.',
                  ),
                const SizedBox(height: 12),
                TextField(
                  controller: search,
                  onChanged: (_) => setState(() {}),
                  decoration: InputDecoration(
                    labelText: localized(
                      context,
                      'الاسم أو الكود',
                      'Name or player code',
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
                    'النتائج: ${filtered.length} من ${players.length}',
                    'Results: ${filtered.length} of ${players.length}',
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
                          if ((widget.groupHeader.isEmpty ||
                                  data!['canManageAttendance'] != true) &&
                              data!['canEvaluate'] == true)
                            evaluationButton(player, session),
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
                                if (widget.groupHeader.isNotEmpty &&
                                    data!['canEvaluate'] == true)
                                  evaluationButton(player, session),
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
