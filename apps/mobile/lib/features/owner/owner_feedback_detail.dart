import 'package:flutter/material.dart';

import '../../core/api/api_client.dart';
import '../../core/auth/auth_controller.dart';
import '../../core/theme/ui_text.dart';
import '../guardian/guardian_ui.dart';

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
    super.dispose();
  }

  @override
  void didChangeAppLifecycleState(AppLifecycleState state) {
    setState(() {
      foreground = state == AppLifecycleState.resumed;
      revision++;
      reply.clear();
    });
  }

  Future<void> save(
    String action, [
    Object body = const <String, dynamic>{},
  ]) async {
    if (busy || identity == null || identity != current) {
      return;
    }
    setState(() => busy = true);
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
                    : 'تقييم مدرب',
              ),
              UiText(
                '${item['playerName'] ?? 'اللاعب غير متاح'} · ${item['groupName']} · ${item['branchName']}',
              ),
              UiText(textOf(item['message'])),
              UiText(
                item['status'] == 'Resolved'
                    ? 'تم الرد والحل'
                    : item['status'] == 'Reviewed'
                    ? 'تمت المراجعة'
                    : 'تنتظر المراجعة',
              ),
              UiText(
                'موعد المتابعة: ${item['dueOn'] ?? 'لم يحدد'}${item['overdue'] == true ? ' · تجاوزت الموعد المحدد' : ''}',
              ),
              const UiText(
                'موعد حدده المالك، وليس وعد استجابة تلقائيًا. لا تظهر هوية مقدم الملاحظة للمدرب موضوعها.',
              ),
              if (message.isNotEmpty) UiText(message),
              if (item['ownerResponse'] is String)
                GCard(child: UiText('رد الإدارة: ${item['ownerResponse']}')),
              if (item['status'] == 'Open')
                OutlinedButton(
                  onPressed: busy ? null : () => save('review'),
                  child: const UiText('تسجيل المراجعة'),
                ),
              if (item['status'] != 'Resolved' &&
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
              if (item['status'] != 'Resolved' && item['kind'] != 'Rating') ...[
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
                  onPressed: busy
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
            ]);
          },
        ),
      );
    },
  );
}
