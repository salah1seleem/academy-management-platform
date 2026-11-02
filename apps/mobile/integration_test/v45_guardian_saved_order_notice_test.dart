import 'package:academy_mobile/app/app.dart';
import 'package:academy_mobile/core/api/api_client.dart';
import 'package:academy_mobile/core/auth/auth_controller.dart';
import 'package:academy_mobile/core/storage/refresh_store.dart';
import 'package:academy_mobile/core/theme/appearance.dart';
import 'package:academy_mobile/features/guardian/guardian_home.dart';
import 'package:academy_mobile/features/guardian/guardian_profile.dart';
import 'package:academy_mobile/features/guardian/guardian_ui.dart';
import 'package:academy_mobile/features/guardian/sport_store.dart';
import 'package:academy_mobile/features/shared/notification_inbox.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:integration_test/integration_test.dart';

class SavedGuardianTransport extends HttpApiTransport {
  SavedGuardianTransport(super.config);
  final writes = <String>[];
  @override
  Future<Object?> send(
    String method,
    String path, {
    Object? body,
    String? token,
    String? idempotencyKey,
  }) async {
    if (method != 'GET' && !path.contains('/auth/')) {
      writes.add('$method $path');
    }
    return super.send(
      method,
      path,
      body: body,
      token: token,
      idempotencyKey: idempotencyKey,
    );
  }
}

void main() {
  final binding = IntegrationTestWidgetsFlutterBinding.ensureInitialized();
  for (final scenario in ['order', 'notice']) {
    testWidgets(
      'guardian resumes saved $scenario through the actual app without new commands',
      (t) async {
        expect(const bool.fromEnvironment('ISOLATED_DEMO'), true);
        const base = String.fromEnvironment('API_BASE_URL');
        expect(base, 'http://127.0.0.1:5278');
        expect((t.view.physicalSize / t.view.devicePixelRatio).width, 375);
        final transport = SavedGuardianTransport(AppConfig('Demo', base));
        final auth = AuthController(
          transport,
          SecureRefreshStore(
            scenario == 'order'
                ? 'v45.5278.guardian-workspace.01099900100'
                : 'v44.5278.notice.period.guardian102',
          ),
        );
        await auth.restore();
        if (auth.session == null) {
          final phone = scenario == 'order' ? '01099900100' : '01099900102';
          final challenge = await auth.requestOtp(phone);
          await auth.verify(phone, challenge, '246810');
        }
        expect(
          auth.session,
          isNotNull,
          reason: 'Restore the existing synthetic session; do not issue another purchase or day adjustment.',
        );
        expect(auth.session!.selected!.role, 'Guardian');
        expect(
          auth.session!.selected!.academyId,
          '30000000-0000-0000-0000-000000000003',
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
          await t.scrollUntilVisible(
            f,
            150,
            scrollable: scroll,
            maxScrolls: 120,
          );
          await t.ensureVisible(f.first);
          await t.pumpAndSettle();
        }

        Future<void> tap(Finder f) async {
          await reach(f);
          await t.tap(f.first);
          await t.pump(const Duration(milliseconds: 400));
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
          await appearance.completeRoleIntro('Guardian', theme);
        }
        Json? order, notice, period;
        var page = 1;
        if (scenario == 'order') {
          final orders = rows(
            await auth.request('GET', '/api/v1/guardian/product-sales'),
          );
          order = orders.singleWhere(
            (o) =>
                o['productName'] == 'زي تدريب مرقم تجريبي' &&
                o['sizeSnapshot'] == 'M' &&
                o['shirtNumberSnapshot'] == 7 &&
                o['status'] == 'Pending',
          );
          expect(order['quantity'], 1);
        } else {
          for (; page <= 20 && notice == null; page++) {
            final inbox = obj(
              await auth.request('GET', '/api/v1/notifications?page=$page'),
            );
            for (final n in rows(
              inbox['items'],
            ).where((n) => n['targetKind'] == 'SubscriptionPeriod')) {
              final p = obj(
                await auth.request(
                  'GET',
                  '/api/v1/guardian/subscriptions/periods/${n['targetId']}/adjustments',
                ),
              );
              if (rows(p['adjustments']).any((a) => a['type'] == 'DaysAdded')) {
                notice = n;
                period = p;
                break;
              }
            }
            if (notice != null) break;
            if (rows(inbox['items']).isEmpty) break;
          }
          expect(
            notice,
            isNotNull,
            reason: 'Use the saved day event, never manufacture another.',
          );
        }
        await t.pumpWidget(
          AcademyApp(auth: auth, environment: 'Demo', appearance: appearance),
        );
        await wait(find.byType(GuardianHome));
        await wait(find.text('أبنائي'));
        if (scenario == 'order') {
          await tap(find.text('المتجر'));
          await wait(find.byType(CatalogSections));
          await tap(find.text('طلباتي من المنتجات الرياضية'));
          await wait(find.byType(GuardianProductOrders));
          final orderName = find.text('زي تدريب مرقم تجريبي');
          final card = find.ancestor(
            of: orderName,
            matching: find.byType(GCard),
          );
          await reach(card);
          expect(
            find.descendant(
              of: card,
              matching: find.textContaining('M · رقم 7'),
            ),
            findsOneWidget,
          );
          await capture(
            'phase-approved-guardian-saved-product-order-list-w375',
          );
          await t.tap(
            find.descendant(of: card, matching: find.text('عرض الطلب والدفع')),
          );
          await t.pump(const Duration(milliseconds: 400));
          await wait(find.byType(GuardianProductOrder));
          expect(
            t
                .widget<GuardianProductOrder>(find.byType(GuardianProductOrder))
                .id,
            order!['id'],
          );
          await wait(find.text('رقم القميص'));
          expect(find.text('M'), findsOneWidget);
          expect(find.text('7'), findsOneWidget);
          await capture(
            'phase-approved-guardian-saved-product-order-detail-w375',
          );
          expect(
            obj(
              await auth.request(
                'GET',
                '/api/v1/guardian/product-sales/${order['id']}',
              ),
            )['status'],
            'Pending',
          );
        } else {
          await t.tap(find.byTooltip('التنبيهات'));
          await t.pump(const Duration(milliseconds: 400));
          await wait(find.byType(NotificationInbox));
          for (var current = 1; current < page; current++) {
            await tap(find.text('التالي'));
            await wait(find.text('السابق'));
          }
          final message = find.text(textOf(notice!['message']));
          final card = find.ancestor(of: message, matching: find.byType(GCard));
          await reach(card);
          await capture('phase-approved-guardian-saved-days-notice-inbox-w375');
          await t.tap(
            find
                .descendant(
                  of: card,
                  matching: find.text('عرض الاشتراك وتعديلاته'),
                )
                .first,
          );
          await t.pump(const Duration(milliseconds: 400));
          await wait(find.byType(PeriodPage));
          expect(
            t.widget<PeriodPage>(find.byType(PeriodPage)).id,
            notice['targetId'],
          );
          await wait(find.text('سجل التعديلات'));
          await reach(find.textContaining('إضافة أيام'));
          expect(find.textContaining('DaysAdded'), findsNothing);
          await capture(
            'phase-approved-guardian-saved-days-arabic-history-w375',
          );
          expect(
            obj(
              await auth.request(
                'GET',
                '/api/v1/guardian/subscriptions/periods/${notice['targetId']}/adjustments',
              ),
            ),
            period,
          );
          final inbox = obj(
            await auth.request('GET', '/api/v1/notifications?page=$page'),
          );
          expect(
            rows(inbox['items'])
                .singleWhere((n) => n['id'] == notice!['id'])['isRead'],
            notice['isRead'],
          );
        }
        expect(transport.writes, isEmpty);
        expect(t.takeException(), isNull);
        await t.pumpWidget(const SizedBox.shrink());
        auth.dispose();
      },
    );
  }
}
