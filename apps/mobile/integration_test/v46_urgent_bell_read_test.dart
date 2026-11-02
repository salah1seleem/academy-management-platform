import '../test/introduction_test_helpers.dart';

import 'package:academy_mobile/app/app.dart';
import 'package:academy_mobile/core/api/api_client.dart';
import 'package:academy_mobile/core/auth/auth_controller.dart';
import 'package:academy_mobile/core/storage/refresh_store.dart';
import 'package:academy_mobile/core/theme/appearance.dart';
import 'package:academy_mobile/features/guardian/guardian_home.dart';
import 'package:academy_mobile/features/guardian/guardian_ui.dart';
import 'package:academy_mobile/features/shared/notification_inbox.dart';
import 'package:flutter/material.dart';
import 'package:flutter_secure_storage/flutter_secure_storage.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:integration_test/integration_test.dart';

class IsolatedNoticeReadTransport extends HttpApiTransport {
  IsolatedNoticeReadTransport(super.config);
  final permittedReads = <String>{};
  @override
  Future<Object?> send(
    String method,
    String path, {
    Object? body,
    String? token,
    String? idempotencyKey,
  }) {
    if (method != 'GET' &&
        !path.startsWith('/api/v1/mobile/auth/') &&
        !(method == 'PUT' && permittedReads.contains(path))) {
      throw StateError(
        'This isolated acceptance permits only selected notice read flags.',
      );
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
  testWidgets(
    'Shared header bell persists read flags and refreshes actual family home while preserving selected child; original flags restored',
    (t) async {
      expect(const bool.fromEnvironment('ISOLATED_DEMO'), true);
      const base = String.fromEnvironment('API_BASE_URL');
      expect(base, 'http://127.0.0.1:5278');
      final transport = IsolatedNoticeReadTransport(AppConfig('Demo', base));
      final auth = AuthController(
        transport,
        SecureRefreshStore('v46.urgent-summary.guardian1'),
      );
      await auth.restore();
      if (auth.session == null) {
        final challenge = await auth.requestOtp('01099900101');
        await auth.verify('01099900101', challenge, '246810');
      }
      expect(auth.session!.selected!.role, 'Guardian');
      expect(
        auth.session!.selected!.academyId,
        '30000000-0000-0000-0000-000000000003',
      );
      final home = obj(await auth.request('GET', '/api/v1/guardian/home'));
      final children = rows(home['children']);
      expect(children.length, greaterThan(1));
      final before = obj(
        await auth.request(
          'GET',
          '/api/v1/notifications?urgentMatchesOnly=true&page=1',
        ),
      );
      final notices = rows(before['items']);
      expect(notices, isNotEmpty);
      expect(before['total'], notices.length);
      for (final n in notices) {
        transport.permittedReads.add('/api/v1/notifications/${n['id']}/read');
        expect(n['isRead'], false);
      }
      Future<void> wait(Finder f) async {
        for (var i = 0; i < 200 && f.evaluate().isEmpty; i++) {
          if (find.text('تخطي').evaluate().isNotEmpty) {
            await finishRoleIntroduction(t);
            await t.pumpAndSettle();
          }
          await t.pump(const Duration(milliseconds: 150));
        }
        expect(f, findsWidgets);
        await t.pumpAndSettle();
      }

      try {
        final appearance = Appearance(storage: const FlutterSecureStorage());
        await appearance.load();
        await t.pumpWidget(
          AcademyApp(auth: auth, environment: 'Demo', appearance: appearance),
        );
        await wait(find.byType(GuardianChildCarousel));
        await t.drag(find.byType(GuardianChildCarousel), const Offset(320, 0));
        await t.pumpAndSettle();
        final selected = t
            .widget<GuardianChildCarousel>(find.byType(GuardianChildCarousel))
            .selectedPlayerId;
        expect(selected, children[1]['id']);
        await t.tap(find.byTooltip('التنبيهات'));
        await wait(find.byType(NotificationInbox));
        for (final notice in notices) {
          final message = find.text(textOf(notice['message']));
          await t.scrollUntilVisible(
            message,
            200,
            scrollable: find.byType(Scrollable).last,
          );
          await t.pumpAndSettle();
          final card = find.ancestor(of: message, matching: find.byType(Card));
          final action = find.descendant(
            of: card,
            matching: find.text('تمت القراءة'),
          );
          await t.ensureVisible(action);
          await t.tap(action);
          await t.pumpAndSettle();
          for (
            var i = 0;
            i < 100 &&
                find
                    .descendant(
                      of: card,
                      matching: find.text('تحديد كغير مقروء'),
                    )
                    .evaluate()
                    .isEmpty;
            i++
          ) {
            await t.pump(const Duration(milliseconds: 150));
          }
        }
        final read = obj(
          await auth.request(
            'GET',
            '/api/v1/notifications?urgentMatchesOnly=true&page=1',
          ),
        );
        expect(read['total'], 0);
        await binding.takeScreenshot('phase-approved-guardian-bell-read-saved');
        Navigator.of(t.element(find.byType(NotificationInbox))).pop();
        await wait(find.byType(GuardianChildCarousel));
        expect(find.byKey(const Key('guardian-urgent-summary')), findsNothing);
        expect(
          t
              .widget<GuardianChildCarousel>(find.byType(GuardianChildCarousel))
              .selectedPlayerId,
          selected,
        );
        await binding.takeScreenshot(
          'phase-approved-guardian-bell-home-refreshed',
        );
        expect(t.takeException(), isNull);
      } finally {
        for (final n in notices) {
          await auth.request(
            'PUT',
            '/api/v1/notifications/${n['id']}/read',
            body: {'isRead': n['isRead']},
          );
        }
        final restored = obj(
          await auth.request(
            'GET',
            '/api/v1/notifications?urgentMatchesOnly=true&page=1',
          ),
        );
        expect(restored['total'], before['total']);
        expect(restored['unread'], before['unread']);
        await t.pumpWidget(const SizedBox());
        auth.dispose();
      }
    },
  );
}
