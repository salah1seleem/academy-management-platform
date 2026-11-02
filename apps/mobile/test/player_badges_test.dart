import 'package:academy_mobile/features/shared/player_badges.dart';
import 'package:flutter/material.dart';
import 'package:flutter_localizations/flutter_localizations.dart';
import 'package:flutter_test/flutter_test.dart';

import 'owner_test.dart' as f;

Map<String, dynamic> badge() => {
  'id': 'g',
  'version': 9,
  'title': 'روح التعاون',
  'criterion': 'تعاون لاحظه المدرب دون ترتيب الأطفال',
  'points': 10,
  'state': 'Granted',
  'observedOn': '2026-10-18',
  'actorName': 'مدرب اصطناعي',
  'familyNote': null,
  'revocation': null,
};
Map<String, dynamic> journey() => {
  'enrollment': {'id': 'e', 'player': 'لاعب اختبار', 'sport': 'كرة القدم'},
  'enrollmentVersion': 5,
  'asOfDate': '2026-10-18',
  'points': 0,
  'canGrant': true,
  'canRevoke': true,
  'rules': [
    {
      'id': 'r',
      'title': 'روح التعاون',
      'criterion': 'تعاون بشري ملاحظ',
      'points': 10,
    },
  ],
  'items': <Object?>[],
  'hasMore': false,
};
void main() {
  testWidgets(
    'Grant sends saved enrollment version and public note, then rereads the true server summary',
    (t) async {
      final writes = <Map>[];
      bool saved = false;
      final auth = await f.owner((m, p, b, k) async {
        if (m == 'POST') {
          expect(p, '/api/v1/evaluations/badges/enrollments/e/grant');
          writes.add(b as Map);
          saved = true;
          return {'id': 'g'};
        }
        return {
          'enrollments': [
            {
              ...journey(),
              if (saved) 'points': 10,
              if (saved) 'items': [badge()],
              if (saved) 'rules': <Object?>[],
            },
          ],
          'hasMore': false,
        };
      });
      await t.pumpWidget(
        f.shell(PlayerBadgesPage(auth: auth, enrollmentId: 'e')),
      );
      await t.pumpAndSettle();
      await t.scrollUntilVisible(
        find.byType(DropdownButtonFormField<String>),
        180,
        scrollable: find.byType(Scrollable).first,
      );
      await t.tap(find.byType(DropdownButtonFormField<String>));
      await t.pumpAndSettle();
      await t.tap(find.text('روح التعاون · 10').last);
      await t.pumpAndSettle();
      final grant = find.widgetWithText(
        FilledButton,
        'منح الشارة ونشرها للأسرة',
      );
      await t.scrollUntilVisible(
        grant,
        180,
        scrollable: find.byType(Scrollable).first,
      );
      await t.ensureVisible(grant);
      await t.tap(grant);
      await t.pumpAndSettle();
      expect(writes.single, containsPair('enrollmentVersion', 5));
      expect(writes.single, containsPair('familyNote', null));
      expect(writes.single, containsPair('observedOn', '2026-10-18'));
      await t.scrollUntilVisible(
        find.text('شارة سارية'),
        180,
        scrollable: find.byType(Scrollable).first,
      );
      expect(find.text('شارة سارية'), findsOneWidget);
    },
  );
  testWidgets(
    'Family gates malformed permissions, reads revoked history and clears private data on background and logout',
    (t) async {
      final auth = await f.owner(
        (m, p, b, k) async => {
          'enrollments': [
            {
              ...journey(),
              'points': 0,
              'items': [
                {
                  ...badge(),
                  'state': 'Revoked',
                  'revocation': {
                    'actorName': 'مراجع',
                    'createdAtUtc': '2026-10-18T13:00:00Z',
                    'familyNote': null,
                  },
                },
              ],
            },
          ],
          'hasMore': false,
        },
      );
      await t.pumpWidget(
        f.shell(PlayerBadgesPage(auth: auth, playerId: 'child')),
      );
      await t.pumpAndSettle();
      expect(find.byType(DropdownButtonFormField<String>), findsNothing);
      expect(find.text('تأكيد سحب الشارة'), findsNothing);
      expect(find.text('شارة مسحوبة — مستبعدة من الملخص'), findsOneWidget);
      t.binding.handleAppLifecycleStateChanged(AppLifecycleState.inactive);
      await t.pumpAndSettle();
      expect(find.text('روح التعاون'), findsNothing);
      t.binding.handleAppLifecycleStateChanged(AppLifecycleState.resumed);
      await t.pumpAndSettle();
      await auth.logout();
      await t.pumpAndSettle();
      expect(find.text('روح التعاون'), findsNothing);
    },
  );
  testWidgets(
    'Failed revocation preserves expanded optional note and one request intent without false success',
    (t) async {
      final writes = <Map>[];
      final auth = await f.owner((m, p, b, k) async {
        if (m == 'POST') {
          expect(p, '/api/v1/evaluations/badges/grants/g/revoke');
          writes.add(b as Map);
          throw StateError('isolated lost response');
        }
        return {
          'enrollments': [
            {
              ...journey(),
              'canGrant': false,
              'points': 10,
              'items': [badge()],
            },
          ],
          'hasMore': false,
        };
      });
      await t.pumpWidget(
        f.shell(PlayerBadgesPage(auth: auth, enrollmentId: 'e')),
      );
      await t.pumpAndSettle();
      await t.scrollUntilVisible(
        find.text('سحب الشارة مع حفظ سجلها'),
        180,
        scrollable: find.byType(Scrollable).first,
      );
      await t.tap(find.text('سحب الشارة مع حفظ سجلها'));
      await t.pumpAndSettle();
      for (var i = 0; i < 2; i++) {
        final b = find.widgetWithText(FilledButton, 'تأكيد سحب الشارة');
        await t.scrollUntilVisible(
          b,
          180,
          scrollable: find.byType(Scrollable).first,
        );
        await t.ensureVisible(b);
        await t.pumpAndSettle();
        await t.tap(b);
        await t.pumpAndSettle();
      }
      expect(writes, hasLength(2));
      expect(writes[0], writes[1]);
      expect(writes.singleOrNull, null);
      expect(writes[0], containsPair('note', null));
      expect(find.textContaining('تم حفظ الشارة'), findsNothing);
    },
  );
  for (final width in [360.0, 390.0, 430.0]) {
    testWidgets(
      'English200% badge evidence at$width is readable without overflow',
      (t) async {
        await t.binding.setSurfaceSize(Size(width, 950));
        addTearDown(() => t.binding.setSurfaceSize(null));
        final auth = await f.owner(
          (m, p, b, k) async => {
            'enrollments': [
              {
                ...journey(),
                'items': [badge()],
                'points': 10,
              },
            ],
            'hasMore': false,
          },
        );
        await t.pumpWidget(
          MaterialApp(
            locale: const Locale('en'),
            supportedLocales: const [Locale('en'), Locale('ar')],
            localizationsDelegates: GlobalMaterialLocalizations.delegates,
            home: MediaQuery(
              data: const MediaQueryData(textScaler: TextScaler.linear(2)),
              child: PlayerBadgesPage(auth: auth, playerId: 'child'),
            ),
          ),
        );
        await t.pumpAndSettle();
        await t.scrollUntilVisible(
          find.text('Active badge'),
          180,
          scrollable: find.byType(Scrollable).first,
        );
        expect(find.text('Active badge'), findsOneWidget);
        expect(t.takeException(), isNull);
      },
    );
  }
}
