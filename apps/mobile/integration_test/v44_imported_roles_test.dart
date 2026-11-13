import 'package:academy_mobile/app/app.dart';
import 'package:academy_mobile/core/api/api_client.dart';
import 'package:academy_mobile/core/auth/auth_controller.dart';
import 'package:academy_mobile/core/storage/refresh_store.dart';
import 'package:academy_mobile/core/theme/app_theme.dart';
import 'package:academy_mobile/core/theme/appearance.dart';
import 'package:academy_mobile/features/owner/native_academy_import.dart';
import 'package:academy_mobile/features/guardian/guardian_ui.dart';
import 'package:flutter/material.dart';
import 'package:flutter_localizations/flutter_localizations.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:integration_test/integration_test.dart';

void main() {
  final binding = IntegrationTestWidgetsFlutterBinding.ensureInitialized();
  testWidgets('real imported workbook replay and role-scoped native screens', (
    t,
  ) async {
    expect(const bool.fromEnvironment('ISOLATED_DEMO'), isTrue);
    const api = String.fromEnvironment('API_BASE_URL');
    expect(api, 'http://127.0.0.1:5278');
    Future<AuthController> login(String phone, String role) async {
      final auth = AuthController(
        HttpApiTransport(AppConfig('Demo', api)),
        SecureRefreshStore('v44.import.$phone'),
      )..restoring = false;
      final challenge = await auth.requestOtp(phone);
      await auth.verify(phone, challenge, '246810');
      expect(auth.session!.selected!.role, role);
      expect(
        auth.session!.selected!.academyId,
        '30000000-0000-0000-0000-000000000003',
      );
      return auth;
    }

    Future<void> waitFor(Finder f) async {
      for (var i = 0; i < 240 && f.evaluate().isEmpty; i++) {
        await t.pump(const Duration(milliseconds: 150));
      }
      expect(f, findsWidgets);
    }

    Future<void> tap(String label) async {
      final f = find.text(label).last;
      await waitFor(f);
      await t.ensureVisible(f);
      await t.pump(const Duration(milliseconds: 250));
      await t.ensureVisible(f);
      await t.tap(f);
      await t.pump(const Duration(milliseconds: 400));
    }

    Future<void> capture(String name) async {
      await t.pumpAndSettle();
      expect(t.takeException(), isNull);
      await binding.takeScreenshot(name);
    }

    Future<void> shell(AuthController auth) async {
      final appearance = Appearance();
      for (final theme in ['football', 'general']) {
        await appearance.completeRoleIntro(auth.session!.selected!.role, theme);
      }
      await t.pumpWidget(
        AcademyApp(
          key: ValueKey(auth.session!.selected!.id),
          auth: auth,
          environment: 'Demo',
          appearance: appearance,
        ),
      );
    }

    final owner = await login('01099900010', 'AcademyOwner');
    final bytes = await owner.asset('/api/v1/people/import/connected-sample');
    expect(bytes.take(2), [80, 75]);
    await t.pumpWidget(
      MaterialApp(
        locale: const Locale('ar'),
        supportedLocales: const [Locale('ar')],
        localizationsDelegates: GlobalMaterialLocalizations.delegates,
        theme: AcademyTheme.light(),
        home: NativeAcademyImport(
          auth: owner,
          pickWorkbook: () async =>
              (name: 'downloaded-isolated-sample.xlsx', bytes: bytes),
        ),
      ),
    );
    await waitFor(find.text('اكتمل الاستيراد'));
    await tap('اختيار ملف XLSX ومعاينته');
    await waitFor(find.text('اكتملت المعاينة دون أخطاء'));
    await t.scrollUntilVisible(
      find.text('راجعت علاقات أولياء الأمور وأعتمد ربط الدفعة.'),
      200,
      scrollable: find.byType(Scrollable).first,
    );
    await tap('راجعت علاقات أولياء الأمور وأعتمد ربط الدفعة.');
    await tap('راجعت الأرصدة الافتتاحية وسندها إن وجدت؛ ليست قبض اليوم.');
    await tap('اعتماد وبدء الاستيراد');
    await waitFor(find.text('اكتمل الاستيراد'));
    await t.scrollUntilVisible(
      find.textContaining('هذا الملف معتمد بالفعل'),
      150,
      scrollable: find.byType(Scrollable).first,
    );
    await capture('phase-approved-native-import-replay');
    final jobs = rows(await owner.request('GET', '/api/v1/people/import/jobs'));
    expect(
      jobs.where((j) => j['sourceSystem'] == 'academy-opening'),
      hasLength(1),
    );
    await t.pumpWidget(const SizedBox());
    await owner.logout();
    owner.dispose();
    final guardian = await login('01200000001', 'Guardian');
    final children = rows(
      await guardian.request('GET', '/api/v1/guardian/children'),
    );
    expect(children, hasLength(2));
    final enrollments = rows(
      await guardian.request(
        'GET',
        '/api/v1/guardian/subscriptions/enrollments',
      ),
    );
    expect(enrollments, hasLength(3));
    await shell(guardian);
    await waitFor(find.text('أبنائي'));
    await waitFor(find.text('لاعب خيالي 1'));
    await capture('phase-approved-imported-guardian');
    final firstPlayer = children.firstWhere(
      (c) => c['arabicName'] == 'لاعب خيالي 1' || c['name'] == 'لاعب خيالي 1',
    );
    expect(firstPlayer['id'], isNotNull);
    await t.pumpWidget(const SizedBox());
    await guardian.logout();
    guardian.dispose();
    final coach = await login('01500900001', 'Coach');
    final groups = rows(await coach.request('GET', '/api/v1/coach/groups'));
    expect(groups, hasLength(1));
    expect(groups.single['arabicName'], 'مجموعة النجوم التجريبية');
    await shell(coach);
    await waitFor(find.text('تدريباتي'));
    await tap('المجموعات');
    await waitFor(find.text('مجموعة النجوم التجريبية'));
    await capture('phase-approved-imported-coach');
    await t.pumpWidget(const SizedBox());
    await coach.logout();
    coach.dispose();
  });
}
