import 'package:academy_mobile/core/theme/ui_text.dart';
import 'package:flutter/material.dart';

import '../../core/api/api_client.dart';
import '../../core/auth/auth_controller.dart';
import '../../core/widgets/app_widgets.dart';
import '../guardian/guardian_ui.dart';
import 'coach_evaluation.dart';

class CoachAttendance extends StatefulWidget {
  final AuthController auth;
  final String sessionId;
  const CoachAttendance({
    super.key,
    required this.auth,
    required this.sessionId,
  });
  @override
  State<CoachAttendance> createState() => _CoachAttendanceState();
}

class _CoachAttendanceState extends State<CoachAttendance> {
  Json? data;
  final statuses = <String, String>{};
  String? error, message;
  bool busy = false;

  @override
  void initState() {
    super.initState();
    load();
  }

  Future<bool> load() async {
    try {
      final next = obj(
        await widget.auth.request(
          'GET',
          '/api/v1/attendance/sessions/${widget.sessionId}/players',
        ),
      );
      statuses.clear();
      for (final player in rows(next['players'])) {
        statuses[textOf(player['sportEnrollmentId'])] = textOf(
          player['attendanceStatus'],
        );
      }
      if (mounted) {
        setState(() {
          data = next;
          error = null;
        });
      }
      return true;
    } on ApiFailure catch (e) {
      if (mounted) setState(() => error = e.message);
      return false;
    }
  }

  Future<void> save() async {
    if (data?['canManageAttendance'] != true || busy) return;
    setState(() {
      busy = true;
      error = null;
      message = null;
    });
    try {
      await widget.auth.request(
        'PUT',
        '/api/v1/attendance/sessions/${widget.sessionId}/players',
        body: {
          'items': statuses.entries
              .map(
                (e) => {
                  'sportEnrollmentId': e.key,
                  'status': e.value,
                  'expectedVersion': rows(data!['players']).firstWhere(
                    (p) => textOf(p['sportEnrollmentId']) == e.key,
                  )['version'],
                },
              )
              .toList(),
        },
      );
      final synchronized = await load();
      if (mounted && synchronized) {
        setState(() => message = 'تم حفظ الحضور ويمكن تصحيحه بأمان.');
      }
    } on ApiFailure catch (e) {
      if (mounted) setState(() => error = e.message);
    } finally {
      if (mounted) setState(() => busy = false);
    }
  }

  @override
  Widget build(BuildContext context) => AppScaffold(
    title: 'تسجيل الحضور',
    child: data == null
        ? Center(
            child: error == null
                ? const CircularProgressIndicator()
                : ErrorNotice(error!),
          )
        : ListView(
            padding: const EdgeInsets.all(18),
            children: [
              AppCard(
                child: UiText(
                  '${obj(data!['session'])['group']} · ${day(obj(data!['session'])['sessionDate'])} · ${obj(data!['session'])['startTime']}',
                ),
              ),
              if (data!['lastSynchronizedAtUtc'] != null)
                UiText('آخر مزامنة مؤكدة: ${data!['lastSynchronizedAtUtc']}'),
              if (message != null)
                UiText(message!, style: const TextStyle(color: Colors.green)),
              if (error != null) ErrorNotice(error!),
              if (data!['canManageAttendance'] != true)
                const UiText(
                  'الحضور للعرض فقط؛ يتولى الإداري المصرح له التسجيل.',
                ),
              for (final player in rows(data!['players']))
                Card(
                  child: Padding(
                    padding: const EdgeInsets.all(12),
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        UiText(
                          textOf(player['arabicName']),
                          style: const TextStyle(fontWeight: FontWeight.bold),
                        ),
                        UiText(textOf(player['playerCode'])),
                        if (data!['canEvaluate'] == true)
                          OutlinedButton.icon(
                            icon: const Icon(Icons.assessment_outlined),
                            label: const UiText('تقييم اللاعب'),
                            onPressed: () => Navigator.of(context).push(
                              MaterialPageRoute<void>(
                                builder: (_) => CoachEvaluation(
                                  auth: widget.auth,
                                  enrollment: {
                                    'id': player['sportEnrollmentId'],
                                    'player': player['arabicName'],
                                    'group': obj(data!['session'])['group'],
                                    'sessionId': widget.sessionId,
                                    'evaluationDate': obj(
                                      data!['session'],
                                    )['sessionDate'],
                                  },
                                ),
                              ),
                            ),
                          ),
                        if (data!['canManageAttendance'] != true)
                          Chip(label: UiText(label(player['attendanceStatus'])))
                        else
                          Wrap(
                            spacing: 6,
                            runSpacing: 6,
                            children: [
                              for (final state in const {
                                'Present': 'حاضر',
                                'Late': 'متأخر',
                                'Absent': 'غائب',
                                'Excused': 'معتذر',
                                'NotRecorded': 'لم يُسجل',
                              }.entries)
                                ChoiceChip(
                                  label: UiText(state.value),
                                  selected:
                                      statuses[textOf(
                                        player['sportEnrollmentId'],
                                      )] ==
                                      state.key,
                                  onSelected: busy
                                      ? null
                                      : (_) => setState(
                                          () =>
                                              statuses[textOf(
                                                    player['sportEnrollmentId'],
                                                  )] =
                                                  state.key,
                                        ),
                                ),
                            ],
                          ),
                        if (player['warning'] != null)
                          UiText(
                            textOf(player['warning']),
                            style: TextStyle(
                              color: Theme.of(context).colorScheme.error,
                            ),
                          ),
                      ],
                    ),
                  ),
                ),
              if (data!['canManageAttendance'] == true)
                FilledButton(
                  onPressed: busy ? null : save,
                  child: UiText(busy ? 'جارٍ الحفظ…' : 'حفظ الحضور'),
                ),
            ],
          ),
  );
}
