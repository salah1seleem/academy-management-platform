import 'package:academy_mobile/core/api/api_client.dart';
import 'package:academy_mobile/core/auth/auth_controller.dart';
import 'package:academy_mobile/core/storage/refresh_store.dart';
import 'package:academy_mobile/features/guardian/guardian_ui.dart';
import 'package:academy_mobile/features/owner/owner_followups.dart';
import 'package:flutter/material.dart';
import 'package:flutter_localizations/flutter_localizations.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:integration_test/integration_test.dart';

void main() {
  final binding = IntegrationTestWidgetsFlutterBinding.ensureInitialized();
  testWidgets(
    'Owner native coverage follow-up preserves actual upcoming session evidence and closing changes no attendance, assignment or money',
    (t) async {
      expect(const bool.fromEnvironment('ISOLATED_DEMO'), isTrue);
      expect(
        const String.fromEnvironment('API_BASE_URL'),
        'http://127.0.0.1:5227',
      );
      final auth = AuthController(
        HttpApiTransport(
          AppConfig('Demo', const String.fromEnvironment('API_BASE_URL')),
        ),
        SecureRefreshStore('academy.v3.coverage-followup.journey'),
      );
      await auth.restore();
      if (auth.session?.selected?.role != 'AcademyOwner') {
        if (auth.session != null) {
          await auth.logout();
        }
        final challenge = await auth.requestOtp('01099900010');
        await auth.verify('01099900010', challenge, '246810');
      }
      expect(
        auth.session!.selected!.academyId,
        '30000000-0000-0000-0000-000000000003',
      );
      final options = obj(
        await auth.request('GET', '/api/v1/manage/owner-followups/options'),
      );
      final c = rows(options['candidates'])
              .firstWhere((c) => c['condition'] == 'GroupSessionCoverage'),
          groupId = obj(c['source'])['groupId'];
      final before = obj(
        await auth.request('GET', '/api/v1/manage/groups/$groupId/details'),
      );
      final financial = await auth.request(
        'GET',
        '/api/v1/reports/financial?from=2026-09-01&to=2026-09-28',
      );
      // Fixture creation uses the actual command; the state change below uses only UI.
      final created = c['activeFollowUpId'] != null
          ? {'id': c['activeFollowUpId']}
          : obj(
              await auth.request(
                'POST',
                '/api/v1/manage/owner-followups/',
                body: {
                  'trainingGroupId': groupId,
                  'condition': c['condition'],
                  'requestId': followUpRequestId(),
                },
              ),
            );
      final id = textOf(created['id']);
      final navigation = GlobalKey<NavigatorState>();
      await t.pumpWidget(
        MaterialApp(
          navigatorKey: navigation,
          locale: const Locale('ar'),
          supportedLocales: const [Locale('ar')],
          localizationsDelegates: GlobalMaterialLocalizations.delegates,
          theme: guardianTheme(),
          home: Scaffold(
            body: FilledButton(
              onPressed: () => navigation.currentState!.push(
                MaterialPageRoute<void>(
                  builder: (_) => OwnerFollowUpPage(auth: auth, id: id),
                ),
              ),
              child: const Text('عرض المتابعة المحددة'),
            ),
          ),
        ),
      );
      Future<void> wait(Finder f) async {
        for (var i = 0; i < 180 && f.evaluate().isEmpty; i++) {
          await t.pump(const Duration(milliseconds: 100));
        }
        expect(f, findsWidgets);
        await t.pumpAndSettle();
      }

      await t.tap(find.text('عرض المتابعة المحددة'));
      await wait(find.text('حالة المتابعة'));
      expect(find.text('حصص قادمة بلا مدرب مسند مخول'), findsOneWidget);
      expect(rows(obj(c['source'])['coverageGaps']), isNotEmpty);
      if (const bool.fromEnvironment('CAPTURE_VISUALS')) {
        await binding.takeScreenshot('phase-coverage-source-ios');
      }
      await t.ensureVisible(find.text('حالة المتابعة'));
      await t.tap(find.text('جديد').first);
      await t.pumpAndSettle();
      await t.tap(find.text('مغلق').last);
      await t.pumpAndSettle();
      final reason = find.widgetWithText(
        TextField,
        'سبب إغلاق أو رفض المتابعة',
      );
      await t.ensureVisible(reason);
      await t.enterText(
        reason,
        'إغلاق متابعة بشرية فقط؛ الحالة الأصلية ما زالت قائمة',
      );
      await t.testTextInput.receiveAction(TextInputAction.done);
      await t.pumpAndSettle();
      await t.ensureVisible(find.text('حفظ المتابعة'));
      await t.tap(find.text('حفظ المتابعة'));
      await t.pumpAndSettle();
      final saved = obj(
        await auth.request('GET', '/api/v1/manage/owner-followups/$id'),
      );
      expect(obj(saved['item'])['state'], 'Closed');
      expect(obj(saved['item'])['conditionActive'], true);
      expect(rows(saved['history']).length, 2);
      expect(
        await auth.request('GET', '/api/v1/manage/groups/$groupId/details'),
        before,
      );
      expect(
        await auth.request(
          'GET',
          '/api/v1/reports/financial?from=2026-09-01&to=2026-09-28',
        ),
        financial,
      );
      expect(obj(saved['item'])['condition'], 'GroupSessionCoverage');
      expect(
        rows(obj(obj(obj(saved['item'])['evidence'])['source'])['CoverageGaps'])
            .map((s) => s['Id'])
            .toList(),
        rows(obj(c['source'])['coverageGaps']).map((s) => s['id']).toList(),
      );
      await auth.logout();
      await t.pumpAndSettle();
      expect(find.text(textOf(obj(c['source'])['name'])), findsNothing);
    },
  );
}
