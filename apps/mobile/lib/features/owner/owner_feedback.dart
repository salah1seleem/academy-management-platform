import 'package:flutter/material.dart';

import '../../core/api/api_client.dart';
import '../../core/auth/auth_controller.dart';
import '../../core/theme/ui_text.dart';
import '../guardian/guardian_ui.dart';

class OwnerFeedbackInbox extends StatefulWidget {
  final AuthController auth;
  const OwnerFeedbackInbox({super.key, required this.auth});
  @override
  State<OwnerFeedbackInbox> createState() => _OwnerFeedbackInboxState();
}

class _OwnerFeedbackInboxState extends State<OwnerFeedbackInbox> {
  String kind = '', message = '';
  int revision = 0;
  Future<void> review(String id) async {
    try {
      await widget.auth.request('POST', '/api/v1/manage/feedback/$id/review', body: {});
      if (mounted) setState(() { message = 'تم تسجيل مراجعة الملاحظة.'; revision++; });
    } catch (error) {
      if (mounted) setState(() => message = error is ApiFailure ? error.message : 'تعذر تحديث الملاحظة.');
    }
  }
  Future<void> resolve(String id) async {
    final controller = TextEditingController();
    final response = await showDialog<String>(context: context, builder: (dialog) => AlertDialog(
      title: const UiText('الرد على ولي الأمر'),
      content: TextField(controller: controller, maxLength: 500, maxLines: 4, decoration: const InputDecoration(labelText: 'رد الإدارة')),
      actions: [TextButton(onPressed: () => Navigator.pop(dialog), child: const UiText('إلغاء')),
        FilledButton(onPressed: () => Navigator.pop(dialog, controller.text.trim()), child: const UiText('إرسال الرد وحل الملاحظة'))],
    ));
    controller.dispose();
    if (response == null) return;
    if (response.length < 5) { if (mounted) setState(() => message = 'اكتب ردًا من خمسة أحرف على الأقل.'); return; }
    try {
      await widget.auth.request('POST', '/api/v1/manage/feedback/$id/resolve', body: {'response': response});
      if (mounted) setState(() { message = 'أُرسل الرد لولي الأمر وحُلّت الملاحظة.'; revision++; });
    } catch (error) {
      if (mounted) setState(() => message = error is ApiFailure ? error.message : 'تعذر إرسال الرد.');
    }
  }
  @override
  Widget build(BuildContext context) => GuardianPage(
    title: 'ملاحظات أولياء الأمور',
    child: RemoteBody(
      key: ValueKey('$revision/$kind'), auth: widget.auth,
      path: '/api/v1/manage/feedback${kind.isEmpty ? '' : '?kind=$kind'}',
      builder: (context, value) => gList([
        if (message.isNotEmpty) UiText(message),
        DropdownButtonFormField<String>(initialValue: kind,
          decoration: const InputDecoration(labelText: 'نوع الملاحظة'),
          items: const [DropdownMenuItem(value: '', child: UiText('الكل')), DropdownMenuItem(value: 'Rating', child: UiText('تقييمات المدربين')), DropdownMenuItem(value: 'Complaint', child: UiText('الشكاوى')), DropdownMenuItem(value: 'Suggestion', child: UiText('الاقتراحات'))],
          onChanged: (value) => setState(() => kind = value ?? '')),
        const SizedBox(height: 14),
        if (rows(value).isEmpty) const EmptyMessage('لا توجد ملاحظات في هذا القسم.'),
        for (final item in rows(value)) GCard(child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
          UiText('${item['kind'] == 'Rating' ? 'تقييم' : item['kind'] == 'Complaint' ? 'شكوى' : 'اقتراح'} · ${item['playerName']}'),
          UiText('${item['coachName'] ?? 'ملاحظة عامة'} · ${item['branchName']}'),
          if (item['score'] != null) UiText('${item['score']}/5'),
          if (textOf(item['message']).isNotEmpty) UiText(textOf(item['message'])),
          if (textOf(item['ownerResponse']).isNotEmpty) UiText('رد الإدارة: ${item['ownerResponse']}'),
          if (item['status'] == 'Open') TextButton(onPressed: () => review(textOf(item['id'])), child: const UiText('تسجيل المراجعة')),
          if (item['status'] != 'Resolved' && item['kind'] != 'Rating') TextButton(onPressed: () => resolve(textOf(item['id'])), child: const UiText('الرد والحل')),
          if (item['status'] == 'Resolved') const UiText('تم الرد والحل') else if (item['status'] == 'Reviewed') const UiText('تمت المراجعة'),
        ])),
      ]),
    ),
  );
}
