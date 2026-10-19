import 'package:academy_mobile/core/api/api_client.dart';
import 'package:academy_mobile/core/auth/auth_controller.dart';
import 'package:academy_mobile/core/storage/refresh_store.dart';
import 'package:academy_mobile/core/theme/ui_text.dart';
import 'package:academy_mobile/features/guardian/guardian_ui.dart';
import 'package:academy_mobile/features/shared/vip_rewards.dart';
import 'package:flutter/material.dart';
import 'package:flutter_localizations/flutter_localizations.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:integration_test/integration_test.dart';

void main() {
  final binding = IntegrationTestWidgetsFlutterBinding.ensureInitialized();
  testWidgets(
    'V4 native owner previews and issues a real reward; linked family sees the same entitlement',
    (tester) async {
      expect(const bool.fromEnvironment('ISOLATED_DEMO'), isTrue);
      Future<AuthController> login(String phone, String key) async {
        final auth = AuthController(
          HttpApiTransport(
            AppConfig('Demo', const String.fromEnvironment('API_BASE_URL')),
          ),
          SecureRefreshStore(key),
        );
        await auth.restore();
        final challenge = await auth.requestOtp(phone);
        await auth.verify(phone, challenge, '246810');
        expect(
          auth.session!.selected!.academyId,
          '30000000-0000-0000-0000-000000000003',
        );
        return auth;
      }

      final owner = await login(
        '01099900010',
        'academy.v4.vip.owner.acceptance',
      );
      Widget app(Widget home) => MaterialApp(
        debugShowCheckedModeBanner: false,
        locale: const Locale('ar'),
        supportedLocales: const [Locale('ar')],
        localizationsDelegates: GlobalMaterialLocalizations.delegates,
        theme: guardianTheme(),
        home: home,
      );
      final before = obj(
        await owner.request('GET', '/api/v1/manage/vip-rewards'),
      );
      final message =
          'مكافأة تجربة جهاز اصطناعية ${DateTime.now().microsecondsSinceEpoch}';
      await tester.pumpWidget(
        app(
          Builder(
            builder: (context) => FilledButton(
              onPressed: () => Navigator.of(context).push(
                MaterialPageRoute(builder: (_) => NativeVipIssue(auth: owner)),
              ),
              child: const UiText('فتح المكافأة'),
            ),
          ),
        ),
      );
      await tester.tap(find.text('فتح المكافأة'));
      await tester.pumpAndSettle();
      for (
        var i = 0;
        i < 120 && find.byType(CheckboxListTile).evaluate().isEmpty;
        i++
      ) {
        await tester.pump(const Duration(milliseconds: 200));
      }
      expect(find.byType(CheckboxListTile), findsWidgets);
      await tester.tap(find.byType(CheckboxListTile).first);
      final percentage = find.byWidgetPredicate(
        (w) => w is TextField && w.decoration?.labelText == 'نسبة الخصم %',
      );
      final messageField = find.byWidgetPredicate(
        (w) => w is TextField && w.decoration?.labelText == 'رسالة الأسرة',
      );
      await tester.scrollUntilVisible(
        percentage,
        180,
        scrollable: find.byType(Scrollable).first,
      );
      await tester.enterText(percentage, '35');
      await tester.scrollUntilVisible(
        messageField,
        120,
        scrollable: find.byType(Scrollable).first,
      );
      await tester.enterText(messageField, message);
      FocusManager.instance.primaryFocus?.unfocus();
      await tester.pumpAndSettle();
      await tester.scrollUntilVisible(
        find.text('صالحة حتى'),
        120,
        scrollable: find.byType(Scrollable).first,
      );
      await tester.tap(find.text('صالحة حتى'));
      await tester.pumpAndSettle();
      await tester.tap(
        find.text(
          MaterialLocalizations.of(
            tester.element(find.byType(DatePickerDialog)),
          ).okButtonLabel,
        ),
      );
      await tester.pumpAndSettle();
      await tester.scrollUntilVisible(
        find.text('معاينة المكافأة'),
        150,
        scrollable: find.byType(Scrollable).first,
      );
      await tester.tap(find.text('معاينة المكافأة'));
      await tester.pumpAndSettle();
      for (
        var i = 0;
        i < 120 && find.text('تأكيد الإصدار').evaluate().isEmpty;
        i++
      ) {
        await tester.pump(const Duration(milliseconds: 200));
        await tester.drag(find.byType(Scrollable).first, const Offset(0, -150));
      }
      await tester.scrollUntilVisible(
        find.text('تأكيد الإصدار وحفظ الإشعار الداخلي'),
        120,
        scrollable: find.byType(Scrollable).first,
      );
      expect(
        (obj(
          await owner.request('GET', '/api/v1/manage/vip-rewards'),
        ))['count'],
        before['count'],
      );
      final confirm = find.text('تأكيد الإصدار وحفظ الإشعار الداخلي');
      for (var i = 0; i < 12 && confirm.hitTestable().evaluate().isEmpty; i++) {
        await tester.drag(find.byType(Scrollable).first, const Offset(0, -220));
        await tester.pumpAndSettle();
      }
      expect(confirm.hitTestable(), findsOneWidget);
      if (const bool.fromEnvironment('CAPTURE_VISUALS')) {
        await binding.takeScreenshot('phase-vfour-vip-preview');
      }
      await tester.tap(confirm.hitTestable());
      await tester.pumpAndSettle();
      for (
        var i = 0;
        i < 120 && find.text('فتح المكافأة').evaluate().isEmpty;
        i++
      ) {
        await tester.pump(const Duration(milliseconds: 200));
      }
      expect(find.text('فتح المكافأة'), findsOneWidget);
      final ledger = obj(
        await owner.request('GET', '/api/v1/manage/vip-rewards'),
      );
      expect(ledger['count'], (before['count'] as num) + 1);
      final reward = rows(ledger['items'])
          .singleWhere((r) => r['message'] == message);
      expect(reward['percentage'], 35);
      expect(reward['state'], 'Available');
      expect(reward['delivery'], 'StoredInApp');
      final profile = obj(
        await owner.request(
          'GET',
          '/api/v1/people/guardians/${reward['guardianId']}',
        ),
      );
      final guardian = await login(
        textOf(profile['contactPhone']),
        'academy.v4.vip.family.acceptance',
      );
      final mine = obj(
        await guardian.request('GET', '/api/v1/guardian/vip-rewards'),
      );
      expect(rows(mine['items']).any((r) => r['id'] == reward['id']), isTrue);
      await tester.pumpWidget(app(NativeVipRewards(auth: guardian)));
      for (var i = 0; i < 120 && find.text(message).evaluate().isEmpty; i++) {
        await tester.pump(const Duration(milliseconds: 200));
      }
      await tester.pumpAndSettle();
      expect(find.text(message), findsOneWidget);
      expect(find.text('إصدار مكافأة'), findsNothing);
      expect(tester.takeException(), isNull);
      if (const bool.fromEnvironment('CAPTURE_VISUALS')) {
        await binding.takeScreenshot('phase-vfour-vip-family');
      }
    },
  );
}
