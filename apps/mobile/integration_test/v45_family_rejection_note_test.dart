import 'package:academy_mobile/app/app.dart';
import 'package:academy_mobile/core/api/api_client.dart';
import 'package:academy_mobile/core/auth/auth_controller.dart';
import 'package:academy_mobile/core/storage/refresh_store.dart';
import 'package:academy_mobile/core/theme/appearance.dart';
import 'package:academy_mobile/features/guardian/family_renewal_request.dart';
import 'package:academy_mobile/features/guardian/guardian_ui.dart';
import 'package:academy_mobile/features/shared/role_navigation.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:integration_test/integration_test.dart';

void main() {
  final binding = IntegrationTestWidgetsFlutterBinding.ensureInitialized();
  testWidgets(
    'native owner preserves family review note across scrolling and rejects an unpaid synthetic request without money or periods',
    (t) async {
      expect(const bool.fromEnvironment('ISOLATED_DEMO'), isTrue);
      const base = String.fromEnvironment('API_BASE_URL');
      expect(base, 'http://127.0.0.1:5278');
      expect((t.view.physicalSize / t.view.devicePixelRatio).width, 375);
      Future<AuthController> login(String phone, String key) async {
        final a = AuthController(
          HttpApiTransport(AppConfig('Demo', base)),
          SecureRefreshStore(key),
        );
        await a.restore();
        if (a.session == null) {
          final c = await a.requestOtp(phone);
          await a.verify(phone, c, '246810');
        }
        expect(
          a.session!.selected!.academyId,
          '30000000-0000-0000-0000-000000000003',
        );
        return a;
      }

      final owner = await login(
            '01099900010',
            'v45.5278.os-media-entry.01099900010',
          ),
          family = await login(
            '01099900101',
            'v45.5278.rejection-note.guardian101',
          );
      expect(owner.session!.selected!.role, 'AcademyOwner');
      expect(family.session!.selected!.role, 'Guardian');
      final settings = obj(
        await owner.request('GET', '/api/v1/payment-settings'),
      );
      if (settings['configured'] != true) {
        await owner.request(
          'PUT',
          '/api/v1/payment-settings',
          body: {
            'instaPayAddress': 'synthetic-family-v45@example.test',
            'walletNumber': null,
            'recipientName': 'إعداد اختبار اصطناعي — لا تحول أموالًا',
            'isActive': true,
          },
        );
        addTearDown(() async {
          await owner.request(
            'PUT',
            '/api/v1/payment-settings',
            body: {
              'instaPayAddress': settings['instaPayAddress'],
              'walletNumber': settings['walletNumber'],
              'recipientName': settings['recipientName'],
              'isActive': false,
            },
          );
          expect(
            obj(
              await owner.request('GET', '/api/v1/payment-settings'),
            )['configured'],
            isFalse,
          );
          owner.dispose();
          family.dispose();
        });
      } else {
        addTearDown(() {
          owner.dispose();
          family.dispose();
        });
      }
      final before = obj(
        await owner.request(
          'GET',
          '/api/v1/reports/financial?allDates=true&pageSize=1',
        ),
      );
      final periodIds = rows(
        await owner.request('GET', '/api/v1/subscriptions/periods'),
      ).map((p) => p['id']).toSet();
      final options = rows(
        obj(
          await family.request(
            'GET',
            '/api/v1/guardian/subscriptions/family/options',
          ),
        )['items'],
      );
      final sport = options.firstWhere(
        (e) =>
            options
                .where((other) => other['sportId'] == e['sportId'])
                .map((other) => other['playerId'] ?? other['player'])
                .toSet()
                .length >=
            2,
      )['sportId'];
      final chosen = options
          .where((e) => e['sportId'] == sport)
          .take(2)
          .toList();
      final plan = rows(chosen.first['plans']).firstWhere(
        (p) => chosen.every(
          (e) => rows(e['plans']).any(
            (other) =>
                other['id'] == p['id'] && other['currency'] == p['currency'],
          ),
        ),
      );
      final lines = [
        for (final e in chosen)
          {'sportEnrollmentId': e['id'], 'subscriptionPlanId': plan['id']},
      ];
      final quote = obj(
        await family.request(
          'POST',
          '/api/v1/guardian/subscriptions/family/quote',
          body: {'lines': lines, 'sharedPlanOnly': true},
        ),
      );
      final created = obj(
        await family.request(
          'POST',
          '/api/v1/guardian/subscriptions/family/requests',
          body: {
            'lines': lines,
            'sharedPlanOnly': true,
            'quoteHash': quote['quoteHash'],
            'idempotencyKey': 'v45-native-rejection-note-20261021',
          },
        ),
      );
      final id = textOf(created['id']);
      debugPrint('PERSISTED_FAMILY_REJECTION: $id');
      expect(['Pending', 'Rejected'], contains(created['status']));
      Future<void> waitFor(Finder f) async {
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
        await t.scrollUntilVisible(f, 180, scrollable: scroll, maxScrolls: 120);
        await t.ensureVisible(f.last);
        await t.pumpAndSettle();
      }

      final appearance = Appearance();
      for (final theme in ['football', 'general']) {
        await appearance.completeRoleIntro('AcademyOwner', theme);
      }
      await t.pumpWidget(
        AcademyApp(auth: owner, environment: 'Demo', appearance: appearance),
      );
      await waitFor(find.byType(NativeRoleTabs));
      Navigator.of(t.element(find.byType(NativeRoleTabs))).push(
        MaterialPageRoute<void>(
          builder: (_) =>
              NativeFamilyRenewalRequest(auth: owner, id: id, staff: true),
        ),
      );
      await t.pumpAndSettle();
      const reason = 'سبب رفض اصطناعي محفوظ بعد مراجعة تفاصيل الأسرة';
      if (created['status'] == 'Pending') {
        await waitFor(find.text('بانتظار المراجعة'));
        final note = find.widgetWithText(
          TextField,
          'ملاحظة أو سبب الرفض (اختياري)',
        );
        await reach(note);
        await t.enterText(note, reason);
        FocusManager.instance.primaryFocus?.unfocus();
        await t.pumpAndSettle();
        t
            .state<ScrollableState>(find.byType(Scrollable).first)
            .position
            .jumpTo(0);
        await t.pumpAndSettle();
        await reach(note);
        expect(t.widget<TextField>(note).controller!.text, reason);
        await binding.takeScreenshot(
          'phase-approved-family-note-preserved-w375',
        );
        final reject = find.text('رفض الطلب كاملًا');
        await reach(reject);
        await t.tap(reject);
        await t.pumpAndSettle();
        await t.tap(find.text('تأكيد رفض الأسرة'));
        await t.pumpAndSettle();
      }
      await waitFor(find.text('لم يُقبل الطلب'));
      await reach(find.text(reason));
      await binding.takeScreenshot(
        'phase-approved-family-native-rejected-reason-w375',
      );
      final read = obj(
        await family.request(
          'GET',
          '/api/v1/guardian/subscriptions/family/requests/$id',
        ),
      );
      expect(read['status'], 'Rejected');
      expect(read['decisionReason'], reason);
      expect(rows(read['items']).every((l) => l['receiptId'] == null), isTrue);
      final after = obj(
        await owner.request(
          'GET',
          '/api/v1/reports/financial?allDates=true&pageSize=1',
        ),
      );
      expect(after['totalCount'], before['totalCount']);
      expect(after['netAmount'], before['netAmount']);
      expect(
        rows(await owner.request('GET', '/api/v1/subscriptions/periods'))
            .map((p) => p['id'])
            .toSet(),
        periodIds,
      );
      debugPrint(
        'Family rejected with persisted reason; finance unchanged: ${after['totalCount']} / ${after['netAmount']}.',
      );
      expect(t.takeException(), isNull);
      await t.pumpWidget(const SizedBox());
    },
  );
}
