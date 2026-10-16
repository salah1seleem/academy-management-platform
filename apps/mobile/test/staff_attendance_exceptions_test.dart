import 'dart:async';

import 'package:academy_mobile/core/api/api_client.dart';
import 'package:academy_mobile/core/theme/app_theme.dart';
import 'package:academy_mobile/features/guardian/guardian_ui.dart';
import 'package:academy_mobile/features/shared/staff_attendance_exceptions.dart';
import 'package:flutter/material.dart';
import 'package:flutter_localizations/flutter_localizations.dart';
import 'package:flutter_test/flutter_test.dart';

import 'auth_controller_test.dart' show FakeApi;
import 'coach_test.dart' show coach, shell;
import 'owner_test.dart' show owner;

final options = <String, dynamic>{
  'configured': true,
  'nowUtc': '2026-10-16T08:00:00Z',
  'branches': [
    {'id': 'b1', 'name': 'فرع العرض'},
  ],
};
final claim = <String, dynamic>{
  'id': 'r1',
  'operation': 'CheckIn',
  'claimedAtUtc': '2026-10-16T07:00:00Z',
  'receivedAtUtc': '2026-10-16T07:05:00Z',
  'reason': 'موقع غير دقيق داخل الفرع',
  'status': 'Pending',
  'version': 7,
};
Future<void> reach(WidgetTester t, Finder f) async {
  await t.scrollUntilVisible(f, 200, scrollable: find.byType(Scrollable).first);
  await t.ensureVisible(f);
  await t.pumpAndSettle();
}

Future<void> selectBranch(WidgetTester t) async {
  await t.tap(find.byType(DropdownButtonFormField<String>).first);
  await t.pumpAndSettle();
  await t.tap(find.text('فرع العرض').last);
  await t.pumpAndSettle();
}

void main() {
  testWidgets(
    'Checkout binds the displayed open workday, including yesterday, without choosing another employee',
    (t) async {
      Json? body;
      final auth = await coach((m, p, b, token) async {
        if (m == 'POST') {
          body = b as Json;
          return {...claim, 'operation': 'CheckOut', 'workDate': '2026-10-15'};
        }
        return p.endsWith('/options')
            ? {
                ...options,
                'openAttendance': {
                  'id': 'daily-previous',
                  'workDate': '2026-10-15',
                  'checkIn': '23:30:00',
                },
              }
            : <Json>[];
      });
      await t.pumpWidget(shell(StaffAttendanceExceptionPage(auth: auth)));
      await t.pumpAndSettle();
      expect(find.text('حضور مفتوح — يحتاج انصرافًا'), findsOneWidget);
      await reach(t, find.byType(DropdownButtonFormField<String>).first);
      await selectBranch(t);
      await reach(t, find.byType(DropdownButtonFormField<String>).at(1));
      await t.tap(find.byType(DropdownButtonFormField<String>).at(1));
      await t.pumpAndSettle();
      await t.tap(find.text('انصراف').last);
      await t.pumpAndSettle();
      await reach(t, find.text('إرسال للمراجعة'));
      await t.tap(find.text('إرسال للمراجعة'));
      await t.pumpAndSettle();
      expect(body!['targetAttendanceId'], 'daily-previous');
      expect(body!['operation'], 'CheckOut');
      expect(body!.containsKey('membershipId'), isFalse);
      expect(t.takeException(), isNull);
      await t.pumpWidget(const SizedBox());
      auth.dispose();
    },
  );
  testWidgets(
    'Checkout without an open source is disabled; reviewer displays the saved overnight date',
    (t) async {
      final auth = await coach((m, p, b, token) async {
        expect(m, 'GET');
        return p.endsWith('/options') ? options : <Json>[];
      });
      await t.pumpWidget(shell(StaffAttendanceExceptionPage(auth: auth)));
      await t.pumpAndSettle();
      await selectBranch(t);
      await t.tap(find.byType(DropdownButtonFormField<String>).at(1));
      await t.pumpAndSettle();
      await t.tap(find.text('انصراف').last);
      await t.pumpAndSettle();
      await reach(t, find.text('إرسال للمراجعة'));
      expect(
        t
            .widget<FilledButton>(
              find.widgetWithText(FilledButton, 'إرسال للمراجعة'),
            )
            .onPressed,
        isNull,
      );
      await t.pumpWidget(const SizedBox());
      auth.dispose();
      final reviewer = await owner(
        (m, p, b, token) async => {
          'name': 'موظف وردية ليلية',
          'branch': 'فرع العرض',
          'canReview': false,
          'request': {
            ...claim,
            'operation': 'CheckOut',
            'workDate': '2026-10-15',
            'status': 'Approved',
          },
          'currentAttendance': {
            'status': 'Present',
            'checkIn': '23:30:00',
            'checkOut': '04:00:00',
            'checkOutDate': '2026-10-16',
          },
        },
      );
      await t.pumpWidget(
        shell(StaffExceptionReviewPage(auth: reviewer, id: 'r1')),
      );
      await t.pumpAndSettle();
      await reach(t, find.text('تاريخ الانصراف بتوقيت الأكاديمية: 2026-10-16'));
      expect(t.takeException(), isNull);
      await t.pumpWidget(const SizedBox());
      reviewer.dispose();
    },
  );
  testWidgets(
    'Own claim is pending, server-scoped, double-tap safe and hides on logout',
    (t) async {
      final gate = Completer<Object?>();
      var writes = 0;
      Json? body;
      var sent = false;
      final auth = await coach((m, p, b, token) async {
        if (m == 'POST') {
          writes++;
          body = b as Json;
          await gate.future;
          sent = true;
          return claim;
        }
        return p.endsWith('/options')
            ? options
            : sent
            ? [claim]
            : <Json>[];
      });
      await t.pumpWidget(shell(StaffAttendanceExceptionPage(auth: auth)));
      await t.pumpAndSettle();
      await selectBranch(t);
      await reach(t, find.text('إرسال للمراجعة'));
      await t.tap(find.text('إرسال للمراجعة'));
      await t.pump();
      await t.tap(find.text('جارٍ الإرسال…'));
      await t.pump();
      expect(writes, 1);
      gate.complete(claim);
      await t.pumpAndSettle();
      expect(body!.keys.toSet(), {
        'branchId',
        'operation',
        'reason',
        'claimedAtUtc',
      });
      expect(body!['branchId'], 'b1');
      expect(body!['operation'], 'CheckIn');
      expect(
        find.text('تم إرسال الطلب للمراجعة؛ لم يُسجل الحضور بعد.'),
        findsOneWidget,
      );
      await reach(t, find.text('يحتاج مراجعة'));
      expect(find.text('يحتاج مراجعة'), findsOneWidget);
      expect(find.text('مقبول إداريًا'), findsNothing);
      await auth.logout();
      await t.pumpAndSettle();
      expect(find.text('طلباتي — أحدث 100'), findsNothing);
      await t.pumpWidget(const SizedBox());
      auth.dispose();
    },
  );
  testWidgets(
    'Own lost reply retries exact claim with the same idempotency key',
    (t) async {
      var posts = 0;
      final bodies = <Object?>[];
      final auth = await coach((m, p, b, token) async {
        if (m == 'POST') {
          posts++;
          bodies.add(b);
          if (posts == 1) throw const ApiFailure(503, 'لم يتأكد الطلب.');
          return claim;
        }
        return p.endsWith('/options') ? options : <Json>[];
      });
      await t.pumpWidget(shell(StaffAttendanceExceptionPage(auth: auth)));
      await t.pumpAndSettle();
      await selectBranch(t);
      await reach(t, find.text('إرسال للمراجعة'));
      await t.tap(find.text('إرسال للمراجعة'));
      await t.pumpAndSettle();
      await reach(t, find.text('إعادة تأكيد الطلب نفسه'));
      await t.tap(find.text('إعادة تأكيد الطلب نفسه'));
      await t.pumpAndSettle();
      expect(posts, 2);
      expect(bodies[0], bodies[1]);
      final api = auth.transport as FakeApi;
      final keys = [
        for (var i = 0; i < api.paths.length; i++)
          if (api.paths[i] == '$ownStaffExceptions/') api.keys[i],
      ].whereType<String>().toList();
      expect(keys.length, 2);
      expect(keys[0], keys[1]);
      expect(keys[0], isNotEmpty);
      await t.pumpWidget(const SizedBox());
      auth.dispose();
    },
  );
  testWidgets(
    'Reviewer confirms a versioned claim then sees persisted approval; own/readonly has no action',
    (t) async {
      var accepted = false;
      Json? body;
      final auth = await owner((m, p, b, token) async {
        if (m == 'POST') {
          body = b as Json;
          accepted = true;
          return {...claim, 'status': 'Approved'};
        }
        return {
          'name': 'موظف العرض',
          'branch': 'فرع العرض',
          'canReview': !accepted,
          'request': {...claim, 'status': accepted ? 'Approved' : 'Pending'},
        };
      });
      await t.pumpWidget(shell(StaffExceptionReviewPage(auth: auth, id: 'r1')));
      await t.pumpAndSettle();
      await reach(t, find.text('قبول وتسجيل إداري'));
      await t.tap(find.text('قبول وتسجيل إداري'));
      await t.pumpAndSettle();
      await t.tap(find.text('تأكيد القرار'));
      await t.pumpAndSettle();
      expect(body, {'approve': true, 'note': null, 'version': 7});
      await reach(t, find.text('مقبول إداريًا'));
      expect(find.text('قبول وتسجيل إداري'), findsNothing);
      expect(t.takeException(), isNull);
      await t.pumpWidget(const SizedBox());
      auth.dispose();
    },
  );
  testWidgets(
    'Readonly/self review cannot submit; missing employee has no claim form',
    (t) async {
      final auth = await owner((m, p, b, token) async {
        expect(m, 'GET');
        return {
          'name': 'موظف العرض',
          'branch': 'فرع العرض',
          'canReview': false,
          'request': claim,
        };
      });
      await t.pumpWidget(shell(StaffExceptionReviewPage(auth: auth, id: 'r1')));
      await t.pumpAndSettle();
      expect(find.text('قبول وتسجيل إداري'), findsNothing);
      expect(find.text('رفض الطلب'), findsNothing);
      await t.pumpWidget(const SizedBox());
      auth.dispose();
      final staff = await coach(
        (m, p, b, token) async =>
            p.endsWith('/options') ? {'configured': false} : <Json>[],
      );
      await t.pumpWidget(shell(StaffAttendanceExceptionPage(auth: staff)));
      await t.pumpAndSettle();
      expect(find.byType(DropdownButtonFormField<String>), findsNothing);
      expect(find.text('إرسال للمراجعة'), findsNothing);
      await t.pumpWidget(const SizedBox());
      staff.dispose();
    },
  );
  for (final width in [360.0, 390.0, 430.0]) {
    testWidgets('Claim form at $width RTL/English dark/light and 200% text', (
      t,
    ) async {
      t.view.physicalSize = Size(width, 1000);
      t.view.devicePixelRatio = 1;
      addTearDown(t.view.resetPhysicalSize);
      addTearDown(t.view.resetDevicePixelRatio);
      final auth = await coach(
        (m, p, b, token) async => p.endsWith('/options') ? options : [claim],
      );
      for (final locale in [const Locale('ar'), const Locale('en')]) {
        for (final dark in [true, false]) {
          await t.pumpWidget(
            MaterialApp(
              locale: locale,
              supportedLocales: const [Locale('ar'), Locale('en')],
              localizationsDelegates: GlobalMaterialLocalizations.delegates,
              theme: dark ? guardianTheme() : AcademyTheme.light(),
              home: MediaQuery(
                data: MediaQueryData(
                  size: Size(width, 1000),
                  textScaler: const TextScaler.linear(2),
                ),
                child: StaffAttendanceExceptionPage(auth: auth),
              ),
            ),
          );
          await t.pumpAndSettle();
          await reach(t, find.text('إرسال للمراجعة'));
          expect(t.takeException(), isNull);
        }
      }
      await t.pumpWidget(const SizedBox());
      auth.dispose();
    });
  }
}
