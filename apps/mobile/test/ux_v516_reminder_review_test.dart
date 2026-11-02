import 'dart:async';

import 'package:academy_mobile/features/owner/collection_reminders.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

import 'v2_role_workspaces_test.dart' as fixture;

Map<String, dynamic> recipients() => {
  'total': 1,
  'shown': 1,
  'template': 'راجع الاشتراك القادم من فضلك.',
  'items': [
    {
      'id': 'guardian-one',
      'name': 'ولي أمر اصطناعي',
      'children': [
        {'name': 'طفل اصطناعي'},
      ],
      'amount': 100,
      'currency': 'EGP',
    },
  ],
};

void main() {
  testWidgets(
    'preview never sends and changing message requires a new review',
    (t) async {
      final writes = <String>[];
      final auth = await fixture.authenticate('AcademyOwner', (
        m,
        p,
        b,
        token,
      ) async {
        if (m == 'POST') {
          writes.add(p);
          expect((b as Map)['recipients'], ['guardian-one']);
          return {
            'previewHash': 'saved-review',
            'count': 1,
            'messages': [
              {'message': 'نص المعاينة المحفوظ'},
            ],
          };
        }
        return recipients();
      });
      await t.pumpWidget(
        fixture.app(CollectionReminders(auth: auth, kind: 'Renewal')),
      );
      await t.pumpAndSettle();
      expect(
        find.text('قيمة التجديد المتوقعة ليست دينًا قائمًا.'),
        findsOneWidget,
      );
      await t.tap(find.byType(CheckboxListTile).first);
      await t.pumpAndSettle();
      await t.tap(find.text('معاينة 1 تنبيه'));
      await t.pumpAndSettle();
      expect(writes, ['/api/v1/manage/notifications/preview']);
      expect(find.text('تأكيد إرسال 1 تنبيه'), findsOneWidget);
      await t.ensureVisible(find.byType(TextField));
      await t.pumpAndSettle();
      await t.enterText(
        find.byType(TextField),
        'رسالة معدلة تحتاج معاينة جديدة',
      );
      await t.pumpAndSettle();
      expect(find.text('تأكيد إرسال 1 تنبيه'), findsNothing);
      expect(find.text('معاينة 1 تنبيه'), findsOneWidget);
      expect(writes.any((p) => p.endsWith('/send')), false);
      expect(t.takeException(), isNull);
      await t.pumpWidget(const SizedBox());
      auth.dispose();
    },
  );

  testWidgets('a late reminder preview disappears when the account logs out', (
    t,
  ) async {
    final pending = Completer<Object?>();
    var writes = 0;
    final auth = await fixture.authenticate('AcademyOwner', (m, p, b, token) {
      if (m == 'POST') {
        writes++;
        return pending.future;
      }
      return Future.value(recipients());
    });
    await t.pumpWidget(fixture.app(CollectionReminders(auth: auth)));
    await t.pumpAndSettle();
    await t.tap(find.byType(CheckboxListTile).first);
    await t.pumpAndSettle();
    await t.tap(find.text('معاينة 1 تنبيه'));
    await t.pump();
    expect(writes, 1);
    await auth.logout();
    await t.pump();
    pending.complete({
      'previewHash': 'old',
      'count': 1,
      'messages': [
        {'message': 'تفاصيل الأسرة القديمة'},
      ],
    });
    await t.pumpAndSettle();
    expect(find.text('تفاصيل الأسرة القديمة'), findsNothing);
    expect(find.text('ولي أمر اصطناعي'), findsNothing);
    expect(find.textContaining('تأكيد إرسال'), findsNothing);
    expect(writes, 1);
    expect(t.takeException(), isNull);
    await t.pumpWidget(const SizedBox());
    auth.dispose();
  });
}
