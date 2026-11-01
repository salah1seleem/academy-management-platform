import '../test/introduction_test_helpers.dart';

import 'package:academy_mobile/app/app.dart';
import 'package:academy_mobile/core/api/api_client.dart';
import 'package:academy_mobile/core/auth/auth_controller.dart';
import 'package:academy_mobile/core/storage/refresh_store.dart';
import 'package:academy_mobile/core/theme/appearance.dart';
import 'package:academy_mobile/features/guardian/guardian_profile.dart';
import 'package:academy_mobile/features/guardian/guardian_report.dart';
import 'package:academy_mobile/features/guardian/guardian_payments.dart';
import 'package:academy_mobile/features/guardian/guardian_ui.dart';
import 'package:academy_mobile/features/shared/notification_inbox.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:integration_test/integration_test.dart';

import 'v45_single_renewal_saved_receipt_test.dart'
    show SavedRenewalReadTransport;

class GuardianEventReadTransport extends SavedRenewalReadTransport {
  GuardianEventReadTransport(super.config);
  final replies = <String, Object?>{};
  @override
  Future<Object?> send(
    String method,
    String path, {
    Object? body,
    String? token,
    String? idempotencyKey,
  }) async {
    final result = await super.send(
      method,
      path,
      body: body,
      token: token,
      idempotencyKey: idempotencyKey,
    );
    if (method == 'GET') replies[path] = result;
    return result;
  }
}

void main() {
  final binding = IntegrationTestWidgetsFlutterBinding.ensureInitialized();
  testWidgets(
    'Actual guardian notices open published evaluation, exact child stars and saved subscription events in light appearance without new business writes',
    (t) async {
      expect(const bool.fromEnvironment('ISOLATED_DEMO'), true);
      const base = String.fromEnvironment('API_BASE_URL');
      expect(base, 'http://127.0.0.1:5278');
      final controllers = <AuthController>[];
      String? evaluationId;
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

      try {
        for (final account in [
          (
            '01099900100',
            'v46.urgent-summary.guardian0',
            ['Evaluation', 'Star', 'SessionsAdded'],
          ),
          (
            '01099900101',
            'v46.urgent-summary.guardian1',
            ['Expiring', 'Expired'],
          ),
          (
            '01099900102',
            'v51.notice.guardian102',
            ['DaysAdded', 'DaysDeducted'],
          ),
          (
            '01200000007',
            'v45.5278.current-attendance.guardian7',
            ['Enrollment', 'SingleRenewal', 'SessionsAdded', 'Payment'],
          ),
        ]) {
          final transport = GuardianEventReadTransport(AppConfig('Demo', base));
          final auth = AuthController(
            transport,
            SecureRefreshStore(account.$2),
          );
          controllers.add(auth);
          await auth.restore();
          if (auth.session == null) {
            final challenge = await auth.requestOtp(account.$1);
            await auth.verify(account.$1, challenge, '246810');
          }
          expect(auth.session!.selected!.role, 'Guardian');
          expect(
            auth.session!.selected!.academyId,
            '30000000-0000-0000-0000-000000000003',
          );
          final before = obj(
            await auth.request('GET', '/api/v1/notifications'),
          );
          final notices = rows(before['items']);
          if (account.$1 == '01099900101' && evaluationId != null) {
            try {
              await auth.request(
                'GET',
                '/api/v1/guardian/evaluations/$evaluationId/report',
              );
              fail('Unlinked family report must be denied');
            } on ApiFailure catch (error) {
              expect(error.status, 404);
            }
          }
          await t.pumpWidget(
            AcademyApp(
              key: ValueKey(auth),
              auth: auth,
              environment: 'Demo',
              appearance: Appearance()..dark = false,
            ),
          );
          await wait(find.byTooltip('التنبيهات'));
          await t.tap(find.byTooltip('التنبيهات'));
          await wait(find.byType(NotificationInbox));
          for (final event in account.$3) {
            final notice = notices.firstWhere((n) => n['eventKey'] == event);
            final target = textOf(notice['targetId']);
            final action = event == 'Evaluation'
                ? 'عرض التقييم'
                : event == 'Star'
                ? 'عرض إنجاز اللاعب'
                : event == 'Payment'
                ? 'عرض طلب الدفع'
                : 'عرض الاشتراك وتعديلاته';
            await t.scrollUntilVisible(
              find.text(textOf(notice['message'])),
              200,
              scrollable: find.byType(Scrollable).first,
              maxScrolls: 80,
            );
            await t.pumpAndSettle();
            final tile = find
                .ancestor(
                  of: find.text(textOf(notice['message'])),
                  matching: find.byType(GCard),
                )
                .first;
            final button = find.descendant(
              of: tile,
              matching: find.text(action),
            );
            await t.scrollUntilVisible(
              button,
              200,
              scrollable: find.byType(Scrollable).first,
              maxScrolls: 80,
            );
            await t.tap(button);
            await t.pumpAndSettle();
            Finder destination;
            if (event == 'Evaluation') {
              destination = find.byType(PlayerReport);
              await wait(destination);
              expect(t.widget<PlayerReport>(destination).id, target);
              final report = obj(
                transport
                    .replies['/api/v1/guardian/evaluations/$target/report'],
              );
              expect(report['id'], target);
              evaluationId = target;
              final scored = rows(report['criteria']);
              expect(scored.every((c) => validAssessment(c['score'])), true);
              expect(scored.any((c) => c['score'] == 0), true);
              for (final criterion in scored) {
                await t.scrollUntilVisible(
                  find.text(textOf(criterion['name'])),
                  200,
                  scrollable: find.byType(Scrollable).first,
                  maxScrolls: 80,
                );
                expect(find.text(textOf(criterion['name'])), findsWidgets);
              }
            } else if (event == 'Star') {
              destination = find.byType(ChildProfile);
              await wait(destination);
              expect(t.widget<ChildProfile>(destination).playerId, target);
              final profile = obj(
                transport.replies['/api/v1/guardian/children/$target/profile'],
              );
              expect(obj(profile['player'])['id'], target);
            } else if (event == 'Payment') {
              destination = find.byType(CheckoutPage);
              await wait(destination);
              expect(t.widget<CheckoutPage>(destination).id, target);
              final payment = obj(
                transport
                    .replies['/api/v1/guardian/subscriptions/payments/$target'],
              );
              expect(payment['id'], target);
              expect(payment['status'], 'Confirmed');
            } else {
              destination = find.byType(PeriodPage);
              await wait(destination);
              expect(t.widget<PeriodPage>(destination).id, target);
              final period = obj(
                transport
                    .replies['/api/v1/guardian/subscriptions/periods/$target/adjustments'],
              );
              expect(period['plan'], isNotNull);
              expect(period['status'], isNotNull);
            }
            await binding.takeScreenshot(
              'phase-approved-guardian-notice-${account.$1.substring(account.$1.length - 3)}-${event.toLowerCase()}-light',
            );
            Navigator.of(t.element(destination)).pop();
            await t.pumpAndSettle();
            await wait(find.byType(NotificationInbox));
            await t.drag(find.byType(Scrollable).first, const Offset(0, 4000));
            await t.pumpAndSettle();
            debugPrint('GUARDIAN_NOTICE_EVENT=$event/$target');
          }
          final after = obj(await auth.request('GET', '/api/v1/notifications'));
          expect(after['total'], before['total']);
          expect(after['unread'], before['unread']);
          for (final notice in notices) {
            expect(
              rows(after['items'])
                  .singleWhere((n) => n['id'] == notice['id'])['isRead'],
              notice['isRead'],
            );
          }
          await t.pumpWidget(const SizedBox());
        }
        expect(t.takeException(), isNull);
      } finally {
        await t.pumpWidget(const SizedBox());
        for (final auth in controllers) {
          auth.dispose();
        }
      }
    },
  );
}
