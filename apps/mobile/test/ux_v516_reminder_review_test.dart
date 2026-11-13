import 'dart:async';

import 'package:academy_mobile/core/api/api_client.dart';
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
      expect(find.text('قيمة التجديد تقديرية.'), findsOneWidget);
      await t.tap(find.byType(CheckboxListTile).first);
      await t.pumpAndSettle();
      await t.tap(find.text('معاينة التنبيه'));
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
      expect(find.text('معاينة التنبيه'), findsOneWidget);
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
    await t.tap(find.text('معاينة التنبيه'));
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

  testWidgets('empty recipients hide composer and preview at 320', (t) async {
    t.view.physicalSize = const Size(320, 720);
    t.view.devicePixelRatio = 1;
    addTearDown(t.view.resetPhysicalSize);
    addTearDown(t.view.resetDevicePixelRatio);
    final auth = await fixture.authenticate('AcademyOwner', (
      m,
      p,
      b,
      token,
    ) async {
      expect(m, 'GET');
      return {'total': 0, 'shown': 0, 'template': 'رسالة', 'items': []};
    });
    await t.pumpWidget(fixture.app(CollectionReminders(auth: auth), scale: 2));
    await t.pumpAndSettle();
    expect(
      find.text('لا توجد أسر متاحة للتنبيه في هذا النطاق.'),
      findsOneWidget,
    );
    expect(find.byType(CheckboxListTile), findsNothing);
    expect(find.byType(TextField), findsNothing);
    expect(find.text('معاينة التنبيه'), findsNothing);
    expect(t.takeException(), isNull);
    await t.pumpWidget(const SizedBox());
    auth.dispose();
  });

  testWidgets(
    'reuses the reviewed request on retry and refreshes stale recipients',
    (t) async {
      final sends = <Object?>[];
      var reads = 0;
      final auth = await fixture.authenticate('AcademyOwner', (
        m,
        p,
        b,
        token,
      ) async {
        if (p.endsWith('/preview')) {
          return {
            'previewHash': 'reviewed',
            'count': 1,
            'messages': [
              {'message': 'نص تمت مراجعته'},
            ],
          };
        }
        if (p.endsWith('/send')) {
          sends.add(b);
          throw sends.length == 1
              ? const ApiFailure(0, 'انقطع الاتصال')
              : const ApiFailure(409, 'تغيّرت بيانات التذكير');
        }
        if (p.contains('/recipients') && ++reads > 1) {
          return {'total': 0, 'shown': 0, 'template': 'رسالة', 'items': []};
        }
        return recipients();
      });
      await t.pumpWidget(fixture.app(CollectionReminders(auth: auth)));
      await t.pumpAndSettle();
      await t.tap(find.byType(CheckboxListTile).first);
      await t.pumpAndSettle();
      await t.tap(find.text('معاينة التنبيه'));
      await t.pumpAndSettle();
      await t.tap(find.text('تأكيد إرسال 1 تنبيه'));
      await t.pumpAndSettle();
      expect(t.widget<TextField>(find.byType(TextField)).enabled, isFalse);
      await t.tap(find.text('إعادة تأكيد الإرسال'));
      await t.pumpAndSettle();
      expect(sends, hasLength(2));
      expect(sends.first, sends.last);
      expect(find.text('نص تمت مراجعته'), findsNothing);
      final refresh = find.text('تحديث قائمة المستلمين');
      await t.ensureVisible(refresh);
      await t.tap(refresh);
      await t.pumpAndSettle();
      expect(
        find.text('لا توجد أسر متاحة للتنبيه في هذا النطاق.'),
        findsOneWidget,
      );
      expect(find.byType(TextField), findsNothing);
      expect(t.takeException(), isNull);
      await t.pumpWidget(const SizedBox());
      auth.dispose();
    },
  );

  testWidgets('a branch change discards the old selection and composer', (
    t,
  ) async {
    final next = Completer<Object?>();
    final auth = await fixture.authenticate('AcademyOwner', (
      m,
      p,
      b,
      token,
    ) async {
      expect(m, 'GET');
      if (p.contains('branchId=other')) return next.future;
      return recipients();
    });
    await t.pumpWidget(
      fixture.app(CollectionReminders(auth: auth, branch: 'first')),
    );
    await t.pumpAndSettle();
    await t.tap(find.byType(CheckboxListTile).first);
    await t.pumpAndSettle();
    expect(find.text('معاينة التنبيه'), findsOneWidget);
    await t.pumpWidget(
      fixture.app(CollectionReminders(auth: auth, branch: 'other')),
    );
    await t.pump();
    expect(find.text('ولي أمر اصطناعي'), findsNothing);
    expect(find.text('معاينة التنبيه'), findsNothing);
    next.complete({'total': 0, 'shown': 0, 'template': 'رسالة', 'items': []});
    await t.pumpAndSettle();
    expect(
      find.text('لا توجد أسر متاحة للتنبيه في هذا النطاق.'),
      findsOneWidget,
    );
    await t.pumpWidget(const SizedBox());
    auth.dispose();
  });
}
