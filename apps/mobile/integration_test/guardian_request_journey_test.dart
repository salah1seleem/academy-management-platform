import 'package:academy_mobile/core/api/api_client.dart';
import 'package:academy_mobile/core/auth/auth_controller.dart';
import 'package:academy_mobile/core/storage/refresh_store.dart';
import 'package:academy_mobile/features/guardian/guardian_ui.dart';
import 'package:academy_mobile/features/guardian/coach_feedback.dart';
import 'package:academy_mobile/features/owner/owner_feedback_detail.dart';
import 'package:flutter/material.dart';
import 'package:flutter_localizations/flutter_localizations.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:integration_test/integration_test.dart';

void main() {
  final binding = IntegrationTestWidgetsFlutterBinding.ensureInitialized();
  testWidgets(
    'A real family request and owner reply share one source without payment',
    (t) async {
      expect(const bool.fromEnvironment('ISOLATED_DEMO'), isTrue);
      expect(
        const String.fromEnvironment('API_BASE_URL'),
        'http://127.0.0.1:5259',
      );
      Future<AuthController> login(String phone, String role) async {
        final auth = AuthController(
          HttpApiTransport(
            AppConfig('Demo', const String.fromEnvironment('API_BASE_URL')),
          ),
          SecureRefreshStore('academy.v3.requests.$role'),
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

      Future<void> scroll(Finder f) async {
        await t.scrollUntilVisible(
          f,
          250,
          scrollable: find.byType(Scrollable).first,
        );
        await t.ensureVisible(f);
        await t.pumpAndSettle();
      }

      Future<void> select(String label, String value) async {
        final dd = find.widgetWithText(DropdownButtonFormField<String>, label);
        await scroll(dd);
        await t.tap(dd);
        await t.pumpAndSettle();
        await t.tap(find.text(value).last);
        await t.pumpAndSettle();
      }

      final owner = await login('01099900010', 'owner');
      final family = await login('01099900101', 'guardian');
      const financial =
          '/api/v1/reports/financial?from=2026-09-01&to=2026-09-28';
      final money = await owner.request('GET', financial);
      final child = obj(
        rows(await family.request('GET', '/api/v1/guardian/children')).first,
      )['id'].toString();
      final path = '/api/v1/guardian/children/$child/feedback-options';
      final options = obj(await family.request('GET', path));
      final group = obj(rows(options['groups']).first);
      await t.pumpWidget(
        shell(CoachFeedbackPage(auth: family, playerId: child)),
      );
      await wait(find.text('نوع الملاحظة'));
      await select('المجموعة', '${group['name']} · ${group['sport']}');
      await select('نوع الملاحظة', 'طلب للإدارة');
      expect(find.textContaining('لا ينفّذ دفعًا'), findsOneWidget);
      final message =
          'طلب اصطناعي لتوضيح المواعيد ${DateTime.now().microsecondsSinceEpoch}';
      await scroll(find.byType(TextFormField).last);
      await t.enterText(find.byType(TextFormField).last, message);
      await scroll(find.text('إرسال للإدارة'));
      await t.tap(find.text('إرسال للإدارة'));
      await wait(find.text('وصلت ملاحظتك لإدارة الأكاديمية.'));
      final saved = rows(obj(await family.request('GET', path))['mine'])
          .map(obj)
          .singleWhere((x) => x['message'] == message);
      expect(saved['kind'], 'Request');
      expect(saved['score'], isNull);
      expect(saved.containsKey('history'), isFalse);
      final id = saved['id'].toString();
      await t.pumpWidget(shell(OwnerFeedbackDetail(auth: owner, id: id)));
      await wait(find.text('طلب للإدارة'));
      expect(find.text(message), findsOneWidget);
      if (const bool.fromEnvironment('CAPTURE_VISUALS')) {
        await binding.takeScreenshot('phase-request-owner-ios');
      }
      final field = find.widgetWithText(TextField, 'الرد على الملاحظة');
      await scroll(field);
      const reply = 'تم توضيح المواعيد؛ لا تعديل تلقائي للجدول أو الاشتراك.';
      await t.enterText(field, reply);
      await scroll(find.text('إرسال الرد وحل الملاحظة'));
      await t.tap(find.text('إرسال الرد وحل الملاحظة'));
      await wait(find.text('تم الرد والحل'));
      final updated = rows(obj(await family.request('GET', path))['mine'])
          .map(obj)
          .singleWhere((x) => x['id'] == id);
      expect(updated['ownerResponse'], reply);
      expect(updated['status'], 'Resolved');
      expect(updated.containsKey('history'), isFalse);
      await expectLater(
        family.request('GET', '/api/v1/manage/feedback/$id'),
        throwsA(isA<ApiFailure>()),
      );
      expect(await owner.request('GET', financial), equals(money));
      await t.pumpWidget(
        shell(CoachFeedbackPage(auth: family, playerId: child)),
      );
      await wait(find.text('نوع الملاحظة'));
      await scroll(find.textContaining('رد الإدارة: $reply'));
      expect(find.text('طلب للإدارة'), findsWidgets);
      await family.logout();
      await owner.logout();
      expect(t.takeException(), isNull);
    },
  );
}
