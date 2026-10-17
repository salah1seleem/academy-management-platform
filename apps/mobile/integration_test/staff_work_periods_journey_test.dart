import 'support/role_intro.dart';

import 'dart:convert';

import 'package:academy_mobile/app/app.dart';
import 'package:academy_mobile/core/api/api_client.dart';
import 'package:academy_mobile/core/auth/auth_controller.dart';
import 'package:academy_mobile/core/storage/refresh_store.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:integration_test/integration_test.dart';

void main() {
  final binding = IntegrationTestWidgetsFlutterBinding.ensureInitialized();
  testWidgets(
    'Owner declares separate periods; Coach reads only own selected workday and refreshes persisted corrections',
    (t) async {
      expect(const bool.fromEnvironment('ISOLATED_DEMO'), isTrue);
      final transport = HttpApiTransport(
        AppConfig('Demo', const String.fromEnvironment('API_BASE_URL')),
      );
      final staffTransport = HttpApiTransport(
        AppConfig('Demo', const String.fromEnvironment('API_BASE_URL')),
      );
      final owner = AuthController(
        transport,
        SecureRefreshStore('academy.v3.period.owner-test'),
      );
      final staff = AuthController(
        staffTransport,
        SecureRefreshStore('academy.v3.period.staff-test'),
      );
      Future<void> login(AuthController a, String phone) async {
        await a.restore();
        if (a.session != null) await a.logout();
        final challenge = await a.requestOtp(phone);
        await a.verify(phone, challenge, '246810');
      }

      await login(owner, '01099900010');
      expect(
        owner.session!.selected!.academyId,
        '30000000-0000-0000-0000-000000000003',
      );
      final stamp = DateTime.now().microsecondsSinceEpoch.toString(),
          phone = '015${stamp.substring(stamp.length - 8)}',
          name = 'موظف فترات محاكي اصطناعي $stamp';
      final options =
          await owner.request('GET', '/api/v1/manage/structure/options') as Map;
      final member = await owner.request(
        'POST',
        '/api/v1/manage/coaches',
        body: {
          'displayName': name,
          'phoneNumber': phone,
          'email': 'native-period.$stamp@example.test',
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
          'jobTitle': 'عرض فترات بلا صرف مالي',
          'branchId': ((options['branches'] as List).first as Map)['id'],
          'membershipId': member['membershipId'],
          'monthlySalary': 0,
          'contractStart': '2026-09-01',
          'isActive': true,
        },
      );
      final root = '/api/v1/manage/staff-attendance/${member['membershipId']}';
      final calendar = await owner.request('GET', root) as Map;
      final today = DateTime.parse(calendar['today'] as String),
          date = today.subtract(const Duration(days: 2));
      final workDate = date.toIso8601String().substring(0, 10);
      final before =
          await owner.request('GET', '/api/v1/reports/owner-summary') as Map;
      await owner.request(
        'PUT',
        root,
        body: {
          'date': workDate,
          'status': 'Present',
          'checkIn': '08:00:00',
          'checkOut': '10:00:00',
          'approvedDeduction': 0,
        },
      );
      var d = await owner.request('GET', '$root/periods?date=$workDate') as Map;
      await owner.request(
        'POST',
        '$root/periods',
        idempotencyKey: 'native-period-add-$stamp',
        body: {
          'date': workDate,
          'version': d['version'],
          'startTime': '14:00:00',
          'endTime': '16:00:00',
          'endDate': workDate,
        },
      );
      d = await owner.request('GET', '$root/periods?date=$workDate') as Map;
      final first = (d['items'] as List).first as Map;
      await owner.request(
        'PUT',
        "$root/periods/${first['id']}",
        idempotencyKey: 'native-period-confirm-$stamp',
        body: {
          'date': workDate,
          'version': d['version'],
          'periodVersion': first['version'],
          'startTime': '08:00:00',
          'endTime': '10:00:00',
          'endDate': workDate,
          'reason': 'تأكيد الفترة الأصلية الاصطناعية من المالك',
        },
      );
      await login(staff, phone);
      Future<void> waitFor(Finder f) async {
        for (var i = 0; i < 150 && f.evaluate().isEmpty; i++) {
          await t.pump(const Duration(milliseconds: 150));
        }
        expect(f, findsWidgets);
        await t.pumpAndSettle();
      }

      Future<void> reach(Finder f) async {
        await t.scrollUntilVisible(
          f,
          180,
          scrollable: find.byType(Scrollable).first,
        );
        await t.ensureVisible(f);
        await t.pumpAndSettle();
      }

      await t.pumpWidget(AcademyApp(auth: staff, environment: 'Demo'));
      await skipRoleIntroIfPresent(t);
      await waitFor(find.text('المزيد'));
      await t.tap(find.text('المزيد'));
      await t.pumpAndSettle();
      await reach(find.text('فترات عملي'));
      await t.tap(find.text('فترات عملي'));
      await waitFor(find.text('اختيار يوم'));
      await t.tap(find.text('اختيار يوم'));
      await t.pumpAndSettle();
      if (date.month != today.month || date.year != today.year) {
        await t.tap(find.byTooltip('الشهر السابق'));
        await t.pumpAndSettle();
      }
      await t.tap(find.text('${date.day}').last);
      await t.pumpAndSettle();
      await t.tap(find.text('عرض اليوم'));
      await waitFor(find.text('إجمالي الفترات: 240 دقيقة'));
      await reach(find.text('الفترة 2'));
      expect(find.text('حفظ الفترة'), findsNothing);
      expect(find.byType(TextFormField), findsNothing);
      expect(t.takeException(), isNull);
      if (const bool.fromEnvironment('CAPTURE_VISUALS')) {
        await binding.takeScreenshot('phase-staff-periods-own');
      }
      var own = await staff.request(
        'GET',
        '/api/v1/staff/work-periods?date=$workDate',
      ) as Map;
      expect(own['canEdit'], false);
      expect((own['items'] as List).length, 2);
      expect(own['totalWorkedSeconds'], 14400);
      expect(jsonEncode(own), isNot(contains('monthlySalary')));
      d = await owner.request('GET', '$root/periods?date=$workDate') as Map;
      final second = (d['items'] as List)[1] as Map;
      await owner.request(
        'PUT',
        "$root/periods/${second['id']}",
        idempotencyKey: 'native-period-edit-$stamp',
        body: {
          'date': workDate,
          'version': d['version'],
          'periodVersion': second['version'],
          'startTime': '14:30:00',
          'endTime': '16:00:00',
          'endDate': workDate,
          'reason': 'تصحيح اصطناعي يظهر في سجل الموظف',
        },
      );
      await reach(find.text('تحديث'));
      await t.tap(find.text('تحديث'));
      await waitFor(find.text('إجمالي الفترات: 210 دقيقة'));
      await reach(find.text('الفترة 2'));
      expect(find.textContaining('14:30'), findsOneWidget);
      if (const bool.fromEnvironment('CAPTURE_VISUALS')) {
        await binding.takeScreenshot('phase-staff-periods-corrected');
      }
      final report = await owner.request(
        'GET',
        '/api/v1/reports/staff-daily?coachId=${member['membershipId']}&allDates=true',
      ) as Map;
      expect(report['totalCount'], 1);
      expect(report['totalDeductions'], 0);
      expect(
        (((report['items'] as List).single as Map)['workPeriods']
            as Map)['totalWorkedSeconds'],
        12600,
      );
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
      await staff.logout();
      await owner.logout();
      await t.pumpWidget(const SizedBox());
      staff.dispose();
      owner.dispose();
      transport.close();
      staffTransport.close();
    },
  );
}
