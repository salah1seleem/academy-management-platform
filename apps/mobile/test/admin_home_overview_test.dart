import 'dart:async';

import 'package:academy_mobile/core/api/api_client.dart';
import 'package:academy_mobile/core/auth/auth_controller.dart';
import 'package:academy_mobile/core/theme/app_theme.dart';
import 'package:academy_mobile/features/admin/admin_home.dart';
import 'package:academy_mobile/features/admin/admin_home_overview.dart';
import 'package:academy_mobile/features/shared/assigned_followups.dart';
import 'package:flutter/material.dart';
import 'package:flutter_localizations/flutter_localizations.dart';
import 'package:flutter_test/flutter_test.dart';

import 'auth_controller_test.dart' show FakeApi, MemoryStore, credentials;
import 'v2_role_workspaces_test.dart' as f;

const sessions = [
  {
    'id': 'one',
    'group': 'مجموعة أولى اصطناعية',
    'status': 'Scheduled',
    'recorded': 2,
    'present': 1,
    'playerCount': 5,
    'staff': [
      {'role': 'Coach', 'attendanceStatus': 'Late'},
      {'role': 'AcademyAdmin', 'attendanceStatus': 'Absent'},
    ],
  },
  {
    'id': 'two',
    'group': 'مجموعة ثانية اصطناعية',
    'status': 'Held',
    'recorded': 8,
    'present': 8,
    'playerCount': 8,
    'staff': [
      {'role': 'Coach', 'attendanceStatus': 'Absent'},
      {'role': 'AcademyAdmin', 'attendanceStatus': 'Present'},
    ],
  },
  {
    'id': 'three',
    'status': 'Scheduled',
    'recorded': 0,
    'present': 0,
    'playerCount': 4,
    'staff': [
      {'role': 'Coach', 'attendanceStatus': 'NotRecorded'},
      {'name': 'مدرب بلا دور موثوق', 'attendanceStatus': 'Present'},
    ],
  },
  {
    'id': 'cancelled',
    'status': 'Cancelled',
    'recorded': 100,
    'present': 100,
    'playerCount': 100,
    'staff': [
      {'role': 'Coach', 'attendanceStatus': 'Present'},
    ],
  },
];
Map<String, dynamic> follow(String id) => {
  'id': id,
  'version': 2,
  'condition': 'RecordFollowUp',
  'state': 'Assigned',
  'dueOn': '2026-10-23',
  'source': {'name': 'متابعة اصطناعية $id'},
};
Object? response(String path) {
  if (path == '/api/v1/my-access') return <String, dynamic>{};
  if (path == '/api/v1/my-permissions') {
    return [
      'attendance.read',
      'payment.read',
      'people.manage',
      'followups.read',
    ];
  }
  if (path == '/api/v1/attendance/sessions') return sessions;
  if (path.startsWith('/api/v1/manage/payment-requests')) {
    return {'pending': 7, 'items': []};
  }
  if (path.startsWith('/api/v1/my-followups?')) {
    return {
      'items': [for (var i = 1; i <= 4; i++) follow('$i')],
      'hasMore': false,
    };
  }
  if (path == '/api/v1/my-followups/1') {
    return {'item': follow('1'), 'canEdit': false, 'history': []};
  }
  if (path == '/api/v1/staff/passkeys/attendance') return {'branches': []};
  return <Object>[];
}

Widget app(
  Widget page, {
  bool dark = false,
  double scale = 1,
  bool component = false,
}) => MaterialApp(
  locale: const Locale('ar'),
  supportedLocales: const [Locale('ar')],
  localizationsDelegates: GlobalMaterialLocalizations.delegates,
  theme: dark ? AcademyTheme.dark() : AcademyTheme.light(),
  builder: (context, child) => MediaQuery(
    data: MediaQuery.of(context).copyWith(textScaler: TextScaler.linear(scale)),
    child: child!,
  ),
  home: component ? Scaffold(body: SingleChildScrollView(child: page)) : page,
);
void main() {
  testWidgets(
    'session overview uses weighted saved records, partial sessions, roles and no inferred absence',
    (t) async {
      await t.pumpWidget(
        app(const AdminAttendanceOverview(sessions: sessions), component: true),
      );
      await t.pumpAndSettle();
      expect(find.text('2 من 3'), findsOneWidget);
      expect(
        find.text('90%'),
        findsOneWidget,
      ); // 9/10 recorded player appearances.
      expect(
        find.text('50%'),
        findsNWidgets(2),
      ); // Each role: one present/late +one absent.
      expect(find.textContaining('ليست حضور يوم العمل'), findsOneWidget);
      expect(t.takeException(), isNull);
    },
  );
  testWidgets(
    'no recorded attendance or no role produces unknown, not fake zero or guessed coach',
    (t) async {
      await t.pumpWidget(
        app(
          const AdminAttendanceOverview(
            sessions: [
              {
                'playerCount': 25,
                'staff': [
                  {'name': 'مدرب اسمه تجريبي', 'attendanceStatus': 'Present'},
                ],
              },
            ],
          ),
          component: true,
        ),
      );
      await t.pumpAndSettle();
      expect(find.text('0 من 1'), findsOneWidget);
      expect(find.text('—'), findsNWidgets(3));
      expect(find.text('0%'), findsNothing);
    },
  );
  for (final width in [375.0, 440.0]) {
    for (final dark in [false, true]) {
      for (final scale in [1.0, 2.0]) {
        testWidgets(
          'admin summary and tasks $width dark=$dark scale=$scale retain readable data and actions',
          (t) async {
            t.view.physicalSize = Size(width, 956);
            t.view.devicePixelRatio = 1;
            addTearDown(t.view.resetPhysicalSize);
            addTearDown(t.view.resetDevicePixelRatio);
            final auth = await f.authenticate(
              'AcademyAdmin',
              (m, p, b, k) async => response(p),
            );
            await t.pumpWidget(
              app(
                AdminHome(auth: auth),
                dark: dark,
                scale: scale,
              ),
            );
            await t.pumpAndSettle();
            expect(find.byType(AdminAttendanceOverview), findsOneWidget);
            for (final task in [
              ('admin-home-attendance-task', 2),
              ('admin-home-payments-task', 7),
            ]) {
              final target = find.byKey(ValueKey(task.$1));
              await t.scrollUntilVisible(
                target,
                180,
                scrollable: find.byType(Scrollable).first,
                maxScrolls: 60,
              );
              expect(t.widget<AdminHomeTask>(target).count, task.$2);
            }
            await t.scrollUntilVisible(
              find.text('متابعات مسندة إليّ'),
              180,
              scrollable: find.byType(Scrollable).first,
              maxScrolls: 60,
            );
            await t.pumpAndSettle();
            expect(find.text('متابعة اصطناعية 1'), findsOneWidget);
            expect(find.text('متابعة اصطناعية 4'), findsNothing);
            expect(t.takeException(), isNull);
            await t.pumpWidget(const SizedBox());
            auth.dispose();
          },
        );
      }
    }
  }
  testWidgets(
    'permission denial hides corresponding summaries/actions and never requests their sources',
    (t) async {
      final calls = <String>[];
      final auth = await f.authenticate('AcademyAdmin', (m, p, b, k) async {
        calls.add(p);
        if (p == '/api/v1/my-permissions') return <String>[];
        return response(p);
      });
      await t.pumpWidget(app(AdminHome(auth: auth)));
      await t.pumpAndSettle();
      expect(find.byType(AdminAttendanceOverview), findsNothing);
      expect(find.byType(AdminHomeTask), findsNothing);
      expect(find.text('متابعاتي المسندة'), findsNothing);
      expect(
        calls.any(
          (p) =>
              p.contains('/my-followups') ||
              p.contains('/attendance/sessions') ||
              p.contains('/payment-requests') ||
              p.contains('/reports/'),
        ),
        false,
      );
      await t.pumpWidget(const SizedBox());
      auth.dispose();
    },
  );
  testWidgets(
    'partial source failure shows unknown count and retries while other tasks stay available',
    (t) async {
      var fail = true;
      final auth = await f.authenticate('AcademyAdmin', (m, p, b, k) async {
        if (fail && p == '/api/v1/attendance/sessions') {
          throw const ApiFailure(0, 'تعذر قراءة الحصص');
        }
        return response(p);
      });
      await t.pumpWidget(app(AdminHome(auth: auth)));
      await t.pumpAndSettle();
      expect(find.byType(AdminAttendanceOverview), findsNothing);
      final tasks = t
          .widgetList<AdminHomeTask>(find.byType(AdminHomeTask))
          .toList();
      expect(tasks.first.count, isNull);
      expect(tasks.last.count, 7);
      await t.scrollUntilVisible(
        find.text('إعادة المحاولة'),
        220,
        scrollable: find.byType(Scrollable).first,
        maxScrolls: 60,
      );
      fail = false;
      await t.tap(find.text('إعادة المحاولة'));
      await t.pumpAndSettle();
      await t.scrollUntilVisible(
        find.byType(AdminAttendanceOverview),
        -220,
        scrollable: find.byType(Scrollable).first,
        maxScrolls: 60,
      );
      expect(find.byType(AdminAttendanceOverview), findsOneWidget);
      await t.pumpWidget(const SizedBox());
      auth.dispose();
    },
  );
  testWidgets(
    'home opens the assigned follow-up original, requires existing detail workflow and refreshes on return without a write',
    (t) async {
      var writes = 0, reads = 0;
      final auth = await f.authenticate('AcademyAdmin', (m, p, b, k) async {
        if (m != 'GET') writes++;
        if (p.startsWith('/api/v1/my-followups?')) reads++;
        return response(p);
      });
      await t.pumpWidget(app(AdminHome(auth: auth)));
      await t.pumpAndSettle();
      await t.scrollUntilVisible(
        find.byKey(const ValueKey('admin-home-followup-1')),
        200,
        scrollable: find.byType(Scrollable).first,
        maxScrolls: 60,
      );
      await t.ensureVisible(
        find.byKey(const ValueKey('admin-home-followup-1')),
      );
      await t.pumpAndSettle();
      await t.ensureVisible(
        find.byKey(const ValueKey('admin-home-followup-1')),
      );
      await t.pumpAndSettle();
      await t.tap(find.byKey(const ValueKey('admin-home-followup-1')));
      await t.pumpAndSettle();
      expect(
        t.widget<AssignedFollowUpsPage>(find.byType(AssignedFollowUpsPage)).id,
        '1',
      );
      expect(find.text('تفاصيل المتابعة المسندة'), findsOneWidget);
      await t.tap(find.byTooltip('رجوع'));
      await t.pumpAndSettle();
      expect(reads, 2);
      expect(writes, 0);
      await t.pumpWidget(const SizedBox());
      auth.dispose();
    },
  );
  testWidgets(
    'home distinguishes an empty inbox and opens the same all-date scope',
    (t) async {
      final reads = <Uri>[];
      final auth = await f.authenticate('AcademyAdmin', (m, p, b, k) async {
        if (p.startsWith('/api/v1/my-followups?')) {
          reads.add(Uri.parse(p));
          return {'items': []};
        }
        return response(p);
      });
      await t.pumpWidget(app(AdminHome(auth: auth)));
      await t.pumpAndSettle();
      final empty = find.text('لا توجد متابعات مفتوحة.');
      await t.scrollUntilVisible(
        empty,
        120,
        scrollable: find.byType(Scrollable).first,
      );
      expect(empty, findsOneWidget);
      final inbox = find.text('متابعاتي المسندة');
      await t.scrollUntilVisible(
        inbox,
        120,
        scrollable: find.byType(Scrollable).first,
      );
      await t.ensureVisible(inbox);
      await t.pumpAndSettle();
      await t.tap(inbox);
      await t.pumpAndSettle();
      expect(
        t
            .widget<AssignedFollowUpsPage>(find.byType(AssignedFollowUpsPage))
            .initialAllDates,
        isTrue,
      );
      expect(reads, hasLength(2));
      expect(
        reads.map((uri) => uri.queryParameters),
        everyElement({'allDates': 'true', 'state': 'Open'}),
      );
      await t.pumpWidget(const SizedBox());
      auth.dispose();
    },
  );
  testWidgets(
    'second academy and logout redact old pending home and a late source cannot restore it',
    (t) async {
      var second = false;
      final pending = Completer<Object?>();
      final auth = AuthController(
        FakeApi((m, p, b, k) async {
          if (p.contains('/auth/')) {
            return {
              ...credentials(),
              'membershipId': 'member',
              'memberships': [
                {
                  'id': 'member',
                  'academyId': second ? 'academy-two' : 'academy-one',
                  'academyName': second ? 'الثانية' : 'الأولى',
                  'role': 'AcademyAdmin',
                  'contextRevision': second ? 2 : 1,
                },
              ],
            };
          }
          if (p.startsWith('/api/v1/my-followups?')) {
            return second
                ? {
                    'items': [follow('الثانية')],
                  }
                : pending.future;
          }
          return response(p);
        }),
        MemoryStore(),
      )..restoring = false;
      await auth.verify('synthetic', 'challenge', '246810');
      await t.pumpWidget(app(AdminHome(auth: auth)));
      await t.pump(const Duration(milliseconds: 100));
      second = true;
      await auth.verify('synthetic', 'challenge', '246810');
      await t.pumpAndSettle();
      await t.scrollUntilVisible(
        find.text('متابعة اصطناعية الثانية'),
        200,
        scrollable: find.byType(Scrollable).first,
        maxScrolls: 60,
      );
      expect(find.text('متابعة اصطناعية الثانية'), findsOneWidget);
      await auth.logout();
      pending.complete({
        'items': [follow('الأولى')],
      });
      await t.pumpAndSettle();
      expect(find.textContaining('متابعة اصطناعية'), findsNothing);
      expect(find.byType(AdminHomeTask), findsNothing);
      await t.pumpWidget(const SizedBox());
      auth.dispose();
    },
  );
}
