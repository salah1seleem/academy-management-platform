import 'package:academy_mobile/app/app.dart';
import 'package:academy_mobile/core/api/api_client.dart';
import 'package:academy_mobile/core/auth/auth_controller.dart';
import 'package:academy_mobile/core/storage/refresh_store.dart';
import 'package:academy_mobile/core/theme/appearance.dart';
import 'package:academy_mobile/features/guardian/guardian_ui.dart';
import 'package:academy_mobile/features/owner/native_staff_management.dart';
import 'package:academy_mobile/features/shared/native_media_groups.dart';
import 'package:academy_mobile/features/shared/private_media.dart';
import 'package:academy_mobile/features/shared/role_navigation.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:integration_test/integration_test.dart';

void main() {
  final binding = IntegrationTestWidgetsFlutterBinding.ensureInitialized();
  testWidgets(
    'actual owner and authorized admin open the protected group upload through native Operations; saved employee hint remains separated from contract input',
    (t) async {
      expect(const bool.fromEnvironment('ISOLATED_DEMO'), isTrue);
      const base = String.fromEnvironment('API_BASE_URL');
      expect(base, 'http://127.0.0.1:5278');
      expect((t.view.physicalSize / t.view.devicePixelRatio).width, 375);
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

      Future<void> nav(String label) async {
        final f = find.descendant(
          of: find.byType(NavigationBar),
          matching: find.text(label),
        );
        await t.tap(f);
        await t.pumpAndSettle();
      }

      Future<void> back() async {
        Navigator.of(t.element(find.byType(UploadSessionMedia))).pop();
        await t.pumpAndSettle();
      }

      for (final role in [
        ('AcademyOwner', '01099900010', 'owner'),
        ('AcademyAdmin', '01099900011', 'admin'),
      ]) {
        final auth = AuthController(
          HttpApiTransport(AppConfig('Demo', base)),
          SecureRefreshStore('v45.5278.os-media-entry.${role.$2}'),
        );
        await auth.restore();
        if (auth.session == null) {
          final c = await auth.requestOtp(role.$2);
          await auth.verify(role.$2, c, '246810');
        }
        expect(auth.session!.selected!.role, role.$1);
        expect(
          auth.session!.selected!.academyId,
          '30000000-0000-0000-0000-000000000003',
        );
        final appearance = Appearance();
        for (final theme in ['football', 'general']) {
          await appearance.completeRoleIntro(role.$1, theme);
        }
        await t.pumpWidget(
          AcademyApp(
            key: ValueKey(role.$1),
            auth: auth,
            environment: 'Demo',
            appearance: appearance,
          ),
        );
        await waitFor(find.byType(NativeRoleTabs));
        await nav('العمليات');
        final open = find.text('رفع صور وفيديو');
        await reach(open);
        await t.tap(open);
        await t.pumpAndSettle();
        await waitFor(find.byType(NativeMediaGroups));
        final options = obj(
          await auth.request('GET', '/api/v1/assets/media-groups?pageSize=25'),
        );
        final group = rows(options['items']).first;
        await waitFor(find.text(textOf(group['arabicName'])));
        await reach(find.text(textOf(group['arabicName'])));
        await binding.takeScreenshot(
          'phase-approved-${role.$3}-media-group-options-w375',
        );
        await t.tap(find.text(textOf(group['arabicName'])));
        await t.pumpAndSettle();
        await waitFor(find.byType(UploadSessionMedia));
        expect(
          t.widget<UploadSessionMedia>(find.byType(UploadSessionMedia)).groupId,
          group['id'],
        );
        await waitFor(find.text('لا يوجد طفل يمكن التعرف عليه في هذه اللقطة'));
        await binding.takeScreenshot(
          'phase-approved-${role.$3}-media-upload-form-w375',
        );
        await back();
        Navigator.of(t.element(find.byType(NativeMediaGroups))).pop();
        await t.pumpAndSettle();
        if (role.$1 == 'AcademyOwner') {
          await nav('المزيد');
          final staff = find.text('الموظفون والصلاحيات');
          await reach(staff);
          await t.tap(staff);
          await t.pumpAndSettle();
          await waitFor(find.byType(NativeStaffManagement));
          final search = find.widgetWithText(
            TextField,
            'بحث بالاسم أو الهاتف أو الوظيفة',
          );
          await reach(search);
          await t.enterText(search, 'إداري حصص اصطناعي992');
          await t.pumpAndSettle();
          final details = find.text('بيانات الموظف');
          await reach(details);
          await t.tap(details);
          await t.pumpAndSettle();
          await waitFor(find.byType(NativeEmployeeEditor));
          final hint = find.text(
            'تغييرات الراتب والعقد تحفظ في سجلهما المؤرخ.',
          );
          await reach(hint);
          await t.ensureVisible(
            find.widgetWithText(TextFormField, 'بداية العقد'),
          );
          await t.pumpAndSettle();
          final hintBounds = t.getRect(hint);
          final startBounds = t.getRect(
            find.widgetWithText(TextFormField, 'بداية العقد'),
          );
          expect(startBounds.top, greaterThan(hintBounds.bottom));
          await binding.takeScreenshot(
            'phase-approved-employee-contract-hint-spacing-w375',
          );
        }
        expect(t.takeException(), isNull);
        await t.pumpWidget(const SizedBox());
        auth.dispose();
        appearance.dispose();
      }
    },
  );
}
