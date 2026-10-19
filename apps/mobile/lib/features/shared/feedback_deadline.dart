import 'dart:convert';

import 'package:flutter/material.dart';

import '../../core/theme/ui_text.dart';

class FeedbackDeadlineEvidence extends StatelessWidget {
  const FeedbackDeadlineEvidence({super.key, this.evidence});
  final String? evidence;
  @override
  Widget build(BuildContext context) {
    if (evidence == null) {
      return const UiText(
        'موعد يدوي إن حُدد؛ لم تحفظ سياسة تلقائية عند التقديم. لا تظهر هوية مقدم الملاحظة للمدرب موضوعها.',
      );
    }
    try {
      final p = jsonDecode(evidence!) as Map<String, dynamic>;
      return UiText(
        'سياسة المتابعة وقت التقديم · الإصدار ${p['revision']} · ${p['days']} أيام تقويمية من ${p['receivedOn']} إلى ${p['dueOn']} · ${p['timeZone']}. ليست وعد حل تلقائيًا؛ انتظار المعلومات وإعادة الفتح لا يعيدان المهلة.',
      );
    } catch (_) {
      return const UiText('تعذر عرض دليل المهلة؛ لم تُعدّل البيانات المحفوظة.');
    }
  }
}
