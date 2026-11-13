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
import '../core/widgets/page_status.dart';
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

class _AcademyAppState extends State<AcademyApp> with WidgetsBindingObserver {
  late final AcademyBrand brand;
  Timer? entryTimer;
  late bool entryReady;
  int entryStage = 0;
  int scopeCheck = 0;
  bool checkStaffScope = false;
  bool checkingStaffScope = false;
  bool scopeCheckFailed = false;
  AuthController get auth => widget.auth;
  String get environment => widget.environment;
  Appearance? get appearance => widget.appearance;
  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addObserver(this);
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
    WidgetsBinding.instance.removeObserver(this);
    entryTimer?.cancel();
    brand.dispose();
    super.dispose();
  }

  @override
  void didChangeAppLifecycleState(AppLifecycleState state) {
    if (state == AppLifecycleState.resumed) {
      if (checkStaffScope) unawaited(recheckStaffScope());
    } else if ((state == AppLifecycleState.hidden ||
            state == AppLifecycleState.paused) &&
        auth.session?.selected?.role == 'AcademyAdmin') {
      scopeCheck++;
      setState(() {
        checkStaffScope = true;
        checkingStaffScope = false;
        scopeCheckFailed = false;
      });
    }
  }

  Future<void> recheckStaffScope() async {
    if (checkingStaffScope) return;
    final attempt = ++scopeCheck;
    setState(() {
      checkingStaffScope = true;
      scopeCheckFailed = false;
    });
    var verified = false;
    try {
      await auth.recheckMemberships();
      verified = true;
    } catch (_) {
      // Keep the previous workspace covered until the server answers.
    }
    if (!mounted || attempt != scopeCheck) return;
    setState(() {
      checkingStaffScope = false;
      checkStaffScope = !verified && auth.session != null;
      scopeCheckFailed = checkStaffScope;
    });
  }

  Widget _protectWorkspace(Widget workspace) => Stack(
    fit: StackFit.expand,
    children: [
      Offstage(
        offstage: checkStaffScope,
        child: TickerMode(
          enabled: !checkStaffScope,
          child: ExcludeFocus(excluding: checkStaffScope, child: workspace),
        ),
      ),
      if (checkStaffScope)
        Scaffold(
          body: SafeArea(
            child: PageStatus(
              child: scopeCheckFailed
                  ? StatusPanel(
                      message: 'تعذر التحقق من صلاحياتك. أعد المحاولة.',
                      icon: Icons.lock_outline_rounded,
                      actionLabel: 'إعادة المحاولة',
                      onAction: recheckStaffScope,
                    )
                  : const Center(child: UiText('جارٍ التحقق من صلاحياتك…')),
            ),
          ),
        ),
    ],
  );

  @override
  Widget build(BuildContext context) => AppearanceScope(
    appearance: appearance ?? fallbackAppearance,
    child: BrandScope(
      brand: brand,
      child: ListenableBuilder(
        listenable: Listenable.merge([
          appearance ?? fallbackAppearance,
          brand,
          auth,
        ]),
        builder: (context, _) => MaterialApp(
          // Root dialogs must close along with the membership's inner routes.
          key: ValueKey((
            auth.session?.userId,
            auth.session?.selected?.id,
            auth.session?.selected?.role,
            auth.session?.selected?.contextRevision,
          )),
          title: 'iAcademy One',
          builder: (context, child) => _protectWorkspace(
            LauncherIconGuard(
              auth: auth,
              child: MobilePushBridge(
                auth: auth,
                child: child ?? const SizedBox.shrink(),
              ),
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
              if (auth.roleSessionUnconfirmed) {
                return RoleSessionRecovery(auth: auth);
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

class RoleFoundation extends StatefulWidget {
  final AuthController auth;
  const RoleFoundation({super.key, required this.auth});
  @override
  State<RoleFoundation> createState() => _RoleFoundationState();
}

class _RoleFoundationState extends State<RoleFoundation> {
  String? choiceId, error, logoutError;
  bool refreshRequired = false, signingOut = false;

  Future<void> select(Membership membership) async {
    setState(() => error = null);
    try {
      await widget.auth.selectRole(membership.id);
    } on ApiFailure catch (failure) {
      if (!mounted || widget.auth.roleSessionUnconfirmed) return;
      setState(() {
        refreshRequired = const [403, 404, 409].contains(failure.status);
        error = refreshRequired
            ? 'هذا الدور غير متاح حاليًا. حدّث الأدوار المتاحة.'
            : failure.status == 429
            ? 'محاولات كثيرة. انتظر قليلًا ثم أعد المحاولة.'
            : 'تعذر فتح الدور المختار. أعد المحاولة.';
      });
    } catch (_) {
      if (mounted && !widget.auth.roleSessionUnconfirmed) {
        setState(() => error = 'تعذر فتح الدور المختار. أعد المحاولة.');
      }
    }
  }

  Future<void> refreshChoices() async {
    try {
      await widget.auth.checkRoleSession();
      if (!mounted) return;
      setState(() {
        choiceId = null;
        error = null;
        refreshRequired = false;
      });
    } catch (_) {
      // The app handles a session that needs confirmation or has expired.
    }
  }

  Future<void> logout() async {
    setState(() {
      signingOut = true;
      logoutError = null;
    });
    try {
      await widget.auth.logout();
    } catch (_) {
      if (mounted) {
        setState(
          () => logoutError =
              'تعذر تسجيل الخروج. حاول مجددًا بعد فتح قفل الجهاز.',
        );
      }
    } finally {
      if (mounted) setState(() => signingOut = false);
    }
  }

  @override
  Widget build(BuildContext context) => ListenableBuilder(
    listenable: widget.auth,
    builder: (context, _) {
      final session = widget.auth.session;
      if (session == null) return const SizedBox.shrink();
      final current = session.selected;
      final choices = session.memberships;
      final choice = choices
          .where((item) => item.id == choiceId && item.id != current?.id)
          .firstOrNull;
      final busy = widget.auth.busy;
      final showRoles = current == null || choices.length > 1;
      return GuardianPage(
        title: current == null ? 'اختيار الدور' : 'حسابي',
        child: ListView(
          padding: const EdgeInsets.all(20),
          children: [
            if (current != null) SelfProfileCard(auth: widget.auth),
            if (showRoles) ...[
              if (current != null) const Heading('الأدوار المتاحة'),
              if (choices.isEmpty)
                const UiText('لا توجد أدوار متاحة لهذا الحساب.'),
              RadioGroup<String>(
                groupValue: choice?.id,
                onChanged: (value) {
                  if (busy || refreshRequired) return;
                  setState(() {
                    choiceId = value;
                    error = null;
                  });
                },
                child: Column(
                  children: [
                    for (final item in choices)
                      _RoleChoiceCard(
                        membership: item,
                        current: current?.id == item.id,
                        selected: choice?.id == item.id,
                        enabled: !busy && !refreshRequired,
                      ),
                  ],
                ),
              ),
              if (error != null) ErrorNotice(error!),
              if (choices.isNotEmpty)
                FilledButton(
                  onPressed: busy
                      ? null
                      : refreshRequired
                      ? refreshChoices
                      : choice == null
                      ? null
                      : () => select(choice),
                  child: UiText(
                    busy && !signingOut
                        ? refreshRequired
                              ? 'جارٍ تحديث الجلسة…'
                              : 'جارٍ فتح الدور…'
                        : refreshRequired
                        ? 'تحديث الأدوار المتاحة'
                        : current == null
                        ? 'متابعة بالدور المختار'
                        : 'تبديل الدور',
                  ),
                ),
              const SizedBox(height: 12),
            ],
            if (current != null) ...[
              GLink(
                'ابدأ من هنا',
                Icons.explore_outlined,
                () => openPage(context, RoleIntroReplay(auth: widget.auth)),
              ),
              const AppearanceSettings(),
            ],
            if (current?.role == 'Coach' || current?.role == 'AcademyAdmin')
              GLink(
                'وسائل التحقق',
                Icons.security_outlined,
                () => openPage(context, StaffPasskeyPage(auth: widget.auth)),
              ),
            if (!showRoles && error != null) ErrorNotice(error!),
            const SizedBox(height: 12),
            if (logoutError != null) ErrorNotice(logoutError!),
            OutlinedButton.icon(
              onPressed: busy ? null : logout,
              icon: const Icon(Icons.logout),
              label: UiText(signingOut ? 'جارٍ تسجيل الخروج…' : 'تسجيل الخروج'),
            ),
          ],
        ),
      );
    },
  );
}

class _RoleChoiceCard extends StatelessWidget {
  const _RoleChoiceCard({
    required this.membership,
    required this.current,
    required this.selected,
    required this.enabled,
  });

  final Membership membership;
  final bool current, selected, enabled;

  @override
  Widget build(BuildContext context) {
    final colors = Theme.of(context).colorScheme;
    final staffScope =
        membership.role == 'AcademyOwner' || membership.role == 'AcademyAdmin';
    final coach = membership.role == 'Coach';
    final showBranches = staffScope || coach && !membership.allBranches;
    final showSports = staffScope || coach && !membership.allSports;
    final title = UiText(
      membership.label,
      style: const TextStyle(fontWeight: FontWeight.w800),
    );
    final details = Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        UiText(membership.academyName),
        if (membership.contextName != null) UiText(membership.roleLabel),
        if (current)
          const UiText(
            'الدور الحالي',
            style: TextStyle(fontWeight: FontWeight.w700),
          ),
        if (coach) const UiText('المجموعات المسندة'),
        if (showBranches)
          Text(
            '${ui(context, 'الفروع')}: ${ui(context, membership.allBranches
                ? 'كل الفروع'
                : membership.branchNames.isEmpty
                ? 'لا توجد فروع مسندة'
                : membership.branchNames.join('، '))}',
          ),
        if (showSports)
          Text(
            '${ui(context, 'الرياضات')}: ${ui(context, membership.allSports
                ? 'كل الرياضات'
                : membership.sportNames.isEmpty
                ? 'لا توجد رياضات مسندة'
                : membership.sportNames.join('، '))}',
          ),
      ],
    );
    return Card(
      margin: const EdgeInsets.only(bottom: 12),
      shape: RoundedRectangleBorder(
        borderRadius: BorderRadius.circular(16),
        side: BorderSide(
          color: selected || current ? colors.primary : colors.outlineVariant,
        ),
      ),
      child: current
          ? ListTile(
              title: title,
              subtitle: details,
              leading: Icon(Icons.check_circle_outline, color: colors.primary),
              titleAlignment: ListTileTitleAlignment.top,
              minVerticalPadding: 12,
            )
          : RadioListTile<String>(
              value: membership.id,
              enabled: enabled,
              title: title,
              subtitle: details,
              controlAffinity: ListTileControlAffinity.leading,
              titleAlignment: ListTileTitleAlignment.top,
              minVerticalPadding: 12,
            ),
    );
  }
}

class RoleSessionRecovery extends StatefulWidget {
  const RoleSessionRecovery({super.key, required this.auth});

  final AuthController auth;

  @override
  State<RoleSessionRecovery> createState() => _RoleSessionRecoveryState();
}

class _RoleSessionRecoveryState extends State<RoleSessionRecovery> {
  String? error;
  bool signingOut = false;

  Future<void> check() async {
    setState(() => error = null);
    try {
      await widget.auth.checkRoleSession();
    } catch (_) {
      if (mounted && widget.auth.session != null) {
        setState(
          () =>
              error = 'تعذر التحقق من الجلسة. تحقّق من الاتصال وأعد المحاولة.',
        );
      }
    }
  }

  Future<void> logout() async {
    setState(() {
      signingOut = true;
      error = null;
    });
    try {
      await widget.auth.logout();
    } catch (_) {
      if (mounted) {
        setState(
          () => error = 'تعذر تسجيل الخروج. حاول مجددًا بعد فتح قفل الجهاز.',
        );
      }
    } finally {
      if (mounted) setState(() => signingOut = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    final busy = widget.auth.busy;
    return Scaffold(
      appBar: AppBar(title: const UiText('تأكيد الجلسة')),
      body: SafeArea(
        child: Center(
          child: ConstrainedBox(
            constraints: const BoxConstraints(maxWidth: 650),
            child: ListView(
              padding: const EdgeInsets.all(20),
              children: [
                const UiText(
                  'تعذر تأكيد تبديل الدور. تحقّق من الجلسة للمتابعة.',
                ),
                if (error != null) ErrorNotice(error!),
                const SizedBox(height: 16),
                FilledButton(
                  onPressed: busy ? null : check,
                  child: UiText(
                    busy && !signingOut
                        ? 'جارٍ تحديث الجلسة…'
                        : 'تحقّق من الجلسة',
                  ),
                ),
                const SizedBox(height: 8),
                OutlinedButton(
                  onPressed: busy ? null : logout,
                  child: UiText(
                    signingOut ? 'جارٍ تسجيل الخروج…' : 'تسجيل الخروج',
                  ),
                ),
              ],
            ),
          ),
        ),
      ),
    );
  }
}
