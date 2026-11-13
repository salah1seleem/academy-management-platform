import 'package:academy_mobile/core/api/api_client.dart';
import 'package:academy_mobile/features/shared/notification_inbox.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:intl/intl.dart';

import 'v2_role_workspaces_test.dart' show authenticate, app;

void main() {
  testWidgets(
    'Day notice toggles stored unread state and opens exactly its authorized subscription period',
    (t) async {
      var read = false;
      final calls = <String>[];
      Map? write;
      final auth = await authenticate('Guardian', (m, p, b, token) async {
        calls.add(p);
        if (m == 'PUT') {
          write = b as Map;
          read = b['isRead'] == true;
          return null;
        }
        if (p.contains('/notifications')) {
          return {
            'unread': read ? 0 : 1,
            'total': 1,
            'items': [
              {
                'id': 'notice',
                'message': 'أضيف يومان لاشتراك لاعب اصطناعي',
                'isRead': read,
                'createdAtUtc': '2026-10-21',
                'targetKind': 'SubscriptionPeriod',
                'targetId': 'my-period',
              },
            ],
          };
        }
        if (p ==
            '/api/v1/guardian/subscriptions/periods/my-period/adjustments') {
          return {
            'plan': 'باقة الطفل المقصود',
            'status': 'Active',
            'startDate': '2026-10-01',
            'endDate': '2026-11-02',
            'remainingSessions': 4,
            'adjustments': [
              {
                'type': 'DaysAdded',
                'effectiveDate': '2026-10-21',
                'daysDelta': 2,
                'newEndDate': '2026-11-02',
              },
            ],
          };
        }
        return {};
      });
      await t.pumpWidget(app(NotificationInbox(auth: auth)));
      await t.pumpAndSettle();
      expect(find.text('غير المقروءة 1'), findsOneWidget);
      expect(find.textContaining(RegExp('2026|٢٠٢٦')), findsOneWidget);
      await t.tap(find.text('تحديد كمقروء'));
      await t.pumpAndSettle();
      expect(write, {'isRead': true});
      expect(find.text('غير المقروءة 0'), findsOneWidget);
      expect(
        calls.where((p) => p == '/api/v1/notifications?page=1'),
        hasLength(1),
      );
      await t.tap(find.text('عرض الاشتراك وتعديلاته'));
      await t.pumpAndSettle();
      expect(find.text('باقة الطفل المقصود'), findsOneWidget);
      expect(find.text('إضافة أيام'), findsOneWidget);
      final date = DateFormat.yMMMd('ar')..useNativeDigits = false;
      expect(find.text(date.format(DateTime(2026, 10, 21))), findsOneWidget);
      expect(
        calls,
        contains(
          '/api/v1/guardian/subscriptions/periods/my-period/adjustments',
        ),
      );
      expect(
        calls.any((p) => p.contains('renewals') || p.contains('manage/')),
        isFalse,
      );
      expect(t.takeException(), isNull);
      await t.pumpWidget(const SizedBox());
      auth.dispose();
    },
  );
  testWidgets(
    'A lost read reply is recovered by checking the list at large text size',
    (t) async {
      t.view.physicalSize = const Size(360, 800);
      t.view.devicePixelRatio = 1;
      addTearDown(t.view.resetPhysicalSize);
      addTearDown(t.view.resetDevicePixelRatio);
      var saved = false, writes = 0, reads = 0;
      final auth = await authenticate('Guardian', (
        method,
        path,
        body,
        _,
      ) async {
        if (method == 'PUT') {
          writes++;
          saved = (body as Map)['isRead'] == true;
          throw const ApiFailure(503, 'انقطع الاتصال بعد الحفظ.');
        }
        reads++;
        return {
          'unread': saved ? 0 : 1,
          'total': 1,
          'items': [
            {
              'id': 'notice',
              'message': 'تحديث اصطناعي لاختبار القراءة\nتفاصيل التحديث في السطر التالي.',
              'isRead': saved,
              'createdAtUtc': '2026-10-22T08:00:00Z',
              'targetKind': 'SubscriptionPeriod',
              'targetId': 'period',
            },
          ],
        };
      });
      await t.pumpWidget(app(NotificationInbox(auth: auth), scale: 2));
      await t.pumpAndSettle();
      expect(find.text('غير المقروءة 1'), findsOneWidget);
      final mark = find.text('تحديد كمقروء');
      await t.ensureVisible(mark);
      await t.pumpAndSettle();
      await t.tap(mark);
      await t.pumpAndSettle();
      expect(mark, findsOneWidget);
      final refresh = find.text('تحديث القائمة');
      await t.ensureVisible(refresh);
      await t.pumpAndSettle();
      await t.tap(refresh);
      await t.pumpAndSettle();
      expect(find.text('غير المقروءة 0'), findsOneWidget);
      expect(find.text('تحديد كغير مقروء'), findsOneWidget);
      expect((writes, reads), (1, 2));
      expect(t.takeException(), isNull);
      await t.pumpWidget(const SizedBox());
      auth.dispose();
    },
  );
  testWidgets(
    'Absence notice opens the named child attendance and denied access never shows another family record',
    (t) async {
      final calls = <String>[];
      final auth = await authenticate('Guardian', (m, p, b, token) async {
        calls.add(p);
        if (p.contains('/notifications')) {
          return {
            'unread': 1,
            'total': 1,
            'items': [
              {
                'id': 'notice',
                'message': 'سجل غياب طفل اصطناعي يوم 2026-10-21',
                'isRead': false,
                'createdAtUtc': '2026-10-21',
                'targetKind': 'Attendance',
                'targetId': 'linked-child',
              },
            ],
          };
        }
        throw const ApiFailure(403, 'لا توجد صلاحية لهذا السجل.');
      });
      await t.pumpWidget(app(NotificationInbox(auth: auth)));
      await t.pumpAndSettle();
      await t.tap(find.text('عرض سجل الحضور'));
      await t.pumpAndSettle();
      expect(calls, contains('/api/v1/guardian/children/linked-child/profile'));
      expect(find.text('لا توجد صلاحية لهذا السجل.'), findsWidgets);
      expect(find.text('طفل أسرة أخرى'), findsNothing);
      expect(t.takeException(), isNull);
      await t.pumpWidget(const SizedBox());
      auth.dispose();
    },
  );
}
