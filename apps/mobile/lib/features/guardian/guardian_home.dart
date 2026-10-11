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
import 'sport_store.dart';
import 'tournament_fees.dart';
import '../shared/matches.dart';

class GuardianHome extends StatelessWidget {
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
  Widget build(BuildContext context) => GuardianPage(
    title: localized(context, 'الأكاديمية', 'Academy'),
    actions: [
      IconButton(
        tooltip: localized(context, 'الحساب', 'Account'),
        onPressed: () => openPage(context, accountPage),
        icon: Icon(
          Icons.person_outline,
          color: Theme.of(context).colorScheme.primary,
        ),
      ),
    ],
    child: RemoteBody(
      auth: auth,
      path: '/api/v1/guardian/home',
      builder: (context, value) {
        final home = obj(value), children = rows(home['children']);
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
                      localized(
                        context,
                        'أهلًا، ${home['guardianName']}',
                        'Welcome, ${home['guardianName']}',
                      ),
                      style: Theme.of(context).textTheme.titleLarge
                          ?.copyWith(fontWeight: FontWeight.w800),
                    ),
                    UiText(
                      textOf(home['academyName']),
                      style: Theme.of(context).textTheme.bodySmall,
                    ),
                  ],
                ),
              ),
            ],
          ),
          NewsStrip(auth: auth),
          Heading(localized(context, 'أبنائي', 'My children')),
          if (children.isEmpty)
            const EmptyMessage('لا يوجد أبناء مرتبطون بهذا الحساب.'),
          for (final child in children) ...[
            PremiumChildCard(
              auth: auth,
              childData: child,
              today: textOf(home['today']),
              onTap: () => openPage(
                context,
                ChildProfile(
                  auth: auth,
                  environment: environment,
                  playerId: child['id'],
                  today: home['today'],
                ),
              ),
            ),
            DailyNutritionCard(
              auth: auth,
              playerId: child['id'],
              playerName: child['arabicName'],
              today: home['today'],
            ),
          ],
          NextMatchStrip(auth: auth),
          const SizedBox(height: 8),
          Wrap(
            spacing: 8,
            runSpacing: 8,
            children: [
              homeAction(
                context,
                'اشتراك جديد',
                'New enrollment',
                Icons.person_add_alt,
                () => openPage(
                  context,
                  EnrollmentForm(auth: auth, environment: environment),
                ),
              ),
              homeAction(
                context,
                'تجديد الاشتراك',
                'Renew',
                Icons.autorenew,
                () => openPage(
                  context,
                  RenewalPicker(auth: auth, environment: environment),
                ),
              ),
              homeAction(
                context,
                'تجديد اشتراك لغيره',
                'Renew for someone',
                Icons.card_giftcard,
                () => openPage(
                  context,
                  ExternalRenewal(auth: auth, environment: environment),
                ),
              ),
              homeAction(
                context,
                'الباقات',
                'Plans',
                Icons.layers_outlined,
                () => openPage(
                  context,
                  RenewalPicker(auth: auth, environment: environment),
                ),
              ),
              homeAction(
                context,
                'المتجر',
                'Store',
                Icons.shopping_bag_outlined,
                () => openPage(
                  context,
                  GuardianPage(
                    title: localized(
                      context,
                      'المنتجات الرياضية',
                      'Sports store',
                    ),
                    child: gList([CatalogSections(auth: auth)]),
                  ),
                ),
              ),
              homeAction(
                context,
                'البطولات',
                'Tournaments',
                Icons.emoji_events_outlined,
                () => openPage(context, TournamentFees(auth: auth)),
              ),
            ],
          ),
          const SizedBox(height: 18),
          for (final child in children) ...[
            for (final sport in rows(child['sports']))
              if (sport['nextSession'] is Map)
                NextSessionCard(
                  childName: textOf(child['arabicName']),
                  sport: sport,
                  onTap: () => openPage(
                    context,
                    ProfileRecords(
                      auth: auth,
                      playerId: child['id'],
                      section: 'schedule',
                    ),
                  ),
                ),
          ],
          Heading(localized(context, 'منتجات الرياضة', 'Sports products')),
          CatalogSections(auth: auth),
          GLink(
            localized(context, 'طلبات الاشتراك', 'Enrollment requests'),
            Icons.inbox_outlined,
            () => openPage(
              context,
              EnrollmentRequests(auth: auth, environment: environment),
            ),
          ),
          GLink(
            localized(context, 'إيصالاتي', 'My receipts'),
            Icons.receipt_long_outlined,
            () => openPage(context, ReceiptsPage(auth: auth)),
          ),
        ]);
      },
    ),
  );
  Widget homeAction(
    BuildContext context,
    String ar,
    String en,
    IconData icon,
    VoidCallback action,
  ) => SizedBox(
    width: 100,
    child: InkWell(
      key: ValueKey('guardian-action-$ar'),
      onTap: action,
      borderRadius: BorderRadius.circular(18),
      child: Container(
        padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 12),
        decoration: BoxDecoration(
          color: Theme.of(context).colorScheme.surface,
          border: Border.all(
            color: Theme.of(context).colorScheme.primary.withValues(alpha: .16),
          ),
          borderRadius: BorderRadius.circular(18),
        ),
        child: Column(
          children: [
            Icon(icon, color: Theme.of(context).colorScheme.primary, size: 25),
            const SizedBox(height: 7),
            UiText(
              localized(context, ar, en),
              textAlign: TextAlign.center,
              style: TextStyle(fontSize: 11, fontWeight: FontWeight.bold),
            ),
          ],
        ),
      ),
    ),
  );
}

class PremiumChildCard extends StatelessWidget {
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
          colors: Theme.of(context).brightness == Brightness.dark
              ? const [Color(0xFF242432), Color(0xFF181A2A)]
              : const [Color(0xFFFFFFFF), Color(0xFFF0E9D8)],
        ),
        border: Border.all(
          color: Theme.of(context).colorScheme.primary.withValues(alpha: .22),
        ),
      ),
      child: InkWell(
        borderRadius: BorderRadius.circular(22),
        onTap: onTap,
        child: Padding(
          padding: const EdgeInsets.all(16),
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
                            fontSize: 14,
                            fontWeight: FontWeight.w800,
                          ),
                        ),
                        UiText(
                          localized(
                            context,
                            '${ageOn(c['dateOfBirth'], today) ?? '—'} سنوات',
                            '${ageOn(c['dateOfBirth'], today) ?? '—'} years',
                          ),
                          style: Theme.of(context).textTheme.bodySmall,
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
                            style: Theme.of(context).textTheme.labelSmall,
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
                  style: Theme.of(context).textTheme.bodySmall,
                ),
              const SizedBox(height: 8),
              for (final sport in rows(c['sports']))
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
                  fontSize: 12,
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
            width: 54,
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
                  style: TextStyle(fontSize: 10),
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
                  '${textOf(session['startTime']).substring(0, 5)} — ${textOf(session['endTime']).substring(0, 5)}',
                ),
                UiText(
                  '${sport['group']} · ${sport['branch']}',
                  style: Theme.of(context).textTheme.labelSmall,
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
