import 'package:flutter/material.dart';

import '../../core/auth/auth_controller.dart';
import '../../core/widgets/app_widgets.dart';
import '../guardian/guardian_ui.dart';

class OwnerReports extends StatefulWidget {
  final AuthController auth;
  const OwnerReports({super.key, required this.auth});
  @override
  State<OwnerReports> createState() => _OwnerReportsState();
}

class _OwnerReportsState extends State<OwnerReports> {
  String report = 'financial';
  @override
  Widget build(BuildContext context) => AppScaffold(
    title: 'التقارير التنفيذية',
    child: Column(
      children: [
        Padding(
          padding: const EdgeInsets.all(16),
          child: SegmentedButton<String>(
            segments: const [
              ButtonSegment(value: 'financial', label: Text('المال')),
              ButtonSegment(value: 'attendance', label: Text('الحضور')),
              ButtonSegment(value: 'subscriptions', label: Text('الاشتراكات')),
            ],
            selected: {report},
            onSelectionChanged: (v) => setState(() => report = v.single),
          ),
        ),
        Expanded(
          child: report == 'subscriptions'
              ? _subscription(widget.auth)
              : _report(widget.auth, report),
        ),
      ],
    ),
  );

  Widget _subscription(AuthController auth) => RemoteBody(
    auth: auth,
    path: '/api/v1/reports/owner-summary',
    builder: (context, value) {
      final d = obj(obj(value)['subscriptions']);
      return ListView(
        padding: const EdgeInsets.all(18),
        children: [
          const Text('ملخص قراءة فقط من فترات الاشتراك المخزنة.'),
          AppCard(
            child: Column(
              children: [
                _row('فعالة', d['active']),
                _row('تنتهي قريبًا', d['expiring']),
                _row('منتهية', d['expired']),
              ],
            ),
          ),
        ],
      );
    },
  );
  Widget _report(AuthController auth, String kind) => RemoteBody(
    key: ValueKey(kind),
    auth: auth,
    path: kind == 'financial'
        ? '/api/v1/reports/financial?pageSize=20'
        : '/api/v1/reports/attendance?subject=players&pageSize=20',
    builder: (context, value) {
      final d = obj(value), items = rows(d['items']);
      return ListView(
        padding: const EdgeInsets.all(18),
        children: [
          Text(
            kind == 'financial' ? 'ملخص التحصيلات' : 'ملخص الحضور المخزن',
            style: Theme.of(context).textTheme.titleLarge,
          ),
          Text('إجمالي السجلات: ${d['totalCount']}'),
          if (kind == 'financial')
            Text('الإجمالي: ${money(d['totalAmount'], d['currency'])}'),
          for (final item in items)
            Card(
              child: ListTile(
                title: Text(
                  textOf(kind == 'financial' ? item['player'] : item['player']),
                ),
                subtitle: Text(
                  kind == 'financial'
                      ? '${item['receiptNumber']} · ${item['branch']}'
                      : '${day(item['date'])} · ${item['group']}',
                ),
                trailing: Text(
                  kind == 'financial'
                      ? money(item['amount'], item['currency'])
                      : label(item['status']),
                ),
              ),
            ),
        ],
      );
    },
  );
  Widget _row(String name, Object? value) =>
      ListTile(title: Text(name), trailing: Text(textOf(value)));
}
