import 'package:flutter/material.dart';

import '../../core/api/api_client.dart';
import '../../core/auth/auth_controller.dart';
import '../../core/theme/ui_text.dart';
import '../guardian/guardian_ui.dart';
import '../../core/theme/appearance.dart';
import 'owner_operations.dart';
import 'owner_feedback_detail.dart';

class OwnerFeedbackInbox extends StatefulWidget {
  final AuthController auth;
  final Map<String, String> initialFilters;
  const OwnerFeedbackInbox({
    super.key,
    required this.auth,
    this.initialFilters = const {},
  });
  @override
  State<OwnerFeedbackInbox> createState() => _OwnerFeedbackInboxState();
}

class _OwnerFeedbackInboxState extends State<OwnerFeedbackInbox> {
  late String kind = widget.initialFilters['kind'] ?? '';
  String message = '';
  int revision = 0;
  Map<String, String> get filters => {
    for (final e in widget.initialFilters.entries)
      if (e.key != 'kind') e.key: e.value,
    if (kind.isNotEmpty) 'kind': kind,
  };
  Future<void> review(String id) async {
    try {
      await widget.auth.request(
        'POST',
        '/api/v1/manage/feedback/$id/review',
        body: {},
      );
      if (mounted) {
        setState(() {
          message = 'تم تسجيل مراجعة الملاحظة.';
          revision++;
        });
      }
    } catch (error) {
      if (mounted) {
        setState(
          () => message = error is ApiFailure
              ? error.message
              : 'تعذر تحديث الملاحظة.',
        );
      }
    }
  }

  Future<void> resolve(String id) async {
    var draft = '';
    final response = await showDialog<String>(
      context: context,
      builder: (dialog) => AlertDialog(
        title: const UiText('الرد على ولي الأمر'),
        content: TextField(
          onChanged: (value) => draft = value,
          maxLength: 500,
          maxLines: 4,
          decoration: const InputDecoration(labelText: 'رد الإدارة'),
        ),
        actions: [
          TextButton(
            onPressed: () => Navigator.pop(dialog),
            child: const UiText('إلغاء'),
          ),
          FilledButton(
            onPressed: () => Navigator.pop(dialog, draft.trim()),
            child: const UiText('إرسال الرد وحل الملاحظة'),
          ),
        ],
      ),
    );
    if (response == null) return;
    if (response.length < 5) {
      if (mounted) {
        setState(() => message = 'اكتب ردًا من خمسة أحرف على الأقل.');
      }
      return;
    }
    try {
      await widget.auth.request(
        'POST',
        '/api/v1/manage/feedback/$id/resolve',
        body: {'response': response},
      );
      if (mounted) {
        setState(() {
          message = 'أُرسل الرد لولي الأمر وحُلّت الملاحظة.';
          revision++;
        });
      }
    } catch (error) {
      if (mounted) {
        setState(
          () => message = error is ApiFailure
              ? error.message
              : 'تعذر إرسال الرد.',
        );
      }
    }
  }

  @override
  Widget build(BuildContext context) => GuardianPage(
    title: 'ملاحظات أولياء الأمور',
    child: OwnerScopedRead(
      key: ValueKey('$revision/$kind'),
      auth: widget.auth,
      path:
          '/api/v1/manage/feedback${filters.isEmpty ? '' : '?${Uri(queryParameters: filters).query}'}',
      builder: (context, value) => gList([
        if (message.isNotEmpty) UiText(message),
        if (widget.initialFilters['status'] == 'Unresolved')
          UiText(
            localized(
              context,
              widget.initialFilters['overdue'] == 'true'
                  ? 'ملاحظات مفتوحة تجاوزت موعد المتابعة'
                  : 'الملاحظات المفتوحة فقط',
              widget.initialFilters['overdue'] == 'true'
                  ? 'Open notes past their follow-up date'
                  : 'Open notes only',
            ),
          ),
        DropdownButtonFormField<String>(
          initialValue: kind,
          decoration: const InputDecoration(labelText: 'نوع الملاحظة'),
          items: const [
            DropdownMenuItem(value: '', child: UiText('الكل')),
            DropdownMenuItem(
              value: 'Rating',
              child: UiText('تقييمات المدربين'),
            ),
            DropdownMenuItem(value: 'Complaint', child: UiText('الشكاوى')),
            DropdownMenuItem(value: 'Suggestion', child: UiText('الاقتراحات')),
          ],
          onChanged: (value) => setState(() => kind = value ?? ''),
        ),
        const SizedBox(height: 14),
        if (rows(value).isEmpty)
          const EmptyMessage('لا توجد ملاحظات في هذا القسم.'),
        for (final item in rows(value))
          GCard(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                UiText(
                  '${item['kind'] == 'Rating'
                      ? 'تقييم'
                      : item['kind'] == 'Complaint'
                      ? 'شكوى'
                      : 'اقتراح'} · ${item['playerName']}',
                ),
                UiText(
                  '${item['coachName'] ?? 'ملاحظة عامة'} · ${item['branchName']}',
                ),
                if (item['score'] != null) UiText('${item['score']}/5'),
                TextButton(
                  onPressed: () => openPage(
                    context,
                    OwnerFeedbackDetail(
                      auth: widget.auth,
                      id: textOf(item['id']),
                    ),
                  ),
                  child: const UiText('فتح الملاحظة'),
                ),
                if (textOf(item['message']).isNotEmpty)
                  UiText(textOf(item['message'])),
                if ((item['ownerResponse'] is String &&
                    (item['ownerResponse'] as String).isNotEmpty))
                  UiText('رد الإدارة: ${item['ownerResponse']}'),
                if (item['status'] == 'Open')
                  TextButton(
                    onPressed: () => review(textOf(item['id'])),
                    child: const UiText('تسجيل المراجعة'),
                  ),
                if (item['status'] != 'Resolved' && item['kind'] != 'Rating')
                  TextButton(
                    onPressed: () => resolve(textOf(item['id'])),
                    child: const UiText('الرد والحل'),
                  ),
                if (item['status'] == 'Resolved')
                  const UiText('تم الرد والحل')
                else if (item['status'] == 'Reviewed')
                  const UiText('تمت المراجعة'),
              ],
            ),
          ),
      ]),
    ),
  );
}
