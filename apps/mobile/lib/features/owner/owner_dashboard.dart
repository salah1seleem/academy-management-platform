import 'package:academy_mobile/core/theme/ui_text.dart';
import 'package:flutter/material.dart';

import '../shared/matches.dart';

import '../../core/auth/auth_controller.dart';
import '../../core/theme/app_theme.dart';
import '../../core/theme/academy_brand.dart';
import '../../core/theme/appearance.dart';
import '../guardian/guardian_ui.dart';
import '../shared/academy_news.dart';
import '../shared/role_navigation.dart';
import 'owner_reports.dart';
import 'owner_operations.dart';
import 'member_values.dart';
import 'coach_rating_leaders.dart';

class OwnerDashboard extends StatefulWidget {
  final AuthController auth;
  final Widget accountPage;
  const OwnerDashboard({
    super.key,
    required this.auth,
    required this.accountPage,
  });
  @override
  State<OwnerDashboard> createState() => _OwnerDashboardState();
}

class _OwnerDashboardState extends State<OwnerDashboard> {
  String from = '', to = '', branch = '';
  int revision = 0;
  String? identity;
  String? get currentIdentity => widget.auth.session?.selected == null
      ? null
      : '${widget.auth.session!.selected!.academyId}:${widget.auth.session!.selected!.id}:${widget.auth.session!.selected!.contextRevision}';
  @override
  void initState() {
    super.initState();
    identity = currentIdentity;
    widget.auth.addListener(contextChanged);
  }

  void contextChanged() {
    if (!mounted || identity == currentIdentity) return;
    setState(() {
      identity = currentIdentity;
      from = '';
      to = '';
      branch = '';
      revision++;
    });
  }

  @override
  void didUpdateWidget(OwnerDashboard old) {
    super.didUpdateWidget(old);
    if (old.auth != widget.auth) {
      old.auth.removeListener(contextChanged);
      widget.auth.addListener(contextChanged);
      identity = currentIdentity;
      from = '';
      to = '';
      branch = '';
      revision++;
    }
  }

  @override
  void dispose() {
    widget.auth.removeListener(contextChanged);
    super.dispose();
  }

  Future<void> chooseRange() async {
    final chosen = await showDateRangePicker(
      context: context,
      firstDate: DateTime(2020),
      lastDate: DateTime(2100),
      initialDateRange: from.isEmpty || to.isEmpty
          ? null
          : DateTimeRange(start: DateTime.parse(from), end: DateTime.parse(to)),
    );
    if (chosen != null && mounted) {
      setState(() {
        from = day(chosen.start.toIso8601String());
        to = day(chosen.end.toIso8601String());
        revision++;
      });
    }
  }

  @override
  Widget build(BuildContext context) => GuardianPage(
    title: localized(context, 'لوحة المالك', 'Owner dashboard'),
    actions: [
      IconButton(
        tooltip: localized(context, 'الحساب', 'Account'),
        onPressed: () => openPage(context, widget.accountPage),
        icon: const Icon(Icons.person_outline),
      ),
    ],
    child: currentIdentity == null
        ? const SizedBox.shrink()
        : RemoteBody(
            key: ValueKey(revision),
            auth: widget.auth,
            path:
                '/api/v1/reports/owner-summary?${Uri(queryParameters: {if (from.isNotEmpty) 'from': from, if (to.isNotEmpty) 'to': to, if (branch.isNotEmpty) 'branchId': branch}).query}',
            builder: (context, value) {
              final d = obj(value),
                  c = obj(d['collections']),
                  s = obj(d['subscriptions']),
                  p = d['paymentRequests'] is Map
                      ? obj(d['paymentRequests'])
                      : <String, dynamic>{
                          'total': d['payments']?['pending'],
                          'newEnrollments': null,
                          'renewals': null,
                          'products': null,
                        };
              final currency = d['currency'];
              final operations = d['operations'] is Map
                  ? obj(d['operations'])
                  : <String, dynamic>{};
              void requests([String type = '']) => openPage(
                context,
                NativePaymentRequests(
                  auth: widget.auth,
                  filters: {
                    if (type.isNotEmpty) 'type': type,
                    if (d['from'] != null) 'from': textOf(d['from']),
                    if (d['to'] != null) 'to': textOf(d['to']),
                    if (branch.isNotEmpty) 'branchId': branch,
                  },
                ),
              );
              void financial(String method, {bool daily = false}) => openPage(
                context,
                OwnerReports(
                  auth: widget.auth,
                  initialFilters: {
                    'from': daily
                        ? textOf(d['to'] ?? d['asOfDate'])
                        : textOf(
                            d['from'] ??
                                '${d['asOfDate'].toString().substring(0, 8)}01',
                          ),
                    'to': textOf(d['to'] ?? d['asOfDate']),
                    if (branch.isNotEmpty) 'branchId': branch,
                    if (method.isNotEmpty) 'provider': method,
                  },
                ),
              );
              return gList([
                Row(
                  children: [
                    const AcademyCrest(size: 44),
                    const SizedBox(width: 12),
                    Expanded(
                      child: Column(
                        crossAxisAlignment: CrossAxisAlignment.start,
                        children: [
                          UiText(
                            BrandScope.maybeOf(context)?.name ??
                                widget.auth.session!.selected!.academyName,
                            style: Theme.of(context).textTheme.titleLarge,
                          ),
                          UiText(
                            localized(
                              context,
                              'مرجع التحليل: ${day(d['asOfDate'])}',
                              'Analysis reference: ${day(d['asOfDate'])}',
                            ),
                            style: const TextStyle(fontSize: 14),
                          ),
                        ],
                      ),
                    ),
                  ],
                ),
                const SizedBox(height: 12),
                Row(
                  children: [
                    Expanded(
                      child: OutlinedButton.icon(
                        onPressed: chooseRange,
                        icon: const Icon(Icons.date_range, size: 18),
                        label: UiText(
                          '${d['from'] ?? from} — ${d['to'] ?? to}',
                          style: const TextStyle(fontSize: 11),
                        ),
                      ),
                    ),
                    TextButton(
                      onPressed: () => setState(() {
                        from = '';
                        to = '';
                        branch = '';
                        revision++;
                      }),
                      child: UiText(localized(context, 'مسح', 'Clear')),
                    ),
                  ],
                ),
                RemoteBody(
                  auth: widget.auth,
                  path: '/api/v1/manage/structure/options',
                  builder: (context, options) => Padding(
                    padding: const EdgeInsets.only(bottom: 14),
                    child: DropdownButtonFormField<String>(
                      initialValue: branch,
                      decoration: InputDecoration(
                        labelText: localized(context, 'الفرع', 'Branch'),
                      ),
                      items: [
                        DropdownMenuItem(
                          value: '',
                          child: UiText(
                            localized(context, 'كل الفروع', 'All branches'),
                          ),
                        ),
                        for (final b in rows(obj(options)['branches']))
                          DropdownMenuItem(
                            value: b['id'],
                            child: UiText(textOf(b['arabicName'])),
                          ),
                      ],
                      onChanged: (v) => setState(() {
                        branch = v ?? '';
                        revision++;
                      }),
                    ),
                  ),
                ),
                GCard(
                  onTap: requests,
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.stretch,
                    children: [
                      Row(
                        children: [
                          Expanded(
                            child: UiText(
                              localized(
                                context,
                                'طلبات الدفع',
                                'Payment requests',
                              ),
                              style: Theme.of(context).textTheme.titleLarge,
                            ),
                          ),
                          Container(
                            padding: const EdgeInsets.symmetric(
                              horizontal: 12,
                              vertical: 4,
                            ),
                            decoration: BoxDecoration(
                              color: AcademyTheme.red,
                              borderRadius: BorderRadius.circular(16),
                            ),
                            child: UiText(
                              textOf(p['total']),
                              style: const TextStyle(
                                color: Colors.white,
                                fontWeight: FontWeight.bold,
                              ),
                            ),
                          ),
                        ],
                      ),
                      const SizedBox(height: 14),
                      Row(
                        children: [
                          for (final x in [
                            (
                              'اشتراكات جديدة',
                              'New',
                              p['newEnrollments'],
                              'Enrollment',
                            ),
                            ('تجديد', 'Renewals', p['renewals'], 'Renewal'),
                            ('منتجات', 'Products', p['products'], 'Product'),
                            if (p['tournaments'] != null &&
                                p['tournaments'] != 0)
                              (
                                'بطولات',
                                'Tournaments',
                                p['tournaments'],
                                'Tournament',
                              ),
                          ])
                            Expanded(
                              child: InkWell(
                                key: ValueKey('owner-request-${x.$4}'),
                                onTap: () => requests(x.$4),
                                child: Column(
                                  children: [
                                    UiText(
                                      textOf(x.$3),
                                      style: TextStyle(
                                        fontSize: 22,
                                        color: Theme.of(context)
                                            .colorScheme
                                            .primary,
                                        fontWeight: FontWeight.bold,
                                      ),
                                    ),
                                    UiText(
                                      localized(context, x.$1, x.$2),
                                      style: const TextStyle(fontSize: 11),
                                    ),
                                  ],
                                ),
                              ),
                            ),
                        ],
                      ),
                    ],
                  ),
                ),
                if (operations.isNotEmpty)
                  OwnerDecisionsPanel(
                    auth: widget.auth,
                    operations: operations,
                    branchId: branch,
                    onReturn: () {
                      if (mounted && currentIdentity != null) {
                        setState(() {
                          revision++;
                        });
                      }
                    },
                  ),
                for (final x in [
                  (
                    'تحصيل اليوم',
                    'Today’s collections',
                    c['today'],
                    c['todayCash'],
                    c['todayOnline'],
                    true,
                    c['todayUnspecified'],
                  ),
                  (
                    'تحصيل الشهر',
                    'Period collections',
                    c['month'],
                    c['monthCash'],
                    c['monthOnline'],
                    false,
                    c['monthUnspecified'],
                  ),
                ])
                  GCard(
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.stretch,
                      children: [
                        UiText(
                          localized(context, x.$1, x.$2),
                          style: Theme.of(context).textTheme.titleMedium,
                        ),
                        InkWell(
                          onTap: () => financial('', daily: x.$6),
                          child: Padding(
                            padding: const EdgeInsets.symmetric(vertical: 12),
                            child: UiText(
                              money(x.$3, currency),
                              style: TextStyle(
                                fontSize: 30,
                                color: Theme.of(context).colorScheme.primary,
                                fontWeight: FontWeight.w800,
                              ),
                            ),
                          ),
                        ),
                        Row(
                          children: [
                            Expanded(
                              child: OutlinedButton(
                                style: OutlinedButton.styleFrom(
                                  padding: const EdgeInsets.symmetric(
                                    horizontal: 12,
                                    vertical: 12,
                                  ),
                                ),
                                onPressed: () => financial('Cash', daily: x.$6),
                                child: UiText(
                                  '${localized(context, 'نقدي', 'Cash')}\n${x.$4 == null ? '—' : money(x.$4, currency)}',
                                  textAlign: TextAlign.center,
                                ),
                              ),
                            ),
                            const SizedBox(width: 10),
                            Expanded(
                              child: OutlinedButton(
                                style: OutlinedButton.styleFrom(
                                  padding: const EdgeInsets.symmetric(
                                    horizontal: 12,
                                    vertical: 12,
                                  ),
                                ),
                                onPressed: () =>
                                    financial('Online', daily: x.$6),
                                child: UiText(
                                  '${localized(context, 'إلكتروني', 'Online')}\n${x.$5 == null ? '—' : money(x.$5, currency)}',
                                  textAlign: TextAlign.center,
                                ),
                              ),
                            ),
                          ],
                        ),
                        if (x.$7 != null &&
                            !RegExp(r'^-?0+(\.0+)?$').hasMatch(textOf(x.$7)))
                          TextButton(
                            onPressed: () =>
                                financial('Unspecified', daily: x.$6),
                            child: UiText(
                              '${localized(context, 'طريقة صرف قديمة غير موثقة', 'Historical payout method unrecorded')}: ${money(x.$7, currency)}',
                            ),
                          ),
                        UiText(
                          localized(
                            context,
                            'صافي التحصيل بعد الاسترداد',
                            'Net collections after refunds',
                          ),
                          style: Theme.of(context).textTheme.labelSmall,
                        ),
                      ],
                    ),
                  ),
                GCard(
                  child: Row(
                    children: [
                      Expanded(
                        child: Column(
                          children: [
                            UiText(
                              localized(
                                context,
                                'الاشتراكات الفعالة',
                                'Active subscriptions',
                              ),
                            ),
                            UiText(
                              textOf(s['active']),
                              style: const TextStyle(
                                fontSize: 30,
                                fontWeight: FontWeight.bold,
                              ),
                            ),
                          ],
                        ),
                      ),
                      Expanded(
                        child: Column(
                          children: [
                            UiText(
                              localized(
                                context,
                                'تنتهي قريبًا',
                                'Expiring soon',
                              ),
                            ),
                            UiText(
                              textOf(s['expiring']),
                              style: TextStyle(
                                fontSize: 30,
                                color: Theme.of(context).colorScheme.primary,
                                fontWeight: FontWeight.bold,
                              ),
                            ),
                          ],
                        ),
                      ),
                    ],
                  ),
                ),
                Wrap(
                  spacing: 12,
                  runSpacing: 12,
                  children: [
                    for (final x in [
                      (
                        'اللاعبون النشطون',
                        'Active players',
                        d['activePlayers'],
                      ),
                      ('المدربون', 'Coaches', d['coaches']),
                      (
                        'اشتراكات جديدة',
                        'New subscriptions',
                        d['newSubscriptions'],
                      ),
                    ])
                      Chip(
                        label: UiText(
                          '${localized(context, x.$1, x.$2)} · ${textOf(x.$3)}',
                        ),
                      ),
                  ],
                ),
                if (operations.isNotEmpty) ...[
                  OwnerOperationsPanel(
                    auth: widget.auth,
                    onReturn: () {
                      if (mounted && currentIdentity != null) {
                        setState(() {
                          revision++;
                        });
                      }
                    },
                    operations: operations,
                    filters: {
                      if (d['from'] != null) 'from': textOf(d['from']),
                      if (d['to'] != null) 'to': textOf(d['to']),
                      if (branch.isNotEmpty) 'branchId': branch,
                    },
                  ),
                  GCard(
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        const UiText('مصروفات معتمدة لم تُرحّل بعد'),
                        for (final expense in rows(
                          operations['pendingExpenses'],
                        ))
                          UiText(
                            '${expense['count']} قيد · ${money(expense['amount'], expense['currency'])}',
                          ),
                        if (rows(operations['pendingExpenses']).isEmpty)
                          const UiText('لا توجد مصروفات معلقة.'),
                      ],
                    ),
                  ),
                ],
                OwnerVipMembers(auth: widget.auth, branch: branch, asOf: to),
                OwnerCoachRatingLeaders(
                  auth: widget.auth,
                  branch: branch,
                  from: from,
                  to: to,
                ),
                NewsStrip(auth: widget.auth),
                NextMatchStrip(auth: widget.auth),
                FilledButton.icon(
                  onPressed: () =>
                      openPage(context, OwnerReports(auth: widget.auth)),
                  icon: const Icon(Icons.analytics_outlined),
                  label: UiText(localized(context, 'التقارير', 'Reports')),
                ),
              ]);
            },
          ),
  );
}
