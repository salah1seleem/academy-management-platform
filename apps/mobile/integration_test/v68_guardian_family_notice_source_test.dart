import 'package:academy_mobile/app/app.dart';
import 'package:academy_mobile/core/api/api_client.dart';
import 'package:academy_mobile/core/auth/auth_controller.dart';
import 'package:academy_mobile/core/storage/refresh_store.dart';
import 'package:academy_mobile/core/theme/appearance.dart';
import 'package:academy_mobile/features/guardian/family_renewal_request.dart';
import 'package:academy_mobile/features/guardian/guardian_payments.dart';
import 'package:academy_mobile/features/guardian/guardian_ui.dart';
import 'package:academy_mobile/features/shared/notification_inbox.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:integration_test/integration_test.dart';

// Existing committed renewal only: this acceptance cannot renew, collect,
// upload proof, approve, cancel, or change a subscription.
class FamilyNoticeReadTransport extends HttpApiTransport {
  FamilyNoticeReadTransport(super.config);
  final replies = <String, Object?>{};
  String? noticeId;
  int markerWrites = 0;
  @override
  Future<Object?> send(
    String method,
    String path, {
    Object? body,
    String? token,
    String? idempotencyKey,
  }) async {
    final marker =
        method == 'PUT' &&
        noticeId != null &&
        path == '/api/v1/notifications/$noticeId/read';
    if (method != 'GET' &&
        !path.startsWith('/api/v1/mobile/auth/') &&
        !marker) {
      throw StateError('Family notice acceptance permits no business writes.');
    }
    final result = await super.send(
      method,
      path,
      body: body,
      token: token,
      idempotencyKey: idempotencyKey,
    );
    if (method == 'GET') replies[path] = result;
    if (marker) markerWrites++;
    return result;
  }
}

void main() {
  final binding = IntegrationTestWidgetsFlutterBinding.ensureInitialized();
  testWidgets(
    'family bell opens its committed two-child renewal and exact receipts without another collection',
    (t) async {
      expect(const bool.fromEnvironment('ISOLATED_DEMO'), true);
      const base = String.fromEnvironment('API_BASE_URL');
      expect(base, 'http://127.0.0.1:5278');
      const width = int.fromEnvironment(
        'FAMILY_NOTICE_EXPECTED_WIDTH',
        defaultValue: 440,
      );
      expect(
        (t.view.physicalSize / t.view.devicePixelRatio).width,
        width.toDouble(),
      );
      const requestId = 'ff9634a6-a3b1-4845-a71f-3b204ae04891';
      const detailPath =
          '/api/v1/guardian/subscriptions/family/requests/$requestId';
      final transport = FamilyNoticeReadTransport(AppConfig('Demo', base));
      final otherTransport = FamilyNoticeReadTransport(AppConfig('Demo', base));
      final controllers = <AuthController>[];
      Future<AuthController> login(
        String phone,
        FamilyNoticeReadTransport api,
      ) async {
        final auth = AuthController(
          api,
          SecureRefreshStore('v45.5278.guardian-workspace.$phone'),
        );
        controllers.add(auth);
        await auth.restore();
        if (auth.session == null) {
          final challenge = await auth.requestOtp(phone);
          await auth.verify(phone, challenge, '246810');
        }
        expect(auth.session!.selected!.role, 'Guardian');
        expect(
          auth.session!.selected!.academyId,
          '30000000-0000-0000-0000-000000000003',
        );
        return auth;
      }

      final guardian = await login('01200000001', transport);
      final other = await login('01200000007', otherTransport);
      final inboxBefore = obj(
        await guardian.request('GET', '/api/v1/notifications'),
      );
      final notice = rows(inboxBefore['items']).singleWhere(
        (n) => n['eventKey'] == 'FamilyRenewal' && n['targetId'] == requestId,
      );
      expect(notice['targetKind'], 'FamilyRenewal');
      final id = textOf(notice['id']);
      transport.noticeId = id;
      otherTransport.noticeId = id;
      final before = obj(await guardian.request('GET', detailPath));
      expect(before['status'], 'Approved');
      expect(double.parse(textOf(before['totalAmount'])), 1600);
      final lines = rows(before['items']);
      expect(lines.length, 2);
      final receiptSources = <String, Json>{};
      for (final line in lines) {
        expect(textOf(notice['message']), contains(textOf(line['player'])));
        final receiptId = textOf(line['receiptId']);
        expect(receiptId, isNotEmpty);
        final receipt = obj(
          await guardian.request(
            'GET',
            '/api/v1/guardian/subscriptions/receipts/$receiptId',
          ),
        );
        expect(receipt['playerNameSnapshot'], line['player']);
        expect(
          double.parse(textOf(receipt['amount'])),
          double.parse(textOf(line['finalAmount'])),
        );
        receiptSources[receiptId] = receipt;
      }
      expect(receiptSources.length, 2);
      expect(textOf(notice['message']), isNot(contains('{')));
      final otherInbox = obj(
        await other.request('GET', '/api/v1/notifications'),
      );
      Future<void> denied(String method, String path, {Object? body}) async {
        try {
          await other.request(method, path, body: body);
          fail('Another family cannot access this source.');
        } on ApiFailure catch (e) {
          expect(e.status, 404);
        }
      }

      await denied('GET', detailPath);
      for (final receiptId in receiptSources.keys) {
        await denied(
          'GET',
          '/api/v1/guardian/subscriptions/receipts/$receiptId',
        );
      }
      await denied(
        'PUT',
        '/api/v1/notifications/$id/read',
        body: {'isRead': true},
      );
      expect(
        obj(await other.request('GET', '/api/v1/notifications')),
        otherInbox,
      );

      Future<void> wait(Finder f) async {
        for (var i = 0; i < 240 && f.evaluate().isEmpty; i++) {
          await t.pump(const Duration(milliseconds: 150));
        }
        expect(f, findsWidgets);
        await t.pumpAndSettle();
      }

      Future<void> reach(Finder f) async {
        final scroll = find.byType(Scrollable).first;
        t.state<ScrollableState>(scroll).position.jumpTo(0);
        await t.pumpAndSettle();
        await t.scrollUntilVisible(f, 160, scrollable: scroll, maxScrolls: 100);
        await t.ensureVisible(f);
        await t.pumpAndSettle();
      }

      final appearance = Appearance();
      for (final sport in ['football', 'general']) {
        await appearance.completeRoleIntro('Guardian', sport);
      }
      try {
        for (final light in [true, false]) {
          await t.pumpWidget(const SizedBox());
          await t.pumpAndSettle();
          await appearance.update(night: !light);
          await t.pumpWidget(
            AcademyApp(
              key: ValueKey(light),
              auth: guardian,
              environment: 'Demo',
              appearance: appearance,
            ),
          );
          await wait(find.byTooltip('التنبيهات'));
          await t.tap(find.byTooltip('التنبيهات'));
          await wait(find.byType(NotificationInbox));
          final tile = find.byKey(ValueKey('notice-$id'));
          await reach(tile);
          expect(
            find.descendant(
              of: tile,
              matching: find.text(textOf(notice['message'])),
            ),
            findsOneWidget,
          );
          final action = find.descendant(
            of: tile,
            matching: find.text('عرض طلب الأسرة'),
          );
          await t.ensureVisible(action);
          await t.tap(action);
          await wait(find.byType(NativeFamilyRenewalRequest));
          final page = t.widget<NativeFamilyRenewalRequest>(
            find.byType(NativeFamilyRenewalRequest),
          );
          expect(page.id, requestId);
          expect(page.staff, false);
          await wait(find.text('تم تجديد الأسرة'));
          expect(transport.replies[detailPath], before);
          expect(find.text('إلغاء طلب التجديد'), findsNothing);
          expect(find.text('متابعة الدفع للأسرة'), findsNothing);
          await binding.takeScreenshot(
            'phase-approved-family-notice-source-w$width-${light ? 'light' : 'dark'}',
          );
          for (var index = 0; index < lines.length; index++) {
            final line = lines[index];
            final player = find.text(textOf(line['player']));
            await reach(player);
            final card = find
                .ancestor(of: player, matching: find.byType(GCard))
                .first;
            final receiptAction = find.descendant(
              of: card,
              matching: find.text('عرض الإيصال'),
            );
            await t.ensureVisible(receiptAction);
            await t.tap(receiptAction);
            await wait(find.byType(ReceiptPage));
            final receiptId = textOf(line['receiptId']);
            expect(
              t.widget<ReceiptPage>(find.byType(ReceiptPage)).id,
              receiptId,
            );
            expect(
              t.widget<ReceiptPage>(find.byType(ReceiptPage)).staff,
              false,
            );
            await wait(
              find.text(textOf(receiptSources[receiptId]!['receiptNumber'])),
            );
            expect(
              transport
                  .replies['/api/v1/guardian/subscriptions/receipts/$receiptId'],
              receiptSources[receiptId],
            );
            await binding.takeScreenshot(
              'phase-approved-family-notice-receipt-${index + 1}-w$width-${light ? 'light' : 'dark'}',
            );
            await t.tap(find.byType(BackButton).first);
            await wait(find.byType(NativeFamilyRenewalRequest));
          }
          await t.tap(find.byType(BackButton).first);
          await wait(find.byType(NotificationInbox));
          await reach(tile);
          final marker = find.descendant(
            of: tile,
            matching: find.text(
              notice['isRead'] == true ? 'تحديد كغير مقروء' : 'تمت القراءة',
            ),
          );
          await t.ensureVisible(marker);
          await t.tap(marker);
          await wait(
            find.descendant(
              of: tile,
              matching: find.text(
                notice['isRead'] == true ? 'تمت القراءة' : 'تحديد كغير مقروء',
              ),
            ),
          );
          final marked = rows(
            obj(
              await guardian.request('GET', '/api/v1/notifications'),
            )['items'],
          ).singleWhere((n) => n['id'] == id);
          expect(marked['isRead'], notice['isRead'] != true);
          await guardian.request(
            'PUT',
            '/api/v1/notifications/$id/read',
            body: {'isRead': notice['isRead'] == true},
          );
        }
        expect(obj(await guardian.request('GET', detailPath)), before);
        expect(
          obj(await guardian.request('GET', '/api/v1/notifications')),
          inboxBefore,
        );
        expect(
          obj(await other.request('GET', '/api/v1/notifications')),
          otherInbox,
        );
        expect(transport.markerWrites, 4);
        expect(otherTransport.markerWrites, 0);
        expect(t.takeException(), isNull);
        debugPrint(
          'Family notice $id -> request $requestId -> two exact receipts; no business writes.',
        );
      } finally {
        await guardian.request(
          'PUT',
          '/api/v1/notifications/$id/read',
          body: {'isRead': notice['isRead'] == true},
        );
        for (final auth in controllers) {
          auth.dispose();
        }
      }
    },
  );
}
