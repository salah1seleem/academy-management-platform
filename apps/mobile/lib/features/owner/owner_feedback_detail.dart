import 'package:flutter/material.dart';

import 'owner_followups.dart' show followUpRequestId;

import '../../core/api/api_client.dart';
import '../../core/auth/auth_controller.dart';
import '../../core/theme/ui_text.dart';
import '../guardian/guardian_ui.dart';
import '../shared/feedback_state.dart';
import '../shared/feedback_deadline.dart';
import '../shared/feedback_investigations.dart';
import 'feedback_triage.dart';
import '../shared/feedback_incident.dart';

class OwnerFeedbackDetail extends StatefulWidget {
  final AuthController auth;
  final String id;
  const OwnerFeedbackDetail({super.key, required this.auth, required this.id});
  @override
  State<OwnerFeedbackDetail> createState() => _OwnerFeedbackDetailState();
}

class _OwnerFeedbackDetailState extends State<OwnerFeedbackDetail>
    with WidgetsBindingObserver {
  String? get current => widget.auth.session?.selected == null
      ? null
      : '${widget.auth.session!.selected!.id}:${widget.auth.session!.selected!.contextRevision}';
  late final identity = current;
  final reply = TextEditingController();
  final note = TextEditingController();
  String? target, attemptSignature, attemptId;
  int revision = 0;
  bool foreground = true, busy = false;
  String message = '';
  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addObserver(this);
  }

  @override
  void dispose() {
    WidgetsBinding.instance.removeObserver(this);
    reply.dispose();
    note.dispose();
    super.dispose();
  }

  @override
  void didChangeAppLifecycleState(AppLifecycleState state) {
    setState(() {
      foreground = state == AppLifecycleState.resumed;
      revision++;
      reply.clear();
      note.clear();
      target = null;
      attemptId = null;
      attemptSignature = null;
    });
  }

  Future<void> save(
    String action, [
    Object body = const <String, dynamic>{},
  ]) async {
    if (busy || identity == null || identity != current) {
      return;
    }
    setState(() {
      busy = true;
      message = '';
    });
    try {
      await widget.auth.request(
        'POST',
        '/api/v1/manage/feedback/${widget.id}/$action',
        body: body,
      );
      if (mounted && identity == current) {
        setState(() {
          revision++;
          reply.clear();
          note.clear();
          target = null;
          attemptId = null;
          attemptSignature = null;
          message = action == 'resolve'
              ? 'تم حفظ الرد وحل الملاحظة. الرد متاح للأسرة المرتبطة داخل التطبيق.'
              : 'تم حفظ تحديث الملاحظة.';
        });
      }
    } catch (e) {
      if (mounted && identity == current) {
        setState(
          () => message = e is ApiFailure ? e.message : 'تعذر حفظ الملاحظة.',
        );
      }
    } finally {
      if (mounted) {
        setState(() => busy = false);
      }
    }
  }

  @override
  Widget build(BuildContext context) => ListenableBuilder(
    listenable: widget.auth,
    builder: (context, _) {
      if (!foreground || identity == null || identity != current) {
        return const SizedBox.shrink();
      }
      return GuardianPage(
        title: 'متابعة ملاحظة الأسرة',
        child: RemoteBody(
          key: ValueKey(revision),
          auth: widget.auth,
          path: '/api/v1/manage/feedback/${widget.id}',
          builder: (context, value) {
            final item = obj(value);
            return gList([
              Heading(
                item['kind'] == 'Complaint'
                    ? 'شكوى'
                    : item['kind'] == 'Suggestion'
                    ? 'اقتراح'
                    : item['kind'] == 'Request'
                    ? 'طلب للإدارة'
                    : 'تقييم مدرب',
              ),
              UiText(
                '${item['playerName'] ?? 'اللاعب غير متاح'} · ${item['groupName']} · ${item['branchName']}',
              ),
              UiText(textOf(item['message'])),
              FeedbackIncidentEvidence(
                evidence: item['incidentSessionEvidence'] as String?,
              ),
              if (item['kind'] != 'Rating') ...[
                UiText(
                  'عند التقديم: ${feedbackSubjects[item['reportedSubject']] ?? 'لم يصنف وقتها'} · ${feedbackUrgencies[item['reportedUrgency']] ?? 'لم يحدد'} · الواقعة: ${item['incidentOn'] ?? 'غير محددة'}',
                ),
                UiText(
                  'مراجعة الإدارة: ${feedbackSubjects[item['subject']] ?? 'لم تصنف بعد'} · ${feedbackUrgencies[item['urgency']] ?? 'لم تحدد'}',
                ),
                if (item['safety'] == true)
                  const UiText(
                    'بلاغ سلامة يحتاج متابعة بشرية، وليس إثبات خطأ أو حكمًا آليًا.',
                  ),
              ],
              UiText(feedbackStates[item['status']] ?? 'غير متاحة'),
              UiText(
                'موعد المتابعة: ${item['dueOn'] ?? 'لم يحدد'}${item['overdue'] == true ? ' · تجاوزت الموعد المحدد' : ''}',
              ),
              FeedbackDeadlineEvidence(
                evidence: item['followUpPolicyEvidence'] as String?,
              ),
              if (message.isNotEmpty) UiText(message),
              if (item['kind'] != 'Rating' &&
                  item['version'] is int &&
                  item['status'] != 'Resolved' &&
                  item['status'] != 'Closed')
                FeedbackHumanTriage(
                  key: ValueKey('triage-${item['version']}'),
                  auth: widget.auth,
                  item: item,
                  saved: () => setState(() {
                    revision++;
                    message =
                        'تم تأكيد الفرز البشري دون تغيير بلاغ الأسرة الأصلي.';
                  }),
                ),
              if (item['kind'] != 'Rating' &&
                  item['version'] is int &&
                  item['status'] != 'Resolved' &&
                  item['status'] != 'Closed')
                OwnerInvestigationAssignment(
                  key: ValueKey('assignment-${item['version']}'),
                  auth: widget.auth,
                  item: item,
                  saved: () => setState(() {
                    revision++;
                    message = 'تم حفظ تكليف التحقيق؛ لم يُرسل شيء للأسرة.';
                  }),
                ),
              if (item['ownerResponse'] is String)
                GCard(child: UiText('رد الإدارة: ${item['ownerResponse']}')),
              if (item['status'] == 'Open')
                OutlinedButton(
                  onPressed: busy ? null : () => save('review'),
                  child: const UiText('تسجيل المراجعة'),
                ),
              if (item['status'] != 'Resolved' &&
                  item['status'] != 'Closed' &&
                  item['kind'] != 'Rating' &&
                  item['dueOn'] == null)
                OutlinedButton(
                  onPressed: busy
                      ? null
                      : () async {
                          final d = await showDatePicker(
                            context: context,
                            initialDate: DateTime.parse(
                              textOf(item['asOfDate']),
                            ),
                            firstDate: DateTime(2000),
                            lastDate: DateTime(2100),
                          );
                          if (d != null) {
                            await save('due', {
                              'dueOn': d.toIso8601String().split('T').first,
                            });
                          }
                        },
                  child: const UiText('تحديد موعد متابعة'),
                ),
              if (item['status'] != 'Resolved' &&
                  item['status'] != 'Closed' &&
                  item['kind'] != 'Rating') ...[
                TextField(
                  controller: reply,
                  maxLength: 500,
                  maxLines: 3,
                  enabled: !busy,
                  decoration: const InputDecoration(
                    labelText: 'الرد على الملاحظة',
                  ),
                ),
                const UiText(
                  'الرد وإشعاره داخل التطبيق للأسرة المرتبطة فقط؛ ليس تواصلًا خارجيًا أو إغلاقًا لمهمة التشغيل.',
                ),
                FilledButton(
                  onPressed:
                      busy ||
                          item['safety'] == true && item['triagedAtUtc'] == null
                      ? null
                      : () {
                          if (reply.text.trim().length < 5) {
                            setState(
                              () =>
                                  message = 'اكتب ردًا من خمسة أحرف على الأقل.',
                            );
                            return;
                          }
                          save('resolve', {'response': reply.text.trim()});
                        },
                  child: const UiText('إرسال الرد وحل الملاحظة'),
                ),
              ],
              if (item['kind'] != 'Rating' && item['version'] is int) ...[
                const Heading('مسار المعالجة الخاص'),
                const UiText(
                  'للمالك فقط. الملاحظة الداخلية اختيارية ولا تُرسل للأسرة أو المدرب. الحل يتطلب الرد على الأسرة.',
                ),
                const SizedBox(height: 12),
                DropdownButtonFormField<String>(
                  key: ValueKey('transition-$revision'),
                  decoration: const InputDecoration(
                    labelText: 'الخطوة التالية',
                  ),
                  initialValue: target,
                  items: [
                    for (final s
                        in feedbackTransitions[item['status']] ?? <String>[])
                      DropdownMenuItem(
                        value: s,
                        child: UiText(feedbackStates[s]!),
                      ),
                  ],
                  onChanged: busy ? null : (v) => setState(() => target = v),
                ),
                const SizedBox(height: 16),
                TextField(
                  controller: note,
                  enabled: !busy,
                  maxLength: 500,
                  maxLines: 3,
                  decoration: const InputDecoration(
                    labelText: 'ملاحظة داخلية (اختياري)',
                  ),
                ),
                const SizedBox(height: 8),
                FilledButton(
                  onPressed: busy || target == null
                      ? null
                      : () {
                          final signature =
                              '${item['version']}/$target/${note.text.trim()}';
                          if (attemptSignature != signature) {
                            attemptSignature = signature;
                            attemptId = followUpRequestId();
                          }
                          save('transition', {
                            'status': target,
                            'version': item['version'],
                            'requestId': attemptId,
                            'internalNote': note.text.trim().isEmpty
                                ? null
                                : note.text.trim(),
                          });
                        },
                  child: const UiText('حفظ مرحلة المعالجة'),
                ),
              ],
              const SizedBox(height: 20),
              const Heading('سجل الملاحظة الخاص'),
              const UiText(
                'الأحدث أولًا، حتى 50 تحديثًا. لا يُختلق سجل سابق لهذه الإضافة.',
              ),
              if (item['historyHasMore'] == true)
                const UiText('توجد تحديثات أقدم غير معروضة؛ لم تُحذف.'),
              if (rows(item['history'] ?? const []).isEmpty)
                const UiText('لم تُسجل تحديثات بعد.'),
              const SizedBox(height: 12),
              for (final c in rows(item['history'] ?? const []))
                GCard(
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      UiText(
                        c['action'] == 'Triage'
                            ? 'فرز بشري: ${feedbackSubjects[c['afterSubject']]} · ${feedbackUrgencies[c['afterUrgency']]}'
                            : c['action'] == 'Assign'
                            ? 'تكليف تحقيق: ${c['investigatorName'] ?? 'إلغاء الإسناد'}'
                            : c['action'] == 'Findings'
                            ? 'نتيجة تحقيق خاصة'
                            : c['action'] == 'Due'
                            ? 'تحديد موعد متابعة: ${c['afterDueOn']}'
                            : '${feedbackStates[c['fromStatus']] ?? 'غير متاحة'} ← ${feedbackStates[c['toStatus']] ?? 'غير متاحة'}',
                      ),
                      UiText(textOf(c['actorName'])),
                      const SizedBox(height: 6),
                      Text(
                        '${feedbackUtcTime(c['createdAtUtc'])} · التوقيت العالمي',
                        style: Theme.of(context).textTheme.bodySmall,
                      ),
                      if (c['internalNote'] is String)
                        UiText('ملاحظة داخلية: ${c['internalNote']}'),
                      if (c['publicResponse'] is String)
                        UiText(
                          '${c['action'] == 'Transition' && c['toStatus'] == 'Reopened' ? 'الرد السابق المحفوظ عند إعادة الفتح' : 'الرد المحفوظ للأسرة'}: ${c['publicResponse']}',
                        ),
                    ],
                  ),
                ),
            ]);
          },
        ),
      );
    },
  );
}
