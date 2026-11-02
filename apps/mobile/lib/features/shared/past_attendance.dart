import 'package:flutter/material.dart';

import '../../core/api/api_client.dart';
import '../../core/auth/auth_controller.dart';
import '../../core/theme/appearance.dart';
import '../../core/theme/ui_text.dart';
import '../../core/widgets/app_widgets.dart';
import '../guardian/guardian_ui.dart';

class PastAttendanceRequestPage extends StatefulWidget {
  final AuthController auth;
  final String sessionId;
  final List<Json> players;
  const PastAttendanceRequestPage({
    super.key,
    required this.auth,
    required this.sessionId,
    required this.players,
  });
  @override
  State<PastAttendanceRequestPage> createState() =>
      _PastAttendanceRequestPageState();
}

class _PastAttendanceRequestPageState extends State<PastAttendanceRequestPage> {
  final reason = TextEditingController();
  final states = <String, String>{};
  String intent = commandKey();
  bool busy = false;
  String? error;
  @override
  void initState() {
    super.initState();
    for (final p in widget.players) {
      states[textOf(p['sportEnrollmentId'])] = textOf(p['attendanceStatus']);
    }
  }

  @override
  void dispose() {
    reason.dispose();
    super.dispose();
  }

  List<Json> get changed => widget.players
      .where(
        (p) =>
            states[textOf(p['sportEnrollmentId'])] !=
            textOf(p['attendanceStatus']),
      )
      .toList();
  Future<void> submit() async {
    if (busy ||
        changed.isEmpty ||
        reason.text.trim().isEmpty ||
        changed.any((p) => p['version'] == null)) {
      return;
    }
    setState(() {
      busy = true;
      error = null;
    });
    try {
      await widget.auth.request(
        'POST',
        '/api/v1/attendance/sessions/${widget.sessionId}/corrections',
        idempotencyKey: intent,
        body: {
          'kind': 'Players',
          'reason': reason.text.trim(),
          'items': changed
              .map(
                (p) => {
                  'subjectId': p['sportEnrollmentId'],
                  'status': states[textOf(p['sportEnrollmentId'])],
                  'expectedVersion': p['version'],
                },
              )
              .toList(),
        },
      );
      if (mounted) Navigator.of(context).pop(true);
    } on ApiFailure catch (e) {
      if (mounted) setState(() => error = e.message);
    } finally {
      if (mounted) setState(() => busy = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    final labels = <String, String>{
      'Present': localized(context, 'حاضر', 'Present'),
      'Late': localized(context, 'متأخر', 'Late'),
      'Absent': localized(context, 'غائب', 'Absent'),
      'Excused': localized(context, 'معتذر', 'Excused'),
      'NotRecorded': localized(context, 'لم يُسجل', 'Not recorded'),
    };
    return AppScaffold(
      title: localized(
        context,
        'طلب تصحيح حضور سابق',
        'Past attendance correction',
      ),
      child: ListView(
        padding: const EdgeInsets.all(18),
        children: [
          AppCard(
            child: UiText(
              localized(
                context,
                'الحالة السابقة محفوظة. يعتمد المالك طلبك قبل تغيير الحضور ورصيد الحصص؛ لا حفظ مباشر أو تحصيل تلقائي.',
                'The previous record is preserved. Owner approval is required before changing attendance or session balance.',
              ),
            ),
          ),
          if (error != null) ErrorNotice(error!),
          for (final p in widget.players)
            AppCard(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  UiText(
                    textOf(p['arabicName']),
                    style: Theme.of(context).textTheme.titleMedium,
                  ),
                  UiText(
                    '${localized(context, 'المسجل', 'Recorded')}: ${labels[p['attendanceStatus']]}',
                  ),
                  DropdownButtonFormField<String>(
                    initialValue: states[textOf(p['sportEnrollmentId'])],
                    isExpanded: true,
                    decoration: InputDecoration(
                      labelText: localized(
                        context,
                        'الحالة المقترحة',
                        'Proposed status',
                      ),
                    ),
                    items: labels.entries
                        .map(
                          (e) => DropdownMenuItem(
                            value: e.key,
                            child: UiText(e.value),
                          ),
                        )
                        .toList(),
                    onChanged: busy
                        ? null
                        : (v) => setState(() {
                            states[textOf(p['sportEnrollmentId'])] = v!;
                            intent = commandKey();
                          }),
                  ),
                ],
              ),
            ),
          TextField(
            controller: reason,
            minLines: 2,
            maxLines: 4,
            maxLength: 500,
            enabled: !busy,
            decoration: InputDecoration(
              labelText: localized(
                context,
                'سبب تصحيح اليوم السابق',
                'Reason for correcting a closed day',
              ),
            ),
            onChanged: (_) => setState(() => intent = commandKey()),
          ),
          UiText(
            '${localized(context, 'التغييرات المقترحة', 'Proposed changes')}: ${changed.length}',
          ),
          for (final p in changed)
            UiText(
              '${p['arabicName']}: ${labels[p['attendanceStatus']]} ← ${labels[states[textOf(p['sportEnrollmentId'])]]}',
            ),
          FilledButton(
            onPressed:
                busy ||
                    changed.isEmpty ||
                    reason.text.trim().isEmpty ||
                    changed.any((p) => p['version'] == null)
                ? null
                : submit,
            child: UiText(
              localized(
                context,
                busy ? 'جارٍ الإرسال…' : 'إرسال الطلب للمالك',
                busy ? 'Sending…' : 'Request owner approval',
              ),
            ),
          ),
        ],
      ),
    );
  }
}
