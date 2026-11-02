import 'dart:convert';

import 'package:flutter/material.dart';

import '../../core/api/api_client.dart';
import '../../core/auth/auth_controller.dart';
import '../../core/theme/ui_text.dart';
import '../guardian/guardian_ui.dart';
import '../shared/feedback_state.dart';
import 'owner_followups.dart' show followUpRequestId;

class FeedbackHumanTriage extends StatefulWidget {
  final AuthController auth;
  final Json item;
  final VoidCallback saved;
  const FeedbackHumanTriage({
    super.key,
    required this.auth,
    required this.item,
    required this.saved,
  });
  @override
  State<FeedbackHumanTriage> createState() => _TriageState();
}

class _TriageState extends State<FeedbackHumanTriage> {
  late String subject = widget.item['subject'] as String? ?? 'Other',
      urgency = widget.item['urgency'] as String? ?? 'Routine';
  final note = TextEditingController();
  String error = '';
  String? signature, requestId;
  bool busy = false;
  @override
  void dispose() {
    note.dispose();
    super.dispose();
  }

  Future<void> save() async {
    if (busy) return;
    final body = {
      'subject': subject,
      'urgency': urgency,
      'version': widget.item['version'],
      'note': note.text.trim().isEmpty ? null : note.text.trim(),
    };
    final draft = jsonEncode(body);
    if (draft != signature) {
      signature = draft;
      requestId = followUpRequestId();
    }
    setState(() {
      busy = true;
      error = '';
    });
    try {
      await widget.auth.request(
        'POST',
        '/api/v1/manage/feedback/${widget.item['id']}/triage',
        body: {...body, 'requestId': requestId},
      );
      if (mounted) widget.saved();
    } catch (e) {
      if (mounted) {
        setState(
          () => error = e is ApiFailure ? e.message : 'تعذر حفظ الفرز البشري.',
        );
      }
    } finally {
      if (mounted) setState(() => busy = false);
    }
  }

  @override
  Widget build(BuildContext context) => GCard(
    child: Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        const Heading('الفرز البشري'),
        const UiText(
          'تصنيف يراجعه المالك، وليس حكمًا أو تقييمًا آليًا. لا يرسل ردًا للأسرة أو يغير راتبًا أو حضورًا.',
        ),
        if (widget.item['safety'] == true)
          const UiText(
            'تبقى في قائمة السلامة حتى لو غُير تصنيفها، إلى أن تُحل برد صريح بعد الفرز البشري.',
          ),
        const SizedBox(height: 12),
        DropdownButtonFormField<String>(
          initialValue: subject,
          isExpanded: true,
          decoration: const InputDecoration(labelText: 'موضوع المراجعة'),
          items: feedbackSubjects.entries
              .map(
                (e) => DropdownMenuItem(value: e.key, child: UiText(e.value)),
              )
              .toList(),
          onChanged: busy
              ? null
              : (v) => setState(() => subject = v ?? 'Other'),
        ),
        const SizedBox(height: 12),
        DropdownButtonFormField<String>(
          initialValue: urgency,
          isExpanded: true,
          decoration: const InputDecoration(
            labelText: 'درجة الاستعجال المراجعة',
          ),
          items: feedbackUrgencies.entries
              .map(
                (e) => DropdownMenuItem(value: e.key, child: UiText(e.value)),
              )
              .toList(),
          onChanged: busy
              ? null
              : (v) => setState(() => urgency = v ?? 'Routine'),
        ),
        const SizedBox(height: 12),
        TextField(
          controller: note,
          maxLength: 500,
          maxLines: 3,
          enabled: !busy,
          decoration: const InputDecoration(
            labelText: 'ملاحظة فرز داخلية (اختياري)',
          ),
        ),
        FilledButton(
          onPressed: busy ? null : save,
          child: const UiText('تأكيد الفرز البشري'),
        ),
        if (error.isNotEmpty) UiText(error),
      ],
    ),
  );
}
