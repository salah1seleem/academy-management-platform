import 'introduction_test_helpers.dart';

import 'dart:async';
import 'dart:typed_data';

import 'package:academy_mobile/core/auth/auth_controller.dart';
import 'package:academy_mobile/core/theme/academy_brand.dart';
import 'package:academy_mobile/core/theme/appearance.dart';
import 'package:academy_mobile/core/theme/logo_palette.dart';
import 'package:academy_mobile/core/widgets/brand_motion.dart';
import 'package:academy_mobile/core/widgets/home_skeleton.dart';
import 'package:academy_mobile/features/shared/academy_reveal.dart';
import 'package:academy_mobile/features/shared/role_intro_gate.dart';
import 'package:flutter/material.dart';
import 'package:flutter_localizations/flutter_localizations.dart';
import 'package:flutter_test/flutter_test.dart';

import 'auth_controller_test.dart' show FakeApi, MemoryStore, credentials;
import 'approved_login_test.dart' show login;

Widget frame(Widget child, {bool reduced = false, double scale = 1}) =>
    MaterialApp(
      locale: const Locale('ar'),
      supportedLocales: const [Locale('ar')],
      localizationsDelegates: GlobalMaterialLocalizations.delegates,
      builder: (context, child) => MediaQuery(
        data: MediaQuery.of(context).copyWith(
          disableAnimations: reduced,
          textScaler: TextScaler.linear(scale),
        ),
        child: child!,
      ),
      home: child,
    );

class PendingHome extends StatefulWidget {
  const PendingHome({super.key, required this.started, required this.data});
  final VoidCallback started;
  final Future<String> data;
  @override
  State<PendingHome> createState() => _PendingHomeState();
}

class _PendingHomeState extends State<PendingHome> {
  @override
  void initState() {
    super.initState();
    widget.started();
  }

  @override
  Widget build(BuildContext context) => Scaffold(
    body: FutureBuilder<String>(
      future: widget.data,
      builder: (context, snapshot) => snapshot.hasData
          ? Text(snapshot.data!)
          : const HomeSkeleton(role: 'Coach'),
    ),
  );
}

void main() {
  testWidgets(
    'phone and OTP actions remain above a simulated small-phone keyboard',
    (t) async {
      t.view.physicalSize = const Size(375, 667);
      t.view.devicePixelRatio = 1;
      t.view.viewInsets = const FakeViewPadding(bottom: 300);
      addTearDown(t.view.reset);
      final auth = AuthController(
        FakeApi(
          (m, p, b, token) async => p.endsWith('/request')
              ? {'challengeId': 'demo-challenge'}
              : credentials(),
        ),
        MemoryStore(),
      )..restoring = false;
      await t.pumpWidget(login(auth));
      await t.enterText(find.byType(TextFormField), '01099900100');
      final action = find.byKey(const ValueKey('login-submit'));
      await t.ensureVisible(action);
      await t.pumpAndSettle();
      expect(t.getBottomRight(action).dy, lessThanOrEqualTo(367));
      await t.tap(action);
      await t.pumpAndSettle();
      expect(
        find.byKey(const ValueKey('login-six-code-boxes')),
        findsOneWidget,
      );
      await t.enterText(find.byType(TextFormField), '246810');
      await t.ensureVisible(action);
      await t.pumpAndSettle();
      expect(t.getBottomRight(action).dy, lessThanOrEqualTo(367));
      await t.tap(action);
      await t.pumpAndSettle();
      expect(auth.session?.selected?.role, 'Guardian');
      expect(t.takeException(), isNull);
      await t.pumpWidget(const SizedBox());
      auth.dispose();
    },
  );
  testWidgets(
    'Home loads under one two-second reveal and retains its mounted state',
    (t) async {
      var requests = 0;
      final pending = Completer<String>();
      final child = PendingHome(
        started: () => requests++,
        data: pending.future,
      );
      await t.pumpWidget(
        frame(
          AcademyReveal(academyName: 'أكاديمية النيل التجريبية', child: child),
        ),
      );
      expect(requests, 1);
      final scale = find.byKey(const ValueKey('academy-reveal-scale'));
      expect(
        t.widget<Transform>(scale).transform.entry(0, 0),
        closeTo(.68, .001),
      );
      await t.pump(const Duration(milliseconds: 700));
      expect(
        t.widget<Transform>(scale).transform.entry(0, 0),
        closeTo(1.04, .001),
      );
      pending.complete('بيانات متاحة');
      await t.pump(const Duration(milliseconds: 150));
      expect(
        t.widget<Transform>(scale).transform.entry(0, 0),
        closeTo(1, .001),
      );
      expect(find.byType(HomeSkeleton), findsNothing);
      expect(find.text('بيانات متاحة'), findsOneWidget);
      await t.pump(const Duration(milliseconds: 750));
      expect(find.byKey(const ValueKey('academy-reveal')), findsOneWidget);
      await t.pump(const Duration(milliseconds: 416));
      await t.pump();
      expect(find.byKey(const ValueKey('academy-reveal')), findsNothing);
      expect(requests, 1);
      await t.pumpWidget(
        frame(
          AcademyReveal(academyName: 'أكاديمية النيل التجريبية', child: child),
        ),
      );
      t.binding.handleAppLifecycleStateChanged(AppLifecycleState.paused);
      t.binding.handleAppLifecycleStateChanged(AppLifecycleState.resumed);
      await t.pump();
      expect(find.byKey(const ValueKey('academy-reveal')), findsNothing);
      expect(requests, 1);
    },
  );

  testWidgets(
    'pending Home never loops reveal and reduced motion finishes in 350ms',
    (t) async {
      final pending = Completer<String>();
      await t.pumpWidget(
        frame(
          AcademyReveal(
            academyName: 'أكاديمية الموج التجريبية',
            child: PendingHome(started: () {}, data: pending.future),
          ),
          reduced: true,
        ),
      );
      await t.pump(const Duration(milliseconds: 150));
      final transform = t.widget<Transform>(
        find.byKey(const ValueKey('academy-reveal-scale')),
      );
      expect(transform.transform.entry(0, 0), 1);
      await t.pump(const Duration(milliseconds: 216));
      await t.pump();
      expect(find.byKey(const ValueKey('academy-reveal')), findsNothing);
      expect(find.byType(HomeSkeleton), findsOneWidget);
      expect(t.binding.transientCallbackCount, 0);
      pending.complete('وصلت');
      await t.pump();
      await t.pump();
      expect(find.byType(HomeSkeleton), findsNothing);
      expect(find.text('وصلت'), findsOneWidget);
    },
  );

  testWidgets('notification route can skip reveal without delaying Home', (
    t,
  ) async {
    await t.pumpWidget(
      frame(
        const AcademyReveal(
          academyName: 'أكاديمية اختبار',
          skip: true,
          child: Text('الوجهة المصرح بها'),
        ),
      ),
    );
    expect(find.byKey(const ValueKey('academy-reveal')), findsNothing);
    expect(find.text('الوجهة المصرح بها'), findsOneWidget);
    expect(t.binding.transientCallbackCount, 0);
  });

  testWidgets(
    'first use onboarding precedes automatic reveal; reopening retains completion',
    (t) async {
      final auth = AuthController(
        FakeApi((m, p, b, token) async => credentials()),
        MemoryStore(),
      )..restoring = false;
      await auth.verify('phone', 'challenge', 'code');
      final prefs = Appearance();
      Widget gate(Key key) => frame(
        RoleIntroGate(
          key: key,
          auth: auth,
          appearance: prefs,
          child: const Text('الرئيسية'),
        ),
      );
      await t.pumpWidget(gate(const ValueKey('first')));
      await t.pumpAndSettle();
      expect(find.byType(AcademyOnboarding), findsOneWidget);
      expect(find.byType(AcademyReveal), findsNothing);
      await finishRoleIntroduction(t);
      await t.pump();
      await t.pump();
      expect(find.byKey(const ValueKey('academy-reveal')), findsNothing);
      expect(find.text('الرئيسية'), findsOneWidget);
      await t.pump(const Duration(milliseconds: 2016));
      await t.pump();
      expect(await prefs.hasRoleIntro('Guardian', 'general'), isTrue);
      await t.pumpWidget(gate(const ValueKey('returning-cold-launch')));
      await t.pump();
      await t.pump();
      expect(find.byType(AcademyOnboarding), findsNothing);
      expect(find.byKey(const ValueKey('academy-reveal')), findsNothing);
      await t.pump(const Duration(milliseconds: 2016));
      await t.pump();
      expect(find.byKey(const ValueKey('academy-reveal')), findsNothing);
      await t.pumpWidget(const SizedBox());
      auth.dispose();
      prefs.dispose();
    },
  );

  testWidgets(
    'small phone at 200% text and a corrupt wide logo remain usable',
    (t) async {
      t.view.physicalSize = const Size(320, 568);
      t.view.devicePixelRatio = 1;
      addTearDown(t.view.reset);
      await t.pumpWidget(
        frame(
          Scaffold(
            body: Center(
              child: AcademyLogoPlate(
                logo: Uint8List.fromList([1, 2, 3]),
                wide: true,
              ),
            ),
          ),
          reduced: true,
          scale: 2,
        ),
      );
      await t.pumpAndSettle();
      expect(find.byIcon(Icons.school_outlined), findsOneWidget);
      expect(t.takeException(), isNull);
      await t.pumpWidget(
        frame(
          const AcademyReveal(
            academyName: 'أكاديمية الرياضات المتعددة التجريبية',
            child: Text('الرئيسية'),
          ),
          reduced: true,
          scale: 2,
        ),
      );
      await t.pump(const Duration(milliseconds: 150));
      expect(t.takeException(), isNull);
    },
  );

  test(
    'light, dark and multicolour logos select neutral contrasting surfaces',
    () {
      expect(
        surfaceFromRgba(Uint8List.fromList([255, 255, 255, 255])),
        const Color(0xFF202B3D),
      );
      expect(surfaceFromRgba(Uint8List.fromList([0, 0, 0, 255])), Colors.white);
      expect(
        surfaceFromRgba(Uint8List.fromList([255, 255, 255, 255, 0, 0, 0, 255])),
        const Color(0xFF8B97A8),
      );
      expect(
        surfaceFromRgba(Uint8List.fromList([255, 255, 255, 0])),
        Colors.white,
      );
    },
  );

  test(
    'late academy A metadata cannot replace academy B identity or sport',
    () async {
      final first = Completer<Object?>(), second = Completer<Object?>();
      var academy = 'أكاديمية النيل التجريبية', request = 0;
      Map<String, dynamic> session() {
        final value = credentials();
        final member = Map<String, dynamic>.from(
          (value['memberships'] as List).first as Map,
        );
        member['academyName'] = academy;
        member['academyId'] = request == 0 ? 'demo-nile' : 'demo-wave';
        member['id'] = request == 0 ? 'member-nile' : 'member-wave';
        member['contextRevision'] = request;
        value['memberships'] = [member];
        value['membershipId'] = member['id'];
        return value;
      }

      final auth = AuthController(
        FakeApi((m, p, b, token) async {
          if (p.contains('/auth/')) return session();
          return request++ == 0 ? first.future : second.future;
        }),
        MemoryStore(),
      );
      await auth.verify('phone', 'challenge', 'code');
      final brand = AcademyBrand(auth);
      academy = 'أكاديمية الموج التجريبية';
      await auth.refresh();
      expect(brand.logo, isNull);
      expect(brand.name, academy);
      second.complete({
        'displayName': academy,
        'onboarding': {'sportTheme': 'swimming'},
      });
      await Future<void>.delayed(Duration.zero);
      first.complete({
        'displayName': 'أكاديمية قديمة',
        'onboarding': {'sportTheme': 'football'},
      });
      await Future<void>.delayed(Duration.zero);
      expect(brand.name, academy);
      expect(brand.sportTheme, 'swimming');
      await auth.logout();
      expect(brand.name, 'iAcademy One');
      expect(brand.logo, isNull);
      brand.dispose();
      auth.dispose();
    },
  );

  test(
    'missing optional logo preserves trusted identity and swimming metadata',
    () async {
      final auth = AuthController(
        FakeApi((m, p, b, token) async {
          if (p.contains('/auth/')) return credentials();
          return {
            'displayName': 'أكاديمية الموج التجريبية',
            'primaryColor': '#FFFFFF',
            'logoUrl': '/api/v1/academy/branding/logo',
            'onboarding': {
              'sportTheme': 'swimming',
              'features': ['training'],
            },
          };
        }),
        MemoryStore(),
      );
      await auth.verify('phone', 'challenge', 'code');
      final brand = AcademyBrand(auth);
      await Future<void>.delayed(Duration.zero);
      expect(brand.loading, isFalse);
      expect(brand.name, 'أكاديمية الموج التجريبية');
      expect(brand.sportTheme, 'swimming');
      expect(brand.logo, isNull);
      expect(brand.features, ['training']);
      brand.dispose();
      auth.dispose();
    },
  );
}
