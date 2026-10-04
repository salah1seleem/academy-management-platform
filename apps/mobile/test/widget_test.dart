import 'package:academy_mobile/app/app.dart';
import 'package:academy_mobile/core/api/api_client.dart';
import 'package:academy_mobile/core/auth/auth_controller.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

import 'auth_controller_test.dart';

void main() {
  testWidgets(
    'Arabic phone OTP form verifies and routes to the server selected role',
    (tester) async {
      final api = FakeApi(
        (method, path, body, token) async => path.endsWith('/request')
            ? {'challengeId': 'challenge'}
            : credentials(),
      );
      final auth = AuthController(api, MemoryStore())..restoring = false;
      await tester.pumpWidget(AcademyApp(auth: auth, environment: 'Demo'));
      expect(find.text('رقم الهاتف'), findsOneWidget);
      await tester.enterText(find.byType(TextFormField).first, '01099900100');
      await tester.tap(find.text('إرسال رمز التحقق'));
      await tester.pumpAndSettle();
      await tester.enterText(find.byType(TextFormField).last, '246810');
      await tester.tap(find.text('تأكيد الدخول'));
      await tester.pumpAndSettle();
      expect(find.text('ولي أمر'), findsOneWidget);
      expect(find.text('أهلًا، حساب تجريبي'), findsOneWidget);
      expect(
        Directionality.of(tester.element(find.text('ولي أمر'))),
        TextDirection.rtl,
      );
      await tester.tap(find.text('تسجيل الخروج'));
      await tester.pumpAndSettle();
      expect(find.text('رقم الهاتف'), findsOneWidget);
    },
  );
  testWidgets(
    'Multiple memberships require an explicit choice without privilege promotion',
    (tester) async {
      final auth = AuthController(
        FakeApi((a, b, c, d) async => credentials(multi: true)),
        MemoryStore(),
      )..restoring = false;
      await auth.verify('phone', 'challenge', 'otp');
      await tester.pumpWidget(AcademyApp(auth: auth, environment: 'Demo'));
      expect(find.text('اختر الأكاديمية والدور'), findsOneWidget);
      expect(find.text('ولي أمر'), findsOneWidget);
      expect(find.text('مالك'), findsOneWidget);
      expect(auth.session!.membershipId, isNull);
    },
  );
  testWidgets('Empty phone never calls API', (tester) async {
    final api = FakeApi((a, b, c, d) async => null);
    await tester.pumpWidget(
      AcademyApp(
        auth: AuthController(api, MemoryStore())..restoring = false,
        environment: 'Demo',
      ),
    );
    await tester.tap(find.text('إرسال رمز التحقق'));
    await tester.pumpAndSettle();
    expect(api.paths, isEmpty);
    expect(find.text('أدخل رقم الهاتف المصري.'), findsOneWidget);
  });
  testWidgets(
    'Restore network failure offers retry instead of discarding the session',
    (tester) async {
      final store = MemoryStore()..token = 'saved';
      final auth = AuthController(
        FakeApi(
          (a, b, c, d) async => throw const ApiFailure(0, 'تعذر الاتصال.'),
        ),
        store,
      );
      await auth.restore();
      await tester.pumpWidget(AcademyApp(auth: auth, environment: 'Demo'));
      await tester.pumpAndSettle();
      expect(find.text('إعادة المحاولة'), findsOneWidget);
      expect(find.text('رقم الهاتف'), findsNothing);
      expect(store.token, 'saved');
    },
  );
}
