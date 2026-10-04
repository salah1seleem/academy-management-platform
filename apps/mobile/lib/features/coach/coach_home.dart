import 'package:flutter/material.dart';

import '../../core/api/api_client.dart';
import '../../core/auth/auth_controller.dart';
import '../../core/widgets/app_widgets.dart';
import '../guardian/guardian_ui.dart';
import 'coach_attendance.dart';
import 'coach_evaluation.dart';

class CoachHome extends StatelessWidget {
  final AuthController auth;
  final Widget accountPage;
  const CoachHome({super.key, required this.auth, required this.accountPage});

  @override
  Widget build(BuildContext context) => AppScaffold(
    title: 'الرئيسية · المدرب',
    actions: [
      IconButton(
        tooltip: 'الحساب',
        onPressed: () => openPage(context, accountPage),
        icon: const Icon(Icons.person_outline),
      ),
    ],
    child: FutureBuilder<List<Object?>>(
      future: Future.wait([
        auth.request('GET', '/api/v1/coach/groups'),
        auth.request('GET', '/api/v1/attendance/sessions'),
        auth.request('GET', '/api/v1/evaluations/options'),
      ]),
      builder: (context, snapshot) {
        if (snapshot.hasError) {
          return ErrorNotice(
            snapshot.error is ApiFailure
                ? (snapshot.error as ApiFailure).message
                : 'تعذر تحميل بيانات المدرب.',
          );
        }
        if (!snapshot.hasData) {
          return const Center(child: CircularProgressIndicator());
        }
        final groups = rows(snapshot.data![0]);
        final sessions = rows(snapshot.data![1]);
        final options = obj(snapshot.data![2]);
        final enrollments = rows(options['enrollments']);
        return ListView(
          padding: const EdgeInsets.all(18),
          children: [
            AppCard(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Text(
                    'أهلًا، ${auth.session!.displayName}',
                    style: Theme.of(context).textTheme.headlineSmall,
                  ),
                  const Text('حصصك ومجموعاتك فقط — لا تظهر بيانات مالية.'),
                ],
              ),
            ),
            const SizedBox(height: 18),
            Text(
              'حصص اليوم والقادمة',
              style: Theme.of(context).textTheme.titleLarge,
            ),
            const SizedBox(height: 8),
            if (sessions.isEmpty) const Text('لا توجد حصص ضمن النطاق الحالي.'),
            for (final session in sessions.take(6))
              Card(
                child: ListTile(
                  title: Text(textOf(session['group'])),
                  subtitle: Text(
                    '${day(session['sessionDate'])} · ${session['startTime']} — ${session['branch']}',
                  ),
                  trailing: Text(
                    '${session['recorded']}/${session['playerCount']}',
                  ),
                  onTap: () => openPage(
                    context,
                    CoachAttendance(
                      auth: auth,
                      sessionId: textOf(session['id']),
                    ),
                  ),
                ),
              ),
            const SizedBox(height: 18),
            Text(
              'المجموعات المسندة',
              style: Theme.of(context).textTheme.titleLarge,
            ),
            for (final group in groups)
              Card(
                child: ListTile(
                  title: Text(textOf(group['arabicName'])),
                  subtitle: Text(
                    '${enrollments.where((e) => e['groupId'] == group['id']).length} لاعبين',
                  ),
                  trailing: const Icon(Icons.chevron_left),
                  onTap: () => openPage(
                    context,
                    CoachGroup(
                      auth: auth,
                      group: group,
                      sessions: sessions
                          .where((s) => s['trainingGroupId'] == group['id'])
                          .toList(),
                      enrollments: enrollments
                          .where((e) => e['groupId'] == group['id'])
                          .toList(),
                    ),
                  ),
                ),
              ),
          ],
        );
      },
    ),
  );
}

class CoachGroup extends StatelessWidget {
  final AuthController auth;
  final Json group;
  final List<Json> sessions, enrollments;
  const CoachGroup({
    super.key,
    required this.auth,
    required this.group,
    required this.sessions,
    required this.enrollments,
  });
  @override
  Widget build(BuildContext context) => AppScaffold(
    title: textOf(group['arabicName']),
    child: ListView(
      padding: const EdgeInsets.all(18),
      children: [
        if (sessions.isNotEmpty)
          AppCard(
            child: Text(
              '${sessions.first['branch']} · ${sessions.first['sport']}\n${sessions.first['startTime']} — ${sessions.first['endTime']}',
            ),
          ),
        Text(
          'اللاعبون (${enrollments.length})',
          style: Theme.of(context).textTheme.titleLarge,
        ),
        for (final player in enrollments)
          ListTile(
            leading: const CircleAvatar(child: Icon(Icons.sports_soccer)),
            title: Text(textOf(player['player'])),
            subtitle: Text(textOf(player['playerCode'])),
            trailing: IconButton(
              tooltip: 'تقييم اللاعب',
              icon: const Icon(Icons.assessment_outlined),
              onPressed: () => openPage(
                context,
                CoachEvaluation(auth: auth, enrollment: player),
              ),
            ),
          ),
        const SizedBox(height: 12),
        FilledButton.icon(
          onPressed: sessions.isEmpty
              ? null
              : () => openPage(
                  context,
                  CoachAttendance(
                    auth: auth,
                    sessionId: textOf(sessions.first['id']),
                  ),
                ),
          icon: const Icon(Icons.fact_check_outlined),
          label: const Text('تسجيل الحضور'),
        ),
      ],
    ),
  );
}
