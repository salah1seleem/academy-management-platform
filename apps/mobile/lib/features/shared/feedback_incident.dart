import 'dart:convert';

import 'package:flutter/material.dart';

import '../../core/auth/auth_controller.dart';
import '../../core/theme/ui_text.dart';
import '../guardian/guardian_ui.dart';

class FeedbackIncidentPicker extends StatelessWidget {
  final AuthController auth;
  final String playerId, day;
  final Json? selected;
  final ValueChanged<Json?> changed;
  const FeedbackIncidentPicker({
    super.key,
    required this.auth,
    required this.playerId,
    required this.day,
    required this.selected,
    required this.changed,
  });
  @override
  Widget build(BuildContext context) => RemoteBody(
    auth: auth,
    path:
        '/api/v1/guardian/children/$playerId/feedback-incidents?incidentOn=$day',
    builder: (context, value) {
      final data = obj(value), sessions = rows(data['sessions']);
      return Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          DropdownButtonFormField<String>(
            key: ValueKey('$day/${selected?['id']}'),
            isExpanded: true,
            initialValue: selected?['id'] as String? ?? '',
            decoration: const InputDecoration(
              labelText: 'الحصة المرتبطة (اختياري)',
            ),
            items: [
              const DropdownMenuItem(
                value: '',
                child: UiText('لا أعرف / دون حصة'),
              ),
              for (final s in sessions)
                DropdownMenuItem(
                  value: textOf(s['id']),
                  child: UiText(
                    '${s['groupName']} · ${textOf(s['startTime']).substring(0, 5)}–${textOf(s['endTime']).substring(0, 5)}',
                  ),
                ),
            ],
            onChanged: (v) =>
                changed(sessions.where((s) => s['id'] == v).firstOrNull),
          ),
          UiText(textOf(data['notice'])),
          if (sessions.isEmpty)
            const UiText('لا توجد حصة متاحة مرتبطة بطفلك في هذا اليوم.'),
          if (data['hasMore'] == true)
            const UiText('تظهر أول 50 حصة؛ راجع الإدارة لبقية حصص اليوم.'),
          if (selected != null && rows(selected!['coaches']).isEmpty)
            const UiText(
              'لا يوجد دليل مدرب مؤرخ لهذه الحصة؛ لن نخمن المدرب الحالي.',
            ),
        ],
      );
    },
  );
}

class FeedbackIncidentEvidence extends StatelessWidget {
  final String? evidence;
  const FeedbackIncidentEvidence({super.key, required this.evidence});
  @override
  Widget build(BuildContext context) {
    if (evidence == null) return const SizedBox.shrink();
    try {
      final s = obj(jsonDecode(evidence!));
      return GCard(
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            const Heading('الحصة المرتبطة وقت تقديم الملاحظة'),
            UiText('${s['groupName']} · ${s['date']}'),
            UiText(
              '${textOf(s['startTime']).split(':').take(2).join(':')}–${textOf(s['endTime']).split(':').take(2).join(':')}',
              textDirection: TextDirection.ltr,
            ),
            for (final c in rows(s['coaches']))
              UiText(
                '${c['name']} · ${c['source'] == 'OccurrenceDecision' ? 'قرار مدرب للحصة' : 'سجل حضور الحصة'}${s['selectedCoachId'] == c['id'] ? ' · اختاره مقدم الملاحظة' : ''}',
              ),
            if (s['selectedCoachId'] == null)
              const UiText('لم يحدد مقدم الملاحظة مدربًا.'),
            const UiText(
              'نسخة وقت التقديم من سجلات النظام، وليست إثبات حضور فعلي أو إدانة. لا يعاد بناؤها من إسناد المجموعة الحالي.',
            ),
          ],
        ),
      );
    } catch (_) {
      return const UiText('تعذر عرض سياق الحصة المحفوظ؛ راجع الإدارة.');
    }
  }
}
