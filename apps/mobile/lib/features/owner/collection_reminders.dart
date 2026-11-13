import 'package:flutter/material.dart';

import '../../core/auth/auth_controller.dart';
import '../../core/api/api_client.dart';
import '../../core/theme/ui_text.dart';
import '../guardian/guardian_ui.dart';
import 'owner_followups.dart';

class CollectionReminders extends StatelessWidget {
  const CollectionReminders({
    super.key,
    required this.auth,
    this.branch = '',
    this.kind = 'Overdue',
    this.playerId,
  });
  final AuthController auth;
  final String branch, kind;
  final String? playerId;

  @override
  Widget build(BuildContext context) => _ReminderScreen(
    key: ValueKey((auth, branch, kind, playerId)),
    auth: auth,
    branch: branch,
    kind: kind,
    playerId: playerId,
  );
}

class _ReminderScreen extends StatefulWidget {
  const _ReminderScreen({
    super.key,
    required this.auth,
    required this.branch,
    required this.kind,
    this.playerId,
  });
  final AuthController auth;
  final String branch, kind;
  final String? playerId;

  @override
  State<_ReminderScreen> createState() => _ReminderScreenState();
}

class _ReminderScreenState extends State<_ReminderScreen> {
  late final identity = followUpContext(widget.auth);
  bool get current =>
      mounted && identity != null && identity == followUpContext(widget.auth);
  final selected = <String>{}, message = TextEditingController();
  Json? preview;
  String? reviewedRequest;
  bool busy = false, initialized = false;
  bool refreshRequired = false, sendUncertain = false, denied = false;
  String? error;
  int revision = 0;
  @override
  void dispose() {
    message.dispose();
    super.dispose();
  }

  Future<void> action(bool send) async {
    if (busy ||
        !current ||
        selected.isEmpty ||
        message.text.trim().isEmpty ||
        refreshRequired ||
        denied ||
        send && preview == null) {
      return;
    }
    final request = send ? reviewedRequest! : followUpRequestId();
    setState(() {
      busy = true;
      error = null;
    });
    try {
      final result = obj(
        await widget.auth.request(
          'POST',
          '/api/v1/manage/notifications/${send ? 'send' : 'preview'}',
          body: {
            'kind': widget.kind,
            if (widget.branch.isNotEmpty) 'branchId': widget.branch,
            'recipients': selected.toList(),
            if (widget.playerId != null) 'playerId': widget.playerId,
            'message': message.text.trim(),
            'previewHash': preview?['previewHash'],
            'requestId': request,
          },
        ),
      );
      if (!mounted || !current) return;
      if (send) {
        if (result['sent'] is! int || (result['sent'] as int) < 1) {
          throw const FormatException();
        }
        ScaffoldMessenger.of(context).showSnackBar(
          SnackBar(
            content: UiText(
              'تم إرسال التنبيه إلى ${result['sent']} ولي أمر داخل التطبيق.',
            ),
          ),
        );
        Navigator.pop(context, true);
      } else {
        if (result['previewHash'] is! String ||
            result['count'] != selected.length ||
            result['messages'] is! List) {
          throw const FormatException();
        }
        setState(() {
          preview = result;
          reviewedRequest = request;
        });
      }
    } catch (failure) {
      if (!current) return;
      setState(() {
        if (failure is ApiFailure && [401, 403, 404].contains(failure.status)) {
          selected.clear();
          preview = null;
          denied = true;
          error = 'تذكيرات هذا النطاق غير متاحة لهذا الحساب.';
        } else if (failure is ApiFailure &&
            failure.code == 'notice_template_disabled') {
          preview = null;
          denied = true;
          error = 'هذا النوع من التنبيهات معطّل. فعّله من إدارة التنبيهات في لوحة الأكاديمية.';
        } else if (failure is ApiFailure && failure.status == 409) {
          preview = null;
          sendUncertain = false;
          refreshRequired = true;
          error =
              'تغيّرت بيانات التذكير. حدّث قائمة المستلمين قبل إعادة المعاينة.';
        } else {
          sendUncertain = send;
          error = send
              ? 'تعذر تأكيد الإرسال. أعد تأكيد المحاولة نفسها للتحقق.'
              : 'تعذرت معاينة التنبيه. أعد المحاولة.';
        }
      });
    } finally {
      if (current) setState(() => busy = false);
    }
  }

  @override
  Widget build(BuildContext context) => ListenableBuilder(
    listenable: widget.auth,
    builder: (context, _) => !current
        ? const SizedBox.shrink()
        : GuardianPage(
            title: switch (widget.kind) {
              'Overdue' => 'تذكيرات التحصيل',
              'Absence' => 'تنبيه الغياب المتتالي',
              'Expiring' => 'تنبيه قرب انتهاء الاشتراك',
              'Expired' => 'تنبيه تجديد الاشتراك المنتهي',
              _ => 'متابعة التجديد',
            },
            bottom: selected.isEmpty || refreshRequired
                ? null
                : FilledButton.icon(
                    onPressed: busy || message.text.trim().isEmpty
                        ? null
                        : () => action(preview != null),
                    icon: Icon(
                      preview == null
                          ? Icons.visibility_outlined
                          : Icons.send_outlined,
                    ),
                    label: UiText(
                      busy
                          ? 'جارٍ تنفيذ الطلب…'
                          : preview == null
                          ? 'معاينة التنبيه'
                          : sendUncertain
                          ? 'إعادة تأكيد الإرسال'
                          : 'تأكيد إرسال ${preview!['count']} تنبيه',
                    ),
                  ),
            child: denied
                ? EmptyMessage(error!)
                : RemoteBody(
                    key: ValueKey(revision),
                    auth: widget.auth,
                    path:
                        '/api/v1/manage/notifications/recipients?${Uri(queryParameters: {'kind': widget.kind, if (widget.branch.isNotEmpty) 'branchId': widget.branch, if (widget.playerId != null) 'playerId': widget.playerId!}).query}',
                    builder: (context, value) {
                      final data = obj(value);
                      final items = rows(data['items']);
                      if (!initialized && items.isNotEmpty) {
                        message.text = textOf(data['template']);
                        initialized = true;
                      }
                      return gList([
                        if (items.isNotEmpty && data['total'] != data['shown'])
                          UiText(
                            'المعروض ${data['shown']} من ${data['total']}. ضيّق النطاق لاختيار أسر أخرى.',
                          ),
                        if (items.isNotEmpty && widget.kind == 'Renewal')
                          const UiText(
                            'قيمة التجديد تقديرية.',
                            style: TextStyle(fontSize: 11),
                          ),
                        if (items.isEmpty)
                          const EmptyMessage(
                            'لا توجد أسر متاحة للتنبيه في هذا النطاق.',
                          ),
                        if (items.isNotEmpty)
                          CheckboxListTile(
                            contentPadding: EdgeInsets.zero,
                            title: UiText('تحديد كل المعروض (${items.length})'),
                            value:
                                items.isNotEmpty &&
                                selected.length == items.length,
                            onChanged: busy || refreshRequired || sendUncertain
                                ? null
                                : (v) => setState(() {
                                    selected.clear();
                                    if (v == true) {
                                      selected.addAll(
                                        items.map((r) => textOf(r['id'])),
                                      );
                                    }
                                    preview = null;
                                  }),
                          ),
                        for (final r in items)
                          GCard(
                            child: CheckboxListTile(
                              contentPadding: EdgeInsets.zero,
                              value: selected.contains(r['id']),
                              title: UiText(textOf(r['name'])),
                              subtitle: UiText(
                                [
                                  rows(r['children'])
                                      .map((c) => c['name'])
                                      .join('، '),
                                  if (widget.kind == 'Renewal' ||
                                      widget.kind == 'Overdue')
                                    r['amount'] == null
                                        ? 'القيمة تحتاج مراجعة'
                                        : money(r['amount'], r['currency']),
                                ].join('\n'),
                                style: const TextStyle(fontSize: 11),
                              ),
                              onChanged:
                                  busy || refreshRequired || sendUncertain
                                  ? null
                                  : (v) => setState(() {
                                      v == true
                                          ? selected.add(textOf(r['id']))
                                          : selected.remove(r['id']);
                                      preview = null;
                                    }),
                            ),
                          ),
                        if (items.isNotEmpty)
                          TextField(
                            controller: message,
                            maxLines: 4,
                            maxLength: 1000,
                            enabled:
                                !busy && !refreshRequired && !sendUncertain,
                            decoration: const InputDecoration(
                              labelText: 'نص التنبيه',
                            ),
                            onChanged: (_) => setState(() => preview = null),
                          ),
                        if (error != null) ...[
                          UiText(error!),
                          if (refreshRequired)
                            TextButton.icon(
                              onPressed: busy
                                  ? null
                                  : () => setState(() {
                                      selected.clear();
                                      refreshRequired = false;
                                      initialized = false;
                                      error = null;
                                      revision++;
                                    }),
                              icon: const Icon(Icons.refresh),
                              label: const UiText('تحديث قائمة المستلمين'),
                            ),
                        ],
                        if (preview != null) ...[
                          const Heading('معاينة الرسائل'),
                          for (final m in rows(preview!['messages']))
                            GCard(child: UiText(textOf(m['message']))),
                          const UiText(
                            'تصل التنبيهات إلى الأسر المحددة داخل التطبيق.',
                            style: TextStyle(fontSize: 10),
                          ),
                        ],
                      ]);
                    },
                  ),
          ),
  );
}
