import 'package:flutter/material.dart';

import '../../core/auth/auth_controller.dart';
import '../../core/theme/app_theme.dart';
import 'guardian_ui.dart';
import 'guardian_profile.dart';
import 'guardian_payments.dart';
import 'enrollment_requests.dart';

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
    bottom: Row(
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
          const Heading('منتجات الرياضة'),
          const Text(
            'كتالوج للعرض فقط · بدون طلبات شراء',
            style: TextStyle(fontSize: 12, color: Colors.white60),
          ),
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
          constraints: const BoxConstraints(minHeight: 84),
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
            for (final item in rows(section['items']))
              GCard(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    DemoImage(item['imageReference']),
                    const SizedBox(height: 10),
                    Text(
                      textOf(item['arabicName']),
                      style: const TextStyle(
                        fontSize: 17,
                        fontWeight: FontWeight.bold,
                      ),
                    ),
                    if (item['arabicDescription'] != null)
                      Text(item['arabicDescription']),
                    if (item['displayPrice'] != null)
                      Text(
                        money(item['displayPrice'], item['currency']),
                        style: const TextStyle(color: AcademyTheme.gold),
                      ),
                  ],
                ),
              ),
          ],
        ],
      );
    },
  );
}
