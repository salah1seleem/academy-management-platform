import 'package:flutter/material.dart';
import 'package:flutter_secure_storage/flutter_secure_storage.dart';

import 'academy_brand.dart';

class Appearance extends ChangeNotifier {
  bool dark = true, onboarded = true;
  Locale locale = const Locale('ar');
  final FlutterSecureStorage? storage;
  Appearance({this.storage});
  Future<void> load() async {
    if (storage == null) return;
    dark = await storage!.read(key: 'academy.appearance') != 'light';
    locale = Locale(
      await storage!.read(key: 'academy.language') == 'en' ? 'en' : 'ar',
    );
    onboarded = await storage!.read(key: 'academy.onboarded') == 'yes';
    notifyListeners();
  }

  Future<void> update({bool? night, String? language, bool? completed}) async {
    if (night != null) dark = night;
    if (language != null) locale = Locale(language == 'en' ? 'en' : 'ar');
    if (completed != null) onboarded = completed;
    notifyListeners();
    await storage?.write(
      key: 'academy.appearance',
      value: dark ? 'dark' : 'light',
    );
    await storage?.write(key: 'academy.language', value: locale.languageCode);
    await storage?.write(
      key: 'academy.onboarded',
      value: onboarded ? 'yes' : 'no',
    );
  }
}

class AppearanceScope extends InheritedNotifier<Appearance> {
  const AppearanceScope({
    super.key,
    required Appearance appearance,
    required super.child,
  }) : super(notifier: appearance);
  static Appearance? of(BuildContext context) =>
      context.dependOnInheritedWidgetOfExactType<AppearanceScope>()?.notifier;
}

String localized(BuildContext context, String arabic, String english) =>
    Localizations.localeOf(context).languageCode == 'en' ? english : arabic;

class AppearanceSettings extends StatelessWidget {
  const AppearanceSettings({super.key});
  @override
  Widget build(BuildContext context) {
    final preferences = AppearanceScope.of(context);
    if (preferences == null) return const SizedBox.shrink();
    return Card(
      child: Column(
        children: [
          SwitchListTile(
            title: Text(localized(context, 'المظهر الداكن', 'Dark appearance')),
            subtitle: Text(
              localized(
                context,
                'نفس الهوية لجميع الأدوار',
                'One visual identity for every role',
              ),
            ),
            value: preferences.dark,
            onChanged: (v) => preferences.update(night: v),
          ),
          ListTile(
            title: Text(localized(context, 'اللغة', 'Language')),
            trailing: DropdownButton<String>(
              value: preferences.locale.languageCode,
              items: const [
                DropdownMenuItem(value: 'ar', child: Text('العربية')),
                DropdownMenuItem(value: 'en', child: Text('English')),
              ],
              onChanged: (v) => preferences.update(language: v),
            ),
          ),
        ],
      ),
    );
  }
}

class AcademyCrest extends StatelessWidget {
  final double size;
  const AcademyCrest({super.key, this.size = 72});
  @override
  Widget build(BuildContext context) {
    final brand = BrandScope.maybeOf(context);
    return Container(
      width: size,
      height: size,
      decoration: BoxDecoration(
        borderRadius: BorderRadius.circular(size * .3),
        color: Colors.white,
      ),
      padding: EdgeInsets.all(size * .07),
      child: brand?.logo != null
          ? Image.memory(brand!.logo!, fit: BoxFit.contain)
          : brand?.signedIn == true
          ? Icon(Icons.sports_soccer, color: brand!.primary, size: size * .58)
          : Image.asset(
              'assets/branding/professional-academy.png',
              fit: BoxFit.contain,
            ),
    );
  }
}

class AcademyOnboarding extends StatefulWidget {
  final Appearance appearance;
  const AcademyOnboarding({super.key, required this.appearance});
  @override
  State<AcademyOnboarding> createState() => _AcademyOnboardingState();
}

class _AcademyOnboardingState extends State<AcademyOnboarding> {
  int page = 0;
  final pages = const [
    (
      'كل رحلة تستحق المتابعة',
      'Every journey deserves attention',
      'مواعيد أبنائك وتقدمهم في مكان واحد.',
      'Your children’s schedule and progress, together.',
      Icons.sports_soccer,
    ),
    (
      'تفاصيل تصنع الفرق',
      'Details make the difference',
      'تقييمات منشورة ووجبات اليوم وصور التدريب.',
      'Published evaluations, daily meals and training memories.',
      Icons.auto_awesome,
    ),
    (
      'أكاديميتك أقرب إليك',
      'Your academy, closer',
      'اختر باقتك وارفع إثبات الدفع وتابع الطلب بأمان.',
      'Choose a plan, upload payment proof and track it securely.',
      Icons.verified_user_outlined,
    ),
  ];
  @override
  Widget build(BuildContext context) {
    final p = pages[page];
    return Scaffold(
      body: SafeArea(
        child: Padding(
          padding: const EdgeInsets.all(28),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              const Align(
                alignment: Alignment.centerRight,
                child: AcademyCrest(),
              ),
              const Spacer(),
              Container(
                height: 210,
                decoration: BoxDecoration(
                  borderRadius: BorderRadius.circular(44),
                  gradient: LinearGradient(
                    colors: [
                      Theme.of(context).colorScheme.primary
                          .withValues(alpha: .2),
                      Theme.of(context).colorScheme.surface,
                    ],
                  ),
                ),
                child: Icon(
                  p.$5,
                  size: 112,
                  color: Theme.of(context).colorScheme.primary,
                ),
              ),
              const SizedBox(height: 32),
              Text(
                localized(context, p.$1, p.$2),
                style: Theme.of(context).textTheme.headlineMedium
                    ?.copyWith(fontWeight: FontWeight.w800),
              ),
              const SizedBox(height: 12),
              Text(
                localized(context, p.$3, p.$4),
                style: Theme.of(context).textTheme.titleMedium,
              ),
              const Spacer(),
              Row(
                mainAxisAlignment: MainAxisAlignment.center,
                children: List.generate(
                  3,
                  (i) => AnimatedContainer(
                    duration: const Duration(milliseconds: 160),
                    width: i == page ? 30 : 8,
                    height: 8,
                    margin: const EdgeInsets.all(4),
                    decoration: BoxDecoration(
                      color: i == page
                          ? Theme.of(context).colorScheme.primary
                          : Theme.of(context).colorScheme.outline,
                      borderRadius: BorderRadius.circular(8),
                    ),
                  ),
                ),
              ),
              const SizedBox(height: 18),
              FilledButton(
                onPressed: () {
                  if (page < 2) {
                    setState(() => page++);
                  } else {
                    widget.appearance.update(completed: true);
                  }
                },
                child: Text(
                  localized(
                    context,
                    page < 2 ? 'التالي' : 'ابدأ رحلتك',
                    page < 2 ? 'Next' : 'Get started',
                  ),
                ),
              ),
              TextButton(
                onPressed: () => widget.appearance.update(completed: true),
                child: Text(localized(context, 'تخطي', 'Skip')),
              ),
            ],
          ),
        ),
      ),
    );
  }
}
