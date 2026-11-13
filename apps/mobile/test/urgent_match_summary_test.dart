import 'dart:async';

import 'package:academy_mobile/features/shared/community_ads.dart';
import 'package:academy_mobile/features/shared/notification_inbox.dart';
import 'package:academy_mobile/features/shared/matches.dart';
import 'package:academy_mobile/core/theme/app_theme.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

import 'v2_role_workspaces_test.dart' show authenticate, app;
import 'guardian_home_workspace_test.dart' as home_fixture;

import 'package:academy_mobile/features/guardian/guardian_home.dart';

void main() {
  for (final width in [360.0, 440.0]) {
    for (final scale in [1.0, 2.0]) {
      testWidgets(
        'One compact urgent summary preserves child access at $width and $scale',
        (t) async {
          t.view.physicalSize = Size(width, 900);
          t.view.devicePixelRatio = 1;
          addTearDown(t.view.resetPhysicalSize);
          addTearDown(t.view.resetDevicePixelRatio);
          final auth = await authenticate(
            'Guardian',
            (m, p, b, token) async => {},
          );
          await t.pumpWidget(
            app(
              Scaffold(
                body: SingleChildScrollView(
                  child: Column(
                    children: [
                      GuardianUrgentMatchAlerts(
                        auth: auth,
                        alerts: [
                          for (var i = 0; i < 3; i++)
                            {
                              'id': 'n$i',
                              'matchId': 'm$i',
                              'message': i == 0
                                  ? 'تغير موعد المباراة. راجعوا التفاصيل الكاملة والمكان والوقت المحدّث قبل التوجه إلى الملعب.'
                                  : 'تحديث آخر طويل مستقل يجب ألا يملأ الرئيسية $i',
                            },
                        ],
                      ),
                      const Text('أبنائي'),
                    ],
                  ),
                ),
              ),
              scale: scale,
            ),
          );
          await t.pumpAndSettle();
          final summary = find.byKey(const Key('guardian-urgent-summary'));
          expect(summary, findsOneWidget);
          expect(t.getSize(summary).height, lessThan(scale == 1 ? 180 : 310));
          expect(
            find.text('تحديث آخر طويل مستقل يجب ألا يملأ الرئيسية 1'),
            findsNothing,
          );
          expect(
            find.text('تحديث آخر طويل مستقل يجب ألا يملأ الرئيسية 2'),
            findsNothing,
          );
          expect(t.getBottomLeft(find.text('أبنائي')).dy, lessThan(500));
          expect(t.takeException(), isNull);
          await t.pumpWidget(const SizedBox());
          auth.dispose();
        },
      );
    }
  }
  testWidgets(
    'Summary opens existing filtered inbox, preserves full text and exact match source, and returns without marking read',
    (t) async {
      final calls = <String>[];
      var returned = 0;
      final auth = await authenticate('Guardian', (m, p, b, token) async {
        calls.add('$m $p');
        if (p.contains('/notifications')) {
          return {
            'unread': 1,
            'total': 1,
            'items': [
              {
                'id': 'notice',
                'message': 'التفاصيل الكاملة لتحديث مباراة اصطناعية',
                'isRead': false,
                'matchId': 'match-exact',
                'createdAtUtc': '2026-10-22',
              },
            ],
          };
        }
        if (p == '/api/v1/guardian/home') return {'children': <Object>[]};
        if (p.startsWith('/api/v1/my-matches')) return <Object>[];
        return {};
      });
      await t.pumpWidget(
        app(
          Scaffold(
            body: GuardianUrgentMatchAlerts(
              auth: auth,
              alerts: [
                {'message': 'ملخص قصير', 'matchId': 'match-exact'},
              ],
              onReturned: () => returned++,
            ),
          ),
        ),
      );
      await t.pumpAndSettle();
      await t.tap(find.text('عرض التحديثات والمواعيد'));
      await t.pumpAndSettle();
      expect(
        t
            .widget<NotificationInbox>(find.byType(NotificationInbox))
            .urgentMatchesOnly,
        true,
      );
      expect(
        calls,
        contains('GET /api/v1/notifications?page=1&urgentMatchesOnly=true'),
      );
      expect(
        find.text('التفاصيل الكاملة لتحديث مباراة اصطناعية'),
        findsOneWidget,
      );
      await t.tap(find.text('عرض المباراة'));
      await t.pumpAndSettle();
      expect(
        t.widget<MatchSchedule>(find.byType(MatchSchedule)).matchId,
        'match-exact',
      );
      Navigator.of(t.element(find.byType(MatchSchedule))).pop();
      await t.pumpAndSettle();
      Navigator.of(t.element(find.byType(NotificationInbox))).pop();
      await t.pumpAndSettle();
      expect(returned, 1);
      expect(
        calls.any((p) => p.startsWith('PUT') || p.startsWith('POST')),
        false,
      );
      expect(t.takeException(), isNull);
      await t.pumpWidget(const SizedBox());
      auth.dispose();
    },
  );
  testWidgets('No urgent records leave no empty banner', (t) async {
    final auth = await authenticate('Guardian', (m, p, b, token) async => {});
    await t.pumpWidget(
      MaterialApp(
        theme: AcademyTheme.light(),
        home: GuardianUrgentMatchAlerts(auth: auth, alerts: const []),
      ),
    );
    expect(find.byType(Card), findsNothing);
    await t.pumpWidget(const SizedBox());
    auth.dispose();
  });
  testWidgets(
    'Reading the filtered notice refreshes the home banner and keeps the selected sibling',
    (t) async {
      var read = false;
      final writes = <String>[];
      final auth = await authenticate('Guardian', (m, p, b, token) async {
        if (m == 'PUT') {
          writes.add(p);
          read = (b as Map)['isRead'] == true;
          return null;
        }
        if (p == '/api/v1/guardian/home') {
          return {
            ...home_fixture.home(),
            'urgentAlerts': read
                ? <Object>[]
                : [
                    {
                      'id': 'notice',
                      'message': 'تغير موعد مباراة اصطناعية',
                      'matchId': 'match',
                    },
                  ],
          };
        }
        if (p.contains('/notifications')) {
          return {
            'unread': read ? 0 : 1,
            'total': read ? 0 : 1,
            'items': read
                ? <Object>[]
                : [
                    {
                      'id': 'notice',
                      'message': 'تغير موعد مباراة اصطناعية',
                      'matchId': 'match',
                      'isRead': false,
                      'createdAtUtc': '2026-10-22',
                    },
                  ],
          };
        }
        return home_fixture.response(p);
      });
      await t.pumpWidget(home_fixture.app(auth));
      await t.pumpAndSettle();
      t
          .widget<GestureDetector>(find.byKey(const ValueKey('select-child-b')))
          .onTap!();
      await t.pumpAndSettle();
      expect(
        t
            .widget<GuardianChildCarousel>(find.byType(GuardianChildCarousel))
            .selectedPlayerId,
        'b',
      );
      await t.tap(find.text('عرض التحديثات والمواعيد'));
      await t.pumpAndSettle();
      await t.tap(find.text('تحديد كمقروء'));
      await t.pumpAndSettle();
      expect(find.text('لا توجد تنبيهات.'), findsOneWidget);
      Navigator.of(t.element(find.byType(NotificationInbox))).pop();
      await t.pumpAndSettle();
      expect(find.byKey(const Key('guardian-urgent-summary')), findsNothing);
      expect(
        t
            .widget<GuardianChildCarousel>(find.byType(GuardianChildCarousel))
            .selectedPlayerId,
        'b',
      );
      expect(writes, ['/api/v1/notifications/notice/read']);
      expect(t.takeException(), isNull);
      await t.pumpWidget(const SizedBox());
      auth.dispose();
    },
  );
  testWidgets(
    'Reading from the shared header bell also refreshes the existing family home',
    (t) async {
      var read = false;
      final auth = await authenticate('Guardian', (m, p, b, token) async {
        if (m == 'PUT') {
          read = (b as Map)['isRead'] == true;
          return null;
        }
        if (p == '/api/v1/guardian/home') {
          return {
            ...home_fixture.home(),
            'urgentAlerts': read
                ? <Object>[]
                : [
                    {
                      'id': 'notice',
                      'message': 'تحديث موعد من الجرس',
                      'matchId': 'match',
                    },
                  ],
          };
        }
        if (p.contains('/notifications')) {
          return {
            'unread': read ? 0 : 1,
            'total': 1,
            'items': [
              {
                'id': 'notice',
                'message': 'تحديث موعد من الجرس',
                'matchId': 'match',
                'isRead': read,
                'createdAtUtc': '2026-10-22',
              },
            ],
          };
        }
        return home_fixture.response(p);
      });
      try {
        await t.pumpWidget(
          app(
            Scaffold(
              body: Column(
                children: [
                  NotificationBell(auth: auth),
                  Expanded(
                    child: GuardianHome(
                      auth: auth,
                      environment: 'Demo',
                      accountPage: const SizedBox(),
                    ),
                  ),
                ],
              ),
            ),
          ),
        );
        await t.pumpAndSettle();
        t
            .widget<GestureDetector>(
              find.byKey(const ValueKey('select-child-b')),
            )
            .onTap!();
        await t.pumpAndSettle();
        await t.tap(find.byTooltip('التنبيهات'));
        await t.pumpAndSettle();
        await t.tap(find.text('تحديد كمقروء'));
        await t.pumpAndSettle();
        Navigator.of(t.element(find.byType(NotificationInbox))).pop();
        await t.pumpAndSettle();
        expect(find.byKey(const Key('guardian-urgent-summary')), findsNothing);
        expect(
          t
              .widget<GuardianChildCarousel>(find.byType(GuardianChildCarousel))
              .selectedPlayerId,
          'b',
        );
        expect(t.takeException(), isNull);
      } finally {
        await t.pumpWidget(const SizedBox());
        auth.dispose();
      }
    },
  );
  testWidgets(
    'Delayed read response after logout cannot invalidate or show the old account notice',
    (t) async {
      final pending = Completer<Object?>();
      var signals = 0;
      final auth = await authenticate('Guardian', (m, p, b, token) async {
        if (m == 'PUT') return pending.future;
        return {
          'unread': 1,
          'total': 1,
          'items': [
            {
              'id': 'private-notice',
              'message': 'تحديث خاص بالأسرة السابقة',
              'isRead': false,
              'createdAtUtc': '2026-10-22',
            },
          ],
        };
      });
      void changed() {
        signals++;
      }

      final changes = notificationChanges(auth);
      changes.addListener(changed);
      try {
        await t.pumpWidget(app(NotificationInbox(auth: auth)));
        await t.pumpAndSettle();
        await t.tap(find.text('تحديد كمقروء'));
        await t.pump();
        await auth.logout();
        pending.complete(null);
        await t.pumpAndSettle();
        expect(signals, 0);
        expect(find.text('تحديث خاص بالأسرة السابقة'), findsNothing);
        expect(t.takeException(), isNull);
      } finally {
        if (!pending.isCompleted) {
          pending.complete(null);
        }
        await t.pumpWidget(const SizedBox());
        changes.removeListener(changed);
        auth.dispose();
      }
    },
  );
}
