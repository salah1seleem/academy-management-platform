import 'package:academy_mobile/app/app.dart';
import 'package:academy_mobile/core/api/api_client.dart';
import 'package:academy_mobile/core/auth/auth_controller.dart';
import 'package:academy_mobile/features/guardian/guardian_ui.dart';
import 'package:academy_mobile/features/guardian/guardian_payments.dart';
import 'package:academy_mobile/features/guardian/guardian_report.dart';
import 'package:academy_mobile/features/guardian/guardian_content.dart';
import 'package:academy_mobile/features/guardian/enrollment_requests.dart';
import 'package:flutter/material.dart';
import 'package:flutter_localizations/flutter_localizations.dart';
import 'package:flutter_test/flutter_test.dart';

import 'auth_controller_test.dart';

Json child(String id) => {
  'id': id,
  'arabicName': 'لاعب $id',
  'dateOfBirth': '2018-03-12',
  'sports': [
    {
      'id': 'e$id',
      'sport': 'كرة القدم',
      'period': {'status': 'Active'},
    },
  ],
};
Json home() => {
  'academyName': 'أكاديمية الاختبار',
  'guardianName': 'حساب تجريبي',
  'today': '2026-09-28',
  'children': [child('أ'), child('ب')],
};
Json plan() => {
  'id': 'plan',
  'arabicName': 'شهري',
  'price': 100,
  'currency': 'EGP',
  'durationDays': 30,
};
Future<AuthController> logged(Handler handler) async {
  final auth = AuthController(
    FakeApi(
      (m, p, b, t) => p.contains('/auth/')
          ? Future.value(credentials())
          : handler(m, p, b, t),
    ),
    MemoryStore(),
  )..restoring = false;
  await auth.verify('phone', 'challenge', 'otp');
  return auth;
}

Widget shell(Widget page) => MaterialApp(
  locale: const Locale('ar'),
  localizationsDelegates: GlobalMaterialLocalizations.delegates,
  supportedLocales: const [Locale('ar')],
  theme: guardianTheme(),
  home: page,
);
void main() {
  testWidgets(
    'An uncertain renewal retries only explicitly with the same idempotency key and body',
    (tester) async {
      var attempts = 0;
      final bodies = <Object?>[];
      final auth = await logged((m, p, b, t) async {
        if (p.endsWith('/renewals')) {
          attempts++;
          bodies.add(b);
          if (attempts == 1) throw const ApiFailure(0, 'انقطع الاتصال');
          return {'paymentId': 'payment'};
        }
        return {
          'player': 'طفل',
          'sport': 'كرة القدم',
          'plan': 'شهري',
          'status': 'Pending',
          'isCurrent': true,
          'originalAmount': 100,
          'discountAmount': 0,
          'finalAmount': 100,
          'currency': 'EGP',
        };
      });
      await tester.pumpWidget(
        shell(
          RenewalReview(
            auth: auth,
            environment: 'Demo',
            childName: 'طفل',
            enrollmentId: 'enrollment',
            plan: plan(),
          ),
        ),
      );
      await tester.tap(find.text('متابعة إلى الدفع'));
      await tester.pumpAndSettle();
      expect(attempts, 1);
      await tester.tap(find.text('متابعة إلى الدفع'));
      await tester.pumpAndSettle();
      expect(attempts, 2);
      expect(bodies.first, bodies.last);
      final api = auth.transport as FakeApi;
      final used = [
        for (var i = 0; i < api.paths.length; i++)
          if (api.paths[i].endsWith('/renewals')) api.keys[i],
      ];
      expect(used.first, isNotNull);
      expect(used.first, used.last);
      expect(find.text('محاكاة دفع ناجح'), findsOneWidget);
    },
  );
  testWidgets('Enrollment submission validates before any write', (
    tester,
  ) async {
    final writes = <String>[];
    final auth = await logged((m, p, b, t) async {
      if (m != 'GET') writes.add(p);
      return {
        'children': [],
        'sports': [
          {'id': 'sport', 'name': 'كرة القدم'},
        ],
        'branches': [
          {'id': 'branch', 'name': 'مدينة نصر'},
        ],
      };
    });
    await tester.pumpWidget(
      shell(EnrollmentForm(auth: auth, environment: 'Demo')),
    );
    await tester.pumpAndSettle();
    await tester.scrollUntilVisible(
      find.text('إرسال طلب الاشتراك'),
      200,
      scrollable: find.byType(Scrollable).first,
    );
    await tester.tap(find.text('إرسال طلب الاشتراك'));
    await tester.pumpAndSettle();
    expect(writes, isEmpty);
  });
  for (final size in [
    const Size(360, 800),
    const Size(390, 844),
    const Size(412, 915),
  ]) {
    testWidgets(
      'Guardian home exactly three actions and unique siblings at $size',
      (tester) async {
        await tester.binding.setSurfaceSize(size);
        addTearDown(() => tester.binding.setSurfaceSize(null));
        final auth = await logged(
          (m, p, b, t) async => p.endsWith('/home') ? home() : [],
        );
        await tester.pumpWidget(AcademyApp(auth: auth, environment: 'Demo'));
        await tester.pumpAndSettle();
        for (final title in [
          'اشتراك جديد',
          'تجديد الاشتراك',
          'تجديد اشتراك لغيره',
        ]) {
          expect(find.text(title), findsOneWidget);
        }
        expect(find.byKey(const ValueKey('child-أ')), findsOneWidget);
        expect(find.byKey(const ValueKey('child-ب')), findsOneWidget);
        expect(tester.takeException(), isNull);
      },
    );
  }
  testWidgets(
    'Logout destroys private deep routes and back cannot reopen them',
    (tester) async {
      final auth = await logged(
        (m, p, b, t) async => p.endsWith('/home')
            ? home()
            : p.endsWith('/enrollments')
            ? []
            : [],
      );
      await tester.pumpWidget(AcademyApp(auth: auth, environment: 'Demo'));
      await tester.pumpAndSettle();
      await tester.tap(find.text('تجديد الاشتراك'));
      await tester.pumpAndSettle();
      expect(find.text('اختر الطفل والرياضة'), findsOneWidget);
      await auth.logout();
      await tester.pumpAndSettle();
      expect(find.text('رقم الهاتف'), findsOneWidget);
      expect(find.text('اختر الطفل والرياضة'), findsNothing);
      await tester.binding.handlePopRoute();
      await tester.pumpAndSettle();
      expect(find.text('رقم الهاتف'), findsOneWidget);
    },
  );
  testWidgets('Read failure is retryable and reveals no stale child data', (
    tester,
  ) async {
    var fail = true;
    final auth = await logged((m, p, b, t) async {
      if (p.endsWith('/home')) {
        if (fail) throw const ApiFailure(403, 'ليس لديك صلاحية.');
        return home();
      }
      return [];
    });
    await tester.pumpWidget(AcademyApp(auth: auth, environment: 'Demo'));
    await tester.pumpAndSettle();
    expect(find.text('لاعب أ'), findsNothing);
    fail = false;
    await tester.tap(find.text('إعادة المحاولة'));
    await tester.pumpAndSettle();
    expect(find.text('لاعب أ'), findsOneWidget);
  });
  testWidgets(
    'Report preserves server score zero, position, missing values and every criterion',
    (tester) async {
      final auth = await logged(
        (m, p, b, t) async => {
          'player': 'لاعب التقرير',
          'footballPosition': 'AMF',
          'isFootballReport': true,
          'reportingPeriod': 'أسبوعي',
          'evaluationDate': '2026-09-28',
          'overallScore': 74,
          'age': 8,
          'heightCm': null,
          'weightKg': 25,
          'preferredFoot': 'Right',
          'completenessPercentage': 50,
          'axes': [
            {'axis': 'Passing', 'value': 0},
            {'axis': 'Speed', 'value': null},
          ],
          'criteria': [
            {'name': 'تمرير دقيق', 'score': 0},
            {'name': 'سرعة الجري', 'score': null},
          ],
          'generalNotes': 'ملاحظة المدرب',
          'evaluator': 'مدرب',
        },
      );
      await tester.pumpWidget(shell(PlayerReport(auth: auth, id: 'report')));
      await tester.pumpAndSettle();
      expect(find.text('74'), findsOneWidget);
      expect(find.text('AMF'), findsOneWidget);
      expect(find.text('التمرير: 0'), findsOneWidget);
      expect(find.text('السرعة: غير متاح'), findsOneWidget);
      await tester.scrollUntilVisible(find.text('ملاحظة المدرب'), 250);
      expect(find.text('ملاحظة المدرب'), findsOneWidget);
      expect(find.text('مخطط الملعب'), findsNothing);
      expect(tester.takeException(), isNull);
    },
  );
  testWidgets(
    'Nutrition shows null as unavailable and no commercial controls',
    (tester) async {
      final auth = await logged(
        (m, p, b, t) async => {
          'arabicName': 'شوفان',
          'arabicDescription': 'معلومات',
          'servingDescription': 'حصة',
          'dataStatus': 'DemoUnreviewed',
        },
      );
      await tester.pumpWidget(shell(MealDetails(auth: auth, id: 'meal')));
      await tester.pumpAndSettle();
      expect(find.text('غير متاح'), findsNWidgets(4));
      await tester.scrollUntilVisible(find.textContaining('تقديرية'), 200);
      expect(find.textContaining('تقديرية'), findsOneWidget);
      for (final forbidden in [
        'السعر',
        'الكمية',
        'أضف للسلة',
        'شراء',
        'تقييم',
      ]) {
        expect(find.text(forbidden), findsNothing);
      }
    },
  );
  testWidgets('Demo payment is hidden in Production', (tester) async {
    final auth = await logged(
      (m, p, b, t) async => {
        'player': 'طفل',
        'sport': 'كرة القدم',
        'plan': 'شهري',
        'status': 'Pending',
        'isCurrent': true,
        'originalAmount': 100,
        'discountAmount': 10,
        'finalAmount': 90,
        'currency': 'EGP',
      },
    );
    await tester.pumpWidget(
      shell(CheckoutPage(auth: auth, environment: 'Production', id: 'payment')),
    );
    await tester.pumpAndSettle();
    expect(find.text('محاكاة دفع ناجح'), findsNothing);
    expect(
      find.text('الدفع الحقيقي غير مفعّل. تواصل مع الأكاديمية.'),
      findsOneWidget,
    );
  });
  testWidgets(
    'External renewal searches by name then exchanges selection for an opaque reference',
    (tester) async {
      final calls = <String>[];
      final sent = <Object?>[];
      final auth = await logged((m, p, b, t) async {
        calls.add(p);
        sent.add(b);
        if (p.contains('/external/search')) {
          return {
            'items': [
              {
                'candidateId': 'candidate',
                'playerDisplayName': 'مستفيد',
                'sport': 'كرة القدم',
                'branch': 'مدينة نصر',
                'group': 'براعم',
              },
            ],
          };
        }
        return <String, Object?>{
          'reference': 'opaque-reference',
          'playerDisplayName': 'مستفيد',
          'plans': [plan()],
        };
      });
      await tester.pumpWidget(
        shell(ExternalRenewal(auth: auth, environment: 'Demo')),
      );
      await tester.enterText(find.byType(TextField), 'مستفيد');
      await tester.tap(find.text('بحث'));
      await tester.pumpAndSettle();
      await tester.tap(find.widgetWithText(ListTile, 'مستفيد'));
      await tester.pumpAndSettle();
      expect(calls.first, contains('/external/search?query='));
      expect(calls.last, '/api/v1/guardian/subscriptions/external/select');
      expect(sent.last, {'candidateId': 'candidate'});
      expect(find.textContaining('لا يمنحك'), findsOneWidget);
    },
  );
  test(
    'Reference date age and missing score are not recomputed business totals',
    () {
      expect(ageOn('2018-10-10', '2026-09-28'), 7);
      expect(ageOn('2018-03-12', '2026-09-28'), 8);
      expect(textOf(0), '0');
      expect(textOf(null), 'غير متاح');
    },
  );
  test('Command keys are opaque unique and within server limit', () {
    final keys = List.generate(100, (_) => commandKey());
    expect(keys.toSet().length, 100);
    expect(keys.every((x) => x.length <= 100), true);
  });
  testWidgets('Unknown media reference never makes an external image request', (
    tester,
  ) async {
    await tester.pumpWidget(
      shell(const DemoImage('https://untrusted.example/child.png')),
    );
    await tester.pumpAndSettle();
    expect(find.byType(Image), findsNothing);
    expect(find.byIcon(Icons.image_not_supported_outlined), findsOneWidget);
  });
}
