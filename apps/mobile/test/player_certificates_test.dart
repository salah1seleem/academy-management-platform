import 'package:academy_mobile/features/shared/player_certificates.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:flutter_localizations/flutter_localizations.dart';

import 'owner_test.dart' as f;

Map<String, dynamic> certificate() => {
  'id': 'c',
  'academyName': 'أكاديمية اصطناعية',
  'playerName': 'لاعب اختبار',
  'sportName': 'كرة القدم',
  'levelName': 'مستوى موثق',
  'milestoneOn': '2026-10-18',
  'issuedOn': '2026-10-18',
  'actorName': 'مراجع مخول',
  'number': 'ABC123',
  'version': 5,
  'state': 'Issued',
  'withdrawal': null,
};
Map<String, dynamic> source() => {
  'source': {
    'id': 'r',
    'version': 7,
    'player': 'لاعب اختبار',
    'sport': 'كرة القدم',
    'toLevelName': 'مستوى موثق',
  },
  'certificate': null,
  'canIssue': true,
};
void main() {
  testWidgets(
    'Withdrawal is an explicit edit with optional note and one stable retry intent',
    (t) async {
      final writes = <Map<String, dynamic>>[];
      final auth = await f.owner((m, p, b, k) async {
        if (m == 'POST') {
          expect(p, '/api/v1/evaluations/certificates/c/withdraw');
          writes.add(Map<String, dynamic>.from(b! as Map));
          throw StateError('isolated lost reply');
        }
        return {'certificate': certificate(), 'canWithdraw': true};
      });
      await t.pumpWidget(f.shell(PlayerCertificatesPage(auth: auth, id: 'c')));
      await t.pumpAndSettle();
      await t.scrollUntilVisible(
        find.text('سحب الشهادة مع حفظ سجلها'),
        180,
        scrollable: find.byType(Scrollable).first,
      );
      await t.tap(find.text('سحب الشهادة مع حفظ سجلها'));
      await t.pumpAndSettle();
      await t.scrollUntilVisible(
        find.text('تأكيد سحب الشهادة'),
        180,
        scrollable: find.byType(Scrollable).first,
      );
      await t.ensureVisible(
        find.widgetWithText(FilledButton, 'تأكيد سحب الشهادة'),
      );
      await t.pumpAndSettle();
      await t.tap(find.widgetWithText(FilledButton, 'تأكيد سحب الشهادة'));
      await t.pumpAndSettle();
      await t.scrollUntilVisible(
        find.text('تأكيد سحب الشهادة'),
        180,
        scrollable: find.byType(Scrollable).first,
      );
      await t.ensureVisible(
        find.widgetWithText(FilledButton, 'تأكيد سحب الشهادة'),
      );
      await t.pumpAndSettle();
      await t.tap(find.widgetWithText(FilledButton, 'تأكيد سحب الشهادة'));
      await t.pumpAndSettle();
      expect(writes, hasLength(2));
      expect(writes[0], writes[1]);
      expect(writes[0], containsPair('familyNote', null));
      expect(writes[0], containsPair('version', 5));
      expect(find.textContaining('تم حفظ الإجراء'), findsNothing);
    },
  );
  testWidgets(
    'Explicit issue binds the actual source version and rereads the published certificate',
    (t) async {
      final writes = <Object?>[];
      bool saved = false;
      final auth = await f.owner((m, p, b, k) async {
        if (m == 'POST') {
          expect(p, '/api/v1/evaluations/certificates/sources/r/issue');
          writes.add(b);
          saved = true;
          return {'id': 'c'};
        }
        return saved
            ? {'certificate': certificate(), 'canWithdraw': false}
            : source();
      });
      await t.pumpWidget(
        f.shell(PlayerCertificatesPage(auth: auth, sourceId: 'r')),
      );
      await t.pumpAndSettle();
      await t.tap(find.text('إصدار شهادة الإنجاز لولي الأمر'));
      await t.pumpAndSettle();
      expect(writes.single, containsPair('sourceVersion', 7));
      expect(find.text('شهادة إنجاز فني'), findsOneWidget);
      expect(find.text('ABC123'), findsOneWidget);
      expect(find.text('إصدار شهادة الإنجاز لولي الأمر'), findsNothing);
    },
  );
  testWidgets(
    'Family cannot mutate even malformed fixture permissions and sees withdrawn status',
    (t) async {
      final auth = await f.owner(
        (m, p, b, k) async => {
          'certificate': {
            ...certificate(),
            'state': 'Withdrawn',
            'withdrawal': {
              'actorName': 'مراجع',
              'createdAtUtc': '2026-10-18T12:00:00Z',
              'familyNote': null,
            },
          },
          'canWithdraw': true,
          'canIssue': true,
        },
      );
      await t.pumpWidget(
        f.shell(PlayerCertificatesPage(auth: auth, id: 'c', playerId: 'child')),
      );
      await t.pumpAndSettle();
      expect(find.text('الشهادة مسحوبة — لا تعد سارية'), findsOneWidget);
      expect(find.text('سحب الشهادة مع حفظ سجلها'), findsNothing);
      expect(find.text('إصدار شهادة الإنجاز لولي الأمر'), findsNothing);
      t.binding.handleAppLifecycleStateChanged(AppLifecycleState.inactive);
      await t.pumpAndSettle();
      expect(find.text('لاعب اختبار'), findsNothing);
      t.binding.handleAppLifecycleStateChanged(AppLifecycleState.resumed);
      await t.pumpAndSettle();
      await auth.logout();
      await t.pumpAndSettle();
      expect(find.text('ABC123'), findsNothing);
    },
  );
  for (final width in [360.0, 390.0, 430.0]) {
    testWidgets('English200 percent certificate at$width remains readable', (
      t,
    ) async {
      await t.binding.setSurfaceSize(Size(width, 1000));
      addTearDown(() => t.binding.setSurfaceSize(null));
      final auth = await f.owner(
        (m, p, b, k) async => {
          'certificate': certificate(),
          'canWithdraw': false,
        },
      );
      await t.pumpWidget(
        MaterialApp(
          locale: const Locale('en'),
          supportedLocales: const [Locale('ar'), Locale('en')],
          localizationsDelegates: GlobalMaterialLocalizations.delegates,
          home: MediaQuery(
            data: const MediaQueryData(textScaler: TextScaler.linear(2)),
            child: PlayerCertificatesPage(auth: auth, id: 'c'),
          ),
        ),
      );
      await t.pumpAndSettle();
      await t.scrollUntilVisible(find.text('ABC123'), 200);
      expect(t.takeException(), isNull);
      expect(find.text('ABC123'), findsOneWidget);
    });
  }
}
