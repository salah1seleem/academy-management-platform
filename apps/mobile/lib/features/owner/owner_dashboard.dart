import 'package:flutter/material.dart';

import '../../core/auth/auth_controller.dart';
import '../../core/theme/app_theme.dart';
import '../../core/widgets/app_widgets.dart';
import '../guardian/guardian_ui.dart';
import 'owner_reports.dart';

class OwnerDashboard extends StatelessWidget {
  final AuthController auth;
  final Widget accountPage;
  const OwnerDashboard({
    super.key,
    required this.auth,
    required this.accountPage,
  });
  @override
  Widget build(BuildContext context) => AppScaffold(
    title: 'لوحة المالك',
    actions: [
      IconButton(
        tooltip: 'الحساب',
        onPressed: () => openPage(context, accountPage),
        icon: const Icon(Icons.person_outline),
      ),
    ],
    child: RemoteBody(
      auth: auth,
      path: '/api/v1/reports/owner-summary',
      builder: (context, value) {
        final d = obj(value),
            collections = obj(d['collections']),
            payments = obj(d['payments']),
            subscriptions = obj(d['subscriptions']),
            attendance = obj(d['attendance']);
        final currency = d['currency'];
        return ListView(
          padding: const EdgeInsets.all(18),
          children: [
            Container(
              padding: const EdgeInsets.all(22),
              decoration: BoxDecoration(
                color: AcademyTheme.teal,
                borderRadius: BorderRadius.circular(22),
              ),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Text(
                    auth.session!.selected!.academyName,
                    style: const TextStyle(color: Colors.white70),
                  ),
                  Text(
                    'ملخص تنفيذي حتى ${day(d['asOfDate'])}',
                    style: const TextStyle(
                      color: Colors.white,
                      fontSize: 22,
                      fontWeight: FontWeight.bold,
                    ),
                  ),
                  const Text(
                    'كل الأرقام من السجلات المخزنة؛ لا توجد اتجاهات تقديرية.',
                    style: TextStyle(color: Colors.white70),
                  ),
                ],
              ),
            ),
            const SizedBox(height: 16),
            _metrics([
              ('تحصيل اليوم', money(collections['today'], currency)),
              ('تحصيل الشهر', money(collections['month'], currency)),
              ('الاشتراكات الفعالة', textOf(subscriptions['active'])),
              ('تنتهي قريبًا', textOf(subscriptions['expiring'])),
              ('إجمالي التحصيلات', money(collections['total'], currency)),
              ('مدفوعات معلقة', textOf(payments['pending'])),
              ('مدفوعات فاشلة', textOf(payments['failed'])),
              ('اللاعبون النشطون', textOf(d['activePlayers'])),
              ('حضور اليوم', textOf(attendance['present'])),
            ]),
            const SizedBox(height: 18),
            Text(
              'التحصيل حسب الفرع',
              style: Theme.of(context).textTheme.titleLarge,
            ),
            for (final branch in rows(d['byBranch']))
              ListTile(
                title: Text(textOf(branch['name'])),
                trailing: Text(money(branch['amount'], currency)),
              ),
            Text(
              'أحدث التحصيلات',
              style: Theme.of(context).textTheme.titleLarge,
            ),
            for (final item in rows(d['latest']))
              ListTile(
                title: Text(textOf(item['player'])),
                subtitle: Text(textOf(item['receiptNumber'])),
                trailing: Text(money(item['amount'], item['currency'])),
              ),
            Text(
              'ملخص الاشتراكات',
              style: Theme.of(context).textTheme.titleLarge,
            ),
            AppCard(
              child: Text(
                'فعالة: ${subscriptions['active']} · تنتهي قريبًا: ${subscriptions['expiring']} · منتهية: ${subscriptions['expired']}',
              ),
            ),
            FilledButton.icon(
              onPressed: () => openPage(context, OwnerReports(auth: auth)),
              icon: const Icon(Icons.analytics_outlined),
              label: const Text('التقارير'),
            ),
            Wrap(
              spacing: 8,
              children: const [
                Chip(label: Text('الاشتراكات')),
                Chip(label: Text('اللاعبون')),
                Chip(label: Text('الحضور')),
              ],
            ),
            const Text('الإدارة والتعديل الكاملان متاحان من لوحة الويب.'),
          ],
        );
      },
    ),
  );
}

Widget _metrics(List<(String, String)> values) => LayoutBuilder(
  builder: (context, c) => Wrap(
    spacing: 10,
    runSpacing: 10,
    children: [
      for (final item in values)
        SizedBox(
          width: (c.maxWidth - 10) / 2,
          child: AppCard(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(item.$1),
                const SizedBox(height: 4),
                Text(
                  item.$2,
                  style: const TextStyle(
                    fontSize: 19,
                    fontWeight: FontWeight.bold,
                    color: AcademyTheme.teal,
                  ),
                ),
              ],
            ),
          ),
        ),
    ],
  ),
);
