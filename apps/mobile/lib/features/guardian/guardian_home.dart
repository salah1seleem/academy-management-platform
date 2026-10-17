import 'package:academy_mobile/core/theme/ui_text.dart';
import 'package:flutter/material.dart';

import '../../core/auth/auth_controller.dart';
import '../../core/theme/appearance.dart';
import 'guardian_ui.dart';
import 'guardian_profile.dart';
import 'guardian_payments.dart';
import 'guardian_report.dart';
import 'daily_nutrition.dart';
import 'enrollment_requests.dart';
import '../shared/academy_news.dart';
import '../shared/community_ads.dart';
import 'sport_store.dart';
import 'tournament_fees.dart';
import 'guardian_day_pass.dart';
import '../shared/matches.dart';
import '../shared/receivable_balances.dart';

class GuardianHome extends StatefulWidget {
  final AuthController auth;
  final String environment;
  final Widget accountPage;
  const GuardianHome({
    super.key,
    required this.auth,
    required this.environment,
    required this.accountPage,
  });
  @override
  State<GuardianHome> createState() => _GuardianHomeState();
}

class _GuardianHomeState extends State<GuardianHome> {
  String? selectedPlayer, identity;
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
      selectedPlayer = null;
    });
  }

  @override
  void didUpdateWidget(GuardianHome old) {
    super.didUpdateWidget(old);
    if (old.auth != widget.auth) {
      old.auth.removeListener(contextChanged);
      widget.auth.addListener(contextChanged);
      identity = currentIdentity;
      selectedPlayer = null;
    }
  }

  @override
  void dispose() {
    widget.auth.removeListener(contextChanged);
    super.dispose();
  }

  void open(Widget page) => openPage(context, page);
  @override
  Widget build(BuildContext context) => GuardianPage(
    title: localized(context, 'الرئيسية', 'Home'),
    actions: [
      IconButton(
        tooltip: localized(context, 'الحساب', 'Account'),
        onPressed: () => open(widget.accountPage),
        icon: const Icon(Icons.person_outline),
      ),
    ],
    child: currentIdentity == null
        ? const SizedBox.shrink()
        : RemoteBody(
            key: ValueKey(identity),
            auth: widget.auth,
            path: '/api/v1/guardian/home',
            builder: (context, value) {
              final home = obj(value), allChildren = rows(home['children']);
              final selected = allChildren.any((c) => c['id'] == selectedPlayer)
                  ? selectedPlayer
                  : null;
              final children = allChildren
                  .where((c) => selected == null || c['id'] == selected)
                  .toList();
              final nutritionToday = textOf(
                home['trainingToday'] ?? home['today'],
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
                            textOf(home['academyName']),
                            style: const TextStyle(
                              fontSize: 20,
                              fontWeight: FontWeight.w800,
                            ),
                          ),
                          UiText(
                            localized(
                              context,
                              'أهلًا، ${home['guardianName']}',
                              'Welcome, ${home['guardianName']}',
                            ),
                            style: const TextStyle(fontSize: 14),
                          ),
                        ],
                      ),
                    ),
                  ],
                ),
                GuardianUrgentMatchAlerts(
                  auth: widget.auth,
                  alerts: rows(home['urgentAlerts'] ?? <Json>[]),
                ),
                GuardianSponsoredCard(auth: widget.auth),
                Heading(localized(context, 'أبنائي', 'My children')),
                if (allChildren.length > 1) ...[
                  DropdownButtonFormField<String>(
                    key: ValueKey(
                      'guardian-child-selector-${selected ?? 'all'}',
                    ),
                    initialValue: selected ?? '',
                    isExpanded: true,
                    decoration: InputDecoration(
                      labelText: localized(
                        context,
                        'متابعة طفل',
                        'Focus on a child',
                      ),
                      prefixIcon: const Icon(Icons.family_restroom_outlined),
                    ),
                    items: [
                      DropdownMenuItem(
                        value: '',
                        child: UiText(
                          localized(context, 'كل الأبناء', 'All children'),
                        ),
                      ),
                      for (final c in allChildren)
                        DropdownMenuItem(
                          value: textOf(c['id']),
                          child: UiText(
                            textOf(c['arabicName']),
                            maxLines: 1,
                            overflow: TextOverflow.ellipsis,
                          ),
                        ),
                    ],
                    onChanged: (v) => setState(() {
                      selectedPlayer = v == '' ? null : v;
                    }),
                  ),
                  const SizedBox(height: 16),
                ],
                if (children.isEmpty)
                  const EmptyMessage('لا يوجد أبناء مرتبطون بهذا الحساب.'),
                for (final child in children) ...[
                  PremiumChildCard(
                    auth: widget.auth,
                    childData: child,
                    today: textOf(home['today']),
                    showSubscription: false,
                    onTap: () => open(
                      ChildProfile(
                        auth: widget.auth,
                        environment: widget.environment,
                        playerId: child['id'],
                        today: home['today'],
                      ),
                    ),
                  ),
                  for (final sport in rows(child['sports']))
                    if (sport['nextSession'] is Map)
                      NextSessionCard(
                        childName: textOf(child['arabicName']),
                        sport: sport,
                        onTap: () => open(
                          ProfileRecords(
                            auth: widget.auth,
                            playerId: child['id'],
                            section: 'schedule',
                          ),
                        ),
                      ),
                  if (!rows(child['sports'])
                      .any((s) => s['nextSession'] is Map))
                    Padding(
                      padding: const EdgeInsets.only(bottom: 14),
                      child: UiText(
                        localized(
                          context,
                          'لا يوجد موعد تدريب قادم منشور لهذا الطفل.',
                          'No upcoming training session published for this child.',
                        ),
                        style: const TextStyle(fontSize: 14),
                      ),
                    ),
                  GuardianSubscriptionSummary(
                    auth: widget.auth,
                    environment: widget.environment,
                    childData: child,
                  ),
                  GuardianLatestProgress(auth: widget.auth, childData: child),
                  DailyNutritionCard(
                    key: ValueKey('home-nutrition-${child['id']}'),
                    auth: widget.auth,
                    playerId: child['id'],
                    playerName: child['arabicName'],
                    today: nutritionToday,
                  ),
                  const SizedBox(height: 18),
                ],
                Heading(localized(context, 'خدمات الأسرة', 'Family services')),
                LayoutBuilder(
                  builder: (context, bounds) => Wrap(
                    spacing: 10,
                    runSpacing: 10,
                    children: [
                      homeAction(
                        context,
                        bounds,
                        'اشتراك جديد',
                        'New enrollment',
                        Icons.person_add_alt,
                        () => open(
                          EnrollmentForm(
                            auth: widget.auth,
                            environment: widget.environment,
                          ),
                        ),
                      ),
                      homeAction(
                        context,
                        bounds,
                        'تجديد الاشتراك',
                        'Renew',
                        Icons.autorenew,
                        () => open(
                          RenewalPicker(
                            auth: widget.auth,
                            environment: widget.environment,
                          ),
                        ),
                      ),
                      homeAction(
                        context,
                        bounds,
                        'تجديد اشتراك لغيره',
                        'Renew for someone',
                        Icons.card_giftcard,
                        () => open(
                          ExternalRenewal(
                            auth: widget.auth,
                            environment: widget.environment,
                          ),
                        ),
                      ),
                      homeAction(
                        context,
                        bounds,
                        'الباقات',
                        'Plans',
                        Icons.layers_outlined,
                        () => open(
                          RenewalPicker(
                            auth: widget.auth,
                            environment: widget.environment,
                          ),
                        ),
                      ),
                      homeAction(
                        context,
                        bounds,
                        'المتجر',
                        'Store',
                        Icons.shopping_bag_outlined,
                        () => open(
                          GuardianPage(
                            title: localized(
                              context,
                              'المنتجات الرياضية',
                              'Sports store',
                            ),
                            child: gList([CatalogSections(auth: widget.auth)]),
                          ),
                        ),
                      ),
                      homeAction(
                        context,
                        bounds,
                        'البطولات',
                        'Tournaments',
                        Icons.emoji_events_outlined,
                        () => open(TournamentFees(auth: widget.auth)),
                      ),
                      homeAction(
                        context,
                        bounds,
                        'زيارة يومية',
                        'Day pass',
                        Icons.confirmation_number_outlined,
                        () => open(GuardianDayPassPage(auth: widget.auth)),
                      ),
                    ],
                  ),
                ),
                const SizedBox(height: 18),
                NextMatchStrip(auth: widget.auth),
                NewsStrip(auth: widget.auth),
                Heading(
                  localized(context, 'منتجات الرياضة', 'Sports products'),
                ),
                CatalogSections(auth: widget.auth),
                GLink(
                  localized(context, 'طلبات الاشتراك', 'Enrollment requests'),
                  Icons.inbox_outlined,
                  () => open(
                    EnrollmentRequests(
                      auth: widget.auth,
                      environment: widget.environment,
                    ),
                  ),
                ),
                GLink(
                  localized(context, 'إيصالاتي', 'My receipts'),
                  Icons.receipt_long_outlined,
                  () => open(ReceiptsPage(auth: widget.auth)),
                ),
                GLink(
                  localized(context, 'أرصدة الأسرة', 'Family balances'),
                  Icons.account_balance_wallet_outlined,
                  () => open(ReceivableBalancesPage(auth: widget.auth)),
                ),
              ]);
            },
          ),
  );
  Widget homeAction(
    BuildContext context,
    BoxConstraints bounds,
    String ar,
    String en,
    IconData icon,
    VoidCallback action,
  ) => SizedBox(
    width: (bounds.maxWidth - 10) / 2,
    child: Material(
      color: Theme.of(context).colorScheme.surface,
      borderRadius: BorderRadius.circular(18),
      child: InkWell(
        key: ValueKey('guardian-action-$ar'),
        onTap: action,
        borderRadius: BorderRadius.circular(18),
        child: Padding(
          padding: const EdgeInsets.all(14),
          child: Column(
            children: [
              Icon(
                icon,
                color: Theme.of(context).colorScheme.primary,
                size: 26,
              ),
              const SizedBox(height: 8),
              UiText(
                localized(context, ar, en),
                textAlign: TextAlign.center,
                style: const TextStyle(
                  fontSize: 14,
                  fontWeight: FontWeight.w700,
                ),
              ),
            ],
          ),
        ),
      ),
    ),
  );
}

class GuardianSubscriptionSummary extends StatelessWidget {
  final AuthController auth;
  final String environment;
  final Json childData;
  const GuardianSubscriptionSummary({
    super.key,
    required this.auth,
    required this.environment,
    required this.childData,
  });
  @override
  Widget build(BuildContext context) => Column(
    crossAxisAlignment: CrossAxisAlignment.stretch,
    children: [
      for (final sport in rows(childData['sports']))
        GCard(
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              UiText(
                localized(
                  context,
                  'الاشتراك · ${sport['sport']}',
                  'Subscription · ${sport['sport']}',
                ),
                style: const TextStyle(
                  fontSize: 16,
                  fontWeight: FontWeight.w700,
                ),
              ),
              const SizedBox(height: 8),
              if (sport['period'] is Map) ...[
                StatusPill(sport['period']['status'], fontSize: 14),
                const SizedBox(height: 6),
                if (sport['period']['plan'] != null)
                  UiText(
                    textOf(sport['period']['plan']),
                    style: const TextStyle(fontSize: 14),
                  ),
                UiText(
                  '${day(sport['period']['startDate'])} — ${day(sport['period']['endDate'])}',
                  style: const TextStyle(fontSize: 14),
                  textDirection: TextDirection.ltr,
                ),
              ] else
                UiText(
                  localized(
                    context,
                    'لا يوجد اشتراك فعال',
                    'No active subscription',
                  ),
                  style: const TextStyle(fontSize: 14),
                ),
              if (sport['id'] != null &&
                  (sport['period'] == null ||
                      [
                        'Expired',
                        'Expiring',
                      ].contains(sport['period']['status']))) ...[
                const SizedBox(height: 12),
                FilledButton.icon(
                  key: ValueKey('home-renew-${sport['id']}'),
                  onPressed: () => openPage(
                    context,
                    PlanPicker(
                      auth: auth,
                      environment: environment,
                      enrollmentId: textOf(sport['id']),
                      childName: textOf(childData['arabicName']),
                    ),
                  ),
                  icon: const Icon(Icons.autorenew),
                  label: UiText(
                    localized(
                      context,
                      'تجديد · ${childData['arabicName']}',
                      'Renew · ${childData['arabicName']}',
                    ),
                  ),
                ),
              ],
            ],
          ),
        ),
    ],
  );
}

class GuardianLatestProgress extends StatelessWidget {
  final AuthController auth;
  final Json childData;
  const GuardianLatestProgress({
    super.key,
    required this.auth,
    required this.childData,
  });
  @override
  Widget build(BuildContext context) {
    final power = childData['power'];
    final latest =
        childData['latestEvaluation'] ??
        (power is Map && power['evaluationId'] != null
            ? {
                'id': power['evaluationId'],
                'overallScore': power['overallScore'],
              }
            : null);
    return GCard(
      onTap: latest is Map
          ? () => openPage(
              context,
              PlayerReport(auth: auth, id: textOf(latest['id'])),
            )
          : () => openPage(
              context,
              ReportHistory(auth: auth, playerId: textOf(childData['id'])),
            ),
      child: Row(
        children: [
          Icon(
            Icons.insights_outlined,
            color: Theme.of(context).colorScheme.primary,
          ),
          const SizedBox(width: 12),
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                UiText(
                  localized(
                    context,
                    'تطور ${childData['arabicName']}',
                    '${childData['arabicName']}’s progress',
                  ),
                  style: const TextStyle(
                    fontSize: 16,
                    fontWeight: FontWeight.w700,
                  ),
                ),
                UiText(
                  latest is Map
                      ? latest['evaluationDate'] == null
                            ? localized(
                                context,
                                'عرض آخر تقييم منشور',
                                'Open latest published report',
                              )
                            : '${latest['sport']} · ${day(latest['evaluationDate'])}'
                      : localized(
                          context,
                          'لا يوجد تقييم منشور بعد',
                          'No published report yet',
                        ),
                  style: const TextStyle(fontSize: 14),
                ),
              ],
            ),
          ),
          if (latest is Map && latest['overallScore'] is num)
            Padding(
              padding: const EdgeInsets.symmetric(horizontal: 8),
              child: UiText(
                '${(latest['overallScore'] as num).round()}',
                style: TextStyle(
                  fontSize: 24,
                  fontWeight: FontWeight.w800,
                  color: Theme.of(context).colorScheme.primary,
                ),
              ),
            ),
          const Icon(Icons.chevron_left),
        ],
      ),
    );
  }
}

class PremiumChildCard extends StatelessWidget {
  final bool showSubscription;
  final AuthController auth;
  final Json childData;
  final String today;
  final VoidCallback onTap;
  const PremiumChildCard({
    super.key,
    required this.auth,
    required this.childData,
    required this.today,
    required this.onTap,
    this.showSubscription = true,
  });
  @override
  Widget build(BuildContext context) {
    final c = childData;
    return Container(
      key: ValueKey('child-${c['id']}'),
      margin: const EdgeInsets.only(bottom: 14),
      decoration: BoxDecoration(
        borderRadius: BorderRadius.circular(22),
        gradient: LinearGradient(
          colors: [
            Color.alphaBlend(
              Theme.of(context).colorScheme.primary.withValues(alpha: .08),
              Theme.of(context).colorScheme.surface,
            ),
            Theme.of(context).colorScheme.surface,
          ],
        ),
        border: Border.all(
          color: Theme.of(context).colorScheme.primary.withValues(alpha: .22),
        ),
      ),
      child: InkWell(
        borderRadius: BorderRadius.circular(22),
        onTap: onTap,
        child: Padding(
          padding: const EdgeInsets.all(12),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Row(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  PlayerAvatar(
                    textOf(c['arabicName']),
                    auth: auth,
                    reference: c['photoReference'],
                    size: 48,
                  ),
                  const SizedBox(width: 8),
                  Expanded(
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        UiText(
                          textOf(c['arabicName']),
                          style: TextStyle(
                            fontSize: 16,
                            fontWeight: FontWeight.w800,
                          ),
                        ),
                        UiText(
                          localized(
                            context,
                            '${ageOn(c['dateOfBirth'], today) ?? '—'} سنوات',
                            '${ageOn(c['dateOfBirth'], today) ?? '—'} years',
                          ),
                          style: const TextStyle(fontSize: 14),
                        ),
                        if (c['power'] is Map)
                          UiText(
                            (c['power']['overallScore'] as num?)
                                    ?.round()
                                    .toString() ??
                                '—',
                            style: TextStyle(
                              fontSize: 30,
                              fontWeight: FontWeight.w900,
                              color: Theme.of(context).colorScheme.primary,
                            ),
                          ),
                        if (c['power'] is Map)
                          UiText(
                            localized(
                              context,
                              'آخر تقييم منشور',
                              'Latest published report',
                            ),
                            style: const TextStyle(fontSize: 14),
                          ),
                      ],
                    ),
                  ),
                  if (c['power'] is Map)
                    SizedBox(
                      width: 105,
                      height: 105,
                      child: RadarChart(
                        rows(c['power']['axes']),
                        compact: true,
                      ),
                    )
                  else
                    Icon(
                      Icons.chevron_left,
                      color: Theme.of(context).colorScheme.primary,
                    ),
                ],
              ),
              for (final s in rows(c['sports']))
                UiText(
                  '${s['sport']} · ${s['branch'] ?? ''}',
                  style: const TextStyle(fontSize: 14),
                ),
              const SizedBox(height: 8),
              for (final sport in rows(
                c['sports'],
              ).where((_) => showSubscription))
                Wrap(
                  spacing: 8,
                  crossAxisAlignment: WrapCrossAlignment.center,
                  children: [
                    if (sport['period'] != null) ...[
                      StatusPill(sport['period']['status']),
                      UiText(
                        '${day(sport['period']['startDate'])} — ${day(sport['period']['endDate'])}',
                        style: Theme.of(context).textTheme.labelSmall,
                      ),
                    ] else
                      UiText(
                        localized(
                          context,
                          'لا يوجد اشتراك فعال',
                          'No active subscription',
                        ),
                      ),
                  ],
                ),
              const SizedBox(height: 6),
              UiText(
                localized(context, 'عرض الملف', 'View profile'),
                style: TextStyle(
                  color: Theme.of(context).colorScheme.primary,
                  fontSize: 14,
                ),
              ),
            ],
          ),
        ),
      ),
    );
  }
}

class NextSessionCard extends StatelessWidget {
  final String childName;
  final Json sport;
  final VoidCallback onTap;
  const NextSessionCard({
    super.key,
    required this.childName,
    required this.sport,
    required this.onTap,
  });
  @override
  Widget build(BuildContext context) {
    final session = obj(sport['nextSession']),
        date = DateTime.tryParse(textOf(session['sessionDate']));
    return GCard(
      onTap: onTap,
      child: Row(
        children: [
          Container(
            width: 70,
            padding: const EdgeInsets.all(8),
            decoration: BoxDecoration(
              color: Theme.of(context).colorScheme.primary
                  .withValues(alpha: .12),
              borderRadius: BorderRadius.circular(12),
            ),
            child: Column(
              children: [
                UiText(
                  date == null
                      ? '—'
                      : localized(
                          context,
                          label(
                            [
                              'Sunday',
                              'Monday',
                              'Tuesday',
                              'Wednesday',
                              'Thursday',
                              'Friday',
                              'Saturday',
                            ][date.weekday % 7],
                          ),
                          [
                            'Sun',
                            'Mon',
                            'Tue',
                            'Wed',
                            'Thu',
                            'Fri',
                            'Sat',
                          ][date.weekday % 7],
                        ),
                  style: TextStyle(fontSize: 14),
                ),
                UiText(
                  date?.day.toString() ?? '—',
                  style: TextStyle(
                    fontSize: 24,
                    color: Theme.of(context).colorScheme.primary,
                    fontWeight: FontWeight.bold,
                  ),
                ),
              ],
            ),
          ),
          const SizedBox(width: 12),
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                UiText(
                  localized(
                    context,
                    'الحصة القادمة · $childName',
                    'Next session · $childName',
                  ),
                  style: TextStyle(fontWeight: FontWeight.bold),
                ),
                UiText(
                  '${textOf(session['startTime']).split(':').take(2).join(':')} — ${textOf(session['endTime']).split(':').take(2).join(':')}',
                  textDirection: TextDirection.ltr,
                ),
                UiText(
                  day(session['sessionDate']),
                  style: const TextStyle(fontSize: 14),
                ),
                UiText(
                  '${session['group'] ?? sport['group']} · ${sport['branch']}',
                  style: const TextStyle(fontSize: 14),
                ),
                if (session['resource'] != null)
                  UiText(
                    textOf(session['resource']),
                    style: const TextStyle(fontSize: 14),
                  ),
              ],
            ),
          ),
          Icon(Icons.chevron_left),
        ],
      ),
    );
  }
}

class CatalogSections extends StatefulWidget {
  final AuthController auth;
  const CatalogSections({super.key, required this.auth});
  @override
  State<CatalogSections> createState() => _CatalogSectionsState();
}

class _CatalogSectionsState extends State<CatalogSections> {
  late Future<Object?> request;
  @override
  void initState() {
    super.initState();
    request = load();
  }

  Future<Object?> load() =>
      widget.auth.request('GET', '/api/v1/guardian/catalog');
  @override
  Widget build(BuildContext context) => FutureBuilder(
    future: request,
    builder: (context, snapshot) {
      if (snapshot.hasError) {
        return TextButton(
          onPressed: () => setState(() {
            request = load();
          }),
          child: const UiText('تعذر تحميل الكتالوج · إعادة المحاولة'),
        );
      }
      if (!snapshot.hasData) {
        return const Padding(
          padding: EdgeInsets.all(20),
          child: LinearProgressIndicator(),
        );
      }
      final sections = rows(snapshot.data);
      return Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          if (sections.isEmpty)
            const EmptyMessage('لا توجد منتجات منشورة لرياضات الأبناء.'),
          for (final section in sections) ...[
            Heading(textOf(section['sport'])),
            SizedBox(
              height: 260,
              child: ListView.separated(
                scrollDirection: Axis.horizontal,
                itemCount: rows(section['items']).length,
                separatorBuilder: (_, index) => const SizedBox(width: 12),
                itemBuilder: (context, index) {
                  final item = rows(section['items'])[index];
                  final base = (item['displayPrice'] as num?)?.toDouble();
                  final price = base == null
                      ? null
                      : base *
                            (100 - (item['discountPercentage'] as num? ?? 0)) /
                            100;
                  return SizedBox(
                    width: 166,
                    child: GCard(
                      child: Column(
                        crossAxisAlignment: CrossAxisAlignment.stretch,
                        children: [
                          ClipRRect(
                            borderRadius: BorderRadius.circular(12),
                            child: DemoImage(
                              item['imageReference'],
                              height: 106,
                            ),
                          ),
                          const SizedBox(height: 8),
                          UiText(
                            textOf(item['arabicName']),
                            maxLines: 2,
                            overflow: TextOverflow.ellipsis,
                            style: TextStyle(
                              fontSize: 13,
                              fontWeight: FontWeight.bold,
                            ),
                          ),
                          const Spacer(),
                          if (price != null)
                            UiText(
                              money(price, item['currency']),
                              style: TextStyle(
                                color: Theme.of(context).colorScheme.primary,
                                fontWeight: FontWeight.bold,
                              ),
                            ),
                          FilledButton(
                            onPressed:
                                price != null &&
                                    (item['stockQuantity'] as num? ?? 0) > 0
                                ? () => openPage(
                                    context,
                                    ProductPurchase(
                                      auth: widget.auth,
                                      product: {
                                        ...item,
                                        'sportId': section['sportId'],
                                      },
                                    ),
                                  )
                                : null,
                            child: UiText(localized(context, 'شراء', 'Buy')),
                          ),
                        ],
                      ),
                    ),
                  );
                },
              ),
            ),
          ],
        ],
      );
    },
  );
}
