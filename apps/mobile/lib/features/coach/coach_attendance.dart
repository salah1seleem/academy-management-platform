import 'package:academy_mobile/core/theme/ui_text.dart';
import 'package:flutter/material.dart';

import '../../core/api/api_client.dart';
import '../../core/auth/auth_controller.dart';
import '../../core/theme/appearance.dart';
import '../../core/widgets/app_widgets.dart';
import '../guardian/guardian_ui.dart';
import 'coach_evaluation.dart';
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
  String? identity;
  String? get currentIdentity => widget.auth.session?.selected == null
      ? null
      : '${widget.auth.session!.selected!.academyId}:${widget.auth.session!.selected!.id}';

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
      blocked = true;
      error = 'تغير حساب الدخول؛ أعد فتح كشف الحضور.';
    });
  }

  @override
  void dispose() {
    widget.auth.removeListener(authChanged);
    draft = null;
    conflicts.clear();
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
        statuses[textOf(player['sportEnrollmentId'])] = textOf(
          player['attendanceStatus'],
        );
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
  Widget build(BuildContext context) => AppScaffold(
    title: widget.title ?? 'تسجيل الحضور',
    child: data == null
        ? Center(
            child: error == null
                ? const CircularProgressIndicator()
                : ErrorNotice(error!),
          )
        : ListView(
            padding: const EdgeInsets.all(18),
            children: [
              AppCard(
                child: UiText(
                  '${obj(data!['session'])['group']} · ${day(obj(data!['session'])['sessionDate'])} · ${textOf(obj(data!['session'])['startTime']).split(':').take(2).join(':')}',
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
                          child: const UiText('الاحتفاظ باختياري بعد المراجعة'),
                        ),
                    ],
                  ),
                ),
              if (data!['canManageAttendance'] != true)
                const UiText(
                  'الحضور للعرض فقط؛ يتولى الإداري المصرح له التسجيل.',
                ),
              for (final player in rows(data!['players']))
                Card(
                  child: Padding(
                    padding: const EdgeInsets.all(12),
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        UiText(
                          textOf(player['arabicName']),
                          style: const TextStyle(fontWeight: FontWeight.bold),
                        ),
                        UiText(textOf(player['playerCode'])),
                        if (data!['canEvaluate'] == true)
                          OutlinedButton.icon(
                            icon: const Icon(Icons.assessment_outlined),
                            label: const UiText('تقييم اللاعب'),
                            onPressed: () => Navigator.of(context).push(
                              MaterialPageRoute<void>(
                                builder: (_) => CoachEvaluation(
                                  auth: widget.auth,
                                  enrollment: {
                                    'id': player['sportEnrollmentId'],
                                    'player': player['arabicName'],
                                    'group': obj(data!['session'])['group'],
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
                          Chip(label: UiText(label(player['attendanceStatus'])))
                        else
                          Wrap(
                            spacing: 6,
                            runSpacing: 6,
                            children: [
                              for (final state in const {
                                'Present': 'حاضر',
                                'Late': 'متأخر',
                                'Absent': 'غائب',
                                'Excused': 'معتذر',
                                'NotRecorded': 'لم يُسجل',
                              }.entries)
                                ChoiceChip(
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
                                                  )] =
                                                  state.key,
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
              if (data!['canManageAttendance'] == true)
                FilledButton(
                  onPressed: busy || conflicts.isNotEmpty ? null : save,
                  child: UiText(
                    busy
                        ? 'جارٍ الحفظ…'
                        : draft != null
                        ? 'مراجعة وإعادة المزامنة'
                        : 'حفظ الحضور',
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
