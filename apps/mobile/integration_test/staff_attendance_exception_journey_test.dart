import 'dart:convert';

import 'package:academy_mobile/app/app.dart';
import 'package:academy_mobile/core/api/api_client.dart';
import 'package:academy_mobile/core/auth/auth_controller.dart';
import 'package:academy_mobile/core/storage/refresh_store.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:integration_test/integration_test.dart';

// Creates only synthetic employment and a claim in an explicitly isolated Demo.
// No salary payment, GPS/biometric attestation, real communication or data reset.
void main() {
  final binding = IntegrationTestWidgetsFlutterBinding.ensureInitialized();
  testWidgets(
    'Staff native claim to independent Owner review and persisted staff history',
    (tester) async {
      expect(const bool.fromEnvironment('ISOLATED_DEMO'), isTrue);
      final ownerTransport = HttpApiTransport(
        AppConfig('Demo', const String.fromEnvironment('API_BASE_URL')),
      );
      final staffTransport = HttpApiTransport(
        AppConfig('Demo', const String.fromEnvironment('API_BASE_URL')),
      );
      final owner = AuthController(
        ownerTransport,
        SecureRefreshStore('academy.v3.exception.owner-test'),
      );
      final staff = AuthController(
        staffTransport,
        SecureRefreshStore('academy.v3.exception.staff-test'),
      );
      Future<void> login(AuthController auth, String phone) async {
        await auth.restore();
        if (auth.session != null) {
          await auth.logout();
        }
        final challenge = await auth.requestOtp(phone);
        await auth.verify(phone, challenge, '246810');
      }

      await login(owner, '01099900010');
      expect(
        owner.session!.selected!.academyId,
        '30000000-0000-0000-0000-000000000003',
      );
      final stamp = DateTime.now().microsecondsSinceEpoch.toString(),
          phone = '015${stamp.substring(stamp.length - 8)}',
          name = 'موظف محاكي اصطناعي $stamp';
      final options =
          await owner.request('GET', '/api/v1/manage/structure/options') as Map;
      final branch = (options['branches'] as List).first as Map;
      final member = await owner.request(
        'POST',
        '/api/v1/manage/coaches',
        body: {
          'displayName': name,
          'phoneNumber': phone,
          'email': 'native-claim.$stamp@example.test',
          'isActive': true,
          'groupIds': <String>[],
        },
      ) as Map;
      await owner.request(
        'POST',
        '/api/v1/manage/employees/',
        body: {
          'name': name,
          'phone': phone,
          'jobTitle': 'موظف عرض بلا راتب مالي',
          'branchId': branch['id'],
          'membershipId': member['membershipId'],
          'monthlySalary': 0,
          'contractStart': '2026-09-01',
          'contractEnd': null,
          'isActive': true,
        },
      );
      await login(staff, phone);
      final ownOptions = await staff.request(
        'GET',
        '/api/v1/staff/attendance-exceptions/options',
      ) as Map;
      final branchName =
          ((ownOptions['branches'] as List).singleWhere(
                (b) => (b as Map)['id'] == branch['id'],
              ) as Map)['name']
              as String;
      final before =
          await owner.request('GET', '/api/v1/reports/owner-summary') as Map;
      Future<void> waitFor(Finder f) async {
        for (var i = 0; i < 150 && f.evaluate().isEmpty; i++) {
          await tester.pump(const Duration(milliseconds: 150));
        }
        expect(f, findsWidgets);
        await tester.pumpAndSettle();
      }

      Future<void> reach(Finder f) async {
        await tester.scrollUntilVisible(
          f,
          180,
          scrollable: find.byType(Scrollable).first,
        );
        await tester.ensureVisible(f);
        await tester.pumpAndSettle();
      }

      Future<void> capture(String n) async {
        if (const bool.fromEnvironment('CAPTURE_VISUALS')) {
          await binding.takeScreenshot('phase-staff-exception-$n');
        }
      }

      Future<void> ownScreen() async {
        await tester.pumpWidget(const SizedBox());
        await tester.pumpWidget(AcademyApp(auth: staff, environment: 'Demo'));
        await waitFor(find.text('المزيد'));
        await tester.tap(find.text('المزيد'));
        await tester.pumpAndSettle();
        await reach(find.text('طلب إثبات حضوري'));
        await tester.tap(find.text('طلب إثبات حضوري'));
        await waitFor(find.text('إرسال للمراجعة'));
      }

      await ownScreen();
      await tester.tap(find.byType(DropdownButtonFormField<String>).first);
      await tester.pumpAndSettle();
      await tester.tap(find.text(branchName).last);
      await tester.pumpAndSettle();
      await reach(find.text('إرسال للمراجعة'));
      await capture('claim-form');
      await tester.tap(find.text('إرسال للمراجعة'));
      await waitFor(find.text('تم إرسال الطلب للمراجعة؛ لم يُسجل الحضور بعد.'));
      await reach(find.text('يحتاج مراجعة'));
      await capture('claim-pending');
      final claims = await staff.request(
        'GET',
        '/api/v1/staff/attendance-exceptions/',
      ) as List;
      expect(claims.length, 1);
      final claim = claims.single as Map;
      expect(claim['status'], 'Pending');
      expect(claim['attendanceId'], isNull);
      await tester.pumpWidget(const SizedBox());
      await tester.pumpWidget(AcademyApp(auth: owner, environment: 'Demo'));
      await waitFor(find.text('المزيد'));
      await tester.tap(find.text('المزيد'));
      await tester.pumpAndSettle();
      await reach(find.text('مراجعة إثبات حضور الموظفين'));
      await tester.tap(find.text('مراجعة إثبات حضور الموظفين'));
      await waitFor(find.textContaining(name));
      await reach(find.textContaining(name));
      await tester.tap(find.textContaining(name));
      await waitFor(find.text('قبول وتسجيل إداري'));
      await capture('owner-review');
      await reach(find.text('قبول وتسجيل إداري'));
      await tester.tap(find.text('قبول وتسجيل إداري'));
      await waitFor(find.text('تأكيد القرار'));
      await tester.tap(find.text('تأكيد القرار'));
      await waitFor(find.text('تم قبول الطلب وتسجيل الحضور إداريًا.'));
      await capture('owner-approved');
      final saved = await staff.request(
        'GET',
        '/api/v1/staff/attendance-exceptions/',
      ) as List;
      expect((saved.single as Map)['status'], 'Approved');
      expect((saved.single as Map)['attendanceId'], isNotNull);
      final detail = await owner.request(
        'GET',
        '/api/v1/manage/staff-attendance-exceptions/${claim['id']}',
      ) as Map;
      expect((detail['currentAttendance'] as Map)['status'], 'Present');
      expect(detail['beforeSnapshot'], 'null');
      expect(detail['afterSnapshot'], isNotNull);
      await ownScreen();
      await reach(find.text('مقبول إداريًا'));
      await capture('staff-approved');
      expect(find.text('مقبول إداريًا'), findsOneWidget);
      final after =
          await owner.request('GET', '/api/v1/reports/owner-summary') as Map;
      expect(
        jsonEncode(after['collections']),
        jsonEncode(before['collections']),
      );
      expect(
        jsonEncode(after['paymentRequests']),
        jsonEncode(before['paymentRequests']),
      );
      expect(tester.takeException(), isNull);
      await staff.logout();
      await owner.logout();
      await tester.pumpWidget(const SizedBox());
      staff.dispose();
      owner.dispose();
      staffTransport.close();
      ownerTransport.close();
    },
  );
}
