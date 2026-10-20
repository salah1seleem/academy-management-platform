import 'dart:async';

import 'package:academy_mobile/core/theme/appearance.dart';
import 'package:academy_mobile/core/theme/academy_brand.dart';
import 'package:academy_mobile/core/auth/auth_controller.dart';
import 'package:academy_mobile/features/shared/role_intro_gate.dart';
import 'package:academy_mobile/features/shared/role_navigation.dart';
import 'package:flutter/material.dart';
import 'package:flutter_secure_storage/flutter_secure_storage.dart';
import 'package:flutter_localizations/flutter_localizations.dart';
import 'package:flutter_test/flutter_test.dart';

import 'v2_role_workspaces_test.dart' show authenticate, app;
import 'auth_controller_test.dart' show FakeApi, MemoryStore, credentials;

class FailingIntroStore extends Appearance {
  @override
  Future<void> completeRoleIntro(String role, String sportTheme) async =>
      throw StateError('synthetic storage failure');
}

class PendingIntroStore extends Appearance {
  final pending = Completer<bool>();
  @override
  Future<bool> hasRoleIntro(String role, String sportTheme) => pending.future;
}

void main() {
  testWidgets(
    'English introduction remains LTR and usable at 360 with 200% text',
    (t) async {
      t.view.physicalSize = const Size(360, 850);
      t.view.devicePixelRatio = 1;
      addTearDown(t.view.resetPhysicalSize);
      addTearDown(t.view.resetDevicePixelRatio);
      await t.pumpWidget(
        MaterialApp(
          locale: const Locale('en'),
          supportedLocales: const [Locale('ar'), Locale('en')],
          localizationsDelegates: GlobalMaterialLocalizations.delegates,
          builder: (context, child) => MediaQuery(
            data: MediaQuery.of(context)
                .copyWith(textScaler: const TextScaler.linear(2)),
            child: child!,
          ),
          home: AcademyOnboarding(
            role: 'AcademyOwner',
            sportTheme: 'football',
            features: const ['training'],
            onComplete: () async {},
          ),
        ),
      );
      await t.pumpAndSettle();
      expect(find.text('Understand your money'), findsOneWidget);
      expect(
        Directionality.of(t.element(find.text('Understand your money'))),
        TextDirection.ltr,
      );
      await t.tap(find.text('Next'));
      await t.pumpAndSettle();
      expect(find.text('Follow subscriptions'), findsOneWidget);
      expect(find.text('Skip'), findsOneWidget);
      expect(t.takeException(), isNull);
    },
  );
  test('brand tutorial metadata refreshes on live context revision and unknown theme never becomes swimming', () async {
    var revision = 1;
    var enabled = true;
    final auth = AuthController(
      FakeApi((m, p, b, token) async {
        if (p.contains('/auth/')) {
          final value = credentials();
          value['memberships'] = [
            <String, dynamic>{
              ...Map<String, dynamic>.from(
                (value['memberships'] as List).first as Map,
              ),
              'contextRevision': revision,
            },
          ];
          return value;
        }
        return {
          'onboarding': {
            'sportTheme': revision == 1
                ? 'football'
                : revision == 2
                ? 'swimming'
                : 'invalid',
            'features': enabled && revision == 1 ? ['training'] : [],
          },
        };
      }),
      MemoryStore(),
    )..restoring = false;
    await auth.verify('phone', 'challenge', 'otp');
    final brand = AcademyBrand(auth);
    await Future<void>.delayed(Duration.zero);
    expect(brand.sportTheme, 'football');
    expect(brand.features, ['training']);
    enabled = false;
    await brand.refresh(force: true);
    expect(brand.features, isEmpty);
    revision = 2;
    await auth.refresh();
    await Future<void>.delayed(Duration.zero);
    expect(brand.sportTheme, 'swimming');
    expect(brand.features, isEmpty);
    revision = 3;
    await auth.refresh();
    await Future<void>.delayed(Duration.zero);
    expect(brand.sportTheme, 'general');
    await auth.logout();
    expect(brand.sportTheme, 'general');
    expect(brand.features, isEmpty);
    brand.dispose();
    auth.dispose();
  });
  const titles = {
    'Guardian': ['مواعيد ابنك قدامك', 'افهم رحلة تعلّمه', 'خدماتك بسهولة'],
    'Coach': ['حصتك الجاية جاهزة', 'كل لاعب معروف', 'تقدّم يتشاف'],
    'AcademyAdmin': [
      'ابدأ بحصص الآن',
      'سجّل بدون تكرار',
      'طلباتك في مكان واحد',
    ],
    'AcademyOwner': ['فلوسك واضحة', 'تابع الاشتراكات', 'قرارات من بياناتك'],
  };
  for (final role in titles.keys) {
    for (final width in [360.0, 390.0, 430.0]) {
      testWidgets(
        '$role three role pages fit $width at 200% text with skip and back',
        (t) async {
          t.view.physicalSize = Size(width, 850);
          t.view.devicePixelRatio = 1;
          addTearDown(t.view.resetPhysicalSize);
          addTearDown(t.view.resetDevicePixelRatio);
          var finished = 0;
          await t.pumpWidget(
            app(
              AcademyOnboarding(
                role: role,
                sportTheme: 'football',
                features: const ['training'],
                onComplete: () async {
                  finished++;
                },
              ),
              scale: 2,
            ),
          );
          await t.pumpAndSettle();
          expect(find.text(titles[role]![0]), findsOneWidget);
          expect(find.text('تخطي'), findsOneWidget);
          await t.tap(find.text('التالي'));
          await t.pumpAndSettle();
          expect(find.text(titles[role]![1]), findsOneWidget);
          await t.tap(find.text('السابق'));
          await t.pumpAndSettle();
          expect(find.text(titles[role]![0]), findsOneWidget);
          await t.tap(find.text('التالي'));
          await t.pumpAndSettle();
          await t.tap(find.text('التالي'));
          await t.pumpAndSettle();
          expect(find.text(titles[role]![2]), findsOneWidget);
          await t.tap(find.text('ابدأ الآن'));
          await t.pumpAndSettle();
          expect(finished, 1);
          expect(t.takeException(), isNull);
          await t.pumpWidget(const SizedBox());
        },
      );
    }
  }
  testWidgets(
    'disabled training replaces feature claims; swimming illustration never becomes football',
    (t) async {
      await t.pumpWidget(
        app(
          AcademyOnboarding(
            role: 'Coach',
            sportTheme: 'swimming',
            features: const [],
            onComplete: () async {},
          ),
        ),
      );
      expect(find.text('أكاديميتك في مكان واحد'), findsOneWidget);
      expect(find.text('حصتك الجاية جاهزة'), findsNothing);
      expect(find.byIcon(Icons.pool_outlined), findsOneWidget);
      expect(find.byIcon(Icons.sports_soccer), findsNothing);
    },
  );
  test('role completion persists across instances by version and sport, not branch or credentials', () async {
    FlutterSecureStorage.setMockInitialValues({});
    final first = Appearance(storage: const FlutterSecureStorage());
    expect(await first.hasRoleIntro('Guardian', 'football'), isFalse);
    await first.completeRoleIntro('Guardian', 'football');
    final reopened = Appearance(storage: const FlutterSecureStorage());
    expect(await reopened.hasRoleIntro('Guardian', 'football'), isTrue);
    expect(await reopened.hasRoleIntro('Coach', 'football'), isFalse);
    expect(await reopened.hasRoleIntro('Guardian', 'swimming'), isFalse);
    final stored = await const FlutterSecureStorage().readAll();
    expect(stored, {'academy.role-intro.1.Guardian.football': 'yes'});
    first.dispose();
    reopened.dispose();
  });
  testWidgets(
    'authenticated role gate skips once and page video help never replays onboarding or resets completion',
    (t) async {
      final calls = <String>[];
      final replayMetadata = Completer<Object?>();
      final auth = await authenticate('Guardian', (m, p, b, token) async {
        calls.add(p);
        if (p == '/api/v1/academy/experience') {
          if (calls.where((x) => x == p).length > 1) {
            return replayMetadata.future;
          }
          return {
            'onboarding': {
              'sportTheme': 'football',
              'features': ['training'],
            },
          };
        }
        if (p.startsWith('/api/v1/page-help/')) {
          return {'available': false, 'url': null};
        }
        return [];
      });
      final brand = AcademyBrand(auth), prefs = Appearance();
      Widget gate(Key key) => app(
        BrandScope(
          brand: brand,
          child: RoleIntroGate(
            key: key,
            auth: auth,
            appearance: prefs,
            child: NativeRoleTabs(
              auth: auth,
              role: 'Guardian',
              home: const Text('محتوى الدور'),
              account: const SizedBox(),
            ),
          ),
        ),
      );
      await t.pumpWidget(gate(const ValueKey('first')));
      await t.pumpAndSettle();
      expect(find.text('مواعيد ابنك قدامك'), findsOneWidget);
      expect(calls, ['/api/v1/academy/experience']);
      expect(find.text('محتوى الدور'), findsNothing);
      await t.tap(find.text('تخطي'));
      await t.pumpAndSettle();
      expect(find.text('محتوى الدور'), findsOneWidget);
      expect(await prefs.hasRoleIntro('Guardian', 'football'), isTrue);
      await t.pumpWidget(gate(const ValueKey('reopened')));
      await t.pumpAndSettle();
      expect(find.byType(AcademyOnboarding), findsNothing);
      await t.tap(find.text('المزيد'));
      await t.pumpAndSettle();
      await t.tap(find.text('فيديوهات شرح الصفحات'));
      await t.pumpAndSettle();
      expect(find.text('فيديوهات شرح الصفحات'), findsOneWidget);
      expect(find.byType(AcademyOnboarding), findsNothing);
      replayMetadata.complete({
        'onboarding': {'sportTheme': 'football', 'features': []},
      });
      await t.pumpAndSettle();
      expect(calls.where((p) => p == '/api/v1/academy/experience').length, 1);
      expect(find.text('مواعيد ابنك قدامك'), findsNothing);
      await t.tap(find.byTooltip('شرح الصفحة بالفيديو').first);
      await t.pumpAndSettle();
      expect(
        find.text('لم تضف الأكاديمية فيديو لهذه الصفحة بعد.'),
        findsOneWidget,
      );
      await t.tap(find.text('حسنًا'));
      await t.pumpAndSettle();
      expect(find.byType(AcademyOnboarding), findsNothing);
      expect(await prefs.hasRoleIntro('Guardian', 'football'), isTrue);
      expect(
        calls.every((p) => !p.contains('/people/') && !p.contains('/reports/')),
        isTrue,
      );
      await t.pumpWidget(const SizedBox());
      brand.dispose();
      prefs.dispose();
      auth.dispose();
    },
  );
  testWidgets(
    'storage failure offers honest session-only continuation without marking a saved completion',
    (t) async {
      final auth = await authenticate('Coach', (m, p, b, token) async => []),
          prefs = FailingIntroStore();
      await t.pumpWidget(
        app(
          RoleIntroGate(
            auth: auth,
            appearance: prefs,
            child: const Text('متابعة'),
          ),
        ),
      );
      await t.pumpAndSettle();
      await t.tap(find.text('تخطي'));
      await t.pumpAndSettle();
      expect(find.textContaining('لم تُحفظ حالة التعريف'), findsOneWidget);
      expect(await prefs.hasRoleIntro('Coach', 'general'), isFalse);
      await t.scrollUntilVisible(
        find.text('متابعة لهذه الجلسة فقط'),
        150,
        scrollable: find.byType(Scrollable).first,
      );
      await t.tap(find.text('متابعة لهذه الجلسة فقط'));
      await t.pumpAndSettle();
      expect(find.text('متابعة'), findsOneWidget);
      await t.pumpWidget(const SizedBox());
      prefs.dispose();
      auth.dispose();
    },
  );
  testWidgets(
    'late tutorial storage read cannot reopen private content after logout',
    (t) async {
      final auth = await authenticate('Guardian', (m, p, b, token) async => []),
          prefs = PendingIntroStore();
      await t.pumpWidget(
        app(
          RoleIntroGate(
            auth: auth,
            appearance: prefs,
            child: const Text('بيانات خاصة'),
          ),
        ),
      );
      await t.pump();
      await auth.logout();
      prefs.pending.complete(true);
      await t.pumpAndSettle();
      expect(find.text('بيانات خاصة'), findsNothing);
      expect(find.byType(AcademyOnboarding), findsNothing);
      expect(t.takeException(), isNull);
      await t.pumpWidget(const SizedBox());
      prefs.dispose();
      auth.dispose();
    },
  );
}
