import 'package:flutter/material.dart';

import '../../core/api/api_client.dart';
import '../../core/auth/auth_controller.dart';
import '../../core/theme/ui_text.dart';
import 'guardian_ui.dart';

class CoachFeedbackPage extends StatefulWidget {
  final AuthController auth;
  final String playerId;
  const CoachFeedbackPage({
    super.key,
    required this.auth,
    required this.playerId,
  });
  @override
  State<CoachFeedbackPage> createState() => _CoachFeedbackPageState();
}

class _CoachFeedbackPageState extends State<CoachFeedbackPage> {
  final form = GlobalKey<FormState>();
  final message = TextEditingController();
  String? groupId, coachId;
  String kind = 'Rating', result = '', requestKey = commandKey();
  int score = 5, revision = 0;
  bool busy = false;
  @override
  void dispose() {
    message.dispose();
    super.dispose();
  }

  Future<void> submit() async {
    if (busy || !form.currentState!.validate()) return;
    setState(() {
      busy = true;
      result = '';
    });
    try {
      await widget.auth.request(
        'POST',
        '/api/v1/guardian/children/${widget.playerId}/feedback',
        body: {
          'trainingGroupId': groupId,
          'coachMembershipId': coachId,
          'kind': kind,
          'score': kind == 'Rating' ? score : null,
          'message': message.text.trim(),
        },
        idempotencyKey: requestKey,
      );
      if (mounted) {
        setState(() {
          result = 'وصلت ملاحظتك لإدارة الأكاديمية.';
          requestKey = commandKey();
          message.clear();
          revision++;
        });
      }
    } catch (error) {
      if (mounted) {
        setState(
          () => result = error is ApiFailure
              ? error.message
              : 'تعذر إرسال الملاحظة. حاول مرة أخرى.',
        );
      }
    } finally {
      if (mounted) setState(() => busy = false);
    }
  }

  @override
  Widget build(BuildContext context) => GuardianPage(
    title: 'رأيك في التدريب',
    child: RemoteBody(
      key: ValueKey(revision),
      auth: widget.auth,
      path: '/api/v1/guardian/children/${widget.playerId}/feedback-options',
      builder: (context, value) {
        final data = obj(value), groups = rows(data['groups']);
        final selected = groups
            .where((g) => textOf(g['id']) == groupId)
            .toList();
        final coaches = selected.isEmpty
            ? <Json>[]
            : rows(selected.first['coaches']);
        return gList([
          if (result.isNotEmpty) UiText(result),
          const UiText(
            'تقييم المدرب مرة واحدة شهريًا لكل طفل. الشكاوى والاقتراحات تصل للمالك فقط ولا تظهر للمدرب.',
          ),
          if (groups.isEmpty)
            const EmptyMessage('لا توجد مجموعة نشطة لهذا الطفل حاليًا.'),
          if (groups.isNotEmpty)
            GCard(
              child: Form(
                key: form,
                child: Column(
                  children: [
                    DropdownButtonFormField<String>(
                      initialValue: groupId,
                      isExpanded: true,
                      decoration: const InputDecoration(labelText: 'المجموعة'),
                      items: [
                        for (final group in groups)
                          DropdownMenuItem(
                            value: textOf(group['id']),
                            child: UiText(
                              '${group['name']} · ${group['sport']}',
                            ),
                          ),
                      ],
                      onChanged: busy
                          ? null
                          : (value) => setState(() {
                              groupId = value;
                              coachId = null;
                              requestKey = commandKey();
                            }),
                      validator: (value) =>
                          value == null ? 'اختر مجموعة الطفل.' : null,
                    ),
                    const SizedBox(height: 12),
                    DropdownButtonFormField<String>(
                      initialValue: kind,
                      decoration: const InputDecoration(
                        labelText: 'نوع الملاحظة',
                      ),
                      items: const [
                        DropdownMenuItem(
                          value: 'Rating',
                          child: UiText('تقييم المدرب'),
                        ),
                        DropdownMenuItem(
                          value: 'Complaint',
                          child: UiText('شكوى'),
                        ),
                        DropdownMenuItem(
                          value: 'Suggestion',
                          child: UiText('اقتراح'),
                        ),
                      ],
                      onChanged: busy
                          ? null
                          : (value) => setState(() {
                              kind = value ?? 'Rating';
                              coachId = null;
                              requestKey = commandKey();
                            }),
                    ),
                    const SizedBox(height: 12),
                    DropdownButtonFormField<String>(
                      key: ValueKey('$groupId/$kind'),
                      initialValue: coachId,
                      isExpanded: true,
                      decoration: InputDecoration(
                        labelText: kind == 'Rating'
                            ? 'المدرب'
                            : 'المدرب المعني (اختياري)',
                      ),
                      items: [
                        if (kind != 'Rating')
                          const DropdownMenuItem(
                            value: '',
                            child: UiText('ملاحظة عامة'),
                          ),
                        for (final coach in coaches)
                          DropdownMenuItem(
                            value: textOf(coach['id']),
                            child: UiText(textOf(coach['name'])),
                          ),
                      ],
                      onChanged: busy
                          ? null
                          : (value) => setState(() {
                              coachId = value == '' ? null : value;
                              requestKey = commandKey();
                            }),
                      validator: (value) =>
                          kind == 'Rating' && (value == null || value.isEmpty)
                          ? 'اختر المدرب.'
                          : null,
                    ),
                    if (kind == 'Rating') ...[
                      const SizedBox(height: 12),
                      DropdownButtonFormField<int>(
                        initialValue: score,
                        decoration: const InputDecoration(
                          labelText: 'تقييم ولي الأمر',
                        ),
                        items: [
                          for (var i = 1; i <= 5; i++)
                            DropdownMenuItem(
                              value: i,
                              child: UiText('$i من 5'),
                            ),
                        ],
                        onChanged: busy
                            ? null
                            : (value) => setState(() {
                                score = value ?? 5;
                                requestKey = commandKey();
                              }),
                      ),
                    ],
                    const SizedBox(height: 12),
                    TextFormField(
                      controller: message,
                      maxLength: 500,
                      maxLines: 3,
                      enabled: !busy,
                      decoration: InputDecoration(
                        labelText: kind == 'Rating'
                            ? 'تعليق (اختياري)'
                            : 'اكتب الملاحظة',
                      ),
                      onChanged: (_) => requestKey = commandKey(),
                      validator: (value) =>
                          kind != 'Rating' && (value?.trim().length ?? 0) < 5
                          ? 'اكتب خمسة أحرف على الأقل.'
                          : null,
                    ),
                    FilledButton(
                      onPressed: busy ? null : submit,
                      child: const UiText('إرسال للإدارة'),
                    ),
                  ],
                ),
              ),
            ),
          if (rows(data['mine']).isNotEmpty) ...[
            const Heading('ملاحظاتي السابقة'),
            for (final item in rows(data['mine']))
              GCard(
                child: ListTile(
                  title: UiText(switch (item['kind']) {
                    'Rating' => 'تقييم المدرب · ${item['score']}/5',
                    'Complaint' => 'شكوى',
                    _ => 'اقتراح',
                  }),
                  subtitle: UiText(
                    '${item['message']?.toString().isEmpty == true ? 'بدون تعليق' : item['message']} · ${item['status'] == 'Resolved'
                        ? 'تم الرد والحل'
                        : item['status'] == 'Reviewed'
                        ? 'راجعتها الإدارة'
                        : 'بانتظار المراجعة'}${(item['ownerResponse'] is String && (item['ownerResponse'] as String).isNotEmpty) ? '\nرد الإدارة: ${item['ownerResponse']}' : ''}',
                  ),
                ),
              ),
          ],
        ]);
      },
    ),
  );
}
