import 'dart:async';

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
import '../features/shared/staff_passkeys.dart';
import '../features/shared/self_profile.dart';
import '../features/shared/role_intro_gate.dart';
import '../features/shared/launcher_icon.dart';
import '../features/shared/mobile_push.dart';
import '../core/widgets/platform_splash.dart';

class AcademyApp extends StatefulWidget {
  final AuthController auth;
  final String environment;
  final Appearance? appearance;

  /// Injectable frame signal for deterministic startup tests.
  final Future<void>? entryFrameReady;
  const AcademyApp({
    super.key,
    required this.auth,
    required this.environment,
    this.appearance,
    this.entryFrameReady,
  });
  @override
  State<AcademyApp> createState() => _AcademyAppState();
}

class _AcademyAppState extends State<AcademyApp> {
  late final AcademyBrand brand;
  Timer? entryTimer;
  late bool entryReady;
  int entryStage = 0;
  AuthController get auth => widget.auth;
  String get environment => widget.environment;
  Appearance? get appearance => widget.appearance;
  @override
  void initState() {
    super.initState();
    brand = AcademyBrand(auth);
    entryReady = !auth.restoring;
    if (!entryReady) {
      (widget.entryFrameReady ??
              WidgetsBinding.instance.waitUntilFirstFrameRasterized)
          .then((_) {
            if (!mounted) return;
            // Start the rise after the engine has actually displayed a frame;
            // shader/font warm-up must not consume the visible animation.
            setState(() => entryStage++);
            entryTimer = Timer(const Duration(milliseconds: 1500), () {
              if (mounted) setState(() => entryReady = true);
            });
          });
    }
  }

  @override
  void dispose() {
    entryTimer?.cancel();
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
          title: 'iAcademy One',
          builder: (context, child) => LauncherIconGuard(
            auth: auth,
            child: MobilePushBridge(
              auth: auth,
              child: child ?? const SizedBox.shrink(),
            ),
          ),
          debugShowCheckedModeBanner: false,
          locale: (appearance ?? fallbackAppearance).locale,
          supportedLocales: const [Locale('ar'), Locale('en')],
          localizationsDelegates: GlobalMaterialLocalizations.delegates,
          theme: AcademyTheme.light(primary: brand.primary),
          darkTheme: (appearance ?? fallbackAppearance).model.fixedPalette
              ? AcademyTheme.themed((appearance ?? fallbackAppearance).model)
              : guardianTheme(
                  primary: brand.primary,
                  secondary: brand.secondary,
                ),
          themeMode: (appearance ?? fallbackAppearance).dark
              ? ThemeMode.dark
              : ThemeMode.light,
          home: ListenableBuilder(
            listenable: auth,
            builder: (context, _) {
              if (!entryReady || auth.restoring) {
                return PlatformSplash(key: ValueKey('entry-$entryStage'));
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
                key: ValueKey(
                  '${auth.session!.membershipId}:${auth.session!.selected?.contextRevision ?? 0}',
                ),
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
    // Scope transient UI messages to this authenticated membership navigator.
    // Switching roles disposes the messenger as well as private routes.
    return ScaffoldMessenger(
      child: NavigatorPopHandler<Object?>(
        onPopWithResult: (result) => navigator.currentState!.pop(result),
        child: Navigator(
          key: navigator,
          onGenerateRoute: (_) => MaterialPageRoute<void>(
            builder: (_) => role == null
                ? RoleFoundation(auth: widget.auth)
                : PushIntentGate(
                    auth: widget.auth,
                    child: RoleIntroGate(
                      auth: widget.auth,
                      appearance:
                          AppearanceScope.of(context) ?? fallbackAppearance,
                      child: NativeRoleTabs(
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
                            ? AdminHome(
                                auth: widget.auth,
                                accountPage: RoleFoundation(auth: widget.auth),
                              )
                            : RoleFoundation(auth: widget.auth),
                      ),
                    ),
                  ),
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
    return GuardianPage(
      title: membership == null ? 'اختيار الأكاديمية' : 'حسابي',
      child: ListView(
        padding: const EdgeInsets.all(20),
        children: [
          if (membership != null)
            SelfProfileCard(auth: widget.auth)
          else ...[
            Heading('أهلًا، ${session.displayName}'),
            const UiText('اختر الأكاديمية والدور للمتابعة.'),
            const SizedBox(height: 16),
          ],
          if (membership != null)
            GLink(
              'ابدأ من هنا',
              Icons.explore_outlined,
              () => openPage(context, RoleIntroReplay(auth: widget.auth)),
            ),
          if (membership != null) const AppearanceSettings(),
          if (error != null) ErrorNotice(error!),
          if (session.memberships.length > 1 || membership == null) ...[
            const SizedBox(height: 16),
            const UiText(
              'الأكاديميات والأدوار المتاحة',
              style: TextStyle(fontWeight: FontWeight.w700),
            ),
            for (final item in session.memberships)
              Card(
                child: ListTile(
                  leading: Icon(
                    item.id == session.membershipId
                        ? Icons.check_circle_rounded
                        : Icons.swap_horiz_rounded,
                  ),
                  title: UiText(item.academyName),
                  subtitle: UiText('${item.label}\n${item.scopeLabel}'),
                  selected: item.id == session.membershipId,
                  trailing: const Icon(Icons.chevron_right_rounded),
                  onTap: widget.auth.busy ? null : () => select(item),
                ),
              ),
          ],
          if (membership?.role == 'Coach' || membership?.role == 'AcademyAdmin')
            GLink(
              'وسائل التحقق',
              Icons.security_outlined,
              () => openPage(context, StaffPasskeyPage(auth: widget.auth)),
            ),
          const SizedBox(height: 16),
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
