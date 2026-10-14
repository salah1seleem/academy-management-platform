import 'package:academy_mobile/core/theme/ui_text.dart';
import 'package:flutter/material.dart';

import '../../core/theme/academy_brand.dart';
import '../shared/matches.dart';

import '../../core/api/api_client.dart';
import '../../core/auth/auth_controller.dart';
import '../../core/widgets/app_widgets.dart';
import '../guardian/guardian_ui.dart';
import 'coach_attendance.dart';
import 'coach_evaluation.dart';
import '../shared/private_media.dart';
import '../shared/academy_news.dart';
import '../shared/staff_amounts.dart';
import 'device_check_in.dart';

class CoachHome extends StatelessWidget {
  final AuthController auth;
  final Widget accountPage;
  const CoachHome({super.key, required this.auth, required this.accountPage});

  @override
  Widget build(BuildContext context) => AppScaffold(
    title: 'الرئيسية · المدرب',
    actions: [
      IconButton(
        tooltip: ui(context, 'الحساب'),
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
                  UiText(
                    'أهلًا، ${auth.session!.displayName}',
                    style: Theme.of(context).textTheme.headlineSmall,
                  ),
                  const UiText('حصصك ومجموعاتك فقط — لا تظهر بيانات مالية.'),
                ],
              ),
            ),
            NewsStrip(auth: auth),
            NextMatchStrip(auth: auth),
            OutlinedButton.icon(
              onPressed: () => openPage(context, StaffAmounts(auth: auth)),
              icon: const Icon(Icons.add_card_outlined),
              label: const UiText('طلب مبلغ إضافي'),
            ),
            OutlinedButton.icon(
              onPressed: () => openPage(context, DeviceCheckIn(auth: auth)),
              icon: const Icon(Icons.fingerprint),
              label: const UiText('تسجيل حضوري بالبصمة والموقع'),
            ),
            const SizedBox(height: 18),
            UiText(
              'حصص اليوم والقادمة',
              style: Theme.of(context).textTheme.titleLarge,
            ),
            const SizedBox(height: 8),
            if (sessions.isEmpty)
              const UiText('لا توجد حصص ضمن النطاق الحالي.'),
            for (final session in sessions.take(6))
              Card(
                child: ListTile(
                  title: UiText(textOf(session['group'])),
                  subtitle: UiText(
                    '${day(session['sessionDate'])} · ${session['startTime']} — ${session['branch']}${session['resource'] == null ? '' : '\n${session['resource']}'}',
                  ),
                  trailing: UiText(
                    '${session['recorded']}/${session['playerCount']}',
                  ),
                  onTap: BrandScope.maybeOf(context)?.coachAttendance != true
                      ? null
                      : () => openPage(
                          context,
                          CoachAttendance(
                            auth: auth,
                            sessionId: textOf(session['id']),
                          ),
                        ),
                ),
              ),
            const SizedBox(height: 18),
            UiText(
              'المجموعات المسندة',
              style: Theme.of(context).textTheme.titleLarge,
            ),
            for (final group in groups)
              Card(
                child: ListTile(
                  title: UiText(textOf(group['arabicName'])),
                  subtitle: UiText(
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
            child: UiText(
              '${sessions.first['branch']} · ${sessions.first['sport']}\n${sessions.first['startTime']} — ${sessions.first['endTime']}',
            ),
          ),
        UiText(
          'اللاعبون (${enrollments.length})',
          style: Theme.of(context).textTheme.titleLarge,
        ),
        for (final player in enrollments)
          ListTile(
            leading: const CircleAvatar(child: Icon(Icons.sports_soccer)),
            title: UiText(textOf(player['player'])),
            subtitle: UiText(textOf(player['playerCode'])),
            trailing: IconButton(
              tooltip: ui(context, 'تقييم اللاعب'),
              icon: const Icon(Icons.assessment_outlined),
              onPressed: () => openPage(
                context,
                CoachEvaluation(auth: auth, enrollment: player),
              ),
            ),
          ),
        const SizedBox(height: 12),
        OutlinedButton.icon(
          onPressed: () => openPage(
            context,
            UploadSessionMedia(auth: auth, groupId: textOf(group['id'])),
          ),
          icon: const Icon(Icons.add_photo_alternate_outlined),
          label: const UiText('رفع صور وفيديوهات للحصة'),
        ),
        if (BrandScope.maybeOf(context)?.coachAttendance == true)
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
            label: const UiText('تسجيل الحضور'),
          ),
      ],
    ),
  );
}
