import 'package:academy_mobile/core/api/api_client.dart';
import 'package:academy_mobile/core/auth/auth_controller.dart';
import 'package:academy_mobile/core/storage/refresh_store.dart';
import 'package:academy_mobile/core/theme/app_theme.dart';
import 'package:academy_mobile/features/owner/native_staff_management.dart';
import 'package:academy_mobile/features/guardian/guardian_ui.dart';
import 'package:flutter/material.dart';
import 'package:flutter_localizations/flutter_localizations.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:integration_test/integration_test.dart';

void main() {
  final binding = IntegrationTestWidgetsFlutterBinding.ensureInitialized();
  testWidgets(
    'actual owner employee creation and edit preserve the linked synthetic account, zero salary and financial totals',
    (t) async {
      expect(const bool.fromEnvironment('ISOLATED_DEMO'), isTrue);
      const base = String.fromEnvironment('API_BASE_URL');
      expect(base, 'http://127.0.0.1:5278');
      expect((t.view.physicalSize / t.view.devicePixelRatio).width, 375);
      final auth = AuthController(
        HttpApiTransport(AppConfig('Demo', base)),
        SecureRefreshStore('v45.5278.employee.owner'),
      );
      await auth.restore();
      if (auth.session == null) {
        final c = await auth.requestOtp('01099900010');
        await auth.verify('01099900010', c, '246810');
      }
      expect(
        auth.session!.selected!.academyId,
        '30000000-0000-0000-0000-000000000003',
      );
      expect(auth.session!.selected!.role, 'AcademyOwner');
      final before = obj(
        await auth.request(
          'GET',
          '/api/v1/reports/financial?allDates=true&pageSize=1',
        ),
      );
      const name = 'إداري حصص اصطناعي992', phone = '01099900992';
      const member = 'f1cb3035-ca00-48dc-b008-b0a1eda9e397';
      final options = obj(
        await auth.request('GET', '/api/v1/manage/employees'),
      );
      final branch = rows(options['branches'])
          .singleWhere((b) => b['name'] == 'فرع الندى التجريبي');
      final existing = rows(options['items'])
          .where((e) => e['membershipId'] == member)
          .firstOrNull;
      Future<void> waitFor(Finder f) async {
        for (var i = 0; i < 240 && f.evaluate().isEmpty; i++) {
          await t.pump(const Duration(milliseconds: 150));
        }
        expect(f, findsWidgets);
        await t.pumpAndSettle();
      }

      Future<void> reach(Finder f) async {
        final scroll = find.byType(Scrollable).first;
        t.state<ScrollableState>(scroll).position.jumpTo(0);
        await t.pumpAndSettle();
        await t.scrollUntilVisible(f, 150, scrollable: scroll, maxScrolls: 100);
        await t.ensureVisible(f.last);
        await t.pumpAndSettle();
      }

      Future<void> tap(String label) async {
        final f = find.text(label);
        await reach(f);
        await t.tap(f.last);
        await t.pumpAndSettle();
      }

      Future<void> field(String label, String value) async {
        final f = find.widgetWithText(TextFormField, label);
        await reach(f);
        await t.enterText(f, value);
        await t.pumpAndSettle();
      }

      await t.pumpWidget(
        MaterialApp(
          debugShowCheckedModeBanner: false,
          locale: const Locale('ar'),
          supportedLocales: const [Locale('ar')],
          localizationsDelegates: GlobalMaterialLocalizations.delegates,
          theme: AcademyTheme.dark(),
          home: NativeStaffManagement(auth: auth),
        ),
      );
      await waitFor(find.text('إضافة موظف'));
      if (existing == null) {
        await tap('إضافة موظف');
        await field('الاسم', name);
        await field('الهاتف', phone);
        await field('المسمى الوظيفي', 'موظف قبول اصطناعي');
        final branchChoice = find.widgetWithText(
          DropdownButtonFormField<String>,
          'الفرع',
        );
        await reach(branchChoice);
        await t.tap(branchChoice);
        await t.pumpAndSettle();
        await t.tap(find.text(textOf(branch['name'])).last);
        await t.pumpAndSettle();
        final memberChoice = find.widgetWithText(
          DropdownButtonFormField<String>,
          'حساب الدخول المرتبط',
        );
        await reach(memberChoice);
        await t.tap(memberChoice);
        await t.pumpAndSettle();
        await t.tap(find.text(name).last);
        await t.pumpAndSettle();
        await field('الراتب الشهري', '0');
        await tap('حفظ بيانات الموظف');
        await waitFor(find.byType(NativeStaffManagement));
        await waitFor(find.text('المدربون والموظفون'));
      }
      final savedList = obj(
        await auth.request('GET', '/api/v1/manage/employees'),
      );
      final saved = rows(savedList['items'])
          .singleWhere((e) => e['membershipId'] == member);
      final id = textOf(saved['id']);
      debugPrint('Synthetic employee record persisted: $id');
      var detail = obj(
        await auth.request('GET', '/api/v1/manage/employees/$id'),
      );
      expect(detail['name'], name);
      expect(detail['monthlySalary'], 0);
      expect(detail['branchId'], branch['id']);
      expect(detail['membershipId'], member);
      final search = find.widgetWithText(
        TextField,
        'بحث بالاسم أو الهاتف أو الوظيفة',
      );
      await reach(search);
      await t.enterText(search, name);
      await t.pumpAndSettle();
      await tap('بيانات الموظف');
      await waitFor(find.text('حفظ بيانات الموظف'));
      final salary = find.widgetWithText(TextFormField, 'الراتب الشهري');
      await reach(salary);
      expect(
        t
            .widget<TextField>(
              find.descendant(of: salary, matching: find.byType(TextField)),
            )
            .readOnly,
        isTrue,
      );
      const finalJob = 'موظف قبول اصطناعي — تم التحقق';
      await field('المسمى الوظيفي', finalJob);
      await tap('حفظ بيانات الموظف');
      await waitFor(find.text('المدربون والموظفون'));
      detail = obj(await auth.request('GET', '/api/v1/manage/employees/$id'));
      expect(detail['jobTitle'], finalJob);
      expect(detail['monthlySalary'], 0);
      expect(detail['membershipId'], member);
      expect(detail['branchId'], branch['id']);
      await reach(find.textContaining(finalJob));
      await binding.takeScreenshot('phase-approved-employee-record-saved-w375');
      await tap('بيانات الموظف');
      await waitFor(find.text('حفظ بيانات الموظف'));
      final job = find.widgetWithText(TextFormField, 'المسمى الوظيفي');
      await reach(job);
      expect(t.widget<TextFormField>(job).controller!.text, finalJob);
      await binding.takeScreenshot(
        'phase-approved-employee-record-reopened-w375',
      );
      final after = obj(
        await auth.request(
          'GET',
          '/api/v1/reports/financial?allDates=true&pageSize=1',
        ),
      );
      expect(after['netAmount'], before['netAmount']);
      expect(after['totalCount'], before['totalCount']);
      expect(t.takeException(), isNull);
      await t.pumpWidget(const SizedBox());
      await auth.logout();
      auth.dispose();
    },
  );
}
