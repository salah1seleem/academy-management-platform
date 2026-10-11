import 'package:academy_mobile/core/theme/ui_text.dart';
import 'package:flutter/material.dart';
import 'package:flutter_localizations/flutter_localizations.dart';

import '../core/api/api_client.dart';
import '../core/auth/auth_controller.dart';
import '../core/auth/session.dart';
import '../core/theme/app_theme.dart';
import '../core/theme/appearance.dart';
import '../core/theme/academy_brand.dart';
import '../core/widgets/app_widgets.dart';
import '../features/auth/login_page.dart';
import '../features/guardian/guardian_home.dart';
import '../features/guardian/guardian_ui.dart';
import '../features/coach/coach_home.dart';
import '../features/owner/owner_dashboard.dart';
import '../features/admin/admin_home.dart';
import '../features/shared/role_navigation.dart';
import '../features/shared/academy_news.dart';

class AcademyApp extends StatefulWidget {
  final AuthController auth;
  final String environment;
  final Appearance? appearance;
  const AcademyApp({
    super.key,
    required this.auth,
    required this.environment,
    this.appearance,
  });
  @override
  State<AcademyApp> createState() => _AcademyAppState();
}

class _AcademyAppState extends State<AcademyApp> {
  late final AcademyBrand brand;
  AuthController get auth => widget.auth;
  String get environment => widget.environment;
  Appearance? get appearance => widget.appearance;
  @override
  void initState() {
    super.initState();
    brand = AcademyBrand(auth);
  }

  @override
  void dispose() {
    brand.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) => AppearanceScope(
    appearance: appearance ?? fallbackAppearance,
    child: BrandScope(
      brand: brand,
      child: ListenableBuilder(
        listenable: Listenable.merge([appearance ?? fallbackAppearance, brand]),
        builder: (context, _) => MaterialApp(
          title: 'أكاديميتي',
          debugShowCheckedModeBanner: false,
          locale: (appearance ?? fallbackAppearance).locale,
          supportedLocales: const [Locale('ar'), Locale('en')],
          localizationsDelegates: GlobalMaterialLocalizations.delegates,
          theme: AcademyTheme.light(primary: brand.primary),
          darkTheme: guardianTheme(
            primary: brand.primary,
            secondary: brand.secondary,
          ),
          themeMode: (appearance ?? fallbackAppearance).dark
              ? ThemeMode.dark
              : ThemeMode.light,
          home: ListenableBuilder(
            listenable: auth,
            builder: (context, _) {
              if (!(appearance ?? fallbackAppearance).onboarded) {
                return AcademyOnboarding(
                  appearance: appearance ?? fallbackAppearance,
                );
              }
              if (auth.restoring) {
                return const Scaffold(
                  body: Center(
                    child: CircularProgressIndicator(
                      semanticsLabel: 'استعادة الجلسة',
                    ),
                  ),
                );
              }
              if (auth.session == null) {
                if (auth.restoreFailed) {
                  return AppScaffold(
                    title: 'استعادة الجلسة',
                    child: Padding(
                      padding: const EdgeInsets.all(24),
                      child: Column(
                        mainAxisAlignment: MainAxisAlignment.center,
                        children: [
                          ErrorNotice(auth.error ?? 'تعذر استعادة الجلسة.'),
                          PrimaryButton(
                            label: 'إعادة المحاولة',
                            onPressed: auth.restore,
                          ),
                        ],
                      ),
                    ),
                  );
                }
                return LoginPage(auth: auth, environment: environment);
              }
              // Session-scoped nested navigator: logout/role selection disposes every
              // private route, not only home. No child data survives account switching.
              return SessionNavigation(
                key: ValueKey(auth.session!.membershipId),
                auth: auth,
                environment: environment,
              );
            },
          ),
        ),
      ),
    ),
  );
}

final fallbackAppearance = Appearance();

class SessionNavigation extends StatefulWidget {
  final AuthController auth;
  final String environment;
  const SessionNavigation({
    super.key,
    required this.auth,
    required this.environment,
  });
  @override
  State<SessionNavigation> createState() => _SessionNavigationState();
}

class _SessionNavigationState extends State<SessionNavigation> {
  final navigator = GlobalKey<NavigatorState>();
  @override
  Widget build(BuildContext context) {
    final role = widget.auth.session!.selected?.role;
    final guardian = role == 'Guardian';
    return NavigatorPopHandler<Object?>(
      onPopWithResult: (result) => navigator.currentState!.pop(result),
      child: Navigator(
        key: navigator,
        onGenerateRoute: (_) => MaterialPageRoute<void>(
          builder: (_) => role == null
              ? RoleFoundation(auth: widget.auth)
              : NativeRoleTabs(
                  auth: widget.auth,
                  role: role,
                  environment: widget.environment,
                  account: RoleFoundation(auth: widget.auth),
                  home: guardian
                      ? GuardianHome(
                          auth: widget.auth,
                          environment: widget.environment,
                          accountPage: RoleFoundation(auth: widget.auth),
                        )
                      : role == 'Coach'
                      ? CoachHome(
                          auth: widget.auth,
                          accountPage: RoleFoundation(auth: widget.auth),
                        )
                      : role == 'AcademyOwner'
                      ? OwnerDashboard(
                          auth: widget.auth,
                          accountPage: RoleFoundation(auth: widget.auth),
                        )
                      : role == 'AcademyAdmin'
                      ? AdminHome(auth: widget.auth)
                      : RoleFoundation(auth: widget.auth),
                ),
        ),
      ),
    );
  }
}

// Real authenticated role/context shell. Business screens are added in D/F/G, not simulated here.
class RoleFoundation extends StatefulWidget {
  final AuthController auth;
  const RoleFoundation({super.key, required this.auth});
  @override
  State<RoleFoundation> createState() => _RoleFoundationState();
}

class _RoleFoundationState extends State<RoleFoundation> {
  String? error;
  Future<void> select(Membership membership) async {
    try {
      await widget.auth.selectRole(membership.id);
    } on ApiFailure catch (failure) {
      if (mounted) setState(() => error = failure.message);
    } catch (_) {
      if (mounted) {
        setState(() => error = 'تعذر تبديل الدور بأمان. حاول مجددًا.');
      }
    }
  }

  Future<void> logout() async {
    try {
      await widget.auth.logout();
    } catch (_) {
      if (mounted) {
        setState(
          () => error = 'تعذر مسح التخزين الآمن. افتح قفل الجهاز وحاول مجددًا.',
        );
      }
    }
  }

  @override
  Widget build(BuildContext context) {
    final session = widget.auth.session!;
    final membership = session.selected;
    return AppScaffold(
      title: membership?.label ?? 'اختر الأكاديمية والدور',
      child: ListView(
        padding: const EdgeInsets.all(20),
        children: [
          AppCard(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                UiText(
                  'أهلًا، ${session.displayName}',
                  style: Theme.of(context).textTheme.headlineSmall,
                ),
                if (membership != null) ...[
                  UiText(membership.academyName),
                  UiText('الدور الحالي: ${membership.label}'),
                  const SizedBox(height: 12),
                  const AppearanceSettings(),
                ] else
                  const UiText(
                    'اختر عضوية واحدة صراحةً. لا يتم جمع صلاحيات الأدوار.',
                  ),
              ],
            ),
          ),
          if (error != null) ErrorNotice(error!),
          if (session.memberships.length > 1 || membership == null) ...[
            const SizedBox(height: 16),
            const UiText('الأكاديميات والأدوار المتاحة'),
            for (final item in session.memberships)
              Card(
                child: ListTile(
                  title: UiText(item.academyName),
                  subtitle: UiText(item.label),
                  selected: item.id == session.membershipId,
                  trailing: const Icon(Icons.chevron_left),
                  onTap: widget.auth.busy ? null : () => select(item),
                ),
              ),
          ],
          const SizedBox(height: 20),
          GLink(
            localized(context, 'الأخبار والإشعارات', 'News & notices'),
            Icons.notifications_outlined,
            () => openPage(context, AcademyNewsPage(auth: widget.auth)),
          ),
          OutlinedButton.icon(
            onPressed: widget.auth.busy ? null : logout,
            icon: const Icon(Icons.logout),
            label: const UiText('تسجيل الخروج'),
          ),
        ],
      ),
    );
  }
}
