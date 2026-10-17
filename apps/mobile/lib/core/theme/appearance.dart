import 'package:flutter/material.dart';
import 'package:flutter_secure_storage/flutter_secure_storage.dart';

import 'academy_brand.dart';

class Appearance extends ChangeNotifier {
  bool dark = true, onboarded = true;
  Locale locale = const Locale('ar');
  final FlutterSecureStorage? storage;
  static const roleIntroVersion = 1;
  final Set<String> _seenRoleIntros = {};
  Appearance({this.storage});
  String roleIntroKey(String role, String sportTheme) =>
      'academy.role-intro.$roleIntroVersion.$role.$sportTheme';
  Future<bool> hasRoleIntro(String role, String sportTheme) async {
    final key = roleIntroKey(role, sportTheme);
    if (_seenRoleIntros.contains(key)) return true;
    final seen = await storage?.read(key: key) == 'yes';
    if (seen) _seenRoleIntros.add(key);
    return seen;
  }

  Future<void> completeRoleIntro(String role, String sportTheme) async {
    final key = roleIntroKey(role, sportTheme);
    await storage?.write(key: key, value: 'yes');
    _seenRoleIntros.add(key);
  }

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
  final String role, sportTheme;
  final List<String> features;
  final Future<void> Function() onComplete;
  final bool busy;
  final String? error;
  final VoidCallback? onContinueOnce;
  const AcademyOnboarding({
    super.key,
    required this.role,
    required this.sportTheme,
    required this.features,
    required this.onComplete,
    this.busy = false,
    this.error,
    this.onContinueOnce,
  });
  @override
  State<AcademyOnboarding> createState() => _AcademyOnboardingState();
}

class _AcademyOnboardingState extends State<AcademyOnboarding> {
  int page = 0;
  List<(String, String, String, String, IconData)> get pages {
    if (!widget.features.contains('training')) {
      return const [
        (
          'أكاديميتك في مكان واحد',
          'Your academy, together',
          'افتح الخدمات المتاحة لحسابك من داخل الأكاديمية المختارة.',
          'Explore the services available in your selected academy.',
          Icons.school_outlined,
        ),
        (
          'سياق واضح وآمن',
          'A clear, secure context',
          'تظهر لك فقط البيانات المسموح بها لدورك. تبديل الدور لا يوسع صلاحياتك.',
          'Only authorized records are shown. Switching roles never combines access.',
          Icons.verified_user_outlined,
        ),
        (
          'ابدأ بثقة',
          'Start with confidence',
          'اختر اللغة والمظهر من الحساب، وأعد مشاهدة هذا التعريف من المزيد في أي وقت.',
          'Choose language and appearance in your account, and replay this introduction from More.',
          Icons.explore_outlined,
        ),
      ];
    }
    return switch (widget.role) {
      'AcademyOwner' => const [
        (
          'فلوسك واضحة',
          'Understand your money',
          'راجع التحصيل والمستحقات من مصادرها. البيانات غير المكتملة تُوضح، ولا تعني أرباحًا مؤكدة.',
          'Review collections and dues at their source. Incomplete data is not confirmed profit.',
          Icons.account_balance_wallet_outlined,
        ),
        (
          'تابع الاشتراكات',
          'Follow subscriptions',
          'ميّز النشط والقريب من الانتهاء والمنتهي. فرصة التجديد ليست دينًا على الأسرة.',
          'Separate active, expiring and expired subscriptions. A renewal opportunity is not family debt.',
          Icons.autorenew_outlined,
        ),
        (
          'قرارات من بياناتك',
          'Decisions from your data',
          'افتح الحالة ثم مصدرها: مجموعة تحتاج متابعة، أداء الفريق أو ملاحظة أسرة. لا تتغير الأموال بمجرد المتابعة.',
          'Open a case and its source: a group, team performance or family feedback. Following up does not move money.',
          Icons.insights_outlined,
        ),
      ],
      'AcademyAdmin' => const [
        (
          'ابدأ بحصص الآن',
          'Start with today’s sessions',
          'اعرف المجموعة والمدرب والموعد، وسجّل الحضور المسموح لك. لا تسجيل لحضور مستقبلي.',
          'See the group, coach and time. Record authorized attendance, never future attendance.',
          Icons.event_available_outlined,
        ),
        (
          'سجّل بدون تكرار',
          'Register without duplicates',
          'ابحث عن اللاعب وولي الأمر قبل التسجيل. راجع الباقة والمجموعة والدفع من الرحلة المتاحة لصلاحياتك.',
          'Find the player and guardian before registering. Review plan, group and payment within your access.',
          Icons.person_add_alt_outlined,
        ),
        (
          'طلباتك في مكان واحد',
          'Requests in one place',
          'افتح الطلب وتحقق من مصدره قبل اتخاذ القرار. بعض الإجراءات تحتاج تفويضًا إضافيًا أو مراجعة المالك.',
          'Check a request and its source before deciding. Some actions require extra authorization or owner review.',
          Icons.task_alt_outlined,
        ),
      ],
      'Coach' => const [
        (
          'حصتك الجاية جاهزة',
          'Your next session',
          'ابدأ من جدولي: الوقت والمجموعة والمكان، ثم افتح تفاصيل حصتك وقائمة لاعبيك.',
          'Start with Schedule: time, group and place, then open your session and assigned players.',
          Icons.sports_outlined,
        ),
        (
          'كل لاعب معروف',
          'Know each player',
          'راجع ملف اللاعب وتقييماته داخل مجموعاتك. حضورك الوظيفي لا يمنحك تسجيل حضور الأطفال.',
          'Review players and evaluations in your groups. Staff check-in does not grant player attendance access.',
          Icons.groups_outlined,
        ),
        (
          'تقدّم يتشاف',
          'Make progress visible',
          'جهّز التقييم وراجعه قبل النشر للأسرة. المواعيد والتغييرات تظهر من نفس مصدر الأكاديمية.',
          'Prepare and review evaluations before publishing to families. Schedules share the academy’s source.',
          Icons.trending_up_outlined,
        ),
      ],
      _ => const [
        (
          'مواعيد ابنك قدامك',
          'Your child’s schedule',
          'من التقويم اختر الابن وشاهد حصصه ومبارياته. راجع التغييرات والمكان قبل التوجه للأكاديمية.',
          'Choose a child in Calendar to see sessions and matches. Check changes and location before arriving.',
          Icons.calendar_month_outlined,
        ),
        (
          'افهم رحلة تعلّمه',
          'Understand their progress',
          'راجع التقييمات المنشورة ونقاط التطوير مع المدرب. كل طفل له ملف مستقل داخل حسابك.',
          'Review published evaluations and development points with the coach. Each child has a separate profile.',
          Icons.school_outlined,
        ),
        (
          'خدماتك بسهولة',
          'Services made simple',
          'جدّد الاشتراك وافتح الإيصال وتابع طلبك من الخدمات المتاحة. ستجد هذا التعريف مرة أخرى في المزيد.',
          'Renew, open receipts and follow requests using available services. Replay this introduction from More.',
          Icons.favorite_outline,
        ),
      ],
    };
  }

  @override
  Widget build(BuildContext context) {
    final p = pages[page];
    final color = Theme.of(context).colorScheme.primary;
    return PopScope(
      canPop: false,
      child: Scaffold(
        body: SafeArea(
          child: Padding(
            padding: const EdgeInsets.all(20),
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: [
                Row(
                  children: [
                    const AcademyCrest(size: 44),
                    const SizedBox(width: 12),
                    Expanded(
                      child: Text(
                        localized(
                          context,
                          'أكاديميتي · ابدأ من هنا',
                          'Academity · Start here',
                        ),
                        style: Theme.of(context).textTheme.titleSmall,
                      ),
                    ),
                    TextButton(
                      onPressed: widget.busy ? null : widget.onComplete,
                      child: Text(localized(context, 'تخطي', 'Skip')),
                    ),
                  ],
                ),
                const SizedBox(height: 12),
                Expanded(
                  child: SingleChildScrollView(
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.stretch,
                      children: [
                        Semantics(
                          label: localized(
                            context,
                            'الخطوة ${page + 1} من 3',
                            'Step ${page + 1} of 3',
                          ),
                          child: Row(
                            children: List.generate(
                              3,
                              (i) => Expanded(
                                child: Container(
                                  height: 4,
                                  margin: const EdgeInsets.symmetric(
                                    horizontal: 3,
                                  ),
                                  decoration: BoxDecoration(
                                    color: i <= page
                                        ? color
                                        : color.withValues(alpha: .12),
                                    borderRadius: BorderRadius.circular(8),
                                  ),
                                ),
                              ),
                            ),
                          ),
                        ),
                        const SizedBox(height: 22),
                        Container(
                          height: 150,
                          decoration: BoxDecoration(
                            borderRadius: BorderRadius.circular(30),
                            gradient: LinearGradient(
                              colors: [
                                color.withValues(alpha: .22),
                                Theme.of(context).colorScheme.surface,
                              ],
                            ),
                          ),
                          child: Stack(
                            alignment: Alignment.center,
                            children: [
                              Icon(p.$5, size: 72, color: color),
                              Positioned(
                                bottom: 14,
                                left: 16,
                                child: Icon(
                                  widget.sportTheme == 'swimming'
                                      ? Icons.pool_outlined
                                      : widget.sportTheme == 'football'
                                      ? Icons.sports_soccer
                                      : Icons.sports_outlined,
                                  color: color.withValues(alpha: .5),
                                  size: 24,
                                ),
                              ),
                            ],
                          ),
                        ),
                        const SizedBox(height: 26),
                        Text(
                          localized(context, p.$1, p.$2),
                          style: Theme.of(context).textTheme.headlineMedium
                              ?.copyWith(fontWeight: FontWeight.w800),
                        ),
                        const SizedBox(height: 14),
                        Text(
                          localized(context, p.$3, p.$4),
                          style: Theme.of(context).textTheme.titleMedium
                              ?.copyWith(height: 1.6),
                        ),
                        const SizedBox(height: 22),
                        Text(
                          localized(
                            context,
                            'بحسب الخدمات والصلاحيات المتاحة لحسابك.',
                            'Subject to your available services and permissions.',
                          ),
                          style: Theme.of(context).textTheme.bodySmall,
                        ),
                        if (widget.error != null) ...[
                          const SizedBox(height: 12),
                          Text(
                            widget.error!,
                            style: TextStyle(
                              color: Theme.of(context).colorScheme.error,
                            ),
                          ),
                        ],
                        if (widget.onContinueOnce != null)
                          TextButton(
                            onPressed: widget.onContinueOnce,
                            child: Text(
                              localized(
                                context,
                                'متابعة لهذه الجلسة فقط',
                                'Continue for this session only',
                              ),
                            ),
                          ),
                      ],
                    ),
                  ),
                ),
                const SizedBox(height: 16),
                Row(
                  children: [
                    if (page > 0) ...[
                      OutlinedButton(
                        onPressed: widget.busy
                            ? null
                            : () => setState(() => page--),
                        child: Text(localized(context, 'السابق', 'Back')),
                      ),
                      const SizedBox(width: 12),
                    ],
                    Expanded(
                      child: FilledButton(
                        onPressed: widget.busy
                            ? null
                            : () {
                                if (page < 2) {
                                  setState(() => page++);
                                } else {
                                  widget.onComplete();
                                }
                              },
                        child: Text(
                          localized(
                            context,
                            widget.busy
                                ? 'جارٍ الحفظ…'
                                : page < 2
                                ? 'التالي'
                                : 'ابدأ الآن',
                            widget.busy
                                ? 'Saving…'
                                : page < 2
                                ? 'Next'
                                : 'Start now',
                          ),
                        ),
                      ),
                    ),
                  ],
                ),
              ],
            ),
          ),
        ),
      ),
    );
  }
}
