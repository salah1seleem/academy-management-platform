import 'package:academy_mobile/core/api/api_client.dart';
import 'package:academy_mobile/features/shared/admin_media_gallery.dart';
import 'package:academy_mobile/features/shared/private_media.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

import 'owner_test.dart' as f;

void main() {
  testWidgets(
    'Session roster is refetched and prior visible-child selection cannot leak into a different session',
    (t) async {
      final paths = <String>[];
      final auth = await f.owner((m, p, b, k) async {
        paths.add(p);
        return p.contains('media-audience')
            ? {
                'eventOn': '2026-10-18',
                'asOfDate': '2026-10-18',
                'notice': 'جمهور اصطناعي',
                'sessions': [
                  {'id': 's', 'startTime': '15:00:00', 'endTime': '16:00:00'},
                ],
                'players': [
                  {
                    'id': p.contains('sessionId=s') ? 'v' : 'p',
                    'name': p.contains('sessionId=s')
                        ? 'ضيف الحصة'
                        : 'عضو المجموعة',
                    'consented': true,
                  },
                ],
              }
            : [];
      });
      await t.pumpWidget(f.shell(UploadSessionMedia(auth: auth, groupId: 'g')));
      await t.pumpAndSettle();
      await t.scrollUntilVisible(
        find.widgetWithText(CheckboxListTile, 'عضو المجموعة'),
        120,
        scrollable: find.byType(Scrollable).first,
      );
      await t.tap(find.widgetWithText(CheckboxListTile, 'عضو المجموعة'));
      await t.pumpAndSettle();
      await t.ensureVisible(find.byType(DropdownButtonFormField<String>));
      await t.tap(find.byType(DropdownButtonFormField<String>));
      await t.pumpAndSettle();
      await t.tap(find.text('15:00 — 16:00').last);
      await t.pumpAndSettle();
      await t.scrollUntilVisible(
        find.widgetWithText(CheckboxListTile, 'ضيف الحصة'),
        120,
        scrollable: find.byType(Scrollable).first,
      );
      expect(
        t
            .widget<CheckboxListTile>(
              find.widgetWithText(CheckboxListTile, 'ضيف الحصة'),
            )
            .value,
        false,
      );
      expect(find.text('عضو المجموعة'), findsNothing);
      expect(paths.any((p) => p.contains('sessionId=s')), true);
      expect(t.takeException(), isNull);
    },
  );
  testWidgets(
    'Administrative withdrawal is explicit, optional-note retry reuses intent, and saved status is reread',
    (t) async {
      var withdrawn = false;
      final writes = <Map>[];
      final auth = await f.owner((m, p, b, k) async {
        if (m == 'POST') {
          writes.add(b as Map);
          if (writes.length == 1) {
            throw const ApiFailure(503, 'اختبار انقطاع رد');
          }
          withdrawn = true;
          return {'id': 'w'};
        }
        return [
          {
            'id': 'a',
            'url': '/api/v1/assets/a',
            'contentType': 'video/mp4',
            'caption': 'وسيط اصطناعي',
            'canWithdraw': true,
            'withdrawn': withdrawn,
          },
        ];
      });
      await t.pumpWidget(f.shell(AdminMediaGallery(auth: auth)));
      await t.pumpAndSettle();
      final open = find.widgetWithText(OutlinedButton, 'سحب النشر');
      await t.scrollUntilVisible(
        open,
        160,
        scrollable: find.byType(Scrollable).first,
      );
      await t.tap(open);
      await t.pumpAndSettle();
      expect(writes, isEmpty);
      final back = find.widgetWithText(TextButton, 'رجوع');
      await t.ensureVisible(back);
      await t.tap(back);
      await t.pumpAndSettle();
      expect(writes, isEmpty);
      await t.tap(open);
      await t.pumpAndSettle();
      final confirm = find.widgetWithText(FilledButton, 'تأكيد سحب النشر');
      await t.ensureVisible(confirm);
      await t.tap(confirm);
      await t.pumpAndSettle();
      expect(find.textContaining('لم نؤكد حفظ السحب'), findsOneWidget);
      expect(withdrawn, false);
      await t.ensureVisible(confirm);
      await t.tap(confirm);
      await t.pumpAndSettle();
      expect(writes.length, 2);
      expect(writes.first, writes.last);
      expect(writes.first['note'], isNull);
      await t.scrollUntilVisible(
        find.text('مسحوبة من الأسر والمدرب'),
        160,
        scrollable: find.byType(Scrollable).first,
      );
      expect(open, findsNothing);
      t.binding.handleAppLifecycleStateChanged(AppLifecycleState.inactive);
      await t.pumpAndSettle();
      expect(find.text('وسيط اصطناعي'), findsNothing);
      t.binding.handleAppLifecycleStateChanged(AppLifecycleState.resumed);
      await t.pumpAndSettle();
      await auth.logout();
      await t.pumpAndSettle();
      expect(find.text('وسيط اصطناعي'), findsNothing);
      expect(t.takeException(), isNull);
    },
  );
}
