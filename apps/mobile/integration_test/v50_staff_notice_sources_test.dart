import '../test/introduction_test_helpers.dart';

import 'package:academy_mobile/app/app.dart';
import 'package:academy_mobile/core/api/api_client.dart';
import 'package:academy_mobile/core/auth/auth_controller.dart';
import 'package:academy_mobile/core/storage/refresh_store.dart';
import 'package:academy_mobile/core/theme/appearance.dart';
import 'package:academy_mobile/features/admin/native_renewal_proof_review.dart';
import 'package:academy_mobile/features/guardian/family_renewal_request.dart';
import 'package:academy_mobile/features/guardian/guardian_ui.dart';
import 'package:academy_mobile/features/shared/matches.dart';
import 'package:academy_mobile/features/shared/notification_inbox.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:integration_test/integration_test.dart';

import 'v46_urgent_bell_read_test.dart' show IsolatedNoticeReadTransport;

class StaffNoticeAcceptanceTransport extends IsolatedNoticeReadTransport {
  StaffNoticeAcceptanceTransport(super.config);
  final responses = <String, Object?>{};
  @override
  Future<Object?> send(
    String method,
    String path, {
    Object? body,
    String? token,
    String? idempotencyKey,
  }) async {
    final value = await super.send(
      method,
      path,
      body: body,
      token: token,
      idempotencyKey: idempotencyKey,
    );
    if (method == 'GET') responses[path] = value;
    return value;
  }
}

void main() {
  final binding = IntegrationTestWidgetsFlutterBinding.ensureInitialized();
  testWidgets(
    'Real owner exact payment notices, coach match inbox and empty administrator inbox preserve recipients and restore read flags',
    (t) async {
      expect(const bool.fromEnvironment('ISOLATED_DEMO'), true);
      const base = String.fromEnvironment('API_BASE_URL');
      expect(base, 'http://127.0.0.1:5278');
      Future<({AuthController auth, StaffNoticeAcceptanceTransport transport})>
      login(String phone, String role, String key) async {
        final transport = StaffNoticeAcceptanceTransport(
          AppConfig('Demo', base),
        );
        final auth = AuthController(transport, SecureRefreshStore(key));
        await auth.restore();
        if (auth.session == null) {
          final challenge = await auth.requestOtp(phone);
          await auth.verify(phone, challenge, '246810');
        }
        expect(auth.session!.selected!.role, role);
        expect(
          auth.session!.selected!.academyId,
          '30000000-0000-0000-0000-000000000003',
        );
        return (auth: auth, transport: transport);
      }

      Future<void> wait(Finder f) async {
        for (var i = 0; i < 240 && f.evaluate().isEmpty; i++) {
          if (find.text('تخطي').evaluate().isNotEmpty) {
            await finishRoleIntroduction(t);
          }
          await t.pump(const Duration(milliseconds: 150));
        }
        expect(f, findsWidgets);
        await t.pumpAndSettle();
      }

      Future<void> mount(AuthController auth) async {
        await t.pumpWidget(
          AcademyApp(
            key: ValueKey(auth),
            auth: auth,
            environment: 'Demo',
            appearance: Appearance(),
          ),
        );
        await wait(find.byTooltip('التنبيهات'));
        await t.tap(find.byTooltip('التنبيهات'));
        await wait(find.byType(NotificationInbox));
      }

      Finder card(Json notice) => find
          .ancestor(
            of: find.text(textOf(notice['message'])),
            matching: find.byType(GCard),
          )
          .first;
      Future<void> tapOn(Json notice, String label) async {
        final control = find.descendant(
          of: card(notice),
          matching: find.text(label),
        );
        await t.scrollUntilVisible(
          control,
          200,
          scrollable: find.byType(Scrollable).first,
          maxScrolls: 80,
        );
        await t.tap(control);
        await t.pumpAndSettle();
      }

      Future<void> back(Finder page) async {
        Navigator.of(t.element(page)).pop();
        await t.pumpAndSettle();
        await wait(find.byType(NotificationInbox));
      }

      final owner = await login(
        '01099900010',
        'AcademyOwner',
        'v47.5278.decision-assignment.owner10',
      );
      final restorations = <({AuthController auth, Json notice})>[];
      final controllers = <AuthController>[owner.auth];
      try {
        final ownerBefore = obj(
          await owner.auth.request('GET', '/api/v1/notifications'),
        );
        final ownerRows = rows(ownerBefore['items']);
        final single = ownerRows.singleWhere(
          (n) =>
              n['targetKind'] == 'PaymentQueue' &&
              n['paymentReviewType'] == 'Renewal',
        );
        final family = ownerRows.singleWhere(
          (n) =>
              n['targetKind'] == 'PaymentQueue' &&
              n['paymentReviewType'] == 'FamilyRenewal',
        );
        await mount(owner.auth);
        await wait(find.text(textOf(single['message'])));
        await tapOn(single, 'عرض طلب الدفع ومراجعته');
        await wait(find.byType(NativeRenewalProofReview));
        expect(
          t
              .widget<NativeRenewalProofReview>(
                find.byType(NativeRenewalProofReview),
              )
              .id,
          single['targetId'],
        );
        final singlePath =
            '/api/v1/subscriptions/payments/${single['targetId']}';
        for (
          var i = 0;
          i < 240 && !owner.transport.responses.containsKey(singlePath);
          i++
        ) {
          await t.pump(const Duration(milliseconds: 150));
        }
        expect(
          obj(owner.transport.responses[singlePath])['id'],
          single['targetId'],
        );
        expect(
          obj(owner.transport.responses[singlePath])['status'],
          'Confirmed',
        );
        await binding.takeScreenshot(
          'phase-approved-owner-notice-single-source',
        );
        await back(find.byType(NativeRenewalProofReview));
        await tapOn(family, 'عرض طلب الدفع ومراجعته');
        await wait(find.byType(NativeFamilyRenewalRequest));
        final page = t.widget<NativeFamilyRenewalRequest>(
          find.byType(NativeFamilyRenewalRequest),
        );
        expect(page.id, family['targetId']);
        expect(page.staff, true);
        final familyPath =
            '/api/v1/manage/family-renewals/${family['targetId']}';
        for (
          var i = 0;
          i < 240 && !owner.transport.responses.containsKey(familyPath);
          i++
        ) {
          await t.pump(const Duration(milliseconds: 150));
        }
        expect(
          obj(owner.transport.responses[familyPath])['id'],
          family['targetId'],
        );
        await binding.takeScreenshot(
          'phase-approved-owner-notice-family-source',
        );
        await back(find.byType(NativeFamilyRenewalRequest));
        owner.transport.permittedReads.add(
          '/api/v1/notifications/${single['id']}/read',
        );
        restorations.add((auth: owner.auth, notice: single));
        await tapOn(
          single,
          single['isRead'] == true ? 'تحديد كغير مقروء' : 'تمت القراءة',
        );
        final ownerAfter = obj(
          await owner.auth.request('GET', '/api/v1/notifications'),
        );
        expect(
          rows(ownerAfter['items'])
              .singleWhere((n) => n['id'] == single['id'])['isRead'],
          single['isRead'] != true,
        );
        final coach = await login(
          '01099900020',
          'Coach',
          'v50.notices.coach20',
        );
        controllers.add(coach.auth);
        final coachBefore = obj(
          await coach.auth.request('GET', '/api/v1/notifications'),
        );
        final matches = rows(
          await coach.auth.request('GET', '/api/v1/my-matches'),
        );
        final notice = rows(coachBefore['items']).firstWhere(
          (n) =>
              n['matchId'] != null &&
              matches.any((m) => m['id'] == n['matchId']),
        );
        expect(
          rows(coachBefore['items'])
              .any((n) => ownerRows.any((o) => o['id'] == n['id'])),
          false,
        );
        await t.pumpWidget(const SizedBox());
        await mount(coach.auth);
        await wait(find.text(textOf(notice['message'])));
        await tapOn(notice, 'عرض المباراة');
        await wait(find.byType(MatchSchedule));
        expect(
          t.widget<MatchSchedule>(find.byType(MatchSchedule)).matchId,
          notice['matchId'],
        );
        await wait(
          find.textContaining(
            textOf(
              matches.singleWhere(
                (m) => m['id'] == notice['matchId'],
              )['opponent'],
            ),
          ),
        );
        await binding.takeScreenshot('phase-approved-coach-notice-exact-match');
        await back(find.byType(MatchSchedule));
        coach.transport.permittedReads.add(
          '/api/v1/notifications/${notice['id']}/read',
        );
        restorations.add((auth: coach.auth, notice: notice));
        await tapOn(
          notice,
          notice['isRead'] == true ? 'تحديد كغير مقروء' : 'تمت القراءة',
        );
        expect(
          rows(
            obj(
              await coach.auth.request('GET', '/api/v1/notifications'),
            )['items'],
          ).singleWhere((n) => n['id'] == notice['id'])['isRead'],
          notice['isRead'] != true,
        );
        final admin = await login(
          '01099900011',
          'AcademyAdmin',
          'v50.notices.admin11',
        );
        controllers.add(admin.auth);
        final adminInbox = obj(
          await admin.auth.request('GET', '/api/v1/notifications'),
        );
        expect(adminInbox['total'], 0);
        await t.pumpWidget(const SizedBox());
        await mount(admin.auth);
        await wait(find.text('لا توجد تنبيهات.'));
        expect(find.text('0 غير مقروءة'), findsOneWidget);
        expect(find.text(textOf(single['message'])), findsNothing);
        await binding.takeScreenshot(
          'phase-approved-admin-isolated-empty-inbox',
        );
        expect(t.takeException(), isNull);
      } finally {
        await t.pumpWidget(const SizedBox());
        for (final restore in restorations) {
          await restore.auth.request(
            'PUT',
            '/api/v1/notifications/${restore.notice['id']}/read',
            body: {'isRead': restore.notice['isRead'] == true},
          );
          final saved = obj(
            await restore.auth.request('GET', '/api/v1/notifications'),
          );
          expect(
            rows(saved['items'])
                .singleWhere((n) => n['id'] == restore.notice['id'])['isRead'],
            restore.notice['isRead'],
          );
        }
        for (final auth in controllers) {
          auth.dispose();
        }
      }
    },
  );
}
