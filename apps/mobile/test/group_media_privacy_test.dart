import 'dart:convert';
import 'dart:typed_data';

import 'package:academy_mobile/core/api/api_client.dart';
import 'package:academy_mobile/core/auth/auth_controller.dart';
import 'package:academy_mobile/features/shared/group_media_consent.dart';
import 'package:academy_mobile/features/shared/private_media.dart';
import 'package:flutter/material.dart';
import 'package:flutter_localizations/flutter_localizations.dart';
import 'package:flutter_test/flutter_test.dart';

import 'auth_controller_test.dart' show FakeApi, MemoryStore;
import 'owner_test.dart' as f;

Map<String, dynamic> choice(bool allowed) => {
  'player': 'طفل اصطناعي',
  'allowed': allowed,
  'revision': allowed ? 1 : 0,
  'notice': 'مشاركة المجموعة اختيارية ومستقلة عن التسويق.',
};
Widget page(Widget child) => Scaffold(body: ListView(children: [child]));
Future<void> confirm(WidgetTester t) async {
  await t.pumpAndSettle();
  await t.tap(find.text('تأكيد اختياري'));
  await t.pumpAndSettle();
}

class PixelApi extends FakeApi implements AssetTransport {
  int reads = 0;
  PixelApi() : super((m, p, b, k) async => f.ownerCredentials());
  @override
  Future<Uint8List> asset(String path, String token) async {
    reads++;
    return base64Decode(
      'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+ip1sAAAAASUVORK5CYII=',
    );
  }

  @override
  Future<Object?> upload(
    Uint8List bytes,
    String contentType,
    Map<String, String> fields,
    String token,
  ) async => {'id': 'test'};
}

void main() {
  testWidgets(
    'Own choice is off by default; confirmation and cancel are explicit; save failure retries one intent',
    (t) async {
      final writes = <Map>[];
      var allowed = false;
      final auth = await f.owner((m, p, b, k) async {
        if (m == 'POST') {
          writes.add(b as Map);
          if (writes.length == 1) {
            throw const ApiFailure(503, 'اختبار تعذر الرد');
          }
          allowed = true;
          return {'id': 'c'};
        }
        return choice(allowed);
      });
      await t.pumpWidget(
        f.shell(page(GroupMediaConsentCard(auth: auth, playerId: 'p'))),
      );
      await t.pumpAndSettle();
      expect(find.textContaining('المشاركة غير مسموحة'), findsOneWidget);
      expect(writes, isEmpty);
      final button = find.widgetWithText(
        FilledButton,
        'السماح بالمشاركة داخل المجموعة',
      );
      await t.ensureVisible(button);
      await t.tap(button);
      await t.pumpAndSettle();
      await t.tap(find.text('رجوع'));
      await t.pumpAndSettle();
      expect(writes, isEmpty);
      await t.tap(button);
      await confirm(t);
      expect(find.textContaining('لم نؤكد حفظ'), findsOneWidget);
      expect(find.textContaining('المشاركة مسموحة باختيارك'), findsNothing);
      await t.ensureVisible(button);
      await t.tap(button);
      await confirm(t);
      expect(writes.length, 2);
      expect(writes.first, writes.last);
      expect(writes.last['revision'], 0);
      expect(writes.last['allowed'], true);
      expect(find.textContaining('المشاركة مسموحة باختيارك'), findsOneWidget);
    },
  );
  testWidgets(
    'Private choice clears on background and logout and rereads on resume',
    (t) async {
      final auth = await f.owner((m, p, b, k) async => choice(false));
      await t.pumpWidget(
        f.shell(page(GroupMediaConsentCard(auth: auth, playerId: 'p'))),
      );
      await t.pumpAndSettle();
      t.binding.handleAppLifecycleStateChanged(AppLifecycleState.inactive);
      await t.pumpAndSettle();
      expect(find.textContaining('طفل اصطناعي'), findsNothing);
      t.binding.handleAppLifecycleStateChanged(AppLifecycleState.resumed);
      await t.pumpAndSettle();
      expect(find.textContaining('طفل اصطناعي'), findsOneWidget);
      await auth.logout();
      await t.pumpAndSettle();
      expect(find.textContaining('طفل اصطناعي'), findsNothing);
    },
  );
  testWidgets(
    'Group review lists only server roster, disables missing consent and requires explicit privacy review',
    (t) async {
      final auth = await f.owner(
        (m, p, b, k) async => p.contains('media-audience')
            ? {
                'eventOn': '2026-10-18',
                'asOfDate': '2026-10-18',
                'notice': 'جمهور الواقعة',
                'players': [
                  {'id': 'a', 'name': 'طفل موافق', 'consented': true},
                  {'id': 'b', 'name': 'طفل غير موافق', 'consented': false},
                ],
              }
            : [],
      );
      await t.pumpWidget(f.shell(UploadSessionMedia(auth: auth, groupId: 'g')));
      await t.pumpAndSettle();
      expect(
        t
            .widget<CheckboxListTile>(
              find.widgetWithText(CheckboxListTile, 'طفل غير موافق'),
            )
            .onChanged,
        isNull,
      );
      expect(
        t
            .widget<CheckboxListTile>(
              find.widgetWithText(CheckboxListTile, 'طفل موافق'),
            )
            .value,
        false,
      );
      final publish = find.widgetWithText(FilledButton, 'رفع صورة أو فيديو');
      await t.ensureVisible(publish);
      await t.tap(publish);
      await t.pumpAndSettle();
      expect(find.textContaining('راجع الأطفال الظاهرين'), findsOneWidget);
      expect(t.takeException(), isNull);
    },
  );
  testWidgets(
    'Private image bytes disappear in background and refetch on resume; logout never retains the bitmap',
    (t) async {
      final api = PixelApi();
      final a = AuthController(api, MemoryStore())..restoring = false;
      await a.verify('p', 'c', 'o');
      await t.pumpWidget(
        f.shell(
          page(
            PrivateImage(
              auth: a,
              path: '/api/v1/assets/00000000-0000-0000-0000-000000000001',
            ),
          ),
        ),
      );
      await t.pumpAndSettle();
      expect(api.reads, 1);
      expect(find.byType(Image), findsOneWidget);
      t.binding.handleAppLifecycleStateChanged(AppLifecycleState.inactive);
      await t.pumpAndSettle();
      expect(find.byType(Image), findsNothing);
      t.binding.handleAppLifecycleStateChanged(AppLifecycleState.resumed);
      await t.pumpAndSettle();
      expect(api.reads, 2);
      await a.logout();
      await t.pumpAndSettle();
      expect(find.byType(Image), findsNothing);
      await t.pumpWidget(const SizedBox());
      a.dispose();
    },
  );
  for (final width in [360.0, 390.0, 430.0]) {
    testWidgets('English 200% group consent at $width stays readable', (
      t,
    ) async {
      await t.binding.setSurfaceSize(Size(width, 900));
      addTearDown(() => t.binding.setSurfaceSize(null));
      final auth = await f.owner((m, p, b, k) async => choice(false));
      await t.pumpWidget(
        MaterialApp(
          locale: const Locale('en'),
          supportedLocales: const [Locale('en')],
          localizationsDelegates: GlobalMaterialLocalizations.delegates,
          builder: (c, w) => MediaQuery(
            data: MediaQuery.of(c)
                .copyWith(textScaler: const TextScaler.linear(2)),
            child: w!,
          ),
          home: page(GroupMediaConsentCard(auth: auth, playerId: 'p')),
        ),
      );
      await t.pumpAndSettle();
      expect(find.text('Group media privacy'), findsOneWidget);
      expect(t.takeException(), isNull);
    });
  }
}
