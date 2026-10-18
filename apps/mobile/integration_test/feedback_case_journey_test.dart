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
    'Private case stages and source replies remain separate on the real Demo API',
    (t) async {
      expect(const bool.fromEnvironment('ISOLATED_DEMO'), isTrue);
      expect(
        const String.fromEnvironment('API_BASE_URL'),
        'http://127.0.0.1:5259',
      );
      Future<AuthController> login(String phone, String suffix) async {
        final auth = AuthController(
          HttpApiTransport(
            AppConfig('Demo', const String.fromEnvironment('API_BASE_URL')),
          ),
          SecureRefreshStore('academy.v3.private-case.$suffix'),
        );
        await auth.restore();
        if (auth.session != null) await auth.logout();
        final challenge = await auth.requestOtp(phone);
        await auth.verify(phone, challenge, '246810');
        return auth;
      }

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

      Future<void> scroll(Finder f, double delta) async {
        await t.scrollUntilVisible(
          f,
          delta,
          scrollable: find.byType(Scrollable).first,
        );
        await t.pumpAndSettle();
      }

      final family = await login('01099900101', 'guardian');
      final children = rows(
        await family.request('GET', '/api/v1/guardian/children'),
      );
      final child = textOf(children.first['id']);
      final options = obj(
        await family.request(
          'GET',
          '/api/v1/guardian/children/$child/feedback-options',
        ),
      );
      final group = rows(options['groups']).first;
      final created = obj(
        await family.request(
          'POST',
          '/api/v1/guardian/children/$child/feedback',
          body: {
            'trainingGroupId': group['id'],
            'coachMembershipId': null,
            'kind': 'Complaint',
            'score': null,
            'message': 'شكوى اصطناعية من رحلة المعالجة الخاصة في المحاكي',
          },
          idempotencyKey: followUpRequestId(),
        ),
      );
      final id = textOf(created['id']);
      final publicReply =
          'رد عام للأسرة من تطبيق المالك بعد متابعة الشكوى ${id.substring(0, 8)}';
      final owner = await login('01099900010', 'owner');
      final money = await owner.request(
        'GET',
        '/api/v1/reports/financial?from=2026-09-01&to=2026-09-28',
      );
      await t.pumpWidget(shell(OwnerFeedbackDetail(auth: owner, id: id)));
      await wait(find.text('شكوى اصطناعية من رحلة المعالجة الخاصة في المحاكي'));
      Future<void> move(String label, String status, String note) async {
        final dd = find.byType(DropdownButtonFormField<String>);
        await scroll(dd, 250);
        expect(dd, findsOneWidget);
        await t.tap(dd);
        await t.pumpAndSettle();
        await t.tap(find.text(label).last);
        await t.pumpAndSettle();
        final field = find.widgetWithText(TextField, 'ملاحظة داخلية (اختياري)');
        await scroll(field, 200);
        await t.enterText(field, note);
        await t.testTextInput.receiveAction(TextInputAction.done);
        await t.pumpAndSettle();
        final save = find.text('حفظ مرحلة المعالجة');
        await t.ensureVisible(save);
        await t.tap(save);
        await wait(find.text('تم حفظ تحديث الملاحظة.'));
        // The message can survive a reload, so also await the returned source widgets.
        await wait(find.byType(DropdownButtonFormField<String>));
        expect(
          obj(
            await owner.request('GET', '/api/v1/manage/feedback/$id'),
          )['status'],
          status,
        );
      }

      await move(
        'قيد المعالجة',
        'InProgress',
        'تحقيق داخلي اصطناعي لا ينشر للأسرة',
      );
      await move('بانتظار معلومات', 'AwaitingInfo', '');
      final history = find.text(
        'ملاحظة داخلية: تحقيق داخلي اصطناعي لا ينشر للأسرة',
      );
      await scroll(history, 250);
      expect(history, findsOneWidget);
      if (const bool.fromEnvironment('CAPTURE_VISUALS')) {
        await binding.takeScreenshot('phase-case-private-ios');
      }
      final reply = find.widgetWithText(TextField, 'الرد على الملاحظة');
      await scroll(reply, -250);
      await t.enterText(reply, publicReply);
      await t.testTextInput.receiveAction(TextInputAction.done);
      await t.pumpAndSettle();
      final resolve = find.text('إرسال الرد وحل الملاحظة');
      await t.ensureVisible(resolve);
      await t.tap(resolve);
      await wait(find.text('تم الرد والحل'));
      await move('مغلقة', 'Closed', '');
      await move('أعيد فتحها', 'Reopened', 'متابعة إضافية داخلية فقط');
      final record = obj(
        await owner.request('GET', '/api/v1/manage/feedback/$id'),
      );
      expect(rows(record['history']).length, 5);
      expect(record['ownerResponse'], publicReply);
      expect(
        await owner.request(
          'GET',
          '/api/v1/reports/financial?from=2026-09-01&to=2026-09-28',
        ),
        money,
      );
      await owner.logout();
      // Keep the independently authorized family session instead of requesting
      // a second OTP within the same short journey. Rate limits stay unchanged.
      final guardian = family;
      final mine = obj(
        await guardian.request(
          'GET',
          '/api/v1/guardian/children/$child/feedback-options',
        ),
      );
      final visible = rows(mine['mine']).singleWhere((x) => x['id'] == id);
      expect(visible['status'], 'Reopened');
      expect(visible['ownerResponse'], record['ownerResponse']);
      expect(visible.containsKey('history'), false);
      expect(mine.toString(), isNot(contains('تحقيق داخلي اصطناعي')));
      await t.pumpWidget(
        shell(CoachFeedbackPage(auth: guardian, playerId: child)),
      );
      await wait(find.text('ملاحظاتي السابقة'));
      await scroll(find.textContaining('رد الإدارة: $publicReply'), 250);
      expect(find.textContaining('تحقيق داخلي اصطناعي'), findsNothing);
      await expectLater(
        guardian.request('GET', '/api/v1/manage/feedback/$id'),
        throwsA(isA<ApiFailure>().having((e) => e.status, 'status', 403)),
      );
      await guardian.logout();
      await t.pumpAndSettle();
    },
  );
}
