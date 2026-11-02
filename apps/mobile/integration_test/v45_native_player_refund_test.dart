import 'package:academy_mobile/app/app.dart';
import 'package:academy_mobile/core/api/api_client.dart';
import 'package:academy_mobile/core/auth/auth_controller.dart';
import 'package:academy_mobile/core/storage/refresh_store.dart';
import 'package:academy_mobile/core/theme/appearance.dart';
import 'package:academy_mobile/features/admin/native_player_workspace.dart';
import 'package:academy_mobile/features/admin/native_refund.dart';
import 'package:academy_mobile/features/guardian/guardian_ui.dart';
import 'package:academy_mobile/features/owner/home_destinations.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:integration_test/integration_test.dart';

class RefundAcceptanceTransport extends HttpApiTransport {
  RefundAcceptanceTransport(super.config);
  ({Json body, String key, Json result})? refund;
  int posts = 0;
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
    if (method == 'POST' && path.endsWith('/refunds')) {
      posts++;
      refund = (body: obj(body), key: idempotencyKey!, result: obj(value));
    }
    return value;
  }
}

void main() {
  final binding = IntegrationTestWidgetsFlutterBinding.ensureInitialized();
  testWidgets(
    'real owner profile refund records one synthetic unit without changing subscription or receipt',
    (t) async {
      expect(const bool.fromEnvironment('ISOLATED_DEMO'), true);
      const base = String.fromEnvironment('API_BASE_URL');
      expect(base, 'http://127.0.0.1:5278');
      expect((t.view.physicalSize / t.view.devicePixelRatio).width, 375);
      final transport = RefundAcceptanceTransport(AppConfig('Demo', base));
      final auth = AuthController(
        transport,
        SecureRefreshStore('v45.5278.os-media-entry.01099900010'),
      );
      await auth.restore();
      if (auth.session == null) {
        final c = await auth.requestOtp('01099900010');
        await auth.verify('01099900010', c, '246810');
      }
      expect(auth.session!.selected!.role, 'AcademyOwner');
      expect(
        auth.session!.selected!.academyId,
        '30000000-0000-0000-0000-000000000003',
      );
      const player = 'ce69ed0a-6849-4f07-b5bb-9e578f909848';
      const period = '554f6ce1-4741-4187-a781-6d5852fecbc7';
      const receipt = 'd13e1ec0-9b6a-47ba-9572-5e358ba43953';
      const root = '/api/v1/people/players/$player/refunds';
      const note = 'استرداد قبول اصطناعي واحد لملف994 — لا تحويل حقيقي';
      final initial = obj(await auth.request('GET', root));
      expect(
        rows(initial['refunds']).where((r) => r['reason'] == note),
        isEmpty,
        reason: 'Do not refund again; resume the saved refund record.',
      );
      final collection = rows(initial['collections']).single;
      expect(collection['available'], '900.00');
      expect(collection['paymentMethod'], 'Online');
      final beforePeriod = obj(
        await auth.request('GET', '/api/v1/subscriptions/periods/$period'),
      );
      final beforeReceipt = obj(
        await auth.request('GET', '/api/v1/subscriptions/receipts/$receipt'),
      );
      final beforeMoney = obj(
        await auth.request(
          'GET',
          '/api/v1/reports/financial?allDates=true&pageSize=1',
        ),
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
        await t.scrollUntilVisible(f, 150, scrollable: scroll, maxScrolls: 120);
        await t.ensureVisible(f.last);
        await t.pumpAndSettle();
      }

      Future<void> tap(Finder f) async {
        await reach(f);
        await t.tap(f.last);
        await t.pump(const Duration(milliseconds: 400));
      }

      Future<void> field(String label, String v) async {
        final f = find.widgetWithText(TextFormField, label);
        await reach(f);
        await t.enterText(f, v);
        await t.pumpAndSettle();
      }

      Future<void> capture(String name) async {
        await t.pumpAndSettle();
        await t.runAsync(
          () => Future<void>.delayed(const Duration(milliseconds: 500)),
        );
        await binding.takeScreenshot(name);
      }

      final appearance = Appearance();
      for (final theme in ['football', 'general']) {
        await appearance.completeRoleIntro('AcademyOwner', theme);
      }
      await t.pumpWidget(
        AcademyApp(auth: auth, environment: 'Demo', appearance: appearance),
      );
      await wait(find.byType(NavigationBar));
      await tap(find.textContaining(RegExp(r'^\d+ إجمالي$')));
      await wait(find.byType(HomeSubscriptionPlayers));
      await tap(find.widgetWithText(ListTile, 'لاعب تسجيل إداري اصطناعي994'));
      await wait(find.byType(NativePlayerWorkspace));
      await tap(find.text('الاشتراكات والماليات'));
      await tap(find.text('تسجيل استرداد'));
      await wait(find.byType(NativePlayerRefund));
      final selector = find.widgetWithText(
        DropdownButtonFormField<String>,
        'التحصيل',
      );
      await tap(selector);
      await t.pumpAndSettle();
      await t.tap(find.text('2026-10-21 · المتاح 900 ج.م').last);
      await t.pumpAndSettle();
      await field('قيمة الاسترداد', '1.00');
      await field('سبب الاسترداد', note);
      await tap(find.text('مراجعة مبلغ الاسترداد'));
      await wait(find.textContaining('المتاح 900'));
      await tap(
        find.text('أؤكد أن التحويل تم خارج النظام وأوثق رده مرة واحدة.'),
      );
      await reach(find.text('تأكيد تسجيل الاسترداد'));
      await capture('phase-approved-player-refund-reviewed-w375');
      await t.tap(find.text('تأكيد تسجيل الاسترداد'));
      await t.pump(const Duration(milliseconds: 400));
      await wait(
        find.text('سُجل الاسترداد وظهر في السجل. لا تصرف المبلغ مرة أخرى.'),
      );
      await capture('phase-approved-player-refund-saved-w375');
      expect(transport.posts, 1);
      final saved = transport.refund!;
      debugPrint(
        'Synthetic player994 refund: ${saved.result} / collection ${collection['id']}',
      );
      final history = obj(await auth.request('GET', root));
      final record = rows(history['refunds'])
          .singleWhere((r) => r['id'] == saved.result['id']);
      expect(record['amount'], 1);
      expect(record['currency'], 'EGP');
      expect(record['collectionId'], collection['id']);
      expect(record['reason'], note);
      expect(record['payoutMethod'], 'Online');
      expect(record['cashSessionId'], null);
      expect(rows(history['collections']).single['available'], '899.00');
      expect(
        await auth.request('GET', '/api/v1/subscriptions/periods/$period'),
        beforePeriod,
      );
      expect(
        await auth.request('GET', '/api/v1/subscriptions/receipts/$receipt'),
        beforeReceipt,
      );
      final replay = obj(
        await auth.request(
          'POST',
          root,
          body: saved.body,
          idempotencyKey: saved.key,
        ),
      );
      expect(replay['id'], saved.result['id']);
      expect(
        rows(obj(await auth.request('GET', root))['refunds']).length,
        rows(initial['refunds']).length + 1,
      );
      final afterMoney = obj(
        await auth.request(
          'GET',
          '/api/v1/reports/financial?allDates=true&pageSize=1',
        ),
      );
      expect(afterMoney['totalCount'], (beforeMoney['totalCount'] as num) + 1);
      expect(afterMoney['totalAmount'], beforeMoney['totalAmount']);
      expect(
        afterMoney['totalRefunds'],
        (beforeMoney['totalRefunds'] as num) + 1,
      );
      expect(afterMoney['netAmount'], (beforeMoney['netAmount'] as num) - 1);
      expect(t.takeException(), isNull);
      await t.pumpWidget(const SizedBox.shrink());
      auth.dispose();
    },
  );
}
