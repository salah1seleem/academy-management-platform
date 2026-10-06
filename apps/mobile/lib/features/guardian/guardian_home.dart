import 'package:flutter/material.dart';

import '../../core/auth/auth_controller.dart';
import '../../core/theme/app_theme.dart';
import 'guardian_ui.dart';
import 'guardian_profile.dart';
import 'guardian_payments.dart';
import 'enrollment_requests.dart';
import 'guardian_content.dart';

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
    title: 'الأكاديمية',
    actions: [
      IconButton(
        tooltip: 'الحساب',
        onPressed: () => openPage(context, accountPage),
        icon: const Icon(Icons.person_outline, color: AcademyTheme.gold),
      ),
    ],
    bottom: NavigationBar(
      selectedIndex: 0,
      onDestinationSelected: (index) {
        if (index == 1) {
          openPage(
            context,
            GuardianChildrenHub(auth: auth, environment: environment),
          );
        } else if (index == 2) {
          openPage(
            context,
            GuardianTrainingHub(auth: auth, environment: environment),
          );
        } else if (index == 3) {
          openPage(context, accountPage);
        }
      },
      destinations: const [
        NavigationDestination(
          icon: Icon(Icons.home_outlined),
          selectedIcon: Icon(Icons.home),
          label: 'الرئيسية',
        ),
        NavigationDestination(
          icon: Icon(Icons.family_restroom_outlined),
          label: 'الأبناء',
        ),
        NavigationDestination(
          icon: Icon(Icons.event_available_outlined),
          label: 'التدريب',
        ),
        NavigationDestination(icon: Icon(Icons.person_outline), label: 'حسابي'),
      ],
    ),
    child: RemoteBody(
      auth: auth,
      path: '/api/v1/guardian/home',
      builder: (context, value) {
        final home = obj(value);
        final children = rows(home['children']);
        return gList([
          Text(
            'أهلًا، ${home['guardianName']}',
            style: const TextStyle(color: AcademyTheme.gold),
          ),
          Text(
            textOf(home['academyName']),
            style: const TextStyle(fontSize: 24, fontWeight: FontWeight.bold),
          ),
          const Text(
            'رحلة أبنائك الرياضية، خطوة بخطوة',
            style: TextStyle(color: Colors.white60),
          ),
          const SizedBox(height: 14),
          Row(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              action(
                'اشتراك جديد',
                Icons.person_add_alt,
                () => openPage(
                  context,
                  EnrollmentForm(auth: auth, environment: environment),
                ),
                primary: true,
              ),
              const SizedBox(width: 8),
              action(
                'تجديد الاشتراك',
                Icons.autorenew,
                () => openPage(
                  context,
                  RenewalPicker(auth: auth, environment: environment),
                ),
              ),
              const SizedBox(width: 8),
              action(
                'تجديد اشتراك لغيره',
                Icons.card_giftcard,
                () => openPage(
                  context,
                  ExternalRenewal(auth: auth, environment: environment),
                ),
              ),
            ],
          ),
          const Heading('أبنائي'),
          if (children.isEmpty)
            const EmptyMessage('لا يوجد أبناء مرتبطون بهذا الحساب.'),
          for (final child in children)
            GCard(
              key: ValueKey('child-${child['id']}'),
              onTap: () => openPage(
                context,
                ChildProfile(
                  auth: auth,
                  environment: environment,
                  playerId: child['id'],
                  today: home['today'],
                ),
              ),
              child: Row(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  PlayerAvatar(textOf(child['arabicName'])),
                  const SizedBox(width: 14),
                  Expanded(
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        Text(
                          textOf(child['arabicName']),
                          style: const TextStyle(
                            fontSize: 17,
                            fontWeight: FontWeight.bold,
                          ),
                        ),
                        Text(
                          '${ageOn(child['dateOfBirth'], home['today']) ?? '—'} سنوات',
                          style: const TextStyle(color: Colors.white60),
                        ),
                        for (final sport in rows(child['sports']))
                          Padding(
                            padding: const EdgeInsets.only(top: 8),
                            child: Wrap(
                              spacing: 8,
                              runSpacing: 6,
                              crossAxisAlignment: WrapCrossAlignment.center,
                              children: [
                                Text(textOf(sport['sport'])),
                                if (sport['period'] != null)
                                  StatusPill(sport['period']['status'])
                                else
                                  const Text('لا يوجد اشتراك فعال'),
                              ],
                            ),
                          ),
                        const SizedBox(height: 10),
                        const Text(
                          'عرض الملف',
                          style: TextStyle(color: AcademyTheme.gold),
                        ),
                      ],
                    ),
                  ),
                ],
              ),
            ),
          const Heading('غذاء اليوم'),
          for (final child in children)
            TodayNutritionCard(
              auth: auth,
              playerId: textOf(child['id']),
              playerName: textOf(child['arabicName']),
              today: textOf(home['today']),
              onTap: () => openPage(
                context,
                NutritionLibrary(
                  auth: auth,
                  playerId: textOf(child['id']),
                  today: textOf(home['today']),
                ),
              ),
            ),
          const Heading('منتجات الرياضة'),
          CatalogSections(auth: auth),
          GLink(
            'طلبات الاشتراك',
            Icons.inbox_outlined,
            () => openPage(
              context,
              EnrollmentRequests(auth: auth, environment: environment),
            ),
          ),
          GLink(
            'إيصالاتي',
            Icons.receipt_long_outlined,
            () => openPage(context, ReceiptsPage(auth: auth)),
          ),
        ]);
      },
    ),
  );
  Widget action(
    String title,
    IconData icon,
    VoidCallback tap, {
    bool primary = false,
  }) => Expanded(
    child: Semantics(
      button: true,
      child: InkWell(
        key: ValueKey('guardian-action-$title'),
        onTap: tap,
        borderRadius: BorderRadius.circular(14),
        child: Container(
          constraints: const BoxConstraints(minHeight: 78),
          padding: const EdgeInsets.symmetric(vertical: 10, horizontal: 5),
          decoration: BoxDecoration(
            color: primary ? AcademyTheme.red : AcademyTheme.card,
            borderRadius: BorderRadius.circular(14),
          ),
          child: Column(
            mainAxisSize: MainAxisSize.min,
            children: [
              Icon(
                icon,
                size: 22,
                color: primary ? Colors.white : AcademyTheme.gold,
              ),
              const SizedBox(height: 5),
              Text(
                title,
                textAlign: TextAlign.center,
                style: const TextStyle(
                  fontSize: 11,
                  fontWeight: FontWeight.bold,
                ),
              ),
            ],
          ),
        ),
      ),
    ),
  );
}

class GuardianChildrenHub extends StatelessWidget {
  final AuthController auth;
  final String environment;
  const GuardianChildrenHub({
    super.key,
    required this.auth,
    required this.environment,
  });
  @override
  Widget build(BuildContext context) => GuardianPage(
    title: 'الأبناء',
    child: RemoteBody(
      auth: auth,
      path: '/api/v1/guardian/home',
      builder: (context, value) {
        final home = obj(value);
        final children = rows(home['children']);
        return gList([
          const Heading('ملفات الأبناء'),
          if (children.isEmpty)
            const EmptyMessage('لا يوجد أبناء مرتبطون بهذا الحساب.'),
          for (final child in children)
            GLink(
              textOf(child['arabicName']),
              Icons.sports_soccer,
              () => openPage(
                context,
                ChildProfile(
                  auth: auth,
                  environment: environment,
                  playerId: textOf(child['id']),
                  today: textOf(home['today']),
                ),
              ),
              subtitle: rows(child['sports'])
                  .map((x) => textOf(x['sport']))
                  .join(' · '),
            ),
        ]);
      },
    ),
  );
}

class GuardianTrainingHub extends StatelessWidget {
  final AuthController auth;
  final String environment;
  const GuardianTrainingHub({
    super.key,
    required this.auth,
    required this.environment,
  });
  @override
  Widget build(BuildContext context) => GuardianPage(
    title: 'التدريب والحضور',
    child: RemoteBody(
      auth: auth,
      path: '/api/v1/guardian/home',
      builder: (context, value) {
        final home = obj(value);
        final children = rows(home['children']);
        return gList([
          const Text('اختر اللاعب لعرض مواعيد التدريب والحضور المسجل.'),
          const SizedBox(height: 12),
          for (final child in children)
            GLink(
              textOf(child['arabicName']),
              Icons.event_available_outlined,
              () => openPage(
                context,
                ChildProfile(
                  auth: auth,
                  environment: environment,
                  playerId: textOf(child['id']),
                  today: textOf(home['today']),
                ),
              ),
              subtitle: 'التدريبات · المواعيد · الحضور',
            ),
        ]);
      },
    ),
  );
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
          child: const Text('تعذر تحميل الكتالوج · إعادة المحاولة'),
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
              height: 280,
              child: ListView.separated(
                scrollDirection: Axis.horizontal,
                itemCount: rows(section['items']).length,
                separatorBuilder: (_, _) => const SizedBox(width: 10),
                itemBuilder: (context, index) {
                  final item = rows(section['items'])[index];
                  return SizedBox(
                    width: 190,
                    child: GCard(
                      child: Column(
                        crossAxisAlignment: CrossAxisAlignment.start,
                        children: [
                          DemoImage(item['imageReference'], height: 135),
                          const SizedBox(height: 10),
                          Text(
                            textOf(item['arabicName']),
                            maxLines: 2,
                            overflow: TextOverflow.ellipsis,
                            style: const TextStyle(
                              fontSize: 15,
                              fontWeight: FontWeight.bold,
                            ),
                          ),
                          const Spacer(),
                          if (item['displayPrice'] != null)
                            Text(
                              money(item['displayPrice'], item['currency']),
                              style: const TextStyle(
                                color: AcademyTheme.gold,
                                fontWeight: FontWeight.bold,
                              ),
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
