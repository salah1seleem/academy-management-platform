import 'introduction_test_helpers.dart';

import 'package:academy_mobile/core/theme/appearance.dart';
import 'package:academy_mobile/core/auth/auth_controller.dart';
import 'package:academy_mobile/features/shared/approved_role_welcome.dart';
import 'package:academy_mobile/features/shared/role_intro_gate.dart';
import 'package:flutter/material.dart';
import 'package:academy_mobile/features/shared/academy_reveal.dart';
import 'package:flutter_secure_storage/flutter_secure_storage.dart';
import 'package:flutter_test/flutter_test.dart';

import 'approved_introduction_test.dart' show view;
import 'v2_role_workspaces_test.dart' as f;
import 'auth_controller_test.dart' show FakeApi, MemoryStore, credentials;

const roles = ['AcademyOwner', 'AcademyAdmin', 'Coach', 'Guardian'];

void main() {
  setUp(() => FlutterSecureStorage.setMockInitialValues({}));
  testWidgets(
    'switching to a second academy redacts the first welcome and preserves completed-user entry',
    (t) async {
      var second = false;
      final auth = AuthController(
        FakeApi(
          (m, p, b, k) async => {
            ...credentials(),
            'displayName': second
                ? 'حساب الأكاديمية الثانية'
                : 'حساب الأكاديمية الأولى',
            'memberships': [
              {
                'id': 'guardian',
                'academyId': second ? 'academy-two' : 'academy-one',
                'academyName': second
                    ? 'أكاديمية ثانية اصطناعية'
                    : 'أكاديمية أولى اصطناعية',
                'role': 'Guardian',
                'contextRevision': second ? 2 : 1,
              },
            ],
          },
        ),
        MemoryStore(),
      )..restoring = false;
      final prefs = Appearance();
      await auth.verify('synthetic', 'challenge', '246810');
      Widget gate(String key, String content) => f.app(
        RoleIntroGate(
          key: ValueKey(key),
          auth: auth,
          appearance: prefs,
          child: Text(content),
        ),
      );
      await t.pumpWidget(gate('one', 'محتوى الأكاديمية الأولى'));
      await t.pumpAndSettle();
      await finishRoleIntroduction(t);
      await t.pumpAndSettle();
      expect(find.byType(ApprovedRoleWelcome), findsNothing);
      expect(find.text('محتوى الأكاديمية الأولى'), findsOneWidget);
      second = true;
      await auth.verify('synthetic', 'challenge', '246810');
      await t.pumpAndSettle();
      expect(find.byType(ApprovedRoleWelcome), findsNothing);
      expect(find.text('أهلًا، حساب الأكاديمية الأولى'), findsNothing);
      expect(find.text('محتوى الأكاديمية الأولى'), findsNothing);
      await t.pumpWidget(gate('two', 'محتوى الأكاديمية الثانية'));
      await t.pumpAndSettle();
      expect(find.byType(ApprovedRoleWelcome), findsNothing);
      expect(find.text('محتوى الأكاديمية الثانية'), findsOneWidget);
      expect(await prefs.hasRoleIntro('Guardian', 'general'), true);
      await t.pumpWidget(const SizedBox());
      auth.dispose();
      prefs.dispose();
    },
  );
  for (final swimming in [false, true]) {
    testWidgets('welcome respects disabled training or swimming=$swimming', (
      t,
    ) async {
      await t.pumpWidget(
        view(
          ApprovedRoleWelcome(
            role: 'Coach',
            displayName: 'حساب تجريبي',
            academyName: 'أكاديمية اختبار',
            training: swimming,
            sportTheme: swimming ? 'swimming' : 'general',
            onEnter: () {},
          ),
        ),
      );
      await t.pumpAndSettle();
      expect(find.byType(Image), findsNothing);
      expect(
        find.byIcon(swimming ? Icons.pool_rounded : Icons.school_outlined),
        findsOneWidget,
      );
      if (!swimming) {
        expect(
          find.text('تصفح الخدمات المتاحة لحسابك داخل الأكاديمية.'),
          findsOneWidget,
        );
        expect(find.textContaining('وسجّل الحضور والتقييم'), findsNothing);
      }
      expect(t.takeException(), isNull);
    });
  }
  for (final role in roles) {
    for (final width in [375.0, 440.0]) {
      for (final dark in [false, true]) {
        testWidgets(
          'approved $role welcome fits $width dark=$dark without prototype metrics',
          (t) async {
            t.view.physicalSize = Size(width, width == 375 ? 667 : 956);
            t.view.devicePixelRatio = 1;
            addTearDown(t.view.reset);
            var entered = 0;
            await t.pumpWidget(
              view(
                ApprovedRoleWelcome(
                  training: true,
                  role: role,
                  displayName: 'حساب مصري اصطناعي',
                  academyName: 'أكاديمية الاختبار',
                  onEnter: () => entered++,
                ),
                dark: dark,
              ),
            );
            await t.pumpAndSettle();
            expect(find.text('أهلًا، حساب مصري اصطناعي'), findsOneWidget);
            expect(
              t.getTopLeft(find.byKey(const ValueKey('welcome-identity'))).dy,
              greaterThanOrEqualTo(
                t
                    .getBottomLeft(
                      find.byKey(const ValueKey('welcome-artwork')),
                    )
                    .dy,
              ),
            );
            expect(find.text('أكاديمية الاختبار'), findsOneWidget);
            expect(find.textContaining('2468'), findsNothing);
            expect(find.textContaining('248,000'), findsNothing);
            await t.ensureVisible(
              find.byKey(const ValueKey('role-welcome-enter')),
            );
            await t.tap(find.byKey(const ValueKey('role-welcome-enter')));
            expect(entered, 1);
            expect(t.takeException(), isNull);
          },
        );
      }
    }
  }
  for (final width in [375.0, 440.0]) {
    for (final dark in [false, true]) {
      for (final language in ['ar', 'en']) {
        testWidgets(
          'welcome long identities fit 200% $width dark=$dark $language',
          (t) async {
            t.view.physicalSize = Size(width, width == 375 ? 667 : 956);
            t.view.devicePixelRatio = 1;
            addTearDown(t.view.reset);
            const name =
                'حساب تجريبي باسم طويل للتحقق من وضوح الترحيب دون اختصار الهوية';
            await t.pumpWidget(
              view(
                ApprovedRoleWelcome(
                  training: true,
                  role: 'Coach',
                  displayName: name,
                  academyName:
                      'أكاديمية مصرية اصطناعية ذات اسم طويل للغاية للاختبار',
                  onEnter: () {},
                ),
                dark: dark,
                scale: 2,
                locale: Locale(language),
              ),
            );
            await t.pumpAndSettle();
            final identity = find.byKey(const ValueKey('welcome-identity'));
            expect(
              t.getTopLeft(identity).dy,
              greaterThanOrEqualTo(
                t
                    .getBottomLeft(
                      find.byKey(const ValueKey('welcome-artwork')),
                    )
                    .dy,
              ),
            );
            expect(
              t.widget<Text>(identity).data,
              '${language == 'ar' ? 'أهلًا، ' : 'Welcome, '}$name',
            );
            expect(
              Directionality.of(t.element(identity)),
              language == 'ar' ? TextDirection.rtl : TextDirection.ltr,
            );
            await t.ensureVisible(
              find.byKey(const ValueKey('role-welcome-enter')),
            );
            expect(t.takeException(), isNull);
          },
        );
      }
    }
  }
  for (final role in roles) {
    testWidgets(
      '$role completes persisted intro then automatic reveal without an extra welcome hold',
      (t) async {
        final auth = await f.authenticate(role, (m, p, b, k) async => []);
        final prefs = Appearance();
        Widget gate(String key) => f.app(
          RoleIntroGate(
            key: ValueKey(key),
            auth: auth,
            appearance: prefs,
            child: const Text('وجهة الدور المصرح بها'),
          ),
        );
        await t.pumpWidget(gate('first'));
        await t.pumpAndSettle();
        await finishRoleIntroduction(t);
        await t.pumpAndSettle();
        expect(find.byType(ApprovedRoleWelcome), findsNothing);
        expect(find.byKey(const ValueKey('role-welcome-enter')), findsNothing);
        expect(await prefs.hasRoleIntro(role, 'general'), true);
        expect(find.text('وجهة الدور المصرح بها'), findsOneWidget);
        await t.pumpWidget(gate('reopened'));
        await t.pumpAndSettle();
        expect(find.byType(ApprovedRoleWelcome), findsNothing);
        expect(find.byType(AcademyOnboarding), findsNothing);
        expect(find.text('وجهة الدور المصرح بها'), findsOneWidget);
        await t.pumpWidget(const SizedBox());
        auth.dispose();
        prefs.dispose();
      },
    );
  }
  testWidgets('logout redacts welcome identity and destination before entry', (
    t,
  ) async {
    final auth = await f.authenticate('Coach', (m, p, b, k) async => []);
    final prefs = Appearance();
    await t.pumpWidget(
      f.app(
        RoleIntroGate(
          auth: auth,
          appearance: prefs,
          child: const Text('وجهة خاصة'),
        ),
      ),
    );
    await t.pumpAndSettle();
    await finishRoleIntroduction(t);
    expect(find.byType(AcademyReveal), findsNothing);
    await auth.logout();
    await t.pumpAndSettle();
    expect(find.byType(ApprovedRoleWelcome), findsNothing);
    expect(find.text('أهلًا، حساب تجريبي'), findsNothing);
    expect(find.text('وجهة خاصة'), findsNothing);
    await t.pumpWidget(const SizedBox());
    auth.dispose();
    prefs.dispose();
  });
}
