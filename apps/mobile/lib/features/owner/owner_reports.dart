import 'package:flutter/material.dart';

import '../../core/auth/auth_controller.dart';
import '../../core/theme/ui_text.dart';
import '../guardian/guardian_ui.dart';
import '../shared/experience_ui.dart';
import '../shared/receivable_balances.dart';
import '../shared/staff_attendance_exceptions.dart';
import '../shared/page_video.dart';
import '../shared/report_export.dart';
import 'owner_cockpit.dart';
import 'home_destinations.dart';
import 'external_agreements.dart';
import 'renewal_opportunities.dart';

const _reports = {
  'financial-hub': 'التقارير المالية',
  'financial': 'التحصيلات',
  'income': 'الإيرادات',
  'receivables': 'المستحقات المالية',
  'expenses': 'المصروفات',
  'operating': 'الأرباح والخسائر',
  'payroll': 'الرواتب',
  'base': 'الرواتب الأساسية',
  'due': 'الرواتب المستحقة',
  'deductions': 'الخصومات',
  'additions': 'الإضافات',
  'attendance-hub': 'سجل الحضور',
  'attendance': 'حضور اللاعبين',
  'staff-attendance': 'حضور المدربين والموظفين',
  'subscriptions': 'الاشتراكات',
  'external': 'الاتفاقيات والإيرادات الخارجية',
};

String expenseName(Object? key) =>
    const {
      'Rent': 'إيجار المنشأة',
      'Electricity': 'الكهرباء',
      'Water': 'المياه',
      'Heating': 'التدفئة',
      'Maintenance': 'الصيانة والتشغيل',
      'CollectionFees': 'رسوم التحصيل الإلكتروني',
      'Supplies': 'المستلزمات',
      'Administration': 'المصروفات الإدارية',
      'Facility': 'تشغيل المرافق',
      'Other': 'مصروفات أخرى',
    }[key] ??
    'المصروف';

class OwnerReports extends StatefulWidget {
  const OwnerReports({
    super.key,
    required this.auth,
    this.initialFilters = const {},
    this.initialReport = '',
  });
  final AuthController auth;
  final Map<String, String> initialFilters;
  final String initialReport;
  @override
  State<OwnerReports> createState() => _OwnerReportsState();
}

class _OwnerReportsState extends State<OwnerReports> {
  late String from = widget.initialFilters['from'] ?? '',
      to = widget.initialFilters['to'] ?? '',
      branch = widget.initialFilters['branchId'] ?? '';
  late bool all = widget.initialFilters['allDates'] == 'true';
  int page = 1;
  late Future<Json> metadata = load();
  Future<Json> load() async {
    final result = await Future.wait([
      widget.auth.request('GET', '/api/v1/academy/calendar'),
      widget.auth.request('GET', '/api/v1/manage/structure/options'),
    ]);
    final calendar = obj(result[0]);
    if (mounted && from.isEmpty && to.isEmpty && !all) {
      setState(() {
        from = textOf(calendar['trailingMonthFrom']);
        to = textOf(calendar['today']);
      });
    }
    return obj(result[1]);
  }

  Map<String, String> get filters => {
    ...widget.initialFilters,
    'from': from,
    'to': to,
    'allDates': all ? 'true' : 'false',
    'branchId': branch,
  };
  void open(String name) => openPage(
    context,
    OwnerReports(
      auth: widget.auth,
      initialReport: name,
      initialFilters: filters,
    ),
  );
  String get report => widget.initialReport;
  @override
  Widget build(BuildContext context) => GuardianPage(
    title: report.isEmpty ? 'التقارير' : _reports[report] ?? 'التقارير',
    actions: [
      if (['financial', 'attendance', 'staff-attendance'].contains(report))
        ReportExportButton(
          auth: widget.auth,
          path:
              '/api/v1/reports/${report == 'financial' ? 'financial' : 'attendance'}/export?${Uri(queryParameters: {...Map.fromEntries(filters.entries.where((e) => e.value.isNotEmpty)), if (report != 'financial') 'subject': report == 'staff-attendance' ? 'staff' : 'players'}).query}',
        ),
      PageVideoButton(auth: widget.auth, screen: 'reports'),
    ],
    child: FutureBuilder<Json>(
      future: metadata,
      builder: (context, snapshot) {
        if (!snapshot.hasData) {
          if (snapshot.hasError) {
            return const EmptyMessage('تعذر تحميل فلاتر التقارير.');
          }
          return const Center(child: CircularProgressIndicator());
        }
        final query = filters..removeWhere((key, value) => value.isEmpty);
        return Column(
          children: [
            Padding(
              padding: const EdgeInsets.fromLTRB(16, 8, 16, 0),
              child: ScopeDateTiles(
                from: from,
                to: to,
                onChanged: (a, b) => setState(() {
                  from = a;
                  to = b;
                  all = false;
                  page = 1;
                }),
                onClear: () => setState(() {
                  from = '';
                  to = '';
                  branch = '';
                  all = true;
                  page = 1;
                }),
              ),
            ),
            Padding(
              padding: const EdgeInsets.symmetric(horizontal: 16),
              child: ScopeBranchTabs(
                items: rows(snapshot.data!['branches']),
                selected: branch,
                onSelected: (v) => setState(() {
                  branch = v;
                  page = 1;
                }),
              ),
            ),
            Expanded(child: content(query)),
          ],
        );
      },
    ),
  );
  Widget menu(List<(String, IconData)> items) => gList([
    for (final item in items)
      GLink(_reports[item.$1] ?? item.$1, item.$2, () => open(item.$1)),
  ]);
  Widget content(Map<String, String> query) {
    if (report.isEmpty) {
      return menu([
        ('financial-hub', Icons.account_balance_wallet_outlined),
        ('attendance-hub', Icons.fact_check_outlined),
        ('subscriptions', Icons.layers_outlined),
      ]);
    }
    if (report == 'financial-hub') {
      return menu([
        ('income', Icons.trending_up_rounded),
        ('financial', Icons.receipt_long_outlined),
        ('receivables', Icons.pending_actions_rounded),
        ('expenses', Icons.payments_outlined),
        ('operating', Icons.analytics_outlined),
        ('payroll', Icons.badge_outlined),
      ]);
    }
    if (report == 'attendance-hub') {
      return gList([
        GLink('حضور اللاعبين', Icons.groups_outlined, () => open('attendance')),
        GLink(
          'حضور المدربين والموظفين',
          Icons.badge_outlined,
          () => open('staff-attendance'),
        ),
        GLink(
          'مراجعة استثناءات حضور الموظفين',
          Icons.rule_outlined,
          () => openPage(
            context,
            StaffAttendanceExceptionPage(auth: widget.auth, review: true),
          ),
        ),
      ]);
    }
    if (report == 'payroll') {
      return menu([
        ('base', Icons.badge_outlined),
        ('due', Icons.account_balance_wallet_outlined),
        ('deductions', Icons.remove_circle_outline),
        ('additions', Icons.add_circle_outline),
      ]);
    }
    if (report == 'income') {
      return RemoteBody(
        auth: widget.auth,
        path:
            '/api/v1/reports/operating-statement?${Uri(queryParameters: query).query}',
        builder: (context, value) {
          final d = obj(value);
          return gList([
            Heading(
              'الإيرادات المحصلة · ${money(d['totalIncome'], d['currency'])}',
            ),
            const UiText(
              'مقبوضات فعلية بعد الاسترداد؛ لا تمثل إيرادًا مكتسبًا نهائيًا.',
            ),
            for (final line in rows(d['income']).where((r) => r['amount'] != 0))
              GCard(
                child: ListTile(
                  title: UiText(textOf(line['label'])),
                  trailing: UiText(money(line['amount'], d['currency'])),
                  onTap: () => openPage(
                    context,
                    OwnerReports(
                      auth: widget.auth,
                      initialReport: 'financial',
                      initialFilters: {
                        ...query,
                        'collectionType': switch (line['key']) {
                          'product' => 'Product',
                          'tournament' => 'Tournament',
                          'rental' || 'external' => 'External',
                          _ => 'Subscription',
                        },
                      },
                    ),
                  ),
                ),
              ),
          ]);
        },
      );
    }
    if (report == 'operating') {
      return NativeOperatingStatement(auth: widget.auth, filters: query);
    }
    if (report == 'receivables') {
      return ReceivableBalances(auth: widget.auth, staff: true, filters: query);
    }
    if (report == 'external') {
      return NativeExternalAgreements(auth: widget.auth, branch: branch);
    }
    if (report == 'renewals') {
      return NativeRenewalOpportunities(auth: widget.auth, branchId: branch);
    }
    if (report == 'subscriptions') {
      return HomeSubscriptionPlayers(auth: widget.auth, filters: query);
    }
    if (['base', 'due', 'deductions', 'additions'].contains(report)) {
      final month = to.isNotEmpty ? '${to.substring(0, 7)}-01' : '';
      return RemoteBody(
        auth: widget.auth,
        path:
            '/api/v1/manage/payroll?${Uri(queryParameters: {if (month.isNotEmpty) 'month': month, if (branch.isNotEmpty) 'branchId': branch}).query}',
        builder: (context, value) {
          final d = obj(value);
          final key = {
            'base': 'baseDue',
            'due': 'remainingAmount',
            'deductions': 'deductions',
            'additions': 'additions',
          }[report]!;
          return gList([
            Heading('كشف شهر ${textOf(d['month']).substring(0, 7)}'),
            for (final employee in rows(d['items']))
              GCard(
                onTap: () => openPage(
                  context,
                  GuardianPage(
                    title: textOf(employee['name']),
                    child: gList([
                      for (final row in [
                        ('الراتب الأساسي', 'baseDue'),
                        ('الخصومات', 'deductions'),
                        ('الإضافات', 'additions'),
                        ('الصافي', 'netDue'),
                        ('المسدّد', 'paidAmount'),
                        ('المتبقي', 'remainingAmount'),
                      ])
                        ListTile(
                          title: UiText(row.$1),
                          trailing: UiText(
                            money(employee[row.$2], employee['currency']),
                          ),
                        ),
                    ]),
                  ),
                ),
                child: ListTile(
                  contentPadding: EdgeInsets.zero,
                  title: UiText(textOf(employee['name'])),
                  subtitle: UiText(
                    textOf(employee['jobTitle']),
                    style: const TextStyle(fontSize: 11),
                  ),
                  trailing: UiText(money(employee[key], employee['currency'])),
                ),
              ),
          ]);
        },
      );
    }
    final financial = report == 'financial';
    final path = financial
        ? '/api/v1/reports/financial'
        : report == 'expenses'
        ? '/api/v1/finance/operating-expenses'
        : '/api/v1/reports/attendance';
    return RemoteBody(
      auth: widget.auth,
      path:
          '$path?${Uri(queryParameters: {...query, 'page': '$page', 'pageSize': '25', if (!financial && report != 'expenses') 'subject': report == 'staff-attendance' ? 'staff' : 'players'}).query}',
      builder: (context, value) {
        final d = obj(value), items = rows(d['items']);
        return gList([
          Heading(_reports[report] ?? 'التقرير'),
          if (financial)
            UiText(
              'صافي التحصيل: ${money(d['netAmount'] ?? d['totalAmount'], d['currency'])}',
            ),
          if (items.isEmpty) const EmptyMessage('لا توجد سجلات مطابقة.'),
          for (final row in items)
            GCard(
              child: ListTile(
                contentPadding: EdgeInsets.zero,
                title: UiText(
                  textOf(
                    row['player'] ??
                        row['staff'] ??
                        row['name'] ??
                        row['description'] ??
                        row['categoryName'] ??
                        expenseName(row['category']),
                  ),
                ),
                subtitle: UiText(
                  financial
                      ? '${row['collectionType'] == 'Void'
                            ? 'عكس إيصال نقدي · الفترة ملغاة · '
                            : row['collectionType'] == 'Refund'
                            ? 'استرداد اشتراك · بتاريخ الصرف · '
                            : ''}${row['branch']}\n${row['receiptNumber']}'
                      : '${row['group'] ?? row['category'] ?? ''} · ${day(row['date'] ?? row['periodFrom'])}',
                  style: const TextStyle(fontSize: 10),
                ),
                trailing: UiText(
                  financial || report == 'expenses'
                      ? money(
                          row['netAmount'] ?? row['amount'],
                          row['currency'] ?? d['currency'],
                        )
                      : label(row['status']),
                  style: const TextStyle(fontSize: 12),
                ),
                onTap: () => openPage(
                  context,
                  GuardianPage(
                    title: financial ? 'تفاصيل التحصيل' : 'تفاصيل السجل',
                    child: gList([
                      for (final field in [
                        ('الاسم', 'player'),
                        ('الفرع', 'branch'),
                        ('المجموعة', 'group'),
                        ('التاريخ', 'date'),
                        ('رقم الإيصال', 'receiptNumber'),
                        ('المبلغ', 'amount'),
                        ('الملاحظة', 'description'),
                      ])
                        if (row[field.$2] != null)
                          ListTile(
                            title: UiText(field.$1),
                            subtitle: UiText(textOf(row[field.$2])),
                          ),
                    ]),
                  ),
                ),
              ),
            ),
          Row(
            mainAxisAlignment: MainAxisAlignment.spaceBetween,
            children: [
              TextButton(
                onPressed: page <= 1 ? null : () => setState(() => page--),
                child: const UiText('السابق'),
              ),
              UiText('$page'),
              TextButton(
                onPressed:
                    page * 25 >=
                        ((d['totalCount'] ??
                                d['total'] ??
                                d['count'] ??
                                items.length)
                            as num)
                    ? null
                    : () => setState(() => page++),
                child: const UiText('التالي'),
              ),
            ],
          ),
        ]);
      },
    );
  }
}
