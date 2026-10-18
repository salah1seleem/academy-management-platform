import 'package:academy_mobile/core/api/api_client.dart';
import 'package:academy_mobile/features/shared/admin_media_gallery.dart';
import 'package:academy_mobile/features/shared/private_media.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

import 'owner_test.dart' as f;

void main() {
  testWidgets(
    'Permanent erasure needs confirmation and does not claim physical removal while pending',
    (t) async {
      var erased = false;
      final writes = <Map>[];
      final auth = await f.owner((m, p, b, k) async {
        if (p.endsWith('storage-policy')) {
          return {
            'notice': 'سياسة اصطناعية: الحذف لا يسترجع تنزيلًا سابقًا.',
            'storedBytes': null,
            'workerEnabled': false,
          };
        }
        if (m == 'POST') {
          writes.add(b as Map);
          if (writes.length == 1) {
            throw const ApiFailure(503, 'اختبار انقطاع الرد');
          }
          erased = true;
          return {'erased': true, 'purged': false};
        }
        return [
          {
            'id': 'a',
            'url': '/api/v1/assets/a',
            'caption': 'ملف حذف اصطناعي',
            'contentType': 'video/mp4',
            'canDelete': true,
            'erased': erased,
            'purged': false,
          },
        ];
      });
      await t.pumpWidget(f.shell(AdminMediaGallery(auth: auth)));
      await t.pumpAndSettle();
      final open = find.widgetWithText(OutlinedButton, 'حذف ملف الوسائط');
      await t.scrollUntilVisible(
        open,
        160,
        scrollable: find.byType(Scrollable).first,
      );
      await t.tap(open);
      await t.pumpAndSettle();
      expect(writes, isEmpty);
      final confirm = find.widgetWithText(
        FilledButton,
        'تأكيد حذف الملف نهائيًا',
      );
      await t.ensureVisible(confirm);
      await t.pumpAndSettle();
      expect(
        t.getCenter(confirm).dy,
        lessThan(t.view.physicalSize.height / t.view.devicePixelRatio),
      );
      await t.tap(confirm);
      await t.pumpAndSettle();
      expect(find.textContaining('لم نؤكد الحذف'), findsOneWidget);
      expect(find.textContaining('تم حذف الملف'), findsNothing);
      await t.ensureVisible(confirm);
      await t.pumpAndSettle();
      expect(
        t.getCenter(confirm).dy,
        lessThan(t.view.physicalSize.height / t.view.devicePixelRatio),
      );
      await t.tap(confirm);
      await t.pumpAndSettle();
      expect(writes, hasLength(2));
      expect(writes[0], writes[1]);
      expect(writes[0]['note'], isNull);
      expect(find.textContaining('إزالة الملف معلقة'), findsOneWidget);
      expect(find.text('هذه النسخة غير متاحة للفتح.'), findsOneWidget);
      expect(
        find.widgetWithText(OutlinedButton, 'حذف ملف الوسائط'),
        findsNothing,
      );
      expect(t.takeException(), isNull);
    },
  );
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
      await t.scrollUntilVisible(
        find.byType(DropdownButtonFormField<String>),
        -120,
        scrollable: find.byType(Scrollable).first,
      );
      await t.pumpAndSettle();
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
