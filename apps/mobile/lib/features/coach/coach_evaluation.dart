import 'package:academy_mobile/core/theme/ui_text.dart';
import 'package:flutter/material.dart';

import '../../core/api/api_client.dart';
import '../../core/auth/auth_controller.dart';
import '../../core/widgets/app_widgets.dart';
import '../guardian/guardian_ui.dart';

class CoachEvaluation extends StatefulWidget {
  final AuthController auth;
  final Json enrollment;
  const CoachEvaluation({
    super.key,
    required this.auth,
    required this.enrollment,
  });
  @override
  State<CoachEvaluation> createState() => _CoachEvaluationState();
}

class _CoachEvaluationState extends State<CoachEvaluation> {
  Json? evaluation;
  String? error, message;
  bool busy = false;
  final notes = TextEditingController();
  final scores = <String, double>{};

  Future<void> create() async {
    setState(() {
      busy = true;
      error = null;
    });
    try {
      final created = obj(
        await widget.auth.request(
          'POST',
          '/api/v1/evaluations',
          body: {
            'sportEnrollmentId': widget.enrollment['id'],
            'evaluationDate': DateTime.now().toIso8601String().split('T').first,
            'reportingPeriod': 'تقييم دوري',
            'generalNotes': null,
          },
          idempotencyKey: commandKey(),
        ),
      );
      final next = obj(
        await widget.auth.request(
          'GET',
          '/api/v1/evaluations/${created['id']}',
        ),
      );
      for (final item in rows(next['scores'])) {
        scores[textOf(item['criterionId'])] = ((item['score'] as num?) ?? 50)
            .toDouble();
      }
      if (mounted) setState(() => evaluation = next);
    } on ApiFailure catch (e) {
      if (mounted) setState(() => error = e.message);
    } finally {
      if (mounted) setState(() => busy = false);
    }
  }

  Future<void> save({required bool publish}) async {
    if (evaluation == null) return;
    setState(() {
      busy = true;
      error = null;
      message = null;
    });
    try {
      final updated = obj(
        await widget.auth.request(
          'PUT',
          '/api/v1/evaluations/${evaluation!['id']}',
          body: {
            'reportingPeriod': evaluation!['reportingPeriod'],
            'generalNotes': notes.text,
            'version': evaluation!['version'],
            'scores': rows(evaluation!['scores'])
                .map(
                  (item) => {
                    'criterionId': item['criterionId'],
                    'score': scores[textOf(item['criterionId'])]!.round(),
                    'notes': null,
                  },
                )
                .toList(),
          },
        ),
      );
      evaluation!['version'] = updated['version'];
      if (publish) {
        await widget.auth.request(
          'POST',
          '/api/v1/evaluations/${evaluation!['id']}/publish',
          body: {'version': evaluation!['version']},
          idempotencyKey: commandKey(),
        );
      }
      if (mounted) {
        setState(
          () => message = publish ? 'تم نشر التقييم.' : 'تم حفظ المسودة.',
        );
      }
    } on ApiFailure catch (e) {
      if (mounted) setState(() => error = e.message);
    } finally {
      if (mounted) setState(() => busy = false);
    }
  }

  @override
  Widget build(BuildContext context) => AppScaffold(
    title: 'تقييم اللاعب',
    child: evaluation == null
        ? ListView(
            padding: const EdgeInsets.all(20),
            children: [
              AppCard(
                child: UiText(
                  '${widget.enrollment['player']}\n${widget.enrollment['group']}',
                ),
              ),
              const UiText(
                'درجات كرة القدم من 0 إلى 100. يحسب الخادم التقرير والنتيجة.',
              ),
              if (error != null) ErrorNotice(error!),
              FilledButton(
                onPressed: busy ? null : create,
                child: const UiText('بدء تقييم جديد'),
              ),
            ],
          )
        : ListView(
            padding: const EdgeInsets.all(18),
            children: [
              AppCard(
                child: UiText(
                  '${evaluation!['player']} · ${evaluation!['group']}',
                ),
              ),
              for (final item in rows(evaluation!['scores'])) ...[
                UiText(
                  '${item['name']} · ${scores[textOf(item['criterionId'])]!.round()}',
                ),
                Slider(
                  min: 0,
                  max: 100,
                  divisions: 100,
                  value: scores[textOf(item['criterionId'])]!,
                  onChanged: busy
                      ? null
                      : (v) => setState(
                          () => scores[textOf(item['criterionId'])] = v,
                        ),
                ),
              ],
              TextField(
                controller: notes,
                maxLines: 3,
                decoration: InputDecoration(
                  labelText: ui(context, 'ملاحظات المدرب'),
                ),
              ),
              if (message != null)
                UiText(message!, style: const TextStyle(color: Colors.green)),
              if (error != null) ErrorNotice(error!),
              const SizedBox(height: 12),
              OutlinedButton(
                onPressed: busy ? null : () => save(publish: false),
                child: const UiText('حفظ كمسودة'),
              ),
              FilledButton(
                onPressed: busy ? null : () => save(publish: true),
                child: const UiText('نشر التقييم'),
              ),
            ],
          ),
  );
}
