import 'dart:async';

import 'package:academy_mobile/core/api/api_client.dart';
import 'package:academy_mobile/core/auth/auth_controller.dart';
import 'package:academy_mobile/features/shared/notification_inbox.dart';
import 'package:academy_mobile/features/shared/vip_rewards.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

import 'owner_test.dart' show owner, shell;
import 'v2_role_workspaces_test.dart' show authenticate;

const terms = {
  'today': '2026-09-28',
  'currency': 'EGP',
  'conditions': 'باقة واحدة دون جمع',
  'items': [
    {'id': 'g', 'name': 'أسرة اصطناعية', 'monthlyValue': 1000},
  ],
};
Map<String, dynamic> preview(Map b) => {
  'quoteHash': 'hash',
  'recipients': terms['items'],
  'currency': 'EGP',
  'percentage': b['percentage'],
  'message': b['message'],
  'expiresOn': b['expiresOn'],
  'delivery': 'إشعار داخلي فقط',
};
Map<String, dynamic> issued(Map b, {num? percentage}) => {
  'items': [
    {
      'id': 'reward',
      'guardianId': 'g',
      'requestId': b['requestId'],
      'currency': 'EGP',
      'percentage': percentage ?? b['percentage'],
      'message': b['message'],
      'expiresOn': b['expiresOn'],
      'state': 'Available',
    },
  ],
};
Future<void> reveal(WidgetTester t, Finder f) async {
  FocusManager.instance.primaryFocus?.unfocus();
  await t.pumpAndSettle();
  final scroll = find.byType(Scrollable).first;
  t.state<ScrollableState>(scroll).position.jumpTo(0);
  await t.pumpAndSettle();
  await t.scrollUntilVisible(f, 150, scrollable: scroll, maxScrolls: 100);
  await t.ensureVisible(f.first);
  await t.pumpAndSettle();
}

Future<void> tapVisible(WidgetTester t, String text) async {
  final f = find.text(text);
  await reveal(t, f);
  await t.tap(f.first);
  await t.pumpAndSettle();
}

Future<void> prepare(WidgetTester t, AuthController auth) async {
  await t.pumpWidget(
    shell(
      Builder(
        builder: (context) => Scaffold(
          body: Column(
            children: [
              const Text('السجل الأصلي'),
              ElevatedButton(
                onPressed: () => Navigator.of(context).push(
                  MaterialPageRoute<void>(
                    builder: (_) => NativeVipIssue(auth: auth),
                  ),
                ),
                child: const Text('فتح الإصدار'),
              ),
            ],
          ),
        ),
      ),
    ),
  );
  await t.tap(find.text('فتح الإصدار'));
  await t.pumpAndSettle();
  await t.tap(find.byType(CheckboxListTile));
  await t.enterText(find.byType(TextField).first, '20');
  await tapVisible(t, 'صالحة حتى');
  await t.tap(
    find.text(
      MaterialLocalizations.of(t.element(find.byType(DatePickerDialog)))
          .okButtonLabel,
    ),
  );
  await t.pumpAndSettle();
  await tapVisible(t, 'معاينة المكافأة');
  await reveal(t, find.text('تأكيد الإصدار'));
  expect(find.text('تأكيد الإصدار'), findsOneWidget);
}

void main() {
  testWidgets(
    'VIP exact reward page never renders another record from a mismatched response',
    (t) async {
      final auth = await authenticate(
        'Guardian',
        (m, p, b, token) async => {
          'count': 1,
          'items': [
            {
              'id': 'wrong-reward',
              'message': 'بيانات مكافأة أخرى لا تعرض',
              'percentage': 20,
              'state': 'Available',
              'history': [],
            },
          ],
        },
      );
      await t.pumpWidget(
        shell(NativeVipRewards(auth: auth, rewardId: 'exact-reward')),
      );
      await t.pumpAndSettle();
      expect(
        find.textContaining('تعذر التحقق من المكافأة المحددة'),
        findsOneWidget,
      );
      expect(find.text('بيانات مكافأة أخرى لا تعرض'), findsNothing);
      await t.pumpWidget(const SizedBox());
      auth.dispose();
    },
  );
  testWidgets(
    'VIP definitive cancellation rejection refreshes state without a false pending success',
    (t) async {
      var rejected = false;
      var writes = 0;
      final auth = await owner((m, p, b, token) async {
        if (m == 'POST') {
          writes++;
          rejected = true;
          throw const ApiFailure(409, 'المكافأة محجوزة لطلب قائم.');
        }
        return {
          'count': 1,
          'items': [
            {
              'id': 'reward',
              'message': 'مكافأة محفوظة',
              'percentage': 20,
              'expiresOn': '2026-10-28',
              'state': rejected ? 'Reserved' : 'Available',
              'history': [],
            },
          ],
        };
      });
      await t.pumpWidget(shell(NativeVipRewards(auth: auth, owner: true)));
      await t.pumpAndSettle();
      await tapVisible(t, 'إلغاء المكافأة');
      await t.tap(find.text('تأكيد الإلغاء'));
      await t.pumpAndSettle();
      expect(find.text('المكافأة محجوزة لطلب قائم.'), findsOneWidget);
      expect(find.textContaining('محجوزة لطلب ·'), findsOneWidget);
      expect(find.text('إلغاء المكافأة'), findsNothing);
      expect(find.text('إعادة التحقق من الإلغاء'), findsNothing);
      expect(writes, 1);
      await t.pumpWidget(const SizedBox());
      auth.dispose();
    },
  );
  testWidgets(
    'VIP absent attempt permits only explicit identical retry after GET404',
    (t) async {
      final bodies = <Map<String, dynamic>>[];
      var missing = true;
      final auth = await owner((m, p, b, token) async {
        if (p.endsWith('/options')) return terms;
        if (p.endsWith('/preview')) return preview(b as Map);
        if (m == 'POST') {
          bodies.add(Map<String, dynamic>.from(b as Map));
          if (bodies.length == 1) throw const ApiFailure(503, 'لم يصل الطلب.');
          missing = false;
          return {
            'ids': ['reward'],
          };
        }
        if (p.contains('/requests/')) {
          if (missing) throw const ApiFailure(404, 'لا توجد محاولة محفوظة.');
          return {
            'requestId': bodies.first['requestId'],
            'ids': ['reward'],
          };
        }
        return issued(bodies.first);
      });
      await prepare(t, auth);
      await tapVisible(t, 'تأكيد الإصدار وحفظ الإشعار الداخلي');
      expect(bodies, hasLength(1));
      await tapVisible(t, 'إعادة التحقق من الإصدار');
      expect(bodies, hasLength(1));
      await tapVisible(t, 'إعادة تأكيد الطلب نفسه');
      expect(bodies, hasLength(2));
      expect(bodies.last, bodies.first);
      expect(find.text('السجل الأصلي'), findsOneWidget);
      await t.pumpWidget(const SizedBox());
      auth.dispose();
    },
  );
  testWidgets(
    'VIP revoked issuance authority redacts the old form without another write',
    (t) async {
      var writes = 0;
      final auth = await owner((m, p, b, token) async {
        if (p.endsWith('/options')) return terms;
        if (p.endsWith('/preview')) return preview(b as Map);
        writes++;
        throw const ApiFailure(403, 'سحبت الصلاحية.');
      });
      await prepare(t, auth);
      await tapVisible(t, 'تأكيد الإصدار وحفظ الإشعار الداخلي');
      expect(writes, 1);
      expect(find.text('أسرة اصطناعية'), findsNothing);
      expect(find.byType(TextField), findsNothing);
      expect(find.text('إعادة التحقق من الإصدار'), findsNothing);
      await t.pumpWidget(const SizedBox());
      auth.dispose();
    },
  );
  for (final lostResponse in [false, true]) {
    testWidgets(
      'VIP $lostResponse loss recovers by GET with frozen intent and one issue',
      (t) async {
        final calls = <String>[];
        Map<String, dynamic>? body;
        var readFails = !lostResponse;
        var writes = 0;
        final auth = await owner((m, p, b, token) async {
          calls.add('$m $p');
          if (p.endsWith('/options')) return terms;
          if (p.endsWith('/preview')) return preview(b as Map);
          if (m == 'POST') {
            writes++;
            body = Map<String, dynamic>.from(b as Map);
            if (lostResponse) {
              throw const ApiFailure(503, 'انقطع الرد بعد الحفظ.');
            }
            return {
              'ids': ['reward'],
            };
          }
          if (p.contains('/requests/')) {
            return {
              'requestId': body!['requestId'],
              'ids': ['reward'],
            };
          }
          if (readFails) throw const ApiFailure(503, 'تعذر التحقق مؤقتًا.');
          return issued(body!);
        });
        await prepare(t, auth);
        await tapVisible(t, 'تأكيد الإصدار وحفظ الإشعار الداخلي');
        expect(find.text('السجل الأصلي'), findsNothing);
        await reveal(t, find.textContaining('لم يتأكد الإصدار'));
        expect(find.textContaining('لم يتأكد الإصدار'), findsOneWidget);
        expect(
          t.widget<TextField>(find.byType(TextField).first).enabled,
          false,
        );
        expect(
          t.widget<CheckboxListTile>(find.byType(CheckboxListTile)).onChanged,
          isNull,
        );
        expect(writes, 1);
        readFails = false;
        await tapVisible(t, 'إعادة التحقق من الإصدار');
        expect(find.text('السجل الأصلي'), findsOneWidget);
        expect(writes, 1);
        expect(body, containsPair('percentage', 20.0));
        expect(calls, contains('GET /api/v1/manage/vip-rewards/reward'));
        if (lostResponse) {
          expect(
            calls,
            contains(
              'GET /api/v1/manage/vip-rewards/requests/${body!['requestId']}',
            ),
          );
        }
        expect(t.takeException(), isNull);
        await t.pumpWidget(const SizedBox());
        auth.dispose();
      },
    );
  }

  testWidgets(
    'VIP mismatched readback never reports success or unlocks the intent',
    (t) async {
      Map? body;
      final auth = await owner((m, p, b, token) async {
        if (p.endsWith('/options')) return terms;
        if (p.endsWith('/preview')) return preview(b as Map);
        if (m == 'POST') {
          body = b as Map;
          return {
            'ids': ['reward'],
          };
        }
        return issued(body!, percentage: 25);
      });
      await prepare(t, auth);
      await tapVisible(t, 'تأكيد الإصدار وحفظ الإشعار الداخلي');
      expect(find.text('السجل الأصلي'), findsNothing);
      await reveal(t, find.textContaining('لم يتأكد الإصدار'));
      expect(find.textContaining('لم يتأكد الإصدار'), findsOneWidget);
      expect(t.widget<TextField>(find.byType(TextField).first).enabled, false);
      await t.pumpWidget(const SizedBox());
      auth.dispose();
    },
  );

  testWidgets(
    'VIP target notice reads exactly one reward and does not resurrect across logout/login',
    (t) async {
      final calls = <String>[];
      final auth = await authenticate('Guardian', (m, p, b, token) async {
        calls.add('$m $p');
        if (p.contains('/notifications')) {
          return {
            'total': 1,
            'unread': 1,
            'items': [
              {
                'id': 'n',
                'message': 'مكافأة اختبار',
                'reward': true,
                'targetId': 'older-reward',
                'targetKind': 'Rewards',
                'isRead': false,
              },
            ],
          };
        }
        expect(p, '/api/v1/guardian/vip-rewards/older-reward');
        return {
          'count': 1,
          'items': [
            {
              'id': 'older-reward',
              'message': 'المكافأة المقصودة القديمة',
              'percentage': 20,
              'expiresOn': '2026-10-28',
              'state': 'Available',
              'history': [],
            },
          ],
        };
      });
      await t.pumpWidget(shell(NotificationInbox(auth: auth)));
      await t.pumpAndSettle();
      await t.tap(find.text('عرض المكافأة'));
      await t.pumpAndSettle();
      expect(find.text('المكافأة المقصودة القديمة'), findsOneWidget);
      expect(find.text('التالي'), findsNothing);
      expect(calls.any((p) => p.contains('/vip-rewards?page=')), false);
      await auth.logout();
      await t.pumpAndSettle();
      expect(find.text('المكافأة المقصودة القديمة'), findsNothing);
      await auth.verify('phone', 'challenge', 'code');
      await t.pumpAndSettle();
      expect(find.text('المكافأة المقصودة القديمة'), findsNothing);
      expect(t.takeException(), isNull);
      await t.pumpWidget(const SizedBox());
      auth.dispose();
    },
  );

  testWidgets(
    'VIP cancellation reads saved state and read retry does not cancel again',
    (t) async {
      var cancelled = false, canRead = false;
      var writes = 0;
      final auth = await owner((m, p, b, token) async {
        if (m == 'POST') {
          writes++;
          cancelled = true;
          return {'id': 'reward'};
        }
        if (p.endsWith('/reward') && !canRead) {
          throw const ApiFailure(503, 'تعذر القراءة.');
        }
        return {
          'count': 1,
          'items': [
            {
              'id': 'reward',
              'message': 'مكافأة محفوظة',
              'percentage': 20,
              'expiresOn': '2026-10-28',
              'state': cancelled ? 'Cancelled' : 'Available',
              'history': [],
            },
          ],
        };
      });
      await t.pumpWidget(shell(NativeVipRewards(auth: auth, owner: true)));
      await t.pumpAndSettle();
      await tapVisible(t, 'إلغاء المكافأة');
      await t.tap(find.text('تأكيد الإلغاء'));
      await t.pumpAndSettle();
      expect(find.textContaining('لم يتأكد الإلغاء'), findsOneWidget);
      expect(writes, 1);
      canRead = true;
      await tapVisible(t, 'إعادة التحقق من الإلغاء');
      expect(writes, 1);
      expect(
        find.textContaining('أُلغي الاستحقاق وأُعيد التحقق'),
        findsOneWidget,
      );
      expect(find.textContaining('ألغيت'), findsWidgets);
      await t.pumpWidget(const SizedBox());
      auth.dispose();
    },
  );

  testWidgets(
    'VIP pending issuance read cannot expose data or pop a new context',
    (t) async {
      final pending = Completer<Object?>();
      var reading = false;
      final auth = await owner((m, p, b, token) async {
        if (p.endsWith('/options')) return terms;
        if (p.endsWith('/preview')) return preview(b as Map);
        if (m == 'POST') {
          return {
            'ids': ['reward'],
          };
        }
        reading = true;
        return pending.future;
      });
      await prepare(t, auth);
      final button = find.text('تأكيد الإصدار وحفظ الإشعار الداخلي');
      await reveal(t, button);
      await t.tap(button);
      await t.pump();
      for (var i = 0; i < 20 && !reading; i++) {
        await t.pump(const Duration(milliseconds: 20));
      }
      expect(reading, true);
      await auth.logout();
      await t.pump();
      await auth.verify('phone', 'challenge', 'otp');
      await t.pump();
      pending.complete({'items': []});
      await t.pumpAndSettle();
      expect(find.text('أسرة اصطناعية'), findsNothing);
      expect(find.text('السجل الأصلي'), findsNothing);
      expect(t.takeException(), isNull);
      await t.pumpWidget(const SizedBox());
      auth.dispose();
    },
  );
}
