import 'dart:async';

import 'package:academy_mobile/core/api/api_client.dart';
import 'package:academy_mobile/core/theme/app_theme.dart';
import 'package:academy_mobile/features/admin/native_refund.dart';
import 'package:flutter/material.dart';
import 'package:flutter/rendering.dart';
import 'package:flutter/services.dart';
import 'package:flutter_test/flutter_test.dart';

import 'approved_native_refund_test.dart' as flow;
import 'shared_ui_quality_test.dart' show capture;
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
  await t.pumpAndSettle();
  await t.enterText(f, value);
  await t.pumpAndSettle();
}

void main() {
  TestWidgetsFlutterBinding.ensureInitialized();
  setUpAll(() async {
    final arabic = FontLoader('AcademyArabic');
    for (final weight in [300, 400, 500, 600, 700]) {
      arabic.addFont(rootBundle.load('assets/fonts/AcademyArabic-$weight.ttf'));
    }
    final icons = FontLoader('MaterialIcons')
      ..addFont(rootBundle.load('fonts/MaterialIcons-Regular.otf'));
    await Future.wait([arabic.load(), icons.load()]);
  });
  testWidgets(
    'RFN01 refund form retains the player identity passed by its file',
    (t) async {
      final auth = await authenticate(
        'AcademyOwner',
        (m, p, b, token) async => source(),
      );
      await t.pumpWidget(
        app(
          NativePlayerRefund(
            auth: auth,
            playerId: 'player',
            playerName: 'لاعب اختبار',
            playerCode: 'TST-001',
          ),
        ),
      );
      await t.pumpAndSettle();
      expect(find.text('لاعب اختبار · TST-001'), findsOneWidget);
      expect(t.takeException(), isNull);
      await t.pumpWidget(const SizedBox());
      auth.dispose();
    },
  );
  testWidgets(
    'RFN24 disabled cash never enables confirmation despite a selected shift',
    (t) async {
      final auth = await authenticate('AcademyOwner', (m, p, b, token) async {
        if (p.endsWith('/quote')) {
          return {
            ...quote(),
            'cashEnabled': false,
            'quoteHash': 'unexpected-hash',
            'sessions': [
              {'id': 'shift', 'name': 'وردية اختبار'},
            ],
          };
        }
        return {...flow.source('Cash', false), 'canRecord': true};
      });
      await t.pumpWidget(
        app(NativePlayerRefund(auth: auth, playerId: 'player')),
      );
      await t.pumpAndSettle();
      await flow.choose(t, 'التحصيل', '2026-10-21 · المتاح 100 ج.م');
      await amount(t, '13.34');
      await flow.tap(t, 'مراجعة مبلغ الاسترداد');
      await flow.choose(t, 'وردية الصرف', 'وردية اختبار');
      await flow.tap(t, 'مراجعة مبلغ الاسترداد');
      final checkbox = find.byType(CheckboxListTile);
      await t.scrollUntilVisible(
        checkbox,
        150,
        scrollable: find.byType(Scrollable).first,
      );
      await t.ensureVisible(checkbox);
      expect(t.widget<CheckboxListTile>(checkbox).onChanged, isNull);
      expect(find.text('النقدي غير مفعل لهذه الأكاديمية.'), findsOneWidget);
      expect(
        t
            .widget<FilledButton>(
              find.widgetWithText(FilledButton, 'تأكيد تسجيل الاسترداد'),
            )
            .onPressed,
        isNull,
      );
      await t.pumpWidget(const SizedBox());
      auth.dispose();
    },
  );
  testWidgets('RFN25 late source cannot restore a logged-out refund form', (
    t,
  ) async {
    final response = Completer<Object?>();
    final auth = await authenticate(
      'AcademyOwner',
      (m, p, b, token) => response.future,
    );
    await t.pumpWidget(app(NativePlayerRefund(auth: auth, playerId: 'player')));
    await t.pump();
    await auth.logout();
    response.complete(source());
    await t.pumpAndSettle();
    expect(find.byType(TextFormField), findsNothing);
    expect(find.text('سجل الاستردادات'), findsNothing);
    expect(t.takeException(), isNull);
    await t.pumpWidget(const SizedBox());
    auth.dispose();
  });
  testWidgets(
    'RFN32 legitimate zero availability remains an empty read-only source',
    (t) async {
      final data = source();
      data['collections'] = [
        {
          ...(data['collections'] as List).first as Map<String, dynamic>,
          'available': '0.00',
        },
      ];
      final auth = await authenticate(
        'AcademyOwner',
        (m, p, b, token) async => data,
      );
      await t.pumpWidget(
        app(NativePlayerRefund(auth: auth, playerId: 'player')),
      );
      await t.pumpAndSettle();
      expect(find.text('لا يوجد مبلغ متاح للاسترداد المباشر.'), findsOneWidget);
      expect(find.byType(TextFormField), findsNothing);
      expect(find.text('مراجعة مبلغ الاسترداد'), findsNothing);
      await t.pumpWidget(const SizedBox());
      auth.dispose();
    },
  );
  for (final text in ['', '   ', 'أ' * 500]) {
    testWidgets(
      'RFN09 RFN31 optional reason has exact boundary ${text.length}',
      (t) async {
        Map? body;
        final auth = await authenticate('AcademyOwner', (m, p, b, token) async {
          if (p.endsWith('/quote')) {
            body = b as Map;
            return quote();
          }
          return source();
        });
        await t.pumpWidget(
          app(NativePlayerRefund(auth: auth, playerId: 'player')),
        );
        await t.pumpAndSettle();
        await flow.choose(t, 'التحصيل', '2026-10-21 · المتاح 100 ج.م');
        await amount(t, '13.34');
        await t.enterText(
          find.widgetWithText(TextFormField, 'سبب الاسترداد'),
          text,
        );
        await flow.tap(t, 'مراجعة مبلغ الاسترداد');
        expect(body!['reason'], text.trim().isEmpty ? null : text);
        expect(t.takeException(), isNull);
        await t.pumpWidget(const SizedBox());
        auth.dispose();
      },
    );
  }
  for (final width in [320.0, 390.0]) {
    for (final scale in [1.0, 2.0]) {
      for (final dark in [false, true]) {
        testWidgets('RFN29 full review fits $width text $scale dark $dark', (
          t,
        ) async {
          t.view.physicalSize = Size(width, 844);
          t.view.devicePixelRatio = 1;
          addTearDown(t.view.reset);
          final data = source();
          data['collections'] = [
            {
              ...(data['collections'] as List).first as Map<String, dynamic>,
              'receiptNumber': 'RC-20261118-ABCD1234',
            },
          ];
          final auth = await authenticate(
            'AcademyOwner',
            (m, p, b, token) async => p.endsWith('/quote') ? quote() : data,
          );
          await t.pumpWidget(
            RepaintBoundary(
              key: const ValueKey('quality-capture'),
              child: app(
                Theme(
                  data: dark ? AcademyTheme.dark() : AcademyTheme.light(),
                  child: NativePlayerRefund(auth: auth, playerId: 'player'),
                ),
                scale: scale,
              ),
            ),
          );
          await t.pumpAndSettle();
          await flow.choose(
            t,
            'التحصيل',
            'RC-20261118-ABCD1234 · 2026-10-21 · المتاح 100 ج.م',
          );
          await capture(
            t,
            'source-${width.toInt()}-text${scale.toInt()}-${dark ? 'dark' : 'light'}',
          );
          final sourceField = find.widgetWithText(
            DropdownButtonFormField<String>,
            'التحصيل',
          );
          final sourceLabel = find
              .text('RC-20261118-ABCD1234 · 2026-10-21 · المتاح 100 ج.م')
              .first;
          final bounds = t.getRect(sourceField);
          final richText = find
              .descendant(of: sourceLabel, matching: find.byType(RichText))
              .first;
          final origin = t.getTopLeft(richText);
          final paragraph = t.renderObject<RenderParagraph>(richText);
          final boxes = paragraph.getBoxesForSelection(
            TextSelection(
              baseOffset: 0,
              extentOffset:
                  'RC-20261118-ABCD1234 · 2026-10-21 · المتاح 100 ج.م'.length,
            ),
          );
          expect(boxes, isNotEmpty);
          for (final box in boxes) {
            expect(
              origin.dy + box.bottom,
              lessThanOrEqualTo(bounds.bottom + .5),
            );
            expect(
              origin.dx + box.left,
              greaterThanOrEqualTo(bounds.left - .5),
            );
            expect(origin.dx + box.right, lessThanOrEqualTo(bounds.right + .5));
          }
          expect(t.takeException(), isNull);
          await amount(t, '13.34');
          await flow.tap(t, 'مراجعة مبلغ الاسترداد');
          await flow.tap(
            t,
            'أؤكد أن التحويل تم خارج النظام وأوثق رده مرة واحدة.',
          );
          final confirm = find.widgetWithText(
            FilledButton,
            'تأكيد تسجيل الاسترداد',
          );
          await t.ensureVisible(confirm);
          await t.pumpAndSettle();
          await capture(
            t,
            'review-${width.toInt()}-text${scale.toInt()}-${dark ? 'dark' : 'light'}',
          );
          expect(t.widget<FilledButton>(confirm).onPressed, isNotNull);
          final rect = t.getRect(confirm);
          expect(rect.left, greaterThanOrEqualTo(0));
          expect(rect.right, lessThanOrEqualTo(width));
          expect(t.takeException(), isNull);
          await t.pumpWidget(const SizedBox());
          auth.dispose();
        });
      }
    }
  }
  testWidgets('RFN15 failed re-review removes an earlier approval', (t) async {
    var requests = 0;
    final auth = await authenticate('AcademyOwner', (m, p, b, token) async {
      if (p.endsWith('/quote')) {
        if (++requests == 2) {
          throw const ApiFailure(503, 'تعذر تحميل المراجعة.');
        }
        return quote();
      }
      return source();
    });
    await t.pumpWidget(app(NativePlayerRefund(auth: auth, playerId: 'player')));
    await t.pumpAndSettle();
    await flow.choose(t, 'التحصيل', '2026-10-21 · المتاح 100 ج.م');
    await amount(t, '13.34');
    await flow.tap(t, 'مراجعة مبلغ الاسترداد');
    await flow.tap(t, 'أؤكد أن التحويل تم خارج النظام وأوثق رده مرة واحدة.');
    await flow.tap(t, 'مراجعة مبلغ الاسترداد');
    expect(find.text('تأكيد تسجيل الاسترداد'), findsNothing);
    expect(find.byType(CheckboxListTile), findsNothing);
    await t.pumpWidget(const SizedBox());
    auth.dispose();
  });
  testWidgets('RFN24 stale cash quote cannot remain approved after rejection', (
    t,
  ) async {
    final auth = await authenticate('AcademyOwner', (m, p, b, token) async {
      if (p.endsWith('/quote')) {
        return {
          ...quote(),
          'cashEnabled': true,
          'quoteHash': 'reviewed-hash',
          'sessions': [
            {'id': 'shift', 'name': 'وردية اختبار'},
          ],
        };
      }
      if (m == 'POST') throw const ApiFailure(409, 'تغير رصيد الوردية.');
      return {...flow.source('Cash', false), 'canRecord': true};
    });
    await t.pumpWidget(app(NativePlayerRefund(auth: auth, playerId: 'player')));
    await t.pumpAndSettle();
    await flow.choose(t, 'التحصيل', '2026-10-21 · المتاح 100 ج.م');
    await amount(t, '13.34');
    await flow.tap(t, 'مراجعة مبلغ الاسترداد');
    await flow.choose(t, 'وردية الصرف', 'وردية اختبار');
    await flow.tap(t, 'مراجعة مبلغ الاسترداد');
    await flow.tap(
      t,
      'راجعت الوردية والمبلغ وأؤكد توثيق صرف الاسترداد مرة واحدة.',
    );
    await flow.tap(t, 'تأكيد تسجيل الاسترداد');
    expect(find.byType(CheckboxListTile), findsNothing);
    expect(find.text('تأكيد تسجيل الاسترداد'), findsNothing);
    expect(find.text('تغير رصيد الوردية.'), findsOneWidget);
    await t.pumpWidget(const SizedBox());
    auth.dispose();
  });
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
          ...(data['collections'] as List).first as Map<String, dynamic>,
          'receiptNumber': 'RC-A',
        },
        {
          ...(data['collections'] as List).first as Map<String, dynamic>,
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
      if (fault == 'broken available') {
        data['collections'] = [
          {
            ...(data['collections'] as List).first as Map<String, dynamic>,
            'available': 'bad',
          },
        ];
      }
      if (fault == 'duplicate source') {
        data['collections'] = [
          ...(data['collections'] as List),
          ...(data['collections'] as List),
        ];
      }
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
