import 'package:academy_mobile/features/shared/media_memories.dart';
import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_test/flutter_test.dart';

import 'owner_test.dart' as f;

void main() {
  testWidgets(
    'metadata index needs explicit copy and failure or background never leaves a false success',
    (t) async {
      var allow = false;
      String? copied;
      final auth = await f.owner((m, p, b, k) async {
        if (p.contains('format=text')) {
          if (!allow) {
            throw StateError('Synthetic denial');
          }
          return {'text': 'فهرس اصطناعي بلا روابط أو ملفات'};
        }
        return {
          'month': '2026-10',
          'from': '2026-10-01',
          'to': '2026-10-18',
          'asOfDate': '2026-10-18',
          'total': 0,
          'images': 0,
          'videos': 0,
          'items': [],
          'hasMore': false,
          'notice': 'الموافقة والنطاق يعاد فحصهما.',
        };
      });
      TestDefaultBinaryMessengerBinding.instance.defaultBinaryMessenger
          .setMockMethodCallHandler(SystemChannels.platform, (call) async {
            if (call.method == 'Clipboard.setData') {
              copied = (call.arguments as Map)['text'] as String;
            }
            return null;
          });
      addTearDown(
        () => TestDefaultBinaryMessengerBinding.instance.defaultBinaryMessenger
            .setMockMethodCallHandler(SystemChannels.platform, null),
      );
      await t.pumpWidget(
        f.shell(MediaMemoriesPage(auth: auth, playerId: 'child')),
      );
      await t.pumpAndSettle();
      expect(copied, isNull);
      final button = find.widgetWithText(
        OutlinedButton,
        'نسخ فهرس الشهر دون الملفات',
      );
      await t.scrollUntilVisible(
        button,
        120,
        scrollable: find.byType(Scrollable).first,
      );
      await t.tap(button);
      await t.pumpAndSettle();
      expect(find.textContaining('لم نؤكد نسخ الفهرس'), findsOneWidget);
      expect(copied, isNull);
      allow = true;
      await t.ensureVisible(button);
      await t.pumpAndSettle();
      await t.tap(button);
      await t.pumpAndSettle();
      expect(copied, 'فهرس اصطناعي بلا روابط أو ملفات');
      expect(find.textContaining('تم نسخ فهرس الشهر'), findsOneWidget);
      t.binding.handleAppLifecycleStateChanged(AppLifecycleState.inactive);
      await t.pump();
      expect(find.textContaining('تم نسخ فهرس الشهر'), findsNothing);
    },
  );
}
