import 'package:academy_mobile/core/api/api_client.dart';
import 'package:academy_mobile/core/auth/auth_controller.dart';
import 'package:academy_mobile/core/storage/refresh_store.dart';
import 'package:academy_mobile/features/guardian/guardian_ui.dart';
import 'package:academy_mobile/features/shared/player_monthly.dart';
import 'package:academy_mobile/features/shared/player_goals.dart'
    show goalCommandId;
import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_localizations/flutter_localizations.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:integration_test/integration_test.dart';

void main() {
  final binding = IntegrationTestWidgetsFlutterBinding.ensureInitialized();
  testWidgets(
    'Authorized family reads real monthly evidence and copies a complete export; withdrawal refreshes the same source without finance changes',
    (t) async {
      expect(const bool.fromEnvironment('ISOLATED_DEMO'), true);
      expect(
        const String.fromEnvironment('API_BASE_URL'),
        'http://127.0.0.1:5259',
      );
      Future<AuthController> login(String phone, String role) async {
        final a = AuthController(
          HttpApiTransport(
            AppConfig('Demo', const String.fromEnvironment('API_BASE_URL')),
          ),
          SecureRefreshStore('academy.v3.monthly.$role'),
        );
        await a.restore();
        if (a.session != null) {
          await a.logout();
        }
        final c = await a.requestOtp(phone);
        await a.verify(phone, c, '246810');
        expect(
          a.session!.selected!.academyId,
          '30000000-0000-0000-0000-000000000003',
        );
        return a;
      }

      Future<void> wait(Finder f) async {
        for (var n = 0; n < 180 && f.evaluate().isEmpty; n++) {
          await t.pump(const Duration(milliseconds: 100));
        }
        expect(f, findsWidgets);
        await t.pumpAndSettle();
      }

      Widget shell(Widget child) => MaterialApp(
        key: UniqueKey(),
        locale: const Locale('ar'),
        supportedLocales: const [Locale('ar')],
        localizationsDelegates: GlobalMaterialLocalizations.delegates,
        theme: guardianTheme(),
        home: child,
      );
      final owner = await login('01099900010', 'owner'),
          family = await login('01099900110', 'family');
      try {
        final child = rows(
          obj(await family.request('GET', '/api/v1/guardian/home'))['children'],
        ).first;
        final player = textOf(child['id']);
        final e = rows(
          obj(
            await owner.request('GET', '/api/v1/evaluations/options'),
          )['enrollments'],
        ).firstWhere((e) => e['playerId'] == player);
        final enrollment = textOf(e['id']);
        final money = await owner.request(
          'GET',
          '/api/v1/reports/financial?from=2026-09-01&to=2026-09-28',
        );
        final title =
            'تعاون شهري iOS اصطناعي ${goalCommandId().substring(0, 5)}';
        final rule = obj(
          await owner.request(
            'POST',
            '/api/v1/evaluations/badges/rules',
            body: {
              'sportId': e['sportId'],
              'title': title,
              'criterion': 'مجهود بشري ملاحظ دون ترتيب أو مال',
              'points': 10,
              'confirmPublication': true,
              'requestId': goalCommandId(),
            },
          ),
        );
        final badges = '/api/v1/evaluations/badges/enrollments/$enrollment';
        final badge = obj(
          rows(obj(await owner.request('GET', badges))['enrollments']).first,
        );
        final grant = obj(
          await owner.request(
            'POST',
            '$badges/grant',
            body: {
              'ruleId': rule['id'],
              'enrollmentVersion': badge['enrollmentVersion'],
              'observedOn': badge['asOfDate'],
              'familyNote': 'نسخة شهرية اصطناعية خاصة بالأسرة',
              'requestId': goalCommandId(),
            },
          ),
        );
        final reportPath =
            '/api/v1/guardian/children/$player/monthly?enrollmentId=$enrollment';
        final report = obj(await family.request('GET', reportPath));
        expect(rows(report['items']).any((r) => r['id'] == grant['id']), true);
        await t.pumpWidget(
          shell(
            PlayerMonthlyPage(
              auth: family,
              playerId: player,
              enrollmentId: enrollment,
            ),
          ),
        );
        await wait(find.text(title));
        final copy = find.widgetWithText(
          OutlinedButton,
          'نسخ تقرير الشهر كاملًا',
        );
        await t.ensureVisible(copy);
        await t.pumpAndSettle();
        await t.tap(copy);
        await wait(find.textContaining('تم نسخ تقرير الشهر.'));
        final clipboard = await Clipboard.getData(Clipboard.kTextPlain);
        expect(clipboard?.text, contains(title));
        expect(
          clipboard?.text,
          (obj(
            await family.request(
              'GET',
              '$reportPath&month=${report['month']}&format=text',
            ),
          ))['text'],
        );
        final saved = rows(
          obj(
            rows(obj(await owner.request('GET', badges))['enrollments']).first,
          )['items'],
        ).singleWhere((r) => r['id'] == grant['id']);
        await owner.request(
          'POST',
          '/api/v1/evaluations/badges/grants/${grant['id']}/revoke',
          body: {
            'version': saved['version'],
            'note': null,
            'requestId': goalCommandId(),
          },
        );
        await t.pumpWidget(
          shell(
            PlayerMonthlyPage(
              auth: family,
              playerId: player,
              enrollmentId: enrollment,
            ),
          ),
        );
        await wait(find.text(title));
        final card = find
            .ancestor(of: find.text(title), matching: find.byType(GCard))
            .first;
        await t.ensureVisible(card);
        await t.pumpAndSettle();
        expect(
          find.descendant(
            of: card,
            matching: find.textContaining('شارة مسحوبة'),
          ),
          findsOneWidget,
        );
        expect(t.takeException(), isNull);
        if (const bool.fromEnvironment('CAPTURE_VISUALS')) {
          await binding.takeScreenshot('phase-family-monthly-ios');
        }
        expect(
          await owner.request(
            'GET',
            '/api/v1/reports/financial?from=2026-09-01&to=2026-09-28',
          ),
          money,
        );
        await t.pumpWidget(const SizedBox.shrink());
      } finally {
        await owner.logout();
        await family.logout();
        owner.dispose();
        family.dispose();
      }
    },
  );
}
