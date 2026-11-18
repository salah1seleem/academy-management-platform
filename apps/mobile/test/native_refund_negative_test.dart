import 'package:academy_mobile/core/api/api_client.dart';
import 'package:academy_mobile/features/admin/native_refund.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

import 'approved_native_refund_test.dart' as flow;
import 'v2_role_workspaces_test.dart' show authenticate, app;

Map<String, dynamic> source() => {
  ...flow.source('Online', false),
  'canRecord': true,
};
Map<String, dynamic> quote() => {
  'amount': '13.34',
  'available': '100.00',
  'currency': 'EGP',
  'sessions': [],
};

Future<void> amount(WidgetTester t, String value) async {
  final f = find.widgetWithText(TextFormField, 'قيمة الاسترداد');
  await t.ensureVisible(f);
  await t.enterText(f, value);
  await t.pumpAndSettle();
}

void main() {
  final badQuotes = <String, Map<String, dynamic>>{
    'different amount': {...quote(), 'amount': '13.35'},
    'zero amount': {...quote(), 'amount': '0.00'},
    'missing amount': {...quote(), 'amount': null},
    'invalid amount': {...quote(), 'amount': 'bad'},
    'wrong currency': {...quote(), 'currency': 'USD'},
    'missing currency': {...quote(), 'currency': null},
    'invalid available': {...quote(), 'available': 'bad'},
    'insufficient available': {...quote(), 'available': '13.33'},
    'missing sessions': {...quote(), 'sessions': null},
    'broken session': {
      ...quote(),
      'sessions': [
        {'id': 'shift'},
      ],
    },
  };
  for (final entry in badQuotes.entries) {
    testWidgets('RFN10 blocks inconsistent quote: ${entry.key}', (t) async {
      var posts = 0;
      final auth = await authenticate('AcademyOwner', (m, p, b, token) async {
        if (p.endsWith('/quote')) return entry.value;
        if (m == 'POST') posts++;
        return source();
      });
      await t.pumpWidget(
        app(NativePlayerRefund(auth: auth, playerId: 'player')),
      );
      await t.pumpAndSettle();
      await flow.choose(t, 'التحصيل', '2026-10-21 · المتاح 100 ج.م');
      await amount(t, '13.34');
      await flow.tap(t, 'مراجعة مبلغ الاسترداد');
      expect(find.text('تأكيد تسجيل الاسترداد'), findsNothing);
      expect(
        find.text('تعذر التحقق من مراجعة الاسترداد. أعد المراجعة قبل الصرف.'),
        findsOneWidget,
      );
      expect(posts, 0);
      expect(t.takeException(), isNull);
      await t.pumpWidget(const SizedBox());
      auth.dispose();
    });
  }
  testWidgets(
    'RFN02 distinguishes equal-date equal-value collections by receipt',
    (t) async {
      final data = source();
      data['collections'] = [
        {
          ...(data['collections'] as List).first as Map,
          'receiptNumber': 'RC-A',
        },
        {
          ...(data['collections'] as List).first as Map,
          'id': 'second',
          'receiptNumber': 'RC-B',
        },
      ];
      String? selected;
      final auth = await authenticate('AcademyOwner', (m, p, b, token) async {
        if (p.endsWith('/quote')) {
          selected = (b as Map)['collectionId'];
          return quote();
        }
        return data;
      });
      await t.pumpWidget(
        app(NativePlayerRefund(auth: auth, playerId: 'player')),
      );
      await t.pumpAndSettle();
      await flow.choose(t, 'التحصيل', 'RC-B · 2026-10-21 · المتاح 100 ج.م');
      await amount(t, '13.34');
      await flow.tap(t, 'مراجعة مبلغ الاسترداد');
      expect(selected, 'second');
      expect(t.takeException(), isNull);
      await t.pumpWidget(const SizedBox());
      auth.dispose();
    },
  );
  testWidgets('RFN11 respects read-only refund access from the real contract', (
    t,
  ) async {
    final auth = await authenticate(
      'AcademyOwner',
      (m, p, b, token) async => {...source(), 'canRecord': false},
    );
    await t.pumpWidget(app(NativePlayerRefund(auth: auth, playerId: 'player')));
    await t.pumpAndSettle();
    expect(find.text('سجل الاستردادات'), findsOneWidget);
    expect(find.byType(TextFormField), findsNothing);
    expect(find.text('مراجعة مبلغ الاسترداد'), findsNothing);
    expect(find.text('هذا السجل للقراءة فقط.'), findsOneWidget);
    await t.pumpWidget(const SizedBox());
    auth.dispose();
  });
  for (final fault in [
    'missing collections',
    'broken available',
    'missing authority',
    'duplicate source',
  ]) {
    testWidgets('RFN32 refuses malformed source: $fault', (t) async {
      final data = source();
      if (fault == 'missing collections') data.remove('collections');
      if (fault == 'missing authority') data.remove('canRecord');
      if (fault == 'broken available')
        data['collections'] = [
          {...(data['collections'] as List).first as Map, 'available': 'bad'},
        ];
      if (fault == 'duplicate source')
        data['collections'] = [
          ...(data['collections'] as List),
          ...(data['collections'] as List),
        ];
      final auth = await authenticate(
        'AcademyOwner',
        (m, p, b, token) async => data,
      );
      await t.pumpWidget(
        app(NativePlayerRefund(auth: auth, playerId: 'player')),
      );
      await t.pumpAndSettle();
      expect(
        find.text('تعذر التحقق من سجل الاستردادات. أعد تحميل السجل.'),
        findsOneWidget,
      );
      expect(find.text('إعادة تحميل السجل'), findsOneWidget);
      expect(find.text('لا يوجد مبلغ متاح للاسترداد المباشر.'), findsNothing);
      expect(find.byType(TextFormField), findsNothing);
      expect(t.takeException(), isNull);
      await t.pumpWidget(const SizedBox());
      auth.dispose();
    });
  }
  testWidgets('RFN14 recovers a failed initial read through an actual button', (
    t,
  ) async {
    var reads = 0;
    final auth = await authenticate('AcademyOwner', (m, p, b, token) async {
      if (++reads == 1) throw const ApiFailure(503, 'تعذر تحميل السجل.');
      return source();
    });
    await t.pumpWidget(app(NativePlayerRefund(auth: auth, playerId: 'player')));
    await t.pumpAndSettle();
    await flow.tap(t, 'إعادة تحميل السجل');
    expect(
      find.widgetWithText(DropdownButtonFormField<String>, 'التحصيل'),
      findsOneWidget,
    );
    expect(reads, 2);
    await t.pumpWidget(const SizedBox());
    auth.dispose();
  });
  testWidgets('RFN20 acknowledged writes offer read-only verification', (
    t,
  ) async {
    var posts = 0, failed = false;
    final auth = await authenticate('AcademyOwner', (m, p, b, token) async {
      if (p.endsWith('/quote')) return quote();
      if (m == 'POST') {
        posts++;
        return {'id': 'refund'};
      }
      if (posts > 0 && !failed) {
        failed = true;
        throw const ApiFailure(503, 'تعذر قراءة السجل.');
      }
      return {...flow.source('Online', posts > 0), 'canRecord': true};
    });
    await t.pumpWidget(app(NativePlayerRefund(auth: auth, playerId: 'player')));
    await t.pumpAndSettle();
    await flow.choose(t, 'التحصيل', '2026-10-21 · المتاح 100 ج.م');
    await amount(t, '13.34');
    await flow.tap(t, 'مراجعة مبلغ الاسترداد');
    await flow.tap(t, 'أؤكد أن التحويل تم خارج النظام وأوثق رده مرة واحدة.');
    await flow.tap(t, 'تأكيد تسجيل الاسترداد');
    await flow.tap(t, 'التحقق من تسجيل الاسترداد');
    expect(posts, 1);
    expect(
      find.text('سُجل الاسترداد وظهر في السجل. لا تصرف المبلغ مرة أخرى.'),
      findsOneWidget,
    );
    await t.pumpWidget(const SizedBox());
    auth.dispose();
  });
  testWidgets('RFN31 refuses an overlong reason before requesting a quote', (
    t,
  ) async {
    var quotes = 0;
    final auth = await authenticate('AcademyOwner', (m, p, b, token) async {
      if (p.endsWith('/quote')) {
        quotes++;
        return quote();
      }
      return source();
    });
    await t.pumpWidget(app(NativePlayerRefund(auth: auth, playerId: 'player')));
    await t.pumpAndSettle();
    await flow.choose(t, 'التحصيل', '2026-10-21 · المتاح 100 ج.م');
    await amount(t, '13.34');
    await t.enterText(
      find.widgetWithText(TextFormField, 'سبب الاسترداد'),
      'أ' * 501,
    );
    await flow.tap(t, 'مراجعة مبلغ الاسترداد');
    expect(quotes, 0);
    expect(find.text('السبب لا يتجاوز 500 حرف.'), findsOneWidget);
    await t.pumpWidget(const SizedBox());
    auth.dispose();
  });
}
