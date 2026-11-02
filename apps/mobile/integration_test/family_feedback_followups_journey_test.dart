import 'package:academy_mobile/core/api/api_client.dart';
import 'package:academy_mobile/core/auth/auth_controller.dart';
import 'package:academy_mobile/core/storage/refresh_store.dart';
import 'package:academy_mobile/features/guardian/guardian_ui.dart';
import 'package:academy_mobile/features/guardian/coach_feedback.dart';
import 'package:academy_mobile/features/owner/owner_feedback_detail.dart';
import 'package:academy_mobile/features/owner/owner_followups.dart';
import 'package:flutter/material.dart';
import 'package:flutter_localizations/flutter_localizations.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:integration_test/integration_test.dart';

void main() {
  final binding = IntegrationTestWidgetsFlutterBinding.ensureInitialized();
  testWidgets(
    'Owner source replies reach only the linked family; task closure is independent and money unchanged',
    (t) async {
      expect(const bool.fromEnvironment('ISOLATED_DEMO'), isTrue);
      expect(
        const String.fromEnvironment('API_BASE_URL'),
        'http://127.0.0.1:5240',
      );
      Future<AuthController> login(String phone, String suffix) async {
        final a = AuthController(
          HttpApiTransport(
            AppConfig('Demo', const String.fromEnvironment('API_BASE_URL')),
          ),
          SecureRefreshStore('academy.v3.family-followup.$suffix'),
        );
        await a.restore();
        if (a.session != null) {
          await a.logout();
        }
        final challenge = await a.requestOtp(phone);
        await a.verify(phone, challenge, '246810');
        return a;
      }

      final auth = await login('01099900010', 'owner');
      expect(
        auth.session!.selected!.academyId,
        '30000000-0000-0000-0000-000000000003',
      );
      final options = obj(
        await auth.request('GET', '/api/v1/manage/owner-followups/options'),
      );
      final c = rows(options['candidates']).firstWhere(
        (c) =>
            c['condition'] == 'GroupOverdueFeedback' &&
            obj(c['source'])['groupId'] ==
                '428e9073-f93c-14ae-d9d5-c38e50278314',
      );
      expect(obj(obj(c['source'])['overdueFeedback'])['complaintCount'], 1);
      expect(obj(obj(c['source'])['overdueFeedback'])['submitterCount'], 1);
      final financial = await auth.request(
        'GET',
        '/api/v1/reports/financial?from=2026-09-01&to=2026-09-28',
      );
      final created = obj(
        await auth.request(
          'POST',
          '/api/v1/manage/owner-followups/',
          body: {
            'trainingGroupId': obj(c['source'])['groupId'],
            'condition': c['condition'],
            'requestId': followUpRequestId(),
          },
        ),
      );
      final id = textOf(created['id']),
          before = obj(
            await auth.request('GET', '/api/v1/manage/owner-followups/$id'),
          );
      final evidence = obj(obj(before['item'])['evidence']),
          ids = evidence['feedbackIds'] as List;
      expect(ids, hasLength(2));
      Widget shell(Widget page) => MaterialApp(
        locale: const Locale('ar'),
        supportedLocales: const [Locale('ar')],
        localizationsDelegates: GlobalMaterialLocalizations.delegates,
        theme: guardianTheme(),
        home: page,
      );
      Future<void> wait(Finder f) async {
        for (var i = 0; i < 180 && f.evaluate().isEmpty; i++) {
          await t.pump(const Duration(milliseconds: 100));
        }
        expect(f, findsWidgets);
        await t.pumpAndSettle();
      }

      await t.pumpWidget(shell(OwnerFollowUpPage(auth: auth, id: id)));
      await wait(find.text('1 شكوى · 1 اقتراح · 1 حساب مقدم فريد'));
      await t.ensureVisible(find.text('فتح ملاحظة من دليل الإنشاء').first);
      await t.pumpAndSettle();
      await t.tap(find.text('فتح ملاحظة من دليل الإنشاء').first);
      await wait(find.text('متابعة ملاحظة الأسرة'));
      if (const bool.fromEnvironment('CAPTURE_VISUALS')) {
        await binding.takeScreenshot('phase-family-source-ios');
      }
      final source = obj(
        await auth.request('GET', '/api/v1/manage/feedback/${ids.first}'),
      );
      final child = textOf(source['playerId']);
      await t.tap(find.byType(BackButton));
      await t.pumpAndSettle();
      // Task creation/lifecycle fixture uses its real API; source responses below are UI only.
      final task = obj(
        obj(
          await auth.request('GET', '/api/v1/manage/owner-followups/$id'),
        )['item'],
      );
      await auth.request(
        'POST',
        '/api/v1/manage/owner-followups/$id',
        body: {
          'state': 'Closed',
          'version': task['version'],
          'requestId': followUpRequestId(),
          'resolutionReason': 'متابعة بشرية فقط قبل الرد على الأسرة',
        },
      );
      expect(
        obj(
          obj(
            await auth.request('GET', '/api/v1/manage/owner-followups/$id'),
          )['item'],
        )['conditionActive'],
        true,
      );
      const response = 'رد اصطناعي محفوظ للأسرة من تطبيق المالك';
      for (final note in ids) {
        await t.pumpWidget(
          shell(
            OwnerFeedbackDetail(
              key: ValueKey(note),
              auth: auth,
              id: note.toString(),
            ),
          ),
        );
        await wait(find.text('متابعة ملاحظة الأسرة'));
        final field = find.widgetWithText(TextField, 'الرد على الملاحظة');
        await wait(field);
        await t.ensureVisible(field);
        await t.enterText(field, response);
        await t.testTextInput.receiveAction(TextInputAction.done);
        await t.pumpAndSettle();
        final save = find.text('إرسال الرد وحل الملاحظة');
        await t.ensureVisible(save);
        await t.pumpAndSettle();
        await t.tap(save);
        await wait(find.text('تم الرد والحل'));
      }
      final saved = obj(
        await auth.request('GET', '/api/v1/manage/owner-followups/$id'),
      );
      expect(obj(saved['item'])['state'], 'Closed');
      expect(obj(saved['item'])['conditionActive'], false);
      expect(obj(obj(saved['item'])['evidence']), evidence);
      expect(
        await auth.request(
          'GET',
          '/api/v1/reports/financial?from=2026-09-01&to=2026-09-28',
        ),
        financial,
      );
      await auth.logout();
      final guardian = await login('01099900109', 'guardian');
      final mine = obj(
        await guardian.request(
          'GET',
          '/api/v1/guardian/children/$child/feedback-options',
        ),
      );
      for (final note in ids) {
        expect(
          rows(mine['mine'])
              .singleWhere((n) => n['id'] == note)['ownerResponse'],
          response,
        );
      }
      await t.pumpWidget(
        shell(CoachFeedbackPage(auth: guardian, playerId: child)),
      );
      await wait(find.textContaining('رد الإدارة: $response'));
      if (const bool.fromEnvironment('CAPTURE_VISUALS')) {
        await binding.takeScreenshot('phase-family-response-ios');
      }
      await guardian.logout();
      await t.pumpWidget(const SizedBox());
    },
  );
}
