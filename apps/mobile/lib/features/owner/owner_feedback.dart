import 'package:flutter/material.dart';

import '../../core/api/api_client.dart';
import '../../core/auth/auth_controller.dart';
import '../../core/theme/ui_text.dart';
import '../guardian/guardian_ui.dart';
import '../../core/theme/appearance.dart';
import 'owner_operations.dart';
import 'owner_feedback_detail.dart';
import '../shared/feedback_state.dart';

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
  late bool safetyOnly = widget.initialFilters['safetyOnly'] == 'true';
  late String subject = widget.initialFilters['subject'] ?? '';
  late String urgency = widget.initialFilters['urgency'] ?? '';
  bool cleared = false, filtersExpanded = false, busy = false;
  String? get contextKey => widget.auth.session?.selected == null
      ? null
      : '${widget.auth.session!.selected!.id}:${widget.auth.session!.selected!.contextRevision}';
  late final identity = contextKey;
  bool get current => mounted && identity != null && identity == contextKey;
  String message = '';
  int revision = 0;
  Map<String, String> get filters => {
    if (!cleared)
      for (final e in widget.initialFilters.entries)
        if (!['kind', 'safetyOnly', 'subject', 'urgency'].contains(e.key))
          e.key: e.value,
    if (kind.isNotEmpty) 'kind': kind,
    if (safetyOnly) 'safetyOnly': 'true',
    if (subject.isNotEmpty) 'subject': subject,
    if (urgency.isNotEmpty) 'urgency': urgency,
  };
  Future<void> review(String id) async {
    if (busy || !current) return;
    setState(() => busy = true);
    try {
      await widget.auth.request(
        'POST',
        '/api/v1/manage/feedback/$id/review',
        body: {},
      );
      if (current) {
        setState(() {
          message = 'تم تسجيل مراجعة الملاحظة.';
          revision++;
        });
      }
    } catch (error) {
      if (current) {
        setState(
          () => message = error is ApiFailure
              ? error.message
              : 'تعذر تحديث الملاحظة.',
        );
      }
    } finally {
      if (current) setState(() => busy = false);
    }
  }

  Future<void> resolve(String id) async {
    if (busy || !current) return;
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
    if (response == null || !current || busy) return;
    if (response.length < 5) {
      if (mounted) {
        setState(() => message = 'اكتب ردًا من خمسة أحرف على الأقل.');
      }
      return;
    }
    setState(() => busy = true);
    try {
      await widget.auth.request(
        'POST',
        '/api/v1/manage/feedback/$id/resolve',
        body: {'response': response},
      );
      if (current) {
        setState(() {
          message = 'أُرسل الرد لولي الأمر وحُلّت الملاحظة.';
          revision++;
        });
      }
    } catch (error) {
      if (current) {
        setState(
          () => message = error is ApiFailure
              ? error.message
              : 'تعذر إرسال الرد.',
        );
      }
    } finally {
      if (current) setState(() => busy = false);
    }
  }

  @override
  Widget build(BuildContext context) => GuardianPage(
    title: 'ملاحظات أولياء الأمور',
    child: OwnerScopedRead(
      key: ValueKey('$revision/${filters.toString()}'),
      auth: widget.auth,
      path:
          '/api/v1/manage/feedback${filters.isEmpty ? '' : '?${Uri(queryParameters: filters).query}'}',
      builder: (context, value) => gList([
        if (message.isNotEmpty) UiText(message),
        if (!cleared && widget.initialFilters['status'] == 'Unresolved')
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
        if (safetyOnly)
          const Padding(
            padding: EdgeInsets.only(bottom: 12),
            child: UiText('المعروض: بلاغات السلامة فقط'),
          ),
        GCard(
          child: ExpansionTile(
            tilePadding: EdgeInsets.zero,
            childrenPadding: const EdgeInsets.only(top: 12),
            initiallyExpanded: filtersExpanded,
            onExpansionChanged: (value) => filtersExpanded = value,
            leading: const Icon(Icons.tune_rounded),
            title: UiText(
              filters.isEmpty
                  ? 'تصفية الملاحظات'
                  : 'تصفية الملاحظات · ${filters.length}',
            ),
            children: [
              DropdownButtonFormField<String>(
                initialValue: kind,
                decoration: const InputDecoration(labelText: 'نوع الملاحظة'),
                items: const [
                  DropdownMenuItem(value: '', child: UiText('الكل')),
                  DropdownMenuItem(
                    value: 'Rating',
                    child: UiText('تقييمات المدربين'),
                  ),
                  DropdownMenuItem(
                    value: 'Complaint',
                    child: UiText('الشكاوى'),
                  ),
                  DropdownMenuItem(
                    value: 'Suggestion',
                    child: UiText('الاقتراحات'),
                  ),
                  DropdownMenuItem(
                    value: 'Request',
                    child: UiText('طلبات الإدارة'),
                  ),
                ],
                onChanged: (value) => setState(() => kind = value ?? ''),
              ),
              const SizedBox(height: 14),
              SwitchListTile(
                title: const UiText('بلاغات السلامة الأصلية أو المراجعة'),
                value: safetyOnly,
                onChanged: (v) => setState(() => safetyOnly = v),
              ),
              DropdownButtonFormField<String>(
                key: ValueKey('subject-$subject'),
                initialValue: subject,
                isExpanded: true,
                decoration: const InputDecoration(labelText: 'الموضوع'),
                items: [
                  const DropdownMenuItem(
                    value: '',
                    child: UiText('كل الموضوعات'),
                  ),
                  ...feedbackSubjects.entries.map(
                    (e) =>
                        DropdownMenuItem(value: e.key, child: UiText(e.value)),
                  ),
                ],
                onChanged: (v) => setState(() => subject = v ?? ''),
              ),
              const SizedBox(height: 12),
              DropdownButtonFormField<String>(
                key: ValueKey('urgency-$urgency'),
                initialValue: urgency,
                isExpanded: true,
                decoration: const InputDecoration(labelText: 'الاستعجال'),
                items: [
                  const DropdownMenuItem(
                    value: '',
                    child: UiText('كل الدرجات'),
                  ),
                  ...feedbackUrgencies.entries.map(
                    (e) =>
                        DropdownMenuItem(value: e.key, child: UiText(e.value)),
                  ),
                ],
                onChanged: (v) => setState(() => urgency = v ?? ''),
              ),
            ],
          ),
        ),
        if (filters.isNotEmpty)
          TextButton(
            onPressed: () => setState(() {
              cleared = true;
              kind = '';
              subject = '';
              urgency = '';
              safetyOnly = false;
              revision++;
            }),
            child: const UiText('مسح الفلاتر'),
          ),
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
                      : item['kind'] == 'Request'
                      ? 'طلب للإدارة'
                      : 'اقتراح'} · ${item['playerName']}',
                ),
                UiText(
                  '${item['coachName'] ?? 'ملاحظة عامة'} · ${item['branchName']}',
                ),
                if (item['score'] != null) UiText('${item['score']}/5'),
                if (item['kind'] != 'Rating')
                  UiText(
                    '${feedbackSubjects[item['subject']] ?? 'لم يصنف وقتها'} · ${feedbackUrgencies[item['urgency']] ?? 'غير محددة'}',
                  ),
                if (item['safety'] == true)
                  UiText(
                    item['triagedAtUtc'] == null
                        ? 'بلاغ سلامة — يحتاج فرزًا بشريًا'
                        : 'بلاغ سلامة — متابعة بشرية',
                  ),
                TextButton(
                  onPressed: busy
                      ? null
                      : () async {
                          await openPage(
                            context,
                            OwnerFeedbackDetail(
                              auth: widget.auth,
                              id: textOf(item['id']),
                            ),
                          );
                          if (current) setState(() => revision++);
                        },
                  child: const UiText('فتح الملاحظة'),
                ),
                if (textOf(item['message']).isNotEmpty)
                  UiText(textOf(item['message'])),
                if ((item['ownerResponse'] is String &&
                    (item['ownerResponse'] as String).isNotEmpty))
                  UiText('رد الإدارة: ${item['ownerResponse']}'),
                if (item['status'] == 'Open')
                  TextButton(
                    onPressed: busy ? null : () => review(textOf(item['id'])),
                    child: const UiText('تسجيل المراجعة'),
                  ),
                if (item['status'] != 'Resolved' &&
                    item['status'] != 'Closed' &&
                    (item['safety'] != true || item['triagedAtUtc'] != null) &&
                    item['kind'] != 'Rating')
                  TextButton(
                    onPressed: busy ? null : () => resolve(textOf(item['id'])),
                    child: const UiText('الرد والحل'),
                  ),
                UiText(feedbackStates[item['status']] ?? 'غير متاحة'),
              ],
            ),
          ),
      ]),
    ),
  );
}
