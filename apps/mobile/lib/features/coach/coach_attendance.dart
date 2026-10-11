import 'package:academy_mobile/core/theme/ui_text.dart';
import 'package:flutter/material.dart';

import '../../core/api/api_client.dart';
import '../../core/auth/auth_controller.dart';
import '../../core/widgets/app_widgets.dart';
import '../guardian/guardian_ui.dart';

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

  Future<void> load() async {
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
    } on ApiFailure catch (e) {
      if (mounted) setState(() => error = e.message);
    }
  }

  Future<void> save() async {
    if (data?['canManageAttendance'] != true) return;
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
              .map((e) => {'sportEnrollmentId': e.key, 'status': e.value})
              .toList(),
        },
      );
      if (mounted) {
        setState(() => message = 'تم حفظ الحضور ويمكن تصحيحه بأمان.');
      }
      await load();
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
                        SegmentedButton<String>(
                          segments: const [
                            ButtonSegment(
                              value: 'Present',
                              label: UiText('حاضر'),
                            ),
                            ButtonSegment(
                              value: 'Absent',
                              label: UiText('غائب'),
                            ),
                            ButtonSegment(
                              value: 'NotRecorded',
                              label: UiText('لم يُسجل'),
                            ),
                          ],
                          selected: {
                            statuses[textOf(player['sportEnrollmentId'])]!,
                          },
                          onSelectionChanged:
                              data!['canManageAttendance'] != true
                              ? null
                              : (v) => setState(
                                  () =>
                                      statuses[textOf(
                                            player['sportEnrollmentId'],
                                          )] =
                                          v.single,
                                ),
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
