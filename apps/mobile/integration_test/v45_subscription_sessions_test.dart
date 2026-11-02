import 'package:academy_mobile/core/api/api_client.dart';
import 'package:academy_mobile/core/auth/auth_controller.dart';
import 'package:academy_mobile/core/storage/refresh_store.dart';
import 'package:academy_mobile/core/theme/app_theme.dart';
import 'package:academy_mobile/features/admin/native_player_workspace.dart';
import 'package:academy_mobile/features/guardian/guardian_profile.dart';
import 'package:academy_mobile/features/guardian/guardian_ui.dart';
import 'package:academy_mobile/features/shared/notification_inbox.dart';
import 'package:academy_mobile/features/shared/staff_capabilities.dart';
import 'package:flutter/material.dart';
import 'package:flutter_localizations/flutter_localizations.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:integration_test/integration_test.dart';

class GrantTransport extends HttpApiTransport {
  GrantTransport(super.config);
  Json? request;
  String? key, audit;
  @override
  Future<Object?> send(
    String method,
    String path, {
    Object? body,
    String? token,
    String? idempotencyKey,
  }) async {
    if (method == 'POST' && path.endsWith('/sessions')) {
      request = obj(body);
      key = idempotencyKey;
      debugPrint('Session grant intent: $path / $key / $request');
    }
    final value = await super.send(
      method,
      path,
      body: body,
      token: token,
      idempotencyKey: idempotencyKey,
    );
    if (method == 'POST' && path.endsWith('/sessions')) {
      audit = textOf(obj(value)['adjustmentId']);
      debugPrint('Session grant committed: $audit');
    }
    return value;
  }
}

void main() {
  final binding = IntegrationTestWidgetsFlutterBinding.ensureInitialized();
  testWidgets(
    'actual session grant and whole-period end reach only linked family without duplicate quota or finance',
    (t) async {
      expect(const bool.fromEnvironment('ISOLATED_DEMO'), isTrue);
      const base = String.fromEnvironment('API_BASE_URL');
      expect(base, 'http://127.0.0.1:5278');
      final logical = t.view.physicalSize / t.view.devicePixelRatio;
      expect(logical.width, 375);
      debugPrint('Session grant viewport: $logical');
      final transport = GrantTransport(AppConfig('Demo', base));
      Future<AuthController> login(String phone) async {
        final auth = AuthController(
          phone == '01099900010'
              ? transport
              : HttpApiTransport(AppConfig('Demo', base)),
          SecureRefreshStore('v45.5278.sessions.$phone'),
        );
        await auth.restore();
        if (auth.session == null) {
          final c = await auth.requestOtp(phone);
          await auth.verify(phone, c, '246810');
        }
        expect(
          auth.session!.selected!.academyId,
          '30000000-0000-0000-0000-000000000003',
        );
        return auth;
      }

      final owner = await login('01099900010'),
          guardian = await login('01099900100'),
          foreign = await login('01099900104');
      final money = obj(
        await owner.request(
          'GET',
          '/api/v1/reports/financial?allDates=true&pageSize=1',
        ),
      );
      const resumePeriod = String.fromEnvironment(
            'RESUME_SESSION_GRANT_PERIOD',
          ),
          resumeAudit = String.fromEnvironment('RESUME_SESSION_GRANT_AUDIT');
      Json? selected;
      if (resumePeriod.isNotEmpty) {
        selected = obj(
          await owner.request(
            'GET',
            '/api/v1/subscriptions/periods/$resumePeriod',
          ),
        );
      } else {
        final enrollments = rows(
          await guardian.request(
            'GET',
            '/api/v1/guardian/subscriptions/enrollments',
          ),
        );
        for (final e in enrollments) {
          if (e['period'] is! Map) {
            continue;
          }
          final p = obj(
            await owner.request(
              'GET',
              '/api/v1/subscriptions/periods/${e['period']['id']}',
            ),
          );
          if (p['canGrantSessions'] == true &&
              ['Combined', 'Sessions'].contains(p['planType']) &&
              p['remainingSessions'] != null &&
              !['Cancelled', 'Frozen'].contains(p['status'])) {
            selected = p;
            break;
          }
        }
      }
      expect(selected, isNotNull);
      final before = selected!, id = textOf(before['id']);
      final calendar = obj(
        await owner.request('GET', '/api/v1/academy/calendar'),
      );
      final today = DateTime.parse(textOf(calendar['today'])),
          oldEnd = DateTime.tryParse(textOf(before['endDate']));
      final newEnd = (oldEnd == null || oldEnd.isBefore(today) ? today : oldEnd)
          .add(const Duration(days: 2));
      debugPrint(
        'Session grant selected: $id / balance ${before['remainingSessions']} / initial ${before['initialSessions']} / end ${before['endDate']}',
      );
      Future<void> waitFor(Finder f) async {
        for (var i = 0; i < 240 && f.evaluate().isEmpty; i++) {
          await t.pump(const Duration(milliseconds: 150));
        }
        expect(f, findsWidgets);
        await t.pumpAndSettle();
      }

      Future<void> reach(Finder f) async {
        await waitFor(find.byType(Scrollable));
        await t.scrollUntilVisible(
          f,
          130,
          scrollable: find.byType(Scrollable).first,
        );
        await t.ensureVisible(f.last);
        await t.pumpAndSettle();
      }

      Future<void> tap(String label) async {
        final f = find.text(label).last;
        await reach(f);
        await t.tap(f);
        await t.pumpAndSettle();
      }

      Future<void> show(Widget child, String key) async {
        await t.pumpWidget(
          MaterialApp(
            key: ValueKey(key),
            debugShowCheckedModeBanner: false,
            locale: const Locale('ar'),
            supportedLocales: const [Locale('ar')],
            localizationsDelegates: GlobalMaterialLocalizations.delegates,
            theme: AcademyTheme.dark(),
            home: child,
          ),
        );
        await t.pump(const Duration(milliseconds: 350));
      }

      await show(
        NativeSubscriptionPeriod(
          auth: owner,
          id: id,
          access: const StaffCapabilities(
            owner: true,
            permissions: [],
            scope: {},
          ),
        ),
        'owner',
      );
      if (resumeAudit.isEmpty) {
        final select = find.widgetWithText(
          DropdownButtonFormField<String>,
          'الإجراء',
        );
        await reach(select);
        await t.tap(select);
        await t.pumpAndSettle();
        expect(find.text('إضافة أيام'), findsNothing);
        await t.tap(find.text('إضافة حصص وتحديد نهاية الاشتراك').last);
        await t.pumpAndSettle();
        final count = find.widgetWithText(TextFormField, 'عدد الحصص الإضافية');
        await reach(count);
        await t.enterText(count, '2');
        final date = find.widgetWithText(
          TextFormField,
          'نهاية الاشتراك الجديدة',
        );
        await reach(date);
        await t.tap(date);
        await t.pumpAndSettle();
        final local = MaterialLocalizations.of(
          t.element(find.byType(DatePickerDialog)),
        );
        await t.tap(find.byIcon(Icons.edit_outlined));
        await t.pumpAndSettle();
        await t.enterText(
          find.descendant(
            of: find.byType(DatePickerDialog),
            matching: find.byType(TextField),
          ),
          local.formatCompactDate(newEnd),
        );
        await t.tap(find.text(local.okButtonLabel));
        await t.pumpAndSettle();
        final reason = find.widgetWithText(TextFormField, 'سبب التعديل');
        await reach(reason);
        await t.enterText(reason, 'قبول إضافة حصص اصطناعي على375');
        await t.pumpAndSettle();
        await binding.takeScreenshot('phase-approved-session-grant-form-w375');
        await tap('راجعت أثر التعديل على الاشتراك وأؤكد تنفيذه.');
        await tap('تأكيد تعديل الاشتراك');
        await waitFor(find.text('حُفظ التعديل وأُعيدت قراءة سجل الاشتراك.'));
      }
      final auditId = resumeAudit.isEmpty ? transport.audit! : resumeAudit;
      final saved = obj(
        await owner.request('GET', '/api/v1/subscriptions/periods/$id'),
      );
      final audit = rows(saved['adjustments'])
          .singleWhere((a) => a['id'] == auditId);
      expect(audit['type'], 'SessionsAdded');
      expect(audit['sessionsDelta'], 2);
      expect(
        (audit['newRemainingSessions'] as int) -
            (audit['oldRemainingSessions'] as int),
        2,
      );
      expect(saved['endDate'], audit['newEndDate']);
      expect(saved['initialSessions'], before['initialSessions']);
      expect(saved['priceSnapshot'], before['priceSnapshot']);
      expect(saved['receiptId'], before['receiptId']);
      if (resumeAudit.isEmpty) {
        expect(saved['endDate'], newEnd.toIso8601String().substring(0, 10));
        expect(
          saved['remainingSessions'],
          (before['remainingSessions'] as int) + 2,
        );
        final replay = obj(
          await owner.request(
            'POST',
            '/api/v1/subscriptions/periods/$id/sessions',
            body: transport.request,
            idempotencyKey: transport.key,
          ),
        );
        expect(replay['adjustmentId'], auditId);
        expect(replay['replay'], isTrue);
        final read = obj(
          await owner.request('GET', '/api/v1/subscriptions/periods/$id'),
        );
        expect(read['remainingSessions'], saved['remainingSessions']);
      }
      await reach(find.textContaining('+2 حصة'));
      await binding.takeScreenshot('phase-approved-session-grant-audit-w375');
      final inbox = obj(await guardian.request('GET', '/api/v1/notifications'));
      final notice = rows(inbox['items']).singleWhere(
        (n) => n['eventKey'] == 'SessionsAdded' && n['targetId'] == id,
      );
      expect(textOf(notice['message']).contains('{'), isFalse);
      final other = obj(await foreign.request('GET', '/api/v1/notifications'));
      expect(
        rows(
          other['items'],
        ).where((n) => n['eventKey'] == 'SessionsAdded' && n['targetId'] == id),
        isEmpty,
      );
      await expectLater(
        foreign.request(
          'GET',
          '/api/v1/guardian/subscriptions/periods/$id/adjustments',
        ),
        throwsA(isA<ApiFailure>().having((e) => e.status, 'status', 404)),
      );
      await show(NotificationInbox(auth: guardian), 'guardian-inbox');
      await waitFor(find.text(textOf(notice['message'])));
      await binding.takeScreenshot(
        'phase-approved-guardian-session-grant-inbox-w375',
      );
      final card = find
          .ancestor(
            of: find.text(textOf(notice['message'])),
            matching: find.byType(GCard),
          )
          .first;
      final link = find.descendant(
        of: card,
        matching: find.text('عرض الاشتراك وتعديلاته'),
      );
      await reach(link);
      await t.tap(link);
      await t.pump(const Duration(milliseconds: 300));
      await waitFor(find.byType(PeriodPage));
      expect(t.widget<PeriodPage>(find.byType(PeriodPage)).id, id);
      await waitFor(
        find.textContaining('الحصص المتبقية: ${saved['remainingSessions']}'),
      );
      await reach(find.textContaining('+2 حصة'));
      await binding.takeScreenshot(
        'phase-approved-guardian-session-grant-period-w375',
      );
      final after = obj(
        await owner.request(
          'GET',
          '/api/v1/reports/financial?allDates=true&pageSize=1',
        ),
      );
      expect(money['totalCount'], isA<int>());
      expect(money['netAmount'], isNotNull);
      expect(after['totalCount'], money['totalCount']);
      expect(after['netAmount'], money['netAmount']);
      expect(t.takeException(), isNull);
      await t.pumpWidget(const SizedBox());
      for (final auth in [owner, guardian, foreign]) {
        await auth.logout();
        auth.dispose();
      }
    },
  );
}
