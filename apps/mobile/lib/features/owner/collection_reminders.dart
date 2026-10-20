import 'package:flutter/material.dart';

import '../../core/auth/auth_controller.dart';
import '../../core/theme/ui_text.dart';
import '../guardian/guardian_ui.dart';
import 'owner_followups.dart';

class CollectionReminders extends StatefulWidget {
  const CollectionReminders({
    super.key,
    required this.auth,
    this.branch = '',
    this.kind = 'Overdue',
  });
  final AuthController auth;
  final String branch, kind;
  @override
  State<CollectionReminders> createState() => _CollectionRemindersState();
}

class _CollectionRemindersState extends State<CollectionReminders> {
  final selected = <String>{}, message = TextEditingController();
  String request = followUpRequestId();
  Json? preview;
  bool busy = false, initialized = false;
  String? error;
  int revision = 0;
  @override
  void dispose() {
    message.dispose();
    super.dispose();
  }

  Future<void> action(bool send) async {
    if (busy) return;
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
            'message': message.text.trim(),
            'previewHash': preview?['previewHash'],
            'requestId': request,
          },
        ),
      );
      if (!mounted) return;
      if (send) {
        ScaffoldMessenger.of(context).showSnackBar(
          SnackBar(
            content: UiText('تم إرسال ${result['sent']} تنبيه داخل التطبيق.'),
          ),
        );
        Navigator.pop(context, true);
      } else {
        setState(() => preview = result);
      }
    } catch (_) {
      if (mounted) {
        setState(
          () => error = 'تعذر تنفيذ الإجراء؛ حدّث المعاينة وحاول مرة أخرى.',
        );
      }
    } finally {
      if (mounted) setState(() => busy = false);
    }
  }

  @override
  Widget build(BuildContext context) => GuardianPage(
    title: widget.kind == 'Overdue' ? 'تذكيرات التحصيل' : 'متابعة التجديد',
    child: RemoteBody(
      key: ValueKey(revision),
      auth: widget.auth,
      path:
          '/api/v1/manage/notifications/recipients?${Uri(queryParameters: {'kind': widget.kind, if (widget.branch.isNotEmpty) 'branchId': widget.branch}).query}',
      builder: (context, value) {
        final data = obj(value), items = rows(data['items']);
        if (!initialized) {
          message.text = textOf(data['template']);
          initialized = true;
        }
        return gList([
          UiText(
            '${data['total']} ولي أمر · المعروض ${data['shown']}',
            style: const TextStyle(fontWeight: FontWeight.w800),
          ),
          if (widget.kind == 'Renewal')
            const UiText(
              'قيمة التجديد المتوقعة ليست دينًا قائمًا.',
              style: TextStyle(fontSize: 11),
            ),
          CheckboxListTile(
            contentPadding: EdgeInsets.zero,
            title: UiText('تحديد كل المعروض (${items.length})'),
            value: items.isNotEmpty && selected.length == items.length,
            onChanged: busy
                ? null
                : (v) => setState(() {
                    selected.clear();
                    if (v == true) {
                      selected.addAll(items.map((r) => textOf(r['id'])));
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
                  '${rows(r['children']).map((c) => c['name']).join('، ')}\n${r['amount'] == null ? 'القيمة تحتاج مراجعة' : money(r['amount'], r['currency'])}',
                  style: const TextStyle(fontSize: 11),
                ),
                onChanged: busy
                    ? null
                    : (v) => setState(() {
                        v == true
                            ? selected.add(textOf(r['id']))
                            : selected.remove(r['id']);
                        preview = null;
                      }),
              ),
            ),
          TextField(
            controller: message,
            maxLines: 4,
            maxLength: 1000,
            enabled: !busy,
            decoration: const InputDecoration(labelText: 'نص التنبيه'),
            onChanged: (_) => setState(() => preview = null),
          ),
          if (error != null) UiText(error!),
          if (preview == null)
            FilledButton(
              onPressed: busy || selected.isEmpty ? null : () => action(false),
              child: UiText('معاينة ${selected.length} تنبيه'),
            ),
          if (preview != null) ...[
            const Heading('معاينة الرسائل'),
            for (final m in rows(preview!['messages']))
              GCard(child: UiText(textOf(m['message']))),
            const UiText(
              'إرسال داخلي فقط للأسر المحددة؛ لا رسائل هاتفية أو دفع.',
              style: TextStyle(fontSize: 10),
            ),
            FilledButton.icon(
              onPressed: busy ? null : () => action(true),
              icon: const Icon(Icons.send_outlined),
              label: UiText('تأكيد إرسال ${preview!['count']} تنبيه'),
            ),
          ],
        ]);
      },
    ),
  );
}
