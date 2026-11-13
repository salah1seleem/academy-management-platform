import 'dart:async';

import 'package:academy_mobile/features/shared/player_monthly.dart';
import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_test/flutter_test.dart';

import 'owner_test.dart' as f;

Future<void> reveal(
  WidgetTester tester,
  Finder target, {
  double delta = 180,
}) async {
  if (target.evaluate().isEmpty) {
    await tester.scrollUntilVisible(
      target,
      delta,
      scrollable: find.byType(Scrollable).first,
    );
  }
  await Scrollable.ensureVisible(tester.element(target), alignment: .45);
  await tester.pumpAndSettle();
  expect(target.hitTestable(), findsOneWidget);
}

void main() {
  final data = {
    'enrollment': {'id': 'e', 'player': 'لاعب اصطناعي', 'sport': 'كرة القدم'},
    'enrollments': [
      {'id': 'e', 'sport': 'كرة القدم', 'branch': 'فرع اصطناعي'},
    ],
    'month': '2026-10',
    'from': '2026-10-01',
    'to': '2026-10-18',
    'asOfDate': '2026-10-18',
    'counts': {'skills': 0, 'badges': 1, 'certificates': 0},
    'notice': 'أدلة منشورة فقط، لا مال أو صور.',
    'hasMore': false,
    'items': [
      {
        'id': 'b',
        'kind': 'Badge',
        'title': 'روح التعاون',
        'on': '2026-10-18',
        'state': 'Revoked',
        'value': 10,
        'unit': 'Encouragement',
        'actor': 'مدرب اصطناعي',
      },
    ],
  };
  testWidgets(
    'month evidence copy is explicit and complete, source stays private, failure never claims clipboard success',
    (t) async {
      var fail = true;
      final requests = <String>[];
      final auth = await f.owner((m, p, b, k) async {
        requests.add(p);
        if (p.contains('format=text')) {
          if (fail) throw StateError('Synthetic denial');
          return {'text': 'تقرير خاص كامل، لا صفحة فقط'};
        }
        return data;
      });
      String? clipboard;
      TestDefaultBinaryMessengerBinding.instance.defaultBinaryMessenger
          .setMockMethodCallHandler(SystemChannels.platform, (call) async {
            if (call.method == 'Clipboard.setData') {
              clipboard = (call.arguments as Map)['text'] as String;
            }
            return null;
          });
      addTearDown(
        () => TestDefaultBinaryMessengerBinding.instance.defaultBinaryMessenger
            .setMockMethodCallHandler(SystemChannels.platform, null),
      );
      await t.pumpWidget(
        f.shell(PlayerMonthlyPage(auth: auth, playerId: 'child')),
      );
      await t.pumpAndSettle();
      expect(find.textContaining('شارة مسحوبة'), findsOneWidget);
      expect(clipboard, isNull);
      final copy = find.widgetWithText(
        OutlinedButton,
        'نسخ تقرير الشهر كاملًا',
      );
      await reveal(t, copy);
      await t.tap(copy);
      await t.pumpAndSettle();
      expect(find.textContaining('تعذر تأكيد التصدير'), findsOneWidget);
      expect(clipboard, isNull);
      fail = false;
      await reveal(t, copy);
      await t.tap(copy);
      await t.pumpAndSettle();
      expect(clipboard, 'تقرير خاص كامل، لا صفحة فقط');
      expect(
        requests.where((p) => p.contains('format=text')),
        everyElement(contains('month=2026-10')),
      );
      t.binding.handleAppLifecycleStateChanged(AppLifecycleState.inactive);
      await t.pump();
      expect(find.textContaining('روح التعاون'), findsNothing);
      t.binding.handleAppLifecycleStateChanged(AppLifecycleState.paused);
      await t.pump();
      expect(find.textContaining('روح التعاون'), findsNothing);
    },
  );

  testWidgets(
    'page navigation keeps the month and selected sport, and export ignores the page',
    (t) async {
      final reads = <Uri>[];
      final auth = await f.owner((method, path, body, token) async {
        final uri = Uri.parse(path);
        reads.add(uri);
        final page = uri.queryParameters['page'];
        final month = uri.queryParameters['month'] ?? '2026-10';
        if (uri.queryParameters['format'] == 'text') {
          return {'text': 'تقرير الشهر كاملًا'};
        }
        return {
          ...data,
          'month': month,
          'hasMore': page == '0' && month == '2026-10',
          'items': [
            {
              'id': 'b',
              'kind': 'Badge',
              'title': page == '1'
                  ? 'شارة الصفحة الثانية'
                  : 'شارة الصفحة الأولى',
              'on': '2026-10-18',
              'state': 'Granted',
              'actor': 'مدرب اصطناعي',
            },
          ],
        };
      });
      TestDefaultBinaryMessengerBinding.instance.defaultBinaryMessenger
          .setMockMethodCallHandler(
            SystemChannels.platform,
            (call) async => null,
          );
      addTearDown(
        () => TestDefaultBinaryMessengerBinding.instance.defaultBinaryMessenger
            .setMockMethodCallHandler(SystemChannels.platform, null),
      );
      await t.pumpWidget(
        f.shell(PlayerMonthlyPage(auth: auth, playerId: 'child')),
      );
      await t.pumpAndSettle();
      await reveal(t, find.text('الصفحة التالية'));
      expect(reads.where((uri) => uri.queryParameters['page'] == '1'), isEmpty);
      await t.tap(find.text('الصفحة التالية'));
      await t.pumpAndSettle();
      expect(reads.last.queryParameters, {
        'page': '1',
        'month': '2026-10',
        'enrollmentId': 'e',
      });
      expect(find.text('شارة الصفحة الثانية'), findsOneWidget);
      expect(find.text('شارة الصفحة الأولى'), findsNothing);
      final copy = find.text('نسخ تقرير الشهر كاملًا');
      await reveal(t, copy);
      await t.tap(copy);
      await t.pumpAndSettle();
      expect(reads.last.queryParameters, {
        'month': '2026-10',
        'enrollmentId': 'e',
        'format': 'text',
      });
      await reveal(t, find.text('الشهر السابق'), delta: -180);
      await t.tap(find.text('الشهر السابق'));
      await t.pumpAndSettle();
      expect(reads.last.queryParameters, {
        'page': '0',
        'month': '2026-09',
        'enrollmentId': 'e',
      });
      expect(t.takeException(), isNull);
    },
  );

  testWidgets(
    'an export started before backgrounding cannot copy after resume',
    (t) async {
      final result = Completer<Map<String, Object?>>();
      var requested = false;
      final auth = await f.owner((method, path, body, token) async {
        if (!path.contains('format=text')) return data;
        requested = true;
        return result.future;
      });
      var copies = 0;
      TestDefaultBinaryMessengerBinding.instance.defaultBinaryMessenger
          .setMockMethodCallHandler(SystemChannels.platform, (call) async {
            if (call.method == 'Clipboard.setData') copies++;
            return null;
          });
      addTearDown(
        () => TestDefaultBinaryMessengerBinding.instance.defaultBinaryMessenger
            .setMockMethodCallHandler(SystemChannels.platform, null),
      );
      await t.pumpWidget(
        f.shell(PlayerMonthlyPage(auth: auth, playerId: 'child')),
      );
      await t.pumpAndSettle();
      final copy = find.text('نسخ تقرير الشهر كاملًا');
      await reveal(t, copy);
      await t.tap(copy);
      await t.pump();
      expect(requested, isTrue);
      t.binding.handleAppLifecycleStateChanged(AppLifecycleState.inactive);
      await t.pump();
      t.binding.handleAppLifecycleStateChanged(AppLifecycleState.resumed);
      result.complete({'text': 'محتوى خاص'});
      await t.pumpAndSettle();
      expect(copies, 0);
      expect(t.takeException(), isNull);
    },
  );
}
