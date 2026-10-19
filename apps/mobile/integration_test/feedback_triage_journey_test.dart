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
    'Safety intake and human triage stay distinct from a family response on the real API',
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
          SecureRefreshStore('academy.v3.triage.$suffix'),
        );
        await auth.restore();
        if (auth.session != null) {
          await auth.logout();
        }
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
        await t.ensureVisible(f);
        await t.pumpAndSettle();
      }

      Future<void> select(
        String label,
        String value, [
        double delta = 200,
      ]) async {
        final dd = find.widgetWithText(DropdownButtonFormField<String>, label);
        await scroll(dd, delta);
        await t.tap(dd);
        await t.pumpAndSettle();
        await t.tap(find.text(value).last);
        await t.pumpAndSettle();
      }

      final owner = await login('01099900010', 'owner');
      final money = await owner.request(
        'GET',
        '/api/v1/reports/financial?from=2026-09-01&to=2026-09-28',
      );
      Future<int> safetyCount() async =>
          (obj(
                    obj(
                      await owner.request(
                        'GET',
                        '/api/v1/reports/owner-summary?from=2026-09-01&to=2026-09-28',
                      ),
                    )['operations'],
                  )['openSafetyFeedback']
                  as num)
              .toInt();
      final before = await safetyCount();
      final family = await login('01099900101', 'family');
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
      await t.pumpWidget(
        shell(CoachFeedbackPage(auth: family, playerId: child)),
      );
      await wait(find.text('إرسال للإدارة'));
      await select('المجموعة', '${group['name']} · ${group['sport']}');
      await select('نوع الملاحظة', 'شكوى');
      await select('موضوع الملاحظة', 'المياه');
      await select('درجة الاستعجال', 'سلامة أو إساءة');
      final text =
          'بلاغ سلامة اصطناعي في رحلة المحاكي ${followUpRequestId().substring(0, 8)}';
      final field = find.widgetWithText(TextFormField, 'اكتب الملاحظة');
      await scroll(field, 200);
      await t.enterText(field, text);
      await t.testTextInput.receiveAction(TextInputAction.done);
      await t.pumpAndSettle();
      final send = find.text('إرسال للإدارة');
      await t.ensureVisible(send);
      await t.tap(send);
      await wait(find.textContaining('وصل بلاغ السلامة'));
      final mine = rows(
        obj(
          await family.request(
            'GET',
            '/api/v1/guardian/children/$child/feedback-options',
          ),
        )['mine'],
      );
      final created = mine.singleWhere((x) => x['message'] == text);
      final id = textOf(created['id']);
      expect(created['reportedUrgency'], 'Safety');
      expect(created['reportedSubject'], 'Water');
      expect(created['coachMembershipId'], isNull);
      expect(await safetyCount(), before + 1);
      await t.pumpWidget(shell(OwnerFeedbackDetail(auth: owner, id: id)));
      await wait(find.text(text));
      final resolve = find.widgetWithText(
        FilledButton,
        'إرسال الرد وحل الملاحظة',
      );
      await scroll(resolve, 200);
      expect(t.widget<FilledButton>(resolve).onPressed, isNull);
      await select('موضوع المراجعة', 'المرفق', -200);
      await select('درجة الاستعجال المراجعة', 'عادية');
      final note =
          'مراجعة داخلية اصطناعية لا تظهر للأسرة ${id.substring(0, 8)}';
      final internal = find.widgetWithText(
        TextField,
        'ملاحظة فرز داخلية (اختياري)',
      );
      await scroll(internal, 200);
      await t.enterText(internal, note);
      await t.testTextInput.receiveAction(TextInputAction.done);
      await t.pumpAndSettle();
      final triage = find.text('تأكيد الفرز البشري');
      await t.ensureVisible(triage);
      await t.tap(triage);
      await wait(
        find.text('تم تأكيد الفرز البشري دون تغيير بلاغ الأسرة الأصلي.'),
      );
      final record = obj(
        await owner.request('GET', '/api/v1/manage/feedback/$id'),
      );
      expect(record['reportedUrgency'], 'Safety');
      expect(record['urgency'], 'Routine');
      expect(record['safety'], isTrue);
      expect(record['ownerResponse'], isNull);
      expect(await safetyCount(), before + 1);
      final evidence = find.text('فرز بشري: المرفق · عادية');
      await scroll(evidence, 250);
      expect(evidence, findsOneWidget);
      if (const bool.fromEnvironment('CAPTURE_VISUALS')) {
        await binding.takeScreenshot('phase-human-triage-ios');
      }
      final reply = find.widgetWithText(TextField, 'الرد على الملاحظة');
      await scroll(reply, -250);
      final public = 'رد عام اصطناعي بعد الفرز البشري ${id.substring(0, 8)}';
      await t.enterText(reply, public);
      await t.testTextInput.receiveAction(TextInputAction.done);
      await t.pumpAndSettle();
      await t.ensureVisible(resolve);
      await t.tap(resolve);
      await wait(find.text('تم الرد والحل'));
      expect(await safetyCount(), before);
      await t.pumpWidget(
        shell(CoachFeedbackPage(auth: family, playerId: child)),
      );
      await wait(find.text('ملاحظاتي السابقة'));
      await scroll(find.textContaining('رد الإدارة: $public'), 250);
      expect(find.textContaining(note), findsNothing);
      final visible = rows(
        obj(
          await family.request(
            'GET',
            '/api/v1/guardian/children/$child/feedback-options',
          ),
        )['mine'],
      ).singleWhere((x) => x['id'] == id);
      expect(visible['reportedUrgency'], 'Safety');
      expect(visible.containsKey('urgency'), isFalse);
      expect(visible.containsKey('triagedAtUtc'), isFalse);
      expect(
        await owner.request(
          'GET',
          '/api/v1/reports/financial?from=2026-09-01&to=2026-09-28',
        ),
        money,
      );
      await owner.logout();
      await family.logout();
      await t.pumpAndSettle();
    },
  );
}
