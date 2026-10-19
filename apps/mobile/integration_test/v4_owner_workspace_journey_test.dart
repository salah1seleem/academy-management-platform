import 'package:academy_mobile/app/app.dart';
import 'package:academy_mobile/core/api/api_client.dart';
import 'package:academy_mobile/core/auth/auth_controller.dart';
import 'package:academy_mobile/core/storage/refresh_store.dart';
import 'package:academy_mobile/features/guardian/guardian_ui.dart';
import 'package:academy_mobile/features/owner/external_agreements.dart';
import 'package:flutter/material.dart';
import 'package:flutter_localizations/flutter_localizations.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:integration_test/integration_test.dart';

import 'support/role_intro.dart';

void main() {
  final binding=IntegrationTestWidgetsFlutterBinding.ensureInitialized();
  testWidgets(
    'V4 owner home and real external receipt use the same isolated authorized API',
    (tester) async {
      expect(const bool.fromEnvironment('ISOLATED_DEMO'), isTrue);
      final auth = AuthController(
        HttpApiTransport(
          AppConfig('Demo', const String.fromEnvironment('API_BASE_URL')),
        ),
        SecureRefreshStore('academy.v4.owner.acceptance'),
      );
      await auth.restore();
      final challenge = await auth.requestOtp('01099900010');
      await auth.verify('01099900010', challenge, '246810');
      expect(
        auth.session!.selected!.academyId,
        '30000000-0000-0000-0000-000000000003',
      );
      final statement = obj(
        await auth.request(
          'GET',
          '/api/v1/reports/operating-statement?allDates=true',
        ),
      );
      final cockpit = obj(
        await auth.request(
          'GET',
          '/api/v1/reports/owner-cockpit?allDates=true',
        ),
      );
      expect(obj(cockpit['collections'])['net'], statement['totalIncome']);
      expect(statement['basis'], 'Cash');
      expect(statement['earnedRevenue'], isNull);
      await tester.pumpWidget(AcademyApp(auth: auth, environment: 'Demo'));
      await skipRoleIntroIfPresent(tester);
      for (
        var i = 0;
        i < 120 && find.text('ملخص الأكاديمية').evaluate().isEmpty;
        i++
      ) {
        await tester.pump(const Duration(milliseconds: 200));
      }
      await tester.scrollUntilVisible(
        find.text('ملخص الأكاديمية'),
        180,
        scrollable: find.byType(Scrollable).first,
      );
      await tester.pumpAndSettle();
      expect(find.text('ملخص الأكاديمية'), findsWidgets);
      expect(tester.takeException(), isNull);
      if(const bool.fromEnvironment('CAPTURE_VISUALS'))await binding.takeScreenshot('phase-vfour-home');
      final contracts = obj(
        await auth.request(
          'GET',
          '/api/v1/finance/external-agreements?allDates=true',
        ),
      );
      expect(
        rows(contracts['items']),
        isNotEmpty,
        reason: 'Run the isolated V4 browser agreement journey first.',
      );
      final id = textOf(rows(contracts['items']).first['id']);
      final detail = obj(
        await auth.request('GET', '/api/v1/finance/external-agreements/$id'),
      );
      expect(detail['paid'], 400);
      expect(detail['remaining'], 600);
      expect(rows(detail['receipts']), hasLength(1));
      await tester.pumpWidget(
        MaterialApp(
          locale: const Locale('ar'),
          supportedLocales: const [Locale('ar')],
          theme: guardianTheme(),
          localizationsDelegates: GlobalMaterialLocalizations.delegates,
          home: NativeExternalAgreementDetail(auth: auth, id: id),
        ),
      );
      for (
        var i = 0;
        i < 120 && find.text('إيصالات التحصيل').evaluate().isEmpty;
        i++
      ) {
        await tester.pump(const Duration(milliseconds: 200));
      }
      await tester.scrollUntilVisible(
        find.text('إيصالات التحصيل'),
        160,
        scrollable: find.byType(Scrollable).first,
      );
      await tester.pumpAndSettle();
      expect(find.textContaining('لا يخصم من راتب الموظف'), findsOneWidget);
      expect(tester.takeException(), isNull);
      if(const bool.fromEnvironment('CAPTURE_VISUALS'))await binding.takeScreenshot('phase-vfour-agreement');
    },
  );
}
