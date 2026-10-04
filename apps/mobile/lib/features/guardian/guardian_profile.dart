import 'package:flutter/material.dart';

import '../../core/auth/auth_controller.dart';
import 'guardian_ui.dart';
import 'guardian_report.dart';
import 'guardian_content.dart';
import 'guardian_payments.dart';

class ChildProfile extends StatelessWidget {
  final AuthController auth;
  final String environment, playerId, today;
  const ChildProfile({
    super.key,
    required this.auth,
    required this.environment,
    required this.playerId,
    required this.today,
  });
  @override
  Widget build(BuildContext context) => GuardianPage(
    title: 'ملف اللاعب',
    child: RemoteBody(
      auth: auth,
      path: '/api/v1/guardian/children/$playerId/profile',
      builder: (context, value) {
        final data = obj(value), player = obj(data['player']);
        final enrollments = rows(data['enrollments']);
        return gList([
          Container(
            padding: const EdgeInsets.all(22),
            decoration: BoxDecoration(
              borderRadius: BorderRadius.circular(18),
              gradient: const LinearGradient(
                colors: [Color(0xFF141F79), Color(0xFF251441)],
              ),
            ),
            child: Column(
              children: [
                PlayerAvatar(textOf(player['arabicName']), size: 84),
                const SizedBox(height: 12),
                Text(
                  textOf(player['arabicName']),
                  textAlign: TextAlign.center,
                  style: const TextStyle(
                    fontSize: 21,
                    fontWeight: FontWeight.bold,
                  ),
                ),
                Text('${ageOn(player['dateOfBirth'], today) ?? '—'} سنوات'),
                for (final e in enrollments) ...[
                  Text('${e['sport']} · ${e['branch']}'),
                  if (e['period'] != null)
                    Text(
                      '${day(e['period']['startDate'])} — ${day(e['period']['endDate'])}',
                      style: const TextStyle(
                        fontSize: 12,
                        color: Colors.white70,
                      ),
                    ),
                ],
              ],
            ),
          ),
          const SizedBox(height: 22),
          GLink(
            'تقرير اللاعب',
            Icons.sports_soccer,
            () => openPage(
              context,
              ReportHistory(auth: auth, playerId: playerId),
            ),
          ),
          GLink(
            'التدريب والمواعيد',
            Icons.calendar_month_outlined,
            () => openPage(
              context,
              ProfileRecords(
                auth: auth,
                playerId: playerId,
                section: 'schedule',
              ),
            ),
          ),
          GLink(
            'التغذية والصحة',
            Icons.restaurant_outlined,
            () => openPage(context, NutritionLibrary(auth: auth)),
          ),
          GLink(
            'الإصابات والاستشارات الطبية',
            Icons.health_and_safety_outlined,
            () =>
                openPage(context, MedicalPage(auth: auth, playerId: playerId)),
          ),
          GLink(
            'الصور والفيديوهات',
            Icons.photo_library_outlined,
            () =>
                openPage(context, GalleryPage(auth: auth, playerId: playerId)),
          ),
          GLink(
            'الحضور',
            Icons.fact_check_outlined,
            () => openPage(
              context,
              ProfileRecords(
                auth: auth,
                playerId: playerId,
                section: 'attendance',
              ),
            ),
          ),
          const Heading('الاشتراكات'),
          for (final e in enrollments)
            GCard(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Text('${e['sport']} · ${e['group']}'),
                  if (e['period'] != null) ...[
                    StatusPill(e['period']['status']),
                    Text(textOf(e['period']['plan'])),
                    Text(
                      '${day(e['period']['startDate'])} — ${day(e['period']['endDate'])}',
                    ),
                    if (e['period']['remainingSessions'] != null)
                      Text(
                        'الحصص المتبقية: ${e['period']['remainingSessions']}',
                      ),
                    TextButton(
                      onPressed: () => openPage(
                        context,
                        PeriodPage(auth: auth, id: e['period']['id']),
                      ),
                      child: const Text('تفاصيل حالة الاشتراك'),
                    ),
                  ],
                  FilledButton(
                    onPressed: () => openPage(
                      context,
                      PlanPicker(
                        auth: auth,
                        environment: environment,
                        enrollmentId: e['id'],
                        childName: player['arabicName'],
                      ),
                    ),
                    child: const Text('تجديد الاشتراك'),
                  ),
                ],
              ),
            ),
          GLink(
            'إيصالاتي',
            Icons.receipt_long,
            () => openPage(context, ReceiptsPage(auth: auth)),
          ),
        ]);
      },
    ),
  );
}

class ProfileRecords extends StatelessWidget {
  final AuthController auth;
  final String playerId, section;
  const ProfileRecords({
    super.key,
    required this.auth,
    required this.playerId,
    required this.section,
  });
  @override
  Widget build(BuildContext context) => GuardianPage(
    title: section == 'schedule' ? 'التدريب والمواعيد' : 'الحضور',
    child: RemoteBody(
      auth: auth,
      path: '/api/v1/guardian/children/$playerId/profile',
      builder: (context, value) {
        final d = obj(value);
        return gList(
          section == 'schedule'
              ? [
                  for (final e in rows(d['enrollments'])) ...[
                    Heading(textOf(e['sport'])),
                    Text('${e['branch']} · ${e['group']}'),
                    const Heading('الجلسات القادمة'),
                    if (rows(e['upcoming']).isEmpty)
                      const EmptyMessage('لا توجد جلسات قادمة منشورة.'),
                    for (final s in rows(e['upcoming']))
                      GCard(
                        child: Text(
                          '${day(s['sessionDate'])}\n${s['startTime']} — ${s['endTime']}',
                        ),
                      ),
                    const Heading('المواعيد الدورية'),
                    for (final s in rows(e['recurring']))
                      GCard(
                        child: Text(
                          '${label(s['dayOfWeek'])}\n${s['startTime']} — ${s['endTime']}',
                        ),
                      ),
                  ],
                ]
              : [
                  Facts([
                    ('حاضر', textOf(d['attendanceSummary']['present'])),
                    ('غائب', textOf(d['attendanceSummary']['absent'])),
                  ]),
                  if (rows(d['attendance']).isEmpty) const EmptyMessage(),
                  for (final a in rows(d['attendance']))
                    GCard(
                      child: Column(
                        crossAxisAlignment: CrossAxisAlignment.start,
                        children: [
                          Text('${a['sport']} · ${a['group']}'),
                          Text(day(a['sessionDate'])),
                          StatusPill(a['status']),
                        ],
                      ),
                    ),
                ],
        );
      },
    ),
  );
}

class PeriodPage extends StatelessWidget {
  final AuthController auth;
  final String id;
  const PeriodPage({super.key, required this.auth, required this.id});
  @override
  Widget build(BuildContext context) => GuardianPage(
    title: 'حالة الاشتراك',
    child: RemoteBody(
      auth: auth,
      path: '/api/v1/guardian/subscriptions/periods/$id/adjustments',
      builder: (context, value) {
        final d = obj(value);
        return gList([
          Heading(textOf(d['plan'])),
          StatusPill(d['status']),
          Text('${day(d['startDate'])} — ${day(d['endDate'])}'),
          if (d['frozenFromDate'] != null)
            Text('مجمد من ${day(d['frozenFromDate'])}'),
          if (d['remainingSessions'] != null)
            Text('الحصص المتبقية: ${d['remainingSessions']}'),
          const Heading('سجل التعديلات'),
          if (rows(d['adjustments']).isEmpty)
            const EmptyMessage('لا توجد تعديلات.'),
          for (final a in rows(d['adjustments']))
            GCard(
              child: Text(
                '${label(a['type'])} · ${day(a['effectiveDate'])}\nنهاية الاشتراك: ${day(a['newEndDate'])}',
              ),
            ),
        ]);
      },
    ),
  );
}
