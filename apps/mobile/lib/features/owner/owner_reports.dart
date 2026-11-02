import 'package:flutter/material.dart';

import '../../core/auth/auth_controller.dart';
import '../../core/theme/ui_text.dart';
import '../guardian/guardian_ui.dart';
import '../guardian/guardian_payments.dart' show ReceiptPage;
import '../shared/experience_ui.dart';
import '../shared/receivable_balances.dart';
import '../shared/staff_attendance_exceptions.dart';
import '../shared/page_video.dart';
import '../shared/report_export.dart';
import 'owner_cockpit.dart';
import 'home_destinations.dart';
import 'external_agreements.dart';
import 'renewal_opportunities.dart';
import '../admin/native_product_orders.dart';

const _reports = {
  'financial-hub': 'التقارير المالية',
  'financial': 'الإيرادات والتحصيلات',
  'income': 'الإيرادات والتحصيلات',
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
  late String collectionType = widget.initialFilters['collectionType'] ?? '';
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
    'collectionType': collectionType,
    'subscriptionKind': collectionType == 'Subscription'
        ? widget.initialFilters['subscriptionKind'] ?? ''
        : '',
    'externalKind': collectionType == 'External'
        ? widget.initialFilters['externalKind'] ?? ''
        : '',
  };
  void open(String name) => openPage(
    context,
    OwnerReports(
      auth: widget.auth,
      initialReport: name,
      initialFilters: filters,
    ),
  );
  String get report =>
      widget.initialReport == 'income' ? 'financial' : widget.initialReport;
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
            if (report != 'receivables')
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
                    collectionType = '';
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
      GLink(
        _reports[item.$1] ?? item.$1,
        item.$2,
        () => open(item.$1),
        subtitle: const {
          'financial-hub': 'التحصيل والمستحقات والتشغيل والرواتب',
          'attendance-hub': 'حضور اللاعبين وفريق العمل',
          'subscriptions': 'حالة الاشتراكات ومتابعة اللاعبين',
          'financial': 'المبالغ المحصلة ومصادرها',
          'receivables': 'الأرصدة والمبالغ المستحقة',
          'expenses': 'مصروفات الأكاديمية المسجلة',
          'operating': 'الإيرادات والمصروفات ونتيجة الفترة',
          'payroll': 'الأساسي والمستحق والتسويات',
        }[item.$1],
      ),
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
          if (financial) ...[
            CollectionSourceSummary(
              data: d,
              selected: collectionType,
              onSelected: (source) => setState(() {
                collectionType = source;
                page = 1;
              }),
            ),
            const Heading('سجل التحصيلات'),
          ] else
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
                      ? '${financialCollectionName(row['collectionType'])}${row['collectionType'] == 'Refund' ? ' · بتاريخ الصرف' : ''} · ${row['branch']}\n${row['receiptNumber']}'
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
                  financial
                      ? FinancialCollectionDetail(auth: widget.auth, row: row)
                      : GuardianPage(
                          title: 'تفاصيل السجل',
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
          const SizedBox.shrink(),
        ]);
      },
    );
  }
}

String financialCollectionName(Object? type) => switch (type) {
  null || '' || 'Subscription' => 'اشتراك',
  'Product' => 'منتجات رياضية',
  'Tournament' => 'بطولة',
  'DayPass' => 'زيارة يومية',
  'External' => 'اتفاق خارجي',
  'Refund' => 'استرداد اشتراك',
  'Void' => 'عكس إيصال نقدي',
  _ => 'حركة مالية',
};

/// A read-only view of the exact authorized ledger row; it does not reprice or
/// reconstruct a receipt from a different source's identifier.
class FinancialCollectionDetail extends StatefulWidget {
  FinancialCollectionDetail({super.key, required this.auth, required Json row})
    : row = Map.unmodifiable(row);
  final AuthController auth;
  final Json row;
  @override
  State<FinancialCollectionDetail> createState() =>
      _FinancialCollectionDetailState();
}

class _FinancialCollectionDetailState extends State<FinancialCollectionDetail> {
  String? get identity => widget.auth.session?.selected == null
      ? null
      : '${widget.auth.session!.selected!.academyId}:${widget.auth.session!.selected!.id}:${widget.auth.session!.selected!.contextRevision}';
  late final opened = identity;
  @override
  Widget build(BuildContext context) => ListenableBuilder(
    listenable: widget.auth,
    builder: (context, _) {
      if (opened == null || identity != opened) return const SizedBox.shrink();
      final row = widget.row;
      final kind = row['collectionType'];
      final refund = kind == 'Refund' || kind == 'Void';
      final subscription = kind == null || kind == '' || kind == 'Subscription';
      final receipt = row['receiptId'];
      final hasReceipt =
          receipt is String &&
          receipt.isNotEmpty &&
          receipt != '00000000-0000-0000-0000-000000000000';
      final owner = widget.auth.session!.selected!.role == 'AcademyOwner';
      return GuardianPage(
        title: refund ? 'تفاصيل الاسترداد' : 'تفاصيل التحصيل',
        child: gList([
          Heading(financialCollectionName(kind)),
          Heading(textOf(row['player'])),
          const UiText('المبلغ في التقرير'),
          MoneyText(
            row['netAmount'] ?? row['amount'],
            row['currency'],
            color: refund ? ExperienceColors.negative(context) : null,
          ),
          if (refund) ...[
            UiText(
              'قيمة المبلغ المردود: ${money(row['refundAmount'], row['currency'])}',
            ),
            const UiText(
              'حركة صرف مستقلة بتاريخها؛ الإيصال المرتبط يعرض التحصيل الأصلي.',
            ),
          ],
          for (final field in [
            ('الرياضة', 'sport'),
            ('الفرع', 'branch'),
            ('المجموعة أو النشاط', 'group'),
            ('الباقة أو الخدمة', 'plan'),
            ('رقم المرجع في السجل', 'receiptNumber'),
          ])
            if (row[field.$2] != null)
              ListTile(
                title: UiText(field.$1),
                subtitle: UiText(textOf(row[field.$2])),
              ),
          if (row['confirmedAtUtc'] != null)
            ListTile(
              title: UiText(
                refund
                    ? 'تاريخ الصرف بالتوقيت العالمي'
                    : 'تاريخ التأكيد بالتوقيت العالمي',
              ),
              subtitle: UiText(day(row['confirmedAtUtc'])),
            ),
          ListTile(
            title: const UiText('طريقة الدفع'),
            subtitle: UiText(switch (row['paymentMethod']) {
              'Cash' => 'نقدي',
              'Online' => 'إلكتروني',
              _ => 'غير مسجلة',
            }),
          ),
          if (row['providerReference'] is String &&
              (row['providerReference'] as String).isNotEmpty)
            ListTile(
              title: const UiText('مرجع العملية'),
              subtitle: UiText(row['providerReference']),
            ),
          if ((subscription || refund) && hasReceipt)
            GLink(
              refund ? 'عرض إيصال التحصيل الأصلي' : 'عرض الإيصال',
              Icons.receipt_long_outlined,
              () => openPage(
                context,
                ReceiptPage(auth: widget.auth, id: receipt, staff: true),
              ),
            ),
          if (owner && kind == 'Product' && row['collectionId'] is String)
            GLink(
              'عرض طلب المنتج',
              Icons.shopping_bag_outlined,
              () => openPage(
                context,
                NativeProductOrderReview(
                  auth: widget.auth,
                  id: row['collectionId'],
                ),
              ),
            ),
          if (owner && kind == 'External' && hasReceipt)
            GLink(
              'عرض الاتفاق وإيصالاته',
              Icons.handshake_outlined,
              () => openPage(
                context,
                NativeExternalAgreementDetail(auth: widget.auth, id: receipt),
              ),
            ),
        ]),
      );
    },
  );
}

class CollectionSourceSummary extends StatelessWidget {
  const CollectionSourceSummary({
    super.key,
    required this.data,
    required this.selected,
    required this.onSelected,
  });
  final Json data;
  final String selected;
  final ValueChanged<String> onSelected;
  @override
  Widget build(BuildContext context) {
    final lines = rows(data['sourceSummary'] ?? <Json>[])
        .where((l) => l['amount'] != 0)
        .toList();
    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        Row(
          children: [
            const Expanded(
              child: UiText(
                'مصادر التحصيل',
                style: TextStyle(fontSize: 16, fontWeight: FontWeight.w700),
              ),
            ),
            IconButton(
              tooltip: 'أساس المبالغ المحصلة',
              onPressed: () => showDialog<void>(
                context: context,
                builder: (context) => AlertDialog(
                  title: const UiText('المبالغ المحصلة'),
                  content: const UiText(
                    'المصادر تعرض المقبوضات الفعلية والاستردادات بالسالب ضمن الفلاتر نفسها لكل السجلات. طلب الدفع المعلق لا يُحسب قبضًا، وانتهاء الاشتراك لا يثبت دينًا.',
                  ),
                  actions: [
                    TextButton(
                      onPressed: () => Navigator.pop(context),
                      child: const UiText('حسنًا'),
                    ),
                  ],
                ),
              ),
              icon: const Icon(Icons.info_outline, size: 19),
            ),
          ],
        ),
        MoneyText(
          data['netAmount'] ?? data['totalAmount'],
          data['currency'],
          size: 28,
        ),
        LayoutBuilder(
          builder: (context, bounds) => Wrap(
            spacing: 10,
            runSpacing: 10,
            children: [
              for (final line in lines)
                SizedBox(
                  width: (bounds.maxWidth - 10) / 2,
                  child: GCard(
                    onTap: () => onSelected(textOf(line['key'])),
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        UiText(
                          textOf(line['label']),
                          style: const TextStyle(fontSize: 11),
                        ),
                        MoneyText(line['amount'], data['currency'], size: 20),
                        if (selected == line['key'])
                          const Icon(Icons.check_circle_outline, size: 16),
                      ],
                    ),
                  ),
                ),
            ],
          ),
        ),
        if (selected.isNotEmpty)
          TextButton(
            onPressed: () => onSelected(''),
            child: const UiText('عرض سجل كل المصادر'),
          ),
      ],
    );
  }
}
