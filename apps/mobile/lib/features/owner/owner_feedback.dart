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
          if (item['status'] == 'Open') TextButton(onPressed: () => review(textOf(item['id'])), child: const UiText('تسجيل المراجعة'))
          else const UiText('تمت المراجعة'),
        ])),
      ]),
    ),
  );
}
