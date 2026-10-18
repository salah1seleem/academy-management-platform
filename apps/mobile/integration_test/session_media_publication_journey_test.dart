import 'dart:convert';

import 'package:academy_mobile/core/api/api_client.dart';
import 'package:academy_mobile/core/auth/auth_controller.dart';
import 'package:academy_mobile/core/storage/refresh_store.dart';
import 'package:academy_mobile/features/guardian/guardian_content.dart';
import 'package:academy_mobile/features/shared/admin_media_gallery.dart';
import 'package:academy_mobile/features/shared/media_memories.dart';
import 'package:academy_mobile/features/guardian/guardian_ui.dart';
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
    'Actual session media and monthly memories are read and indexed by family, withdrawn, republished and permanently erased without restoring old copies',
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
          SecureRefreshStore('academy.v3.sessionmedia.$role'),
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
        var sessions = rows(options['sessions']);
        if (sessions.isEmpty) {
          final today = textOf(options['asOfDate']);
          final all = rows(
            await owner.request(
              'GET',
              '/api/v1/attendance/sessions?from=$today&to=$today',
            ),
          );
          String tm(int h) => '${h.toString().padLeft(2, '0')}:00:00';
          final hour = List.generate(22, (i) => i + 1).firstWhere(
            (h) => !all.any(
              (s) =>
                  textOf(s['startTime']).compareTo(tm(h + 1)) < 0 &&
                  textOf(s['endTime']).compareTo(tm(h)) > 0,
            ),
          );
          sessions = [
            obj(
              await owner.request(
                'POST',
                '/api/v1/attendance/sessions',
                body: {
                  'trainingGroupId': group,
                  'sessionDate': today,
                  'startTime': tm(hour),
                  'endTime': tm(hour + 1),
                  'notes': 'حصة رحلة وسائط iOS اصطناعية بلا حضور',
                },
              ),
            ),
          ];
        }
        final session = textOf(sessions.first['id']);
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
              'sessionId': session,
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
          await binding.takeScreenshot(
            'phase-session-media-family-published-ios',
          );
        }

        await t.pumpWidget(shell(AdminMediaGallery(auth: owner)));
        await wait(find.text(caption));
        final targetCard = find
            .ancestor(of: find.text(caption), matching: find.byType(GCard))
            .first;
        final open = find.descendant(
          of: targetCard,
          matching: find.widgetWithText(OutlinedButton, 'سحب النشر'),
        );
        await t.scrollUntilVisible(
          open,
          160,
          scrollable: find.byType(Scrollable).first,
        );
        await t.tap(open);
        await t.pumpAndSettle();
        final field = find.byType(TextField).first;
        await t.ensureVisible(field);
        await t.enterText(field, 'مراجعة إدارية اصطناعية iOS');
        FocusManager.instance.primaryFocus?.unfocus();
        await t.pumpAndSettle();
        final confirm = find.widgetWithText(FilledButton, 'تأكيد سحب النشر');
        await t.ensureVisible(confirm);
        await t.pumpAndSettle();
        await t.tap(confirm);
        await t.pumpAndSettle();
        final saved = rows(
          await owner.request(
            'GET',
            '/api/v1/assets/gallery?includeWithdrawn=true',
          ),
        ).singleWhere((x) => x['id'] == asset['id']);
        expect(saved['withdrawn'], true);
        expect(saved['withdrawalNote'], 'مراجعة إدارية اصطناعية iOS');
        await t.scrollUntilVisible(
          find.text('مسحوبة من الأسر والمدرب').first,
          160,
          scrollable: find.byType(Scrollable).first,
        );
        await t.pumpAndSettle();
        if (const bool.fromEnvironment('CAPTURE_VISUALS')) {
          await binding.takeScreenshot(
            'phase-owner-withdrawn-session-media-ios',
          );
        }
        await t.pumpWidget(shell(GalleryPage(auth: family, playerId: child)));
        await wait(find.textContaining('المشاركة مسموحة باختيارك'));
        expect(find.text(caption), findsNothing);
        try {
          await family.asset(textOf(asset['url']));
          fail('Administrative withdrawal must reject future byte reads');
        } on ApiFailure catch (e) {
          expect(e.status, 404);
        }
        expect((await owner.asset(textOf(asset['url']))).isNotEmpty, true);
        final fresh = obj(
          await owner.upload(
            base64Decode(
              'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+ip1sAAAAASUVORK5CYII=',
            ),
            'image/png',
            {
              'purpose': 'Media',
              'groupId': group,
              'sessionId': session,
              'eventOn': textOf(options['eventOn']),
              'subjectPlayerIds': child,
              'privacyConfirmed': 'true',
              'noRecognizableChildren': 'false',
              'caption': '$caption — نسخة جديدة',
            },
          ),
        );
        expect(fresh['id'], isNot(asset['id']));
        await t.pumpWidget(shell(GalleryPage(auth: family, playerId: child)));
        await wait(find.text('$caption — نسخة جديدة'));
        await t.ensureVisible(
          find
              .ancestor(
                of: find.text('$caption — نسخة جديدة'),
                matching: find.byType(GCard),
              )
              .first,
        );
        await t.pumpAndSettle();
        expect((await family.asset(textOf(fresh['url']))).isNotEmpty, true);
        await t.scrollUntilVisible(
          find.text('ذكريات الشهر'),
          -180,
          scrollable: find.byType(Scrollable).first,
        );
        await t.tap(find.text('ذكريات الشهر'));
        await wait(find.text('$caption — نسخة جديدة'));
        expect(find.text(caption), findsNothing);
        final copyIndex = find.widgetWithText(
          OutlinedButton,
          'نسخ فهرس الشهر دون الملفات',
        );
        await t.ensureVisible(copyIndex);
        await t.pumpAndSettle();
        await t.tap(copyIndex);
        await wait(find.textContaining('تم نسخ فهرس الشهر دون الصور'));
        final copied = await Clipboard.getData(Clipboard.kTextPlain);
        expect(copied?.text, contains(textOf(fresh['id'])));
        expect(copied?.text, isNot(contains(textOf(asset['id']))));
        expect(copied?.text, isNot(contains('/api/v1/assets/')));
        await t.ensureVisible(
          find
              .ancestor(
                of: find.text('$caption — نسخة جديدة'),
                matching: find.byType(GCard),
              )
              .first,
        );
        await t.pumpAndSettle();
        if (const bool.fromEnvironment('CAPTURE_VISUALS')) {
          await binding.takeScreenshot('phase-family-monthly-memories-ios');
        }
        try {
          await family.asset(textOf(asset['url']));
          fail('A new publication cannot revive an old one');
        } on ApiFailure catch (e) {
          expect(e.status, 404);
        }
        final storageBefore = obj(
          await owner.request('GET', '/api/v1/assets/storage-policy'),
        );
        await t.pumpWidget(shell(AdminMediaGallery(auth: owner)));
        await wait(find.text('$caption — نسخة جديدة'));
        final eraseCard = find.ancestor(
          of: find.text('$caption — نسخة جديدة'),
          matching: find.byType(GCard),
        );
        expect(eraseCard, findsOneWidget);
        final erase = find.descendant(
          of: eraseCard,
          matching: find.widgetWithText(OutlinedButton, 'حذف ملف الوسائط'),
        );
        await t.scrollUntilVisible(
          erase,
          160,
          scrollable: find.byType(Scrollable).first,
        );
        await t.tap(erase);
        await t.pumpAndSettle();
        final eraseConfirm = find.widgetWithText(
          FilledButton,
          'تأكيد حذف الملف نهائيًا',
        );
        await t.ensureVisible(eraseConfirm);
        await t.pumpAndSettle();
        await t.tap(eraseConfirm);
        await t.pumpAndSettle();
        // The erasure transaction commits before the separate filesystem purge.
        // Wait for this card's confirmed response, not merely a settled frame.
        await wait(
          find.descendant(
            of: eraseCard,
            matching: find.text('تم حذف الملف؛ سجل النشر والحذف محفوظ'),
          ),
        );
        final erased = rows(
          await owner.request(
            'GET',
            '/api/v1/assets/gallery?includeWithdrawn=true',
          ),
        ).singleWhere((a) => a['id'] == fresh['id']);
        expect(erased['erased'], true);
        expect(erased['purged'], true);
        await t.ensureVisible(
          find
              .ancestor(
                of: find.text('$caption — نسخة جديدة'),
                matching: find.byType(GCard),
              )
              .first,
        );
        await t.pumpAndSettle();
        if (const bool.fromEnvironment('CAPTURE_VISUALS')) {
          await binding.takeScreenshot('phase-owner-erased-session-media-ios');
        }
        for (final reader in [owner, family]) {
          try {
            await reader.asset(textOf(fresh['url']));
            fail('Permanently erased copy must be unavailable even to owner');
          } on ApiFailure catch (e) {
            expect(e.status, 404);
          }
        }
        final storageAfter = obj(
          await owner.request('GET', '/api/v1/assets/storage-policy'),
        );
        expect(
          (storageAfter['storedBytes'] as num) <
              (storageBefore['storedBytes'] as num),
          true,
        );
        await t.pumpWidget(shell(GalleryPage(auth: family, playerId: child)));
        await wait(find.textContaining('المشاركة مسموحة باختيارك'));
        expect(find.text('$caption — نسخة جديدة'), findsNothing);
        await t.pumpWidget(
          shell(MediaMemoriesPage(auth: family, playerId: child)),
        );
        await wait(find.text('كل لحظة في مكانها'));
        expect(find.text('$caption — نسخة جديدة'), findsNothing);
        expect(
          await owner.request(
            'GET',
            '/api/v1/reports/financial?from=2026-09-01&to=2026-09-28',
          ),
          money,
        );
        expect(t.takeException(), isNull);
      } finally {
        await family.logout();
        await owner.logout();
      }
    },
  );
}
