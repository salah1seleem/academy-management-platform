import 'package:academy_mobile/features/shared/player_monthly.dart';
import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_test/flutter_test.dart';

import 'owner_test.dart' as f;

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
      await t.scrollUntilVisible(
        copy,
        120,
        scrollable: find.byType(Scrollable).first,
      );
      await t.tap(copy);
      await t.pumpAndSettle();
      expect(find.textContaining('تعذر تأكيد التصدير'), findsOneWidget);
      expect(clipboard, isNull);
      fail = false;
      await t.ensureVisible(copy);
      await t.pumpAndSettle();
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
}
