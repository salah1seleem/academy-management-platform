import 'package:academy_mobile/core/api/api_client.dart';
import 'package:academy_mobile/features/shared/notification_inbox.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

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
      expect(find.text('1 غير مقروءة'), findsOneWidget);
      await t.tap(find.text('تمت القراءة'));
      await t.pumpAndSettle();
      expect(write, {'isRead': true});
      expect(find.text('0 غير مقروءة'), findsOneWidget);
      await t.tap(find.text('عرض الاشتراك وتعديلاته'));
      await t.pumpAndSettle();
      expect(find.text('باقة الطفل المقصود'), findsOneWidget);
      expect(find.textContaining('إضافة أيام · 2026-10-21'), findsOneWidget);
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
