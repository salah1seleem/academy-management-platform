import 'package:academy_mobile/core/api/api_client.dart';
import 'package:academy_mobile/core/auth/auth_controller.dart';
import 'package:academy_mobile/core/storage/refresh_store.dart';
import 'package:academy_mobile/features/guardian/guardian_ui.dart';
import 'package:academy_mobile/features/owner/owner_feedback_detail.dart';
import 'package:academy_mobile/features/owner/owner_followups.dart';
import 'package:academy_mobile/features/shared/feedback_investigations.dart';
import 'package:flutter/material.dart';
import 'package:flutter_localizations/flutter_localizations.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:integration_test/integration_test.dart';

void main() {
  final binding = IntegrationTestWidgetsFlutterBinding.ensureInitialized();
  testWidgets(
    'Real private investigation assignment and findings never become a family response',
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
          SecureRefreshStore('academy.v3.investigation.$suffix'),
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

      final family = await login('01099900109', 'guardian');
      final child = textOf(
        rows(await family.request('GET', '/api/v1/guardian/children'))
            .first['id'],
      );
      final group = rows(
        obj(
          await family.request(
            'GET',
            '/api/v1/guardian/children/$child/feedback-options',
          ),
        )['groups'],
      ).first;
      final message =
          'شكوى اصطناعية خاصة لرحلة المحاكي ${followUpRequestId().substring(0, 8)}';
      final id = textOf(
        obj(
          await family.request(
            'POST',
            '/api/v1/guardian/children/$child/feedback',
            body: {
              'trainingGroupId': group['id'],
              'coachMembershipId': null,
              'kind': 'Complaint',
              'score': null,
              'message': message,
            },
            idempotencyKey: followUpRequestId(),
          ),
        )['id'],
      );
      final owner = await login('01099900010', 'owner');
      final money = await owner.request(
        'GET',
        '/api/v1/reports/financial?from=2026-09-01&to=2026-09-28',
      );
      await t.pumpWidget(shell(OwnerFeedbackDetail(auth: owner, id: id)));
      await wait(find.text(message));
      final dd = find.widgetWithText(
        DropdownButtonFormField<String>,
        'مسؤول التحقيق',
      );
      await scroll(dd, 200);
      await t.tap(dd);
      await t.pumpAndSettle();
      await t.tap(find.text('منسق المتابعات — عرض اصطناعي').last);
      await t.pumpAndSettle();
      final brief = 'راجع تنظيم الوقت دون أسماء الأسر ${id.substring(0, 8)}';
      final field = find.widgetWithText(TextField, 'موجز يراجعه المالك');
      await scroll(field, 200);
      await t.enterText(field, brief);
      await t.testTextInput.receiveAction(TextInputAction.done);
      await t.pumpAndSettle();
      final save = find.text('حفظ تكليف التحقيق');
      await t.ensureVisible(save);
      await t.tap(save);
      await wait(find.text('تم حفظ تكليف التحقيق؛ لم يُرسل شيء للأسرة.'));
      final source = obj(
        await owner.request('GET', '/api/v1/manage/feedback/$id'),
      );
      final assignment = textOf(source['investigationId']);
      expect(assignment, isNot(id));
      final admin = await login('01099900041', 'delegate');
      await t.pumpWidget(
        shell(PrivateInvestigationsPage(auth: admin, assignmentId: assignment)),
      );
      await wait(find.text(brief));
      expect(find.text(message), findsNothing);
      expect(find.text('إرسال الرد وحل الملاحظة'), findsNothing);
      final note = 'نتيجة خاصة من الإداري للمالك ${id.substring(0, 8)}';
      final resultField = find.widgetWithText(
        TextField,
        'نتيجة التحقيق للمالك',
      );
      await scroll(resultField, 200);
      await t.enterText(resultField, note);
      await t.testTextInput.receiveAction(TextInputAction.done);
      await t.pumpAndSettle();
      final resultSave = find.text('حفظ النتيجة الخاصة');
      await t.ensureVisible(resultSave);
      await t.tap(resultSave);
      await wait(find.text('تم حفظ النتيجة الخاصة للمالك فقط.'));
      await wait(find.text(note));
      if (const bool.fromEnvironment('CAPTURE_VISUALS')) {
        await binding.takeScreenshot('phase-private-investigation-ios');
      }
      final updated = obj(
        await owner.request('GET', '/api/v1/manage/feedback/$id'),
      );
      expect(updated['status'], 'Open');
      expect(updated['ownerResponse'], isNull);
      expect(
        rows(updated['history'])
            .any((c) => c['action'] == 'Findings' && c['internalNote'] == note),
        isTrue,
      );
      final mine = obj(
        await family.request(
          'GET',
          '/api/v1/guardian/children/$child/feedback-options',
        ),
      );
      expect(mine.toString(), isNot(contains(brief)));
      expect(mine.toString(), isNot(contains(note)));
      expect(mine.toString(), isNot(contains(assignment)));
      await t.pumpWidget(shell(OwnerFeedbackDetail(auth: owner, id: id)));
      await wait(find.text(message));
      final cancel = find.text('إلغاء تكليف التحقيق');
      await scroll(cancel, 200);
      await t.tap(cancel);
      await wait(find.text('تم حفظ تكليف التحقيق؛ لم يُرسل شيء للأسرة.'));
      await expectLater(
        admin.request('GET', '/api/v1/my-followups/investigations/$assignment'),
        throwsA(isA<ApiFailure>().having((e) => e.status, 'status', 404)),
      );
      expect(
        await owner.request(
          'GET',
          '/api/v1/reports/financial?from=2026-09-01&to=2026-09-28',
        ),
        money,
      );
      await admin.logout();
      await owner.logout();
      await family.logout();
      await t.pumpAndSettle();
    },
  );
}
