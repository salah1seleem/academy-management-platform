import 'package:academy_mobile/app/app.dart';
import 'package:academy_mobile/core/api/api_client.dart';
import 'package:academy_mobile/core/auth/auth_controller.dart';
import 'package:academy_mobile/core/storage/refresh_store.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:integration_test/integration_test.dart';

void main() {
  final binding = IntegrationTestWidgetsFlutterBinding.ensureInitialized();

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
    await tester.pumpAndSettle();
    return (auth, transport);
  }

  Future<void> login(WidgetTester tester, String phone, String homeText) async {
    await tester.enterText(find.byType(TextFormField).first, phone);
    await tester.tap(find.text('إرسال رمز التحقق'));
    await tester.pumpAndSettle();
    await tester.enterText(find.byType(TextFormField).last, '246810');
    await tester.tap(find.text('تأكيد الدخول'));
    await tester.pumpAndSettle();
    expect(find.text(homeText), findsWidgets);
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
    await login(tester, '01099900020', 'المجموعات المسندة');
    await capture('coach-home');
    await tester.tap(find.byIcon(Icons.chevron_left).first);
    await tester.pumpAndSettle();
    expect(find.text('تسجيل الحضور'), findsOneWidget);
    await tester.tap(find.text('تسجيل الحضور'));
    await tester.pumpAndSettle();
    await capture('coach-attendance');
    await tester.tap(find.text('لم يُسجل').last);
    await tester.tap(find.text('حفظ الحضور'));
    await tester.pumpAndSettle();
    expect(find.textContaining('يمكن تصحيحه'), findsOneWidget);
    await tester.tap(find.byType(BackButton).last);
    await tester.pumpAndSettle();
    await capture('coach-group');
    await tester.tap(find.byTooltip('تقييم اللاعب').first);
    await tester.pumpAndSettle();
    await tester.tap(find.text('بدء تقييم جديد'));
    await tester.pumpAndSettle();
    expect(find.byType(Slider), findsWidgets);
    await capture('coach-evaluation');
    await tester.scrollUntilVisible(
      find.text('نشر التقييم'),
      500,
      scrollable: find.byType(Scrollable).first,
    );
    await tester.tap(find.text('نشر التقييم'));
    await tester.pumpAndSettle();
    expect(find.text('تم نشر التقييم.'), findsOneWidget);
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
      find.text('التقارير'),
      400,
      scrollable: find.byType(Scrollable).first,
    );
    await tester.tap(find.text('التقارير'));
    await tester.pumpAndSettle();
    expect(find.text('ملخص التحصيلات'), findsOneWidget);
    await capture('owner-reports');
    await tester.tap(find.text('الحضور'));
    await tester.pumpAndSettle();
    expect(find.text('ملخص الحضور المخزن'), findsOneWidget);
    await tester.tap(find.text('الاشتراكات'));
    await tester.pumpAndSettle();
    expect(find.textContaining('ملخص قراءة فقط'), findsOneWidget);
    await session.$1.logout();
    await tester.pumpAndSettle();
    await tester.pumpWidget(const SizedBox.shrink());
    session.$1.dispose();
    session.$2.close();
  });
}
