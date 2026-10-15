import 'package:academy_mobile/app/app.dart';
import 'package:academy_mobile/core/api/api_client.dart';
import 'package:academy_mobile/core/auth/auth_controller.dart';
import 'package:academy_mobile/core/storage/refresh_store.dart';
import 'package:academy_mobile/features/guardian/guardian_ui.dart';
import 'package:academy_mobile/features/guardian/coach_feedback.dart';
import 'package:flutter/material.dart';
import 'package:flutter_localizations/flutter_localizations.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:integration_test/integration_test.dart';

// Resolves one synthetic family note in an explicitly isolated Football Demo.
// No payment/attendance write, seed reset, real message or customer data.
void main() {
  final binding = IntegrationTestWidgetsFlutterBinding.ensureInitialized();
  testWidgets(
    'Actual Admin day and Owner decisions lead to scoped sources; saved family response refreshes priorities',
    (tester) async {
      expect(const bool.fromEnvironment('ISOLATED_DEMO'), isTrue);
      final transport = HttpApiTransport(
        AppConfig('Demo', const String.fromEnvironment('API_BASE_URL')),
      );
      Future<AuthController> login(String phone, String key) async {
        final store = SecureRefreshStore(
          'academy.v3.operational-integration.$key',
        );
        final auth = AuthController(transport, store);
        await auth.restore();
        if (auth.session?.selected != null) {
          expect(
            auth.session!.selected!.academyId,
            '30000000-0000-0000-0000-000000000003',
          );
          return auth;
        }
        final challenge = await auth.requestOtp(phone);
        await auth.verify(phone, challenge, '246810');
        return auth;
      }

      Future<void> waitFor(Finder f) async {
        for (var i = 0; i < 180 && f.evaluate().isEmpty; i++) {
          await tester.pump(const Duration(milliseconds: 150));
        }
        expect(f, findsWidgets);
        await tester.pumpAndSettle();
      }

      Future<void> reach(Finder f) async {
        await tester.scrollUntilVisible(
          f,
          230,
          scrollable: find.byType(Scrollable).first,
        );
        await tester.ensureVisible(f);
        await tester.pumpAndSettle();
      }

      Future<void> capture(String name) async {
        if (const bool.fromEnvironment('CAPTURE_VISUALS')) {
          await binding.takeScreenshot('phase-owner-$name');
        }
      }

      final admin = await login('01099900011', 'admin');
      final sessions = rows(
        await admin.request('GET', '/api/v1/attendance/sessions'),
      ).where((s) => s['status'] != 'Cancelled').toList();
      expect(
        sessions,
        isNotEmpty,
        reason: 'Prepare a group session on the real Cairo day in the isolated Demo.',
      );
      await tester.pumpWidget(AcademyApp(auth: admin, environment: 'Demo'));
      await waitFor(find.text('جدول اليوم · ${sessions.length} حصة'));
      await capture('admin-day');
      final first = await admin.request(
        'GET',
        '/api/v1/attendance/sessions/${sessions.first['id']}/players',
      ) as Map;
      final card = find.byKey(
        ValueKey('coach-session-${sessions.first['id']}'),
      );
      final detail = find.descendant(
        of: card,
        matching: find.text('تفاصيل الحصة'),
      );
      await reach(detail);
      await tester.tap(detail);
      await waitFor(find.text('تفاصيل الحصة'));
      expect(
        find.text('حفظ الحضور'),
        first['canManageAttendance'] == true ? findsOneWidget : findsNothing,
      );
      await capture('admin-session');
      await tester.tap(find.byType(BackButton).last);
      await tester.pumpAndSettle();
      await reach(find.text('طلبات الدفع'));
      await tester.tap(find.text('طلبات الدفع'));
      await tester.pumpAndSettle();
      final queue = obj(
        await admin.request(
          'GET',
          '/api/v1/manage/payment-requests?status=Pending&allDates=true',
        ),
      );
      await waitFor(find.text('${queue['pending']} طلب بانتظار المراجعة'));
      await capture('admin-queue');
      await tester.pumpWidget(const SizedBox());
      await admin.logout();
      final owner = await login('01099900010', 'owner');
      final guardian = await login('01099900100', 'guardian');
      final child = rows(
        obj(await guardian.request('GET', '/api/v1/guardian/home'))['children'],
      ).single;
      final before = obj(
        await owner.request('GET', '/api/v1/reports/owner-summary'),
      );
      final ops = obj(before['operations']);
      final overdue = (ops['overdueFeedback'] as num) > 0;
      final filter = overdue ? 'overdue=true' : 'kind=Complaint';
      var notes = rows(
        await owner.request(
          'GET',
          '/api/v1/manage/feedback?status=Unresolved&$filter',
        ),
      );
      // After an earlier replay, prepare a new note through the real Guardian API,
      // not by rewinding or deleting previously resolved evidence.
      if (notes.where((n) => n['playerId'] == child['id']).isEmpty) {
        final options = obj(
          await guardian.request(
            'GET',
            '/api/v1/guardian/children/${child['id']}/feedback-options',
          ),
        );
        final group = rows(options['groups']).first;
        await guardian.request(
          'POST',
          '/api/v1/guardian/children/${child['id']}/feedback',
          body: {
            'trainingGroupId': group['id'],
            'coachMembershipId': null,
            'kind': 'Complaint',
            'score': null,
            'message':
                'شكوى رحلة تشغيل اصطناعية ${DateTime.now().millisecondsSinceEpoch}',
          },
          idempotencyKey: commandKey(),
        );
        notes = rows(
          await owner.request(
            'GET',
            '/api/v1/manage/feedback?status=Unresolved&kind=Complaint',
          ),
        );
      }
      final note = notes.firstWhere((n) => n['playerId'] == child['id']);
      await tester.pumpWidget(AcademyApp(auth: owner, environment: 'Demo'));
      await waitFor(find.text('لوحة المالك'));
      await waitFor(find.text('أولويات تحتاج قرارك'));
      await reach(find.text('أولويات تحتاج قرارك'));
      await capture('priorities');
      final action = find.text(
        overdue ? 'ملاحظات تجاوزت موعد المتابعة' : 'شكاوى تحتاج متابعة',
      );
      await reach(action);
      await tester.tap(action);
      await waitFor(find.text('ملاحظات أولياء الأمور'));
      final noteCard = find
          .ancestor(
            of: find.text(textOf(note['message'])),
            matching: find.byType(GCard),
          )
          .first;
      await reach(find.text(textOf(note['message'])));
      final resolve = find.descendant(
        of: noteCard,
        matching: find.text('الرد والحل'),
      );
      await tester.ensureVisible(resolve);
      await tester.pumpAndSettle();
      await tester.tap(resolve);
      await tester.pumpAndSettle();
      final reply =
          'رد عرض اصطناعي ${textOf(note['id']).substring(0, 8)}: تمت مراجعة الموعد والتنسيق مع الإدارة.';
      await tester.enterText(find.byType(TextField), reply);
      await tester.tap(find.text('إرسال الرد وحل الملاحظة'));
      await waitFor(find.text('أُرسل الرد لولي الأمر وحُلّت الملاحظة.'));
      await capture('saved-response');
      await tester.tap(find.byType(BackButton).last);
      await waitFor(find.text('أولويات تحتاج قرارك'));
      final after = obj(
        await owner.request('GET', '/api/v1/reports/owner-summary'),
      );
      expect(after['collections'], before['collections']);
      expect(after['paymentRequests'], before['paymentRequests']);
      if (overdue) {
        expect(
          obj(after['operations'])['overdueFeedback'],
          (ops['overdueFeedback'] as int) - 1,
        );
      }
      final performance = rows(obj(after['operations'])['coachPerformance'])
          .first;
      await reach(find.textContaining(textOf(performance['name'])));
      await tester.tap(find.textContaining(textOf(performance['name'])).first);
      await waitFor(find.text('متابعة المدرب'));
      await capture('coach-performance');
      final coach = obj(
        await owner.request(
          'GET',
          '/api/v1/reports/coach-performance/${performance['id']}?from=${before['from']}&to=${before['to']}',
        ),
      );
      final group = rows(coach['groups']).first;
      await reach(find.textContaining('${group['name']} ·'));
      await tester.tap(find.textContaining('${group['name']} ·').first);
      await waitFor(find.text('تفاصيل المجموعة'));
      await capture('group-source');
      final feedback = obj(
        await guardian.request(
          'GET',
          '/api/v1/guardian/children/${child['id']}/feedback-options',
        ),
      );
      expect(
        rows(feedback['mine']).singleWhere((n) => n['id'] == note['id']),
        containsPair('ownerResponse', reply),
      );
      await tester.pumpWidget(const SizedBox());
      await tester.pumpWidget(
        MaterialApp(
          locale: const Locale('ar'),
          supportedLocales: const [Locale('ar')],
          localizationsDelegates: GlobalMaterialLocalizations.delegates,
          theme: guardianTheme(),
          home: CoachFeedbackPage(
            auth: guardian,
            playerId: textOf(child['id']),
          ),
        ),
      );
      await waitFor(find.text('رأيك في التدريب'));
      await tester.scrollUntilVisible(
        find.textContaining(reply),
        250,
        scrollable: find.byType(Scrollable).first,
      );
      expect(find.textContaining(reply), findsOneWidget);
      await capture('guardian-response');
      expect(tester.takeException(), isNull);
      await tester.pumpWidget(const SizedBox());
      for (final auth in [admin, owner, guardian]) {
        await auth.logout();
        auth.dispose();
      }
      transport.close();
    },
  );
}
