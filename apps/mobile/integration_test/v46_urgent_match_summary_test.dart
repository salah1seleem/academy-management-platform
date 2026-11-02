import 'package:academy_mobile/core/api/api_client.dart';
import 'package:academy_mobile/core/auth/auth_controller.dart';
import 'package:academy_mobile/core/storage/refresh_store.dart';
import 'package:academy_mobile/core/theme/app_theme.dart';
import 'package:academy_mobile/features/guardian/guardian_home.dart';
import 'package:academy_mobile/features/guardian/guardian_ui.dart';
import 'package:academy_mobile/features/shared/community_ads.dart';
import 'package:academy_mobile/features/shared/notification_inbox.dart';
import 'package:academy_mobile/features/shared/matches.dart';
import 'package:flutter/material.dart';
import 'package:flutter_localizations/flutter_localizations.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:integration_test/integration_test.dart';

import 'v45_single_renewal_saved_receipt_test.dart'
    show SavedRenewalReadTransport;

void main() {
  final binding = IntegrationTestWidgetsFlutterBinding.ensureInitialized();
  testWidgets(
    'Actual family home shows one urgent preview, opens scoped paged inbox and exact saved match without marking read',
    (t) async {
      expect(const bool.fromEnvironment('ISOLATED_DEMO'), true);
      const base = String.fromEnvironment('API_BASE_URL');
      expect(base, 'http://127.0.0.1:5278');
      final auth = AuthController(
        SavedRenewalReadTransport(AppConfig('Demo', base)),
        SecureRefreshStore('v46.urgent-summary.guardian0'),
      );
      await auth.restore();
      if (auth.session == null) {
        final challenge = await auth.requestOtp('01099900100');
        await auth.verify('01099900100', challenge, '246810');
      }
      expect(auth.session!.selected!.role, 'Guardian');
      expect(
        auth.session!.selected!.academyId,
        '30000000-0000-0000-0000-000000000003',
      );
      final home = obj(await auth.request('GET', '/api/v1/guardian/home'));
      final alerts = rows(home['urgentAlerts']);
      expect(alerts, isNotEmpty);
      final inbox = obj(
        await auth.request(
          'GET',
          '/api/v1/notifications?urgentMatchesOnly=true&page=1',
        ),
      );
      expect(inbox['total'], greaterThanOrEqualTo(alerts.length));
      expect(
        rows(inbox['items'])
            .every((n) => n['matchId'] != null && n['isRead'] == false),
        true,
      );
      final alert = alerts.first;
      final expectedMatch = textOf(alert['matchId']);
      final matches = rows(await auth.request('GET', '/api/v1/my-matches'));
      final match = matches.singleWhere((m) => m['id'] == expectedMatch);
      Future<void> wait(Finder f) async {
        for (var i = 0; i < 200 && f.evaluate().isEmpty; i++) {
          await t.pump(const Duration(milliseconds: 150));
        }
        expect(f, findsWidgets);
        await t.pumpAndSettle();
      }

      await t.pumpWidget(
        MaterialApp(
          locale: const Locale('ar'),
          supportedLocales: const [Locale('ar')],
          localizationsDelegates: GlobalMaterialLocalizations.delegates,
          theme: const bool.fromEnvironment('LIGHT_THEME')
              ? AcademyTheme.light()
              : AcademyTheme.dark(),
          home: GuardianHome(
            auth: auth,
            environment: 'Demo',
            accountPage: const SizedBox(),
          ),
        ),
      );
      await wait(find.byKey(const Key('guardian-urgent-summary')));
      expect(find.byType(GuardianUrgentMatchAlerts), findsOneWidget);
      expect(
        t.getSize(find.byKey(const Key('guardian-urgent-summary'))).height,
        lessThan(180),
      );
      expect(t.getTopLeft(find.text('أبنائي')).dy, lessThan(330));
      await binding.takeScreenshot('phase-approved-guardian-urgent-summary');
      await t.tap(find.text('عرض التحديثات والمواعيد'));
      await wait(find.byType(NotificationInbox));
      await wait(find.text(textOf(alert['message'])));
      expect(
        t
            .widget<NotificationInbox>(find.byType(NotificationInbox))
            .urgentMatchesOnly,
        true,
      );
      await binding.takeScreenshot('phase-approved-guardian-urgent-inbox');
      final card = find
          .ancestor(
            of: find.text(textOf(alert['message'])),
            matching: find.byType(Card),
          )
          .first;
      final open = find.descendant(
        of: card,
        matching: find.text('عرض المباراة'),
      );
      await t.ensureVisible(open);
      await t.tap(open);
      await wait(find.byType(MatchSchedule));
      expect(
        t.widget<MatchSchedule>(find.byType(MatchSchedule)).matchId,
        expectedMatch,
      );
      await wait(find.textContaining(textOf(match['opponent'])));
      await binding.takeScreenshot(
        'phase-approved-guardian-urgent-exact-match',
      );
      Navigator.of(t.element(find.byType(MatchSchedule))).pop();
      await t.pumpAndSettle();
      Navigator.of(t.element(find.byType(NotificationInbox))).pop();
      await wait(find.byKey(const Key('guardian-urgent-summary')));
      final after = obj(
        await auth.request(
          'GET',
          '/api/v1/notifications?urgentMatchesOnly=true&page=1',
        ),
      );
      expect(after['total'], inbox['total']);
      expect(after['unread'], inbox['unread']);
      expect(
        rows(after['items'])
            .singleWhere((n) => n['id'] == alert['id'])['isRead'],
        false,
      );
      expect(t.takeException(), isNull);
      await t.pumpWidget(const SizedBox());
      auth.dispose();
    },
  );
}
