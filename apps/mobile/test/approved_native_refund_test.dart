import 'package:academy_mobile/core/api/api_client.dart';
import 'package:academy_mobile/features/admin/native_refund.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

import 'v2_role_workspaces_test.dart' show authenticate, app;

Future<void> tap(WidgetTester t, String text) async {
  final scrollable = find.byType(Scrollable).first;
  t.state<ScrollableState>(scrollable).position.jumpTo(0);
  await t.pump();
  final f = find.text(text);
  await t.scrollUntilVisible(f, 150, scrollable: scrollable);
  await t.ensureVisible(f.last);
  await t.pumpAndSettle();
  await t.ensureVisible(f.last);
  await t.tap(f.last);
  await t.pumpAndSettle();
}

Future<void> choose(WidgetTester t, String label, String value) async {
  final f = find.widgetWithText(DropdownButtonFormField<String>, label);
  await t.ensureVisible(f);
  await t.tap(f);
  await t.pumpAndSettle();
  await t.tap(find.text(value).last);
  await t.pumpAndSettle();
}

Map<String, dynamic> source(String method, bool saved) => {
  'collections': [
    {
      'id': 'collection',
      'available': '100.00',
      'currency': 'EGP',
      'paymentMethod': method,
      'confirmedAtUtc': '2026-10-21',
    },
  ],
  'refunds': saved
      ? [
          {
            'id': 'refund',
            'collectionId': 'collection',
            'amount': 13.34,
            'currency': 'EGP',
            'payoutMethod': method,
            'reason': null,
            'cashSessionId': method == 'Cash' ? 'shift' : null,
          },
        ]
      : [],
};
void main() {
  testWidgets(
    'Online refund requires explicit completed-transfer review and reads actual refund record before success',
    (t) async {
      var saved = false;
      Map? body;
      final auth = await authenticate('AcademyOwner', (m, p, b, token) async {
        if (p.endsWith('/quote')) {
          return {
            'amount': '13.34',
            'available': '100.00',
            'currency': 'EGP',
            'sessions': [],
          };
        }
        if (m == 'POST') {
          body = b as Map;
          saved = true;
          return {'id': 'refund'};
        }
        return source('Online', saved);
      });
      await t.pumpWidget(
        app(NativePlayerRefund(auth: auth, playerId: 'player')),
      );
      await t.pumpAndSettle();
      await choose(t, 'التحصيل', '2026-10-21 · المتاح 100 ج.م');
      await t.enterText(
        find.widgetWithText(TextFormField, 'قيمة الاسترداد'),
        '13.34',
      );
      await tap(t, 'مراجعة مبلغ الاسترداد');
      await t.scrollUntilVisible(
        find.text('تأكيد تسجيل الاسترداد'),
        150,
        scrollable: find.byType(Scrollable).first,
      );
      expect(
        t
            .widget<FilledButton>(
              find.widgetWithText(FilledButton, 'تأكيد تسجيل الاسترداد'),
            )
            .onPressed,
        isNull,
      );
      await tap(t, 'أؤكد أن التحويل تم خارج النظام وأوثق رده مرة واحدة.');
      await tap(t, 'تأكيد تسجيل الاسترداد');
      expect(body, {
        'collectionId': 'collection',
        'amount': '13.34',
        'reason': null,
      });
      expect(
        find.text('سُجل الاسترداد وظهر في السجل. لا تصرف المبلغ مرة أخرى.'),
        findsOneWidget,
      );
      expect(t.takeException(), isNull);
      await t.pumpWidget(const SizedBox());
      auth.dispose();
    },
  );
  testWidgets(
    'Cash refund requires its own selected shift and fresh server quote hash before confirmation',
    (t) async {
      var saved = false;
      Map? posted;
      final auth = await authenticate('AcademyOwner', (m, p, b, token) async {
        if (p.endsWith('/quote')) {
          return {
            'amount': '13.34',
            'available': '100.00',
            'currency': 'EGP',
            'cashEnabled': true,
            'sessions': [
              {'id': 'shift', 'name': 'وردية مخولة'},
            ],
            if ((b as Map)['cashSessionId'] == 'shift')
              'quoteHash': 'reviewed-hash',
          };
        }
        if (m == 'POST') {
          posted = b as Map;
          saved = true;
          return {'id': 'refund'};
        }
        return source('Cash', saved);
      });
      await t.pumpWidget(
        app(NativePlayerRefund(auth: auth, playerId: 'player')),
      );
      await t.pumpAndSettle();
      await choose(t, 'التحصيل', '2026-10-21 · المتاح 100 ج.م');
      await t.enterText(
        find.widgetWithText(TextFormField, 'قيمة الاسترداد'),
        '13.34',
      );
      await tap(t, 'مراجعة مبلغ الاسترداد');
      await t.scrollUntilVisible(
        find.byType(CheckboxListTile),
        150,
        scrollable: find.byType(Scrollable).first,
      );
      expect(
        t.widget<CheckboxListTile>(find.byType(CheckboxListTile)).onChanged,
        isNull,
      );
      await choose(t, 'وردية الصرف', 'وردية مخولة');
      await tap(t, 'مراجعة مبلغ الاسترداد');
      await tap(
        t,
        'راجعت الوردية والمبلغ وأؤكد توثيق صرف الاسترداد مرة واحدة.',
      );
      await tap(t, 'تأكيد تسجيل الاسترداد');
      expect(posted, containsPair('cashSessionId', 'shift'));
      expect(posted, containsPair('quoteHash', 'reviewed-hash'));
      expect(posted, containsPair('amount', '13.34'));
      expect(t.takeException(), isNull);
      await t.pumpWidget(const SizedBox());
      auth.dispose();
    },
  );
  testWidgets(
    'A lost refund response never reports success and retries unchanged without changing payout details',
    (t) async {
      final writes = <Map>[];
      var saved = false;
      final auth = await authenticate('AcademyOwner', (m, p, b, token) async {
        if (p.endsWith('/quote')) {
          return {
            'amount': '13.34',
            'available': '100.00',
            'currency': 'EGP',
            'sessions': [],
          };
        }
        if (m == 'POST') {
          writes.add(Map.from(b as Map));
          if (writes.length == 1) throw const ApiFailure(0, 'تعذر الاتصال');
          saved = true;
          return {'id': 'refund'};
        }
        return source('Online', saved);
      });
      await t.pumpWidget(
        app(NativePlayerRefund(auth: auth, playerId: 'player')),
      );
      await t.pumpAndSettle();
      await choose(t, 'التحصيل', '2026-10-21 · المتاح 100 ج.م');
      await t.enterText(
        find.widgetWithText(TextFormField, 'قيمة الاسترداد'),
        '13.34',
      );
      await tap(t, 'مراجعة مبلغ الاسترداد');
      await tap(t, 'أؤكد أن التحويل تم خارج النظام وأوثق رده مرة واحدة.');
      await tap(t, 'تأكيد تسجيل الاسترداد');
      expect(
        find.text('سُجل الاسترداد وظهر في السجل. لا تصرف المبلغ مرة أخرى.'),
        findsNothing,
      );
      expect(
        t
            .widgetList<EditableText>(find.byType(EditableText))
            .any((field) => field.focusNode.hasFocus),
        isFalse,
      );
      await tap(t, 'إعادة تأكيد الاسترداد نفسه');
      expect(writes, hasLength(2));
      expect(writes[0], writes[1]);
      expect(t.takeException(), isNull);
      await t.pumpWidget(const SizedBox());
      auth.dispose();
    },
  );
}
