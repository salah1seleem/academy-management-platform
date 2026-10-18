import 'package:academy_mobile/features/shared/assigned_followups.dart';
import 'package:academy_mobile/core/api/api_client.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

import 'owner_test.dart' as fixture;

Map<String, dynamic> item() => {
  'id': 'task',
  'version': 4,
  'state': 'Assigned',
  'condition': 'GroupLowOccupancy',
  'conditionActive': true,
  'dueOn': '2026-10-18',
  'note': 'ملاحظة اصطناعية',
  'source': {
    'name': 'مجموعة العرض',
    'branch': 'فرع التجربة',
    'sport': 'كرة القدم',
    'enrollments': 3,
    'capacity': 20,
  },
};
void main() {
  testWidgets(
    'detail posts delegated state and version without changing assignee; closure becomes read only',
    (t) async {
      var saved = false;
      Map<String, dynamic>? sent;
      final auth = await fixture.owner((m, p, b, k) async {
        if (m == 'POST') {
          sent = b as Map<String, dynamic>;
          saved = true;
          return {'id': 'task'};
        }
        return {
          'item': {
            ...item(),
            'state': saved ? 'Closed' : 'Assigned',
            'version': saved ? 5 : 4,
          },
          'canEdit': !saved,
          'history': [],
        };
      });
      await t.pumpWidget(
        fixture.shell(AssignedFollowUpsPage(auth: auth, id: 'task')),
      );
      await t.pumpAndSettle();
      expect(find.text('إشغال أقل من نصف السعة'), findsOneWidget);
      expect(find.text('تسجيلات فعالة مسندة: 3، السعة: 20'), findsOneWidget);
      await t.ensureVisible(find.text('حفظ تحديث المتابعة'));
      await t.pumpAndSettle();
      expect(t.getCenter(find.text('حفظ تحديث المتابعة')).dy, lessThan(600));
      await t.tap(find.text('حفظ تحديث المتابعة'));
      await t.pumpAndSettle();
      expect(sent, containsPair('state', 'InProgress'));
      expect(sent, containsPair('version', 4));
      expect(sent!.containsKey('assignedToMembershipId'), false);
      expect(sent!['requestId'], matches(RegExp(r'^[a-f0-9-]{36}$')));
      expect(find.text('حفظ تحديث المتابعة'), findsNothing);
      expect(find.textContaining('للقراءة فقط'), findsOneWidget);
      expect(t.takeException(), isNull);
    },
  );
  testWidgets(
    'clear removes date defaults and uses allDates instead of restoring today',
    (t) async {
      final calls = <String>[];
      final auth = await fixture.owner((m, p, b, k) async {
        calls.add(p);
        return {
          'from': '2026-10-01',
          'to': '2026-10-17',
          'items': [item()],
        };
      });
      await t.pumpWidget(fixture.shell(AssignedFollowUpsPage(auth: auth)));
      await t.pumpAndSettle();
      await t.tap(find.text('مسح الفلاتر'));
      await t.pumpAndSettle();
      expect(calls.last, contains('allDates=true'));
      expect(find.text('من تاريخ: الكل'), findsOneWidget);
      expect(find.text('إلى تاريخ: الكل'), findsOneWidget);
    },
  );
  for (final width in [360.0, 390.0, 430.0]) {
    testWidgets(
      'scoped read-only detail at $width with 200% text has no edit or assignment actions',
      (t) async {
        t.view.physicalSize = Size(width, 1100);
        t.view.devicePixelRatio = 1;
        addTearDown(t.view.reset);
        final auth = await fixture.owner(
          (m, p, b, k) async => {
            'item': item(),
            'canEdit': false,
            'history': [],
          },
        );
        await t.pumpWidget(
          fixture.shell(
            MediaQuery(
              data: MediaQueryData(
                size: Size(width, 1100),
                textScaler: const TextScaler.linear(2),
              ),
              child: AssignedFollowUpsPage(auth: auth, id: 'task'),
            ),
          ),
        );
        await t.pumpAndSettle();
        expect(find.text('مجموعة العرض'), findsOneWidget);
        expect(find.text('حفظ تحديث المتابعة'), findsNothing);
        expect(t.takeException(), isNull);
      },
    );
  }
  testWidgets('failed recheck hides previously visible task on app resume', (
    t,
  ) async {
    var revoked = false;
    final auth = await fixture.owner((m, p, b, k) async {
      if (revoked) throw const ApiFailure(404, 'غير متاح');
      return {'item': item(), 'canEdit': true, 'history': []};
    });
    await t.pumpWidget(
      fixture.shell(AssignedFollowUpsPage(auth: auth, id: 'task')),
    );
    await t.pumpAndSettle();
    revoked = true;
    t.binding.handleAppLifecycleStateChanged(AppLifecycleState.resumed);
    await t.pumpAndSettle();
    expect(find.text('مجموعة العرض'), findsNothing);
    expect(find.textContaining('المتابعات غير متاحة'), findsOneWidget);
  });
}
