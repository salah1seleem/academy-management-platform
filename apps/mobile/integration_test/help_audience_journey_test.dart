import 'dart:convert';

import 'package:academy_mobile/core/api/api_client.dart';
import 'package:academy_mobile/core/auth/auth_controller.dart';
import 'package:academy_mobile/core/storage/refresh_store.dart';
import 'package:academy_mobile/features/guardian/guardian_ui.dart';
import 'package:academy_mobile/features/shared/help_center.dart';
import 'package:flutter/material.dart';
import 'package:flutter_localizations/flutter_localizations.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:integration_test/integration_test.dart';

void main() {
  final binding = IntegrationTestWidgetsFlutterBinding.ensureInitialized();
  testWidgets(
    'Owner audience activation is consumed by real native guardian context help, catalog and restored reader',
    (t) async {
      expect(const bool.fromEnvironment('ISOLATED_DEMO'), true);
      expect(
        const String.fromEnvironment('API_BASE_URL'),
        'http://127.0.0.1:5230',
      );
      Future<AuthController> login(String phone, String role) async {
        final a = AuthController(
          HttpApiTransport(
            AppConfig('Demo', const String.fromEnvironment('API_BASE_URL')),
          ),
          SecureRefreshStore('academy.v3.help.audience.$role'),
        );
        await a.restore();
        if (a.session != null) {
          await a.logout();
        }
        final challenge = await a.requestOtp(phone);
        await a.verify(phone, challenge, '246810');
        expect(a.session!.selected!.role, role);
        expect(
          a.session!.selected!.academyId,
          '30000000-0000-0000-0000-000000000003',
        );
        return a;
      }

      final owner = await login('01099900010', 'AcademyOwner'),
          guardian = await login('01099900101', 'Guardian');
      final versions = rows(
        obj(
          await owner.request('GET', '/api/v1/manage/configuration/'),
        )['versions'],
      );
      final original = obj(
        versions.firstWhere((v) => v['state'] == 'Active')['settings'],
      );
      final report = jsonEncode(
        await owner.request(
          'GET',
          '/api/v1/reports/financial?from=2026-09-01&to=2026-09-28',
        ),
      );
      Future<void> activate(Map<String, dynamic> settings) async {
        final draft = obj(
          await owner.request(
            'POST',
            '/api/v1/manage/configuration/drafts',
            body: settings,
          ),
        );
        for (final target in ['Validated', 'Staged', 'Active']) {
          final data = obj(
            await owner.request('GET', '/api/v1/manage/configuration/'),
          );
          final row = rows(data['versions'])
              .firstWhere((v) => v['id'] == draft['id']);
          await owner.request(
            'POST',
            '/api/v1/manage/configuration/${draft['id']}/transition',
            body: {'target': target, 'version': row['version']},
          );
        }
      }

      Future<void> wait(Finder f) async {
        for (var i = 0; i < 200 && f.evaluate().isEmpty; i++) {
          await t.pump(const Duration(milliseconds: 100));
        }
        expect(f, findsWidgets);
        await t.pumpAndSettle();
      }

      Widget shell(Widget page) => MaterialApp(
        locale: const Locale('ar'),
        supportedLocales: const [Locale('ar')],
        localizationsDelegates: GlobalMaterialLocalizations.delegates,
        theme: guardianTheme(),
        home: page,
      );
      try {
        // Authorized setup uses the real existing configuration workflow; Owner editing UI is covered by Browser E2E.
        await activate({
          ...original,
          'helpAudiences': {'family-renewal': <String>[]},
        });
        try {
          await guardian.request('GET', '/api/v1/help/lessons/family-renewal');
          fail('Hidden audience must not return a guide');
        } on ApiFailure catch (e) {
          expect(e.status, 404);
        }
        await t.pumpWidget(shell(HelpPage(auth: guardian)));
        await wait(find.text('مواعيد طفلك وتقدمه'));
        expect(find.text('تجديد اشتراك أبنائك'), findsNothing);
        if (const bool.fromEnvironment('CAPTURE_VISUALS')) {
          await binding.takeScreenshot('phase-help-audience-hidden-ios');
        }
        await t.pumpWidget(const SizedBox.shrink());
        await activate({...original, 'helpAudiences': <String, dynamic>{}});
        await t.pumpWidget(
          shell(
            Scaffold(
              body: Center(
                child: HelpEntry(auth: guardian, lesson: 'family-renewal'),
              ),
            ),
          ),
        );
        await wait(find.text('شرح هذه الخطوة'));
        await t.tap(find.text('شرح هذه الخطوة'));
        await wait(find.text('تجديد اشتراك أبنائك'));
        expect(find.textContaining('لا يوجد فيديو منشور بعد'), findsOneWidget);
        if (const bool.fromEnvironment('CAPTURE_VISUALS')) {
          await binding.takeScreenshot('phase-help-audience-restored-ios');
        }
        expect(
          jsonEncode(
            await owner.request(
              'GET',
              '/api/v1/reports/financial?from=2026-09-01&to=2026-09-28',
            ),
          ),
          report,
        );
        expect(t.takeException(), isNull);
      } finally {
        await activate(original);
        await guardian.logout();
        await owner.logout();
        await t.pumpWidget(const SizedBox.shrink());
        guardian.dispose();
        owner.dispose();
      }
    },
  );
}
