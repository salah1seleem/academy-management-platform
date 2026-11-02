import 'dart:convert';

import 'package:academy_mobile/core/api/api_client.dart';
import 'package:academy_mobile/core/auth/auth_controller.dart';
import 'package:academy_mobile/core/storage/refresh_store.dart';
import 'package:academy_mobile/features/guardian/guardian_content.dart';
import 'package:academy_mobile/features/guardian/guardian_ui.dart';
import 'package:academy_mobile/features/shared/player_goals.dart'
    show goalCommandId;
import 'package:flutter/material.dart';
import 'package:flutter_localizations/flutter_localizations.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:integration_test/integration_test.dart';

void main() {
  final binding = IntegrationTestWidgetsFlutterBinding.ensureInitialized();
  testWidgets(
    'Family opts in, actual API publishes a reviewed synthetic file, family reads and withdraws; bytes and gallery disappear with no finance changes',
    (t) async {
      expect(const bool.fromEnvironment('ISOLATED_DEMO'), true);
      expect(
        const String.fromEnvironment('API_BASE_URL'),
        'http://127.0.0.1:5257',
      );
      Future<AuthController> login(String phone, String role) async {
        final a = AuthController(
          HttpApiTransport(
            AppConfig('Demo', const String.fromEnvironment('API_BASE_URL')),
          ),
          SecureRefreshStore('academy.v3.media.$role'),
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
        for (var i = 0; i < 180 && f.evaluate().isEmpty; i++) {
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
      Future<void> choose(String title) async {
        final button = find.widgetWithText(FilledButton, title);
        await wait(button);
        await t.ensureVisible(button);
        await t.pumpAndSettle();
        await t.tap(button);
        await t.pumpAndSettle();
        await t.tap(find.text('تأكيد اختياري'));
        await t.pumpAndSettle();
      }

      final owner = await login('01099900010', 'owner'),
          family = await login('01099900112', 'guardian');
      try {
        final child = textOf(
          rows(
            obj(
              await family.request('GET', '/api/v1/guardian/home'),
            )['children'],
          ).first['id'],
        );
        final e = rows(
          obj(
            await owner.request('GET', '/api/v1/evaluations/options'),
          )['enrollments'],
        ).singleWhere((x) => x['playerId'] == child);
        final group = textOf(e['groupId']);
        final money = await owner.request(
          'GET',
          '/api/v1/reports/financial?from=2026-09-01&to=2026-09-28',
        );
        final consentPath =
            '/api/v1/guardian/children/$child/group-media-consent';
        var prior = obj(await family.request('GET', consentPath));
        if (prior['allowed'] == true) {
          await family.request(
            'POST',
            consentPath,
            body: {
              'allowed': false,
              'revision': prior['revision'],
              'requestId': goalCommandId(),
            },
          );
        }
        await t.pumpWidget(shell(GalleryPage(auth: family, playerId: child)));
        await choose('السماح بالمشاركة داخل المجموعة');
        await wait(find.textContaining('المشاركة مسموحة باختيارك'));
        final options = obj(
          await owner.request(
            'GET',
            '/api/v1/assets/media-audience?groupId=$group',
          ),
        );
        expect(
          rows(options['players'])
              .singleWhere((x) => x['id'] == child)['consented'],
          true,
        );
        final caption =
            'ملف خصوصية اصطناعي iOS ${goalCommandId().substring(0, 5)}';
        final asset = obj(
          await owner.upload(
            base64Decode(
              'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+ip1sAAAAASUVORK5CYII=',
            ),
            'image/png',
            {
              'purpose': 'Media',
              'groupId': group,
              'eventOn': textOf(options['eventOn']),
              'subjectPlayerIds': child,
              'privacyConfirmed': 'true',
              'noRecognizableChildren': 'false',
              'caption': caption,
            },
          ),
        );
        await t.pumpWidget(shell(GalleryPage(auth: family, playerId: child)));
        await wait(find.text(caption));
        expect((await family.asset(textOf(asset['url']))).isNotEmpty, true);
        final media = find.text(caption);
        await t.ensureVisible(media);
        await t.pumpAndSettle();
        expect(find.byType(Image), findsWidgets);
        if (const bool.fromEnvironment('CAPTURE_VISUALS')) {
          await binding.takeScreenshot('phase-media-family-published-ios');
        }
        await choose('سحب موافقة المشاركة');
        await wait(find.textContaining('المشاركة غير مسموحة'));
        expect(find.text(caption), findsNothing);
        try {
          await family.asset(textOf(asset['url']));
          fail('A withdrawn subject consent must reject future byte reads');
        } on ApiFailure catch (e) {
          expect(e.status, 404);
        }
        expect((await owner.asset(textOf(asset['url']))).isNotEmpty, true);
        expect(
          await owner.request(
            'GET',
            '/api/v1/reports/financial?from=2026-09-01&to=2026-09-28',
          ),
          money,
        );
        await t.ensureVisible(find.text('خصوصية صور المجموعة'));
        await t.pumpAndSettle();
        if (const bool.fromEnvironment('CAPTURE_VISUALS')) {
          await binding.takeScreenshot('phase-media-family-withdrawn-ios');
        }
        expect(t.takeException(), isNull);
      } finally {
        await family.logout();
        await owner.logout();
      }
    },
  );
}
