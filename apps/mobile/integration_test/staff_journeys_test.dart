import 'support/role_intro.dart';

import 'package:academy_mobile/app/app.dart';
import 'package:academy_mobile/core/api/api_client.dart';
import 'package:academy_mobile/core/auth/auth_controller.dart';
import 'package:academy_mobile/core/storage/refresh_store.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:integration_test/integration_test.dart';

void main() {
  final binding = IntegrationTestWidgetsFlutterBinding.ensureInitialized();

  Future<void> waitFor(WidgetTester tester, Finder finder) async {
    for (var i = 0; i < 200 && finder.evaluate().isEmpty; i++) {
      await tester.pump(const Duration(milliseconds: 150));
    }
    expect(finder, findsWidgets);
    await tester.pumpAndSettle();
  }

  Future<(AuthController, HttpApiTransport)> launch(
    WidgetTester tester,
    String key,
  ) async {
    final config = AppConfig(
      'Demo',
      const String.fromEnvironment('API_BASE_URL'),
    );
    final transport = HttpApiTransport(config);
    final store = SecureRefreshStore('${config.storageKey}.$key');
    await store.clear();
    final auth = AuthController(transport, store);
    await auth.restore();
    await tester.pumpWidget(AcademyApp(auth: auth, environment: 'Demo'));
    await skipRoleIntroIfPresent(tester);
    await tester.pumpAndSettle();
    return (auth, transport);
  }

  Future<void> login(WidgetTester tester, String phone, String homeText) async {
    await tester.enterText(find.byType(TextFormField).first, phone);
    await tester.tap(find.text('إرسال رمز التحقق'));
    await tester.pumpAndSettle();
    await waitFor(tester, find.text('رمز التحقق'));
    await tester.enterText(find.byType(TextFormField).last, '246810');
    await tester.tap(find.text('تأكيد الدخول'));
    await tester.pumpAndSettle();
    await waitFor(tester, find.text(homeText));
  }

  Future<void> capture(String name) async {
    if (const bool.fromEnvironment('CAPTURE_VISUALS')) {
      await binding.takeScreenshot('phase-h-$name');
    }
  }

  testWidgets('Coach real Demo attendance and evaluation journey', (
    tester,
  ) async {
    final session = await launch(tester, 'coach-integration');
    await login(tester, '01099900020', 'حصتك الأقرب');
    await capture('coach-home');
    // Groups are a distinct section; the list can build its rows lazily.
    await tester.scrollUntilVisible(
      find.text('المجموعات المسندة'),
      320,
      scrollable: find.byType(Scrollable).first,
    );
    await tester.pumpAndSettle();
    await tester.tap(find.byIcon(Icons.chevron_left).first);
    await tester.pumpAndSettle();
    await waitFor(tester, find.text('فتح الحصة الأقرب'));
    await tester.tap(find.text('فتح الحصة الأقرب'));
    await tester.pumpAndSettle();
    await waitFor(tester, find.text('حفظ الحضور'));
    await capture('coach-attendance');
    await tester.tap(find.text('لم يُسجل').last);
    final saveAttendance = find.widgetWithText(FilledButton, 'حفظ الحضور');
    await tester.ensureVisible(saveAttendance);
    await tester.pumpAndSettle();
    await tester.tap(saveAttendance);
    await tester.pumpAndSettle();
    await tester.drag(find.byType(Scrollable).first, const Offset(0, 1600));
    await tester.pumpAndSettle();
    await waitFor(tester, find.textContaining('يمكن تصحيحه'));
    await tester.tap(find.byType(BackButton).last);
    await tester.pumpAndSettle();
    await capture('coach-group');
    await tester.tap(find.byTooltip('تقييم اللاعب').first);
    await tester.pumpAndSettle();
    await waitFor(tester, find.text('بدء تقييم جديد'));
    await tester.tap(find.text('بدء تقييم جديد'));
    await tester.pumpAndSettle();
    await waitFor(tester, find.byType(Slider));
    await capture('coach-evaluation');
    await tester.scrollUntilVisible(
      find.widgetWithText(FilledButton, 'نشر التقييم'),
      500,
      scrollable: find.byType(Scrollable).first,
    );
    await tester.tap(find.widgetWithText(FilledButton, 'نشر التقييم'));
    await tester.pumpAndSettle();
    await waitFor(tester, find.text('تم نشر التقييم.'));
    await session.$1.logout();
    await tester.pumpAndSettle();
    await tester.pumpWidget(const SizedBox.shrink());
    session.$1.dispose();
    session.$2.close();
  });

  testWidgets('Owner real Demo dashboard and reports journey', (tester) async {
    final session = await launch(tester, 'owner-integration');
    await login(tester, '01099900010', 'تحصيل اليوم');
    await capture('owner-dashboard');
    await tester.scrollUntilVisible(
      find.text('التقارير').first,
      400,
      scrollable: find.byType(Scrollable).first,
    );
    await tester.tap(find.text('التقارير').first);
    await tester.pumpAndSettle();
    await waitFor(tester, find.text('ملخص التحصيلات'));
    await capture('owner-reports');
    await tester.tap(find.text('الحضور'));
    await tester.pumpAndSettle();
    await waitFor(tester, find.text('ملخص الحضور المخزن'));
    await tester.tap(find.text('الاشتراكات'));
    await tester.pumpAndSettle();
    await waitFor(tester, find.textContaining('ملخص قراءة فقط'));
    await session.$1.logout();
    await tester.pumpAndSettle();
    await tester.pumpWidget(const SizedBox.shrink());
    session.$1.dispose();
    session.$2.close();
  }, skip: const String.fromEnvironment('STAFF_JOURNEY') == 'coach');
}
