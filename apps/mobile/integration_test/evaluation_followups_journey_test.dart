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
    'Owner reads actual draft evidence; closing a follow-up does not publish or change money',
    (t) async {
      expect(const bool.fromEnvironment('ISOLATED_DEMO'), isTrue);
      expect(
        const String.fromEnvironment('API_BASE_URL'),
        'http://127.0.0.1:5240',
      );
      final auth = AuthController(
        HttpApiTransport(
          AppConfig('Demo', const String.fromEnvironment('API_BASE_URL')),
        ),
        SecureRefreshStore('academy.v3.evaluation-followup.journey'),
      );
      await auth.restore();
      if (auth.session != null) await auth.logout();
      final challenge = await auth.requestOtp('01099900010');
      await auth.verify('01099900010', challenge, '246810');
      expect(
        auth.session!.selected!.academyId,
        '30000000-0000-0000-0000-000000000003',
      );
      final options = obj(
        await auth.request('GET', '/api/v1/manage/owner-followups/options'),
      );
      final c = rows(options['candidates'])
          .firstWhere((c) => c['condition'] == 'GroupEvaluationDrafts');
      expect(obj(obj(c['source'])['evaluationDrafts'])['draftCount'], 2);
      expect(obj(obj(c['source'])['evaluationDrafts'])['playerCount'], 1);
      final financial = await auth.request(
        'GET',
        '/api/v1/reports/financial?from=2026-09-01&to=2026-09-28',
      );
      final created = c['activeFollowUpId'] == null
          ? obj(
              await auth.request(
                'POST',
                '/api/v1/manage/owner-followups/',
                body: {
                  'trainingGroupId': obj(c['source'])['groupId'],
                  'condition': c['condition'],
                  'requestId': followUpRequestId(),
                },
              ),
            )
          : <String, dynamic>{'id': c['activeFollowUpId']};
      final id = textOf(created['id']);
      final detail = obj(
        await auth.request('GET', '/api/v1/manage/owner-followups/$id'),
      );
      final ids = obj(obj(detail['item'])['evidence'])['evaluationIds'] as List;
      expect(ids, hasLength(2));
      await t.pumpWidget(
        MaterialApp(
          locale: const Locale('ar'),
          supportedLocales: const [Locale('ar')],
          localizationsDelegates: GlobalMaterialLocalizations.delegates,
          theme: guardianTheme(),
          initialRoute: '/detail',
          routes: {
            '/': (_) => OwnerFollowUpsPage(auth: auth),
            '/detail': (_) => OwnerFollowUpPage(auth: auth, id: id),
          },
        ),
      );
      Future<void> wait(Finder f) async {
        for (var i = 0; i < 180 && f.evaluate().isEmpty; i++) {
          await t.pump(const Duration(milliseconds: 100));
        }
        expect(f, findsWidgets);
        await t.pumpAndSettle();
      }

      await wait(find.textContaining('2 مسودة · 1 لاعب فريد'));
      await t.ensureVisible(find.text('عرض تقييم من دليل الإنشاء').first);
      await t.pumpAndSettle();
      await t.tap(find.text('عرض تقييم من دليل الإنشاء').first);
      await wait(find.text('مسودة غير منشورة'));
      expect(find.text('نشر'), findsNothing);
      if (const bool.fromEnvironment('CAPTURE_VISUALS')) {
        await binding.takeScreenshot('phase-evaluation-source-ios');
      }
      // The framework helper assumes the English tooltip; this app is Arabic.
      await t.tap(find.byType(BackButton));
      await t.pumpAndSettle();
      await t.ensureVisible(find.byType(DropdownButtonFormField<String>).first);
      await t.pumpAndSettle();
      await t.tap(find.byType(DropdownButtonFormField<String>).first);
      await t.pumpAndSettle();
      await t.tap(find.text('مغلق').last);
      await t.pumpAndSettle();
      final reason = find.widgetWithText(
        TextField,
        'سبب إغلاق أو رفض المتابعة',
      );
      await t.ensureVisible(reason);
      await t.enterText(reason, 'متابعة بشرية اصطناعية؛ لا نشر أو تغيير مالي');
      await t.testTextInput.receiveAction(TextInputAction.done);
      await t.pumpAndSettle();
      await t.ensureVisible(find.text('حفظ المتابعة'));
      await t.pumpAndSettle();
      await t.tap(find.text('حفظ المتابعة'));
      await wait(find.text('متابعات المالك'));
      final saved = obj(
        await auth.request('GET', '/api/v1/manage/owner-followups/$id'),
      );
      expect(obj(saved['item'])['state'], 'Closed');
      expect(obj(saved['item'])['conditionActive'], true);
      expect(obj(obj(saved['item'])['evidence'])['evaluationIds'], ids);
      for (final draft in ids) {
        expect(
          obj(
            await auth.request('GET', '/api/v1/evaluations/$draft'),
          )['status'],
          'Draft',
        );
      }
      expect(
        await auth.request(
          'GET',
          '/api/v1/reports/financial?from=2026-09-01&to=2026-09-28',
        ),
        financial,
      );
      await auth.logout();
      await t.pumpAndSettle();
      expect(find.textContaining('2 مسودة · 1 لاعب فريد'), findsNothing);
    },
  );
}
