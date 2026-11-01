import 'dart:async';

import 'package:academy_mobile/features/shared/subscription_home.dart';
import 'package:academy_mobile/features/admin/admin_home.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

import 'v2_role_workspaces_test.dart' show authenticate, app;

Map<String, dynamic> home() => {
  'academy': 'أكاديمية خيالية',
  'asOf': '2026-10-25',
  'total': 12,
  'active': 8,
  'expiring': 3,
  'expired': 4,
  'players': 10,
  'periodRecords': 28,
  'dimensions': ['packages', 'groups', 'days', 'ages'],
  'branches': [
    {'id': 'b', 'name': 'الفرع المصرح'},
  ],
  'sports': [
    {'id': 's', 'name': 'السباحة', 'kind': 'Swimming'},
  ],
};
void main() {
  testWidgets(
    'overview is immediate; analysis opens groups and cached dimensions retain the trusted scope',
    (t) async {
      final calls = <String>[];
      final auth = await authenticate('AcademyOwner', (m, p, b, k) async {
        calls.add(p);
        if (p.startsWith('/api/v1/subscriptions/home/breakdown')) {
          return {
            'items': [
              {
                'key': 'plan',
                'name': 'باقة حصص مسجلة',
                'total': 12,
                'detail': '12 حصة · جماعي',
              },
            ],
          };
        }
        if (p.startsWith('/api/v1/subscriptions/home/items')) {
          return {'total': 8, 'asOf': '2026-10-25', 'items': [], 'page': 1};
        }
        return home();
      });
      await t.pumpWidget(
        app(
          Scaffold(
            body: SingleChildScrollView(
              child: SubscriptionHomePanel(
                auth: auth,
                asOf: '2026-10-25',
                branchId: 'b',
              ),
            ),
          ),
        ),
      );
      await t.pumpAndSettle();
      expect(find.text('اشتراكات الأكاديمية'), findsOneWidget);
      expect(calls.where((p) => p.contains('/breakdown')), isEmpty);
      expect(find.text('الفروع'), findsNothing);
      expect(find.text('الرياضات'), findsNothing);
      await t.ensureVisible(find.text('تحليل الاشتراكات'));
      await t.tap(find.text('تحليل الاشتراكات'));
      await t.pumpAndSettle();
      expect(
        calls
            .where(
              (p) =>
                  p.contains('/breakdown') &&
                  Uri.parse(p).queryParameters['dimension'] == 'groups',
            )
            .length,
        1,
      );
      await t.ensureVisible(find.text('الباقات'));
      await t.pumpAndSettle();
      await t.tap(find.text('الباقات'));
      await t.pumpAndSettle();
      expect(find.text('باقة حصص مسجلة'), findsOneWidget);
      expect(calls.where((p) => p.contains('/breakdown')).length, 2);
      await t.tap(find.text('الباقات').first);
      await t.pumpAndSettle();
      expect(find.text('باقة حصص مسجلة'), findsNothing);
      await t.tap(find.text('الباقات'));
      await t.pumpAndSettle();
      expect(calls.where((p) => p.contains('/breakdown')).length, 2);
      await t.ensureVisible(find.text('فعالة'));
      await t.pumpAndSettle();
      await t.tap(find.text('فعالة'));
      await t.pumpAndSettle();
      final query = Uri.parse(calls.last).queryParameters;
      expect(query['state'], 'current');
      expect(query['asOf'], '2026-10-25');
      expect(query['branchId'], 'b');
      await t.pumpWidget(const SizedBox());
      auth.dispose();
    },
  );
  testWidgets(
    'independent admin reads expose ready subscriptions and sessions while payments remain pending',
    (t) async {
      final pending = Completer<Object?>();
      final auth = await authenticate('AcademyAdmin', (m, p, b, k) async {
        if (p == '/api/v1/my-permissions') {
          return ['subscription.read', 'attendance.read', 'payment.read'];
        }
        if (p == '/api/v1/my-access') {
          return {'useActionGrants': true, 'actions': []};
        }
        if (p.startsWith('/api/v1/subscriptions/home')) return home();
        if (p.startsWith('/api/v1/manage/payment-requests')) {
          return pending.future;
        }
        if (p == '/api/v1/attendance/sessions') {
          return [
            {
              'id': 'session',
              'group': 'مجموعة جاهزة فعلًا',
              'sessionDate': '2026-10-25',
              'startTime': '16:00',
              'endTime': '17:00',
              'playerCount': 2,
              'recorded': 0,
              'status': 'Scheduled',
            },
          ];
        }
        if (p == '/api/v1/news') return [];
        return <String, dynamic>{};
      });
      await t.pumpWidget(app(AdminHome(auth: auth)));
      await t.pumpAndSettle();
      expect(find.text('اشتراكات نطاقك'), findsOneWidget);
      await t.scrollUntilVisible(
        find.text('مجموعة جاهزة فعلًا'),
        220,
        scrollable: find.byType(Scrollable).first,
        maxScrolls: 25,
      );
      expect(find.text('مجموعة جاهزة فعلًا'), findsOneWidget);
      expect(pending.isCompleted, false);
      pending.complete({'pending': 7, 'items': []});
      await t.pumpAndSettle();
      expect(t.takeException(), isNull);
      await t.pumpWidget(const SizedBox());
      auth.dispose();
    },
  );
  for (final scale in [1.0, 2.0]) {
    testWidgets(
      'phone 320px, large text $scale, pool artwork and live numbers remain usable',
      (t) async {
        t.view.physicalSize = const Size(320, 800);
        t.view.devicePixelRatio = 1;
        addTearDown(t.view.resetPhysicalSize);
        addTearDown(t.view.resetDevicePixelRatio);
        final auth = await authenticate(
          'AcademyOwner',
          (m, p, b, k) async => home(),
        );
        await t.pumpWidget(
          app(
            Scaffold(
              body: SingleChildScrollView(
                child: SubscriptionHomePanel(auth: auth),
              ),
            ),
            scale: scale,
          ),
        );
        await t.pumpAndSettle();
        expect(find.text('8'), findsOneWidget);
        expect(t.takeException(), isNull);
        await t.pumpWidget(const SizedBox());
        auth.dispose();
      },
    );
  }
}
