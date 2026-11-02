import 'package:academy_mobile/core/api/api_client.dart';
import 'package:academy_mobile/core/auth/auth_controller.dart';
import 'package:academy_mobile/core/storage/refresh_store.dart';
import 'package:academy_mobile/app/app.dart';
import 'package:academy_mobile/core/theme/appearance.dart';
import 'package:academy_mobile/features/shared/staff_capabilities.dart';
import 'package:academy_mobile/features/admin/native_player_workspace.dart';
import 'package:academy_mobile/features/guardian/guardian_ui.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:integration_test/integration_test.dart';

import 'v57_role_footer_design_test.dart' show FooterReadTransport;

void main() {
  final binding = IntegrationTestWidgetsFlutterBinding.ensureInitialized();
  testWidgets(
    'actual authorized admin edits and reopens player identity through home and restores it without changing financial or enrollment sources',
    (t) async {
      expect(const bool.fromEnvironment('ISOLATED_DEMO'), true);
      const base = String.fromEnvironment('API_BASE_URL');
      expect(base, 'http://127.0.0.1:5278');
      final transport = FooterReadTransport(AppConfig('Demo', base));
      final auth = AuthController(
        transport,
        SecureRefreshStore('v45.5278.os-media-entry.01099900011'),
      );
      await auth.restore();
      if (auth.session == null) {
        final c = await auth.requestOtp('01099900011');
        await auth.verify('01099900011', c, '246810');
      }
      expect(
        auth.session!.selected!.academyId,
        '30000000-0000-0000-0000-000000000003',
      );
      expect(auth.session!.selected!.role, 'AcademyAdmin');
      const id = '90445c63-4f72-9320-a1d1-aa99990f6d02';
      Future<Json> read() async =>
          obj(await auth.request('GET', '/api/v1/people/players/$id'));
      final original = await read();
      expect(original['playerCode'], 'FB-0011');
      final rights = await StaffCapabilities.load(auth);
      expect(
        rights.allows('people.manage', 'players', 'Edit', [
          'workflow.players.edit',
        ]),
        true,
      );
      const reportPath = '/api/v1/reports/financial?allDates=true&pageSize=1';
      final observer = AuthController(
        HttpApiTransport(AppConfig('Demo', base)),
        SecureRefreshStore('v45.5278.os-media-entry.01099900010'),
      );
      await observer.restore();
      expect(observer.session?.selected?.role, 'AcademyOwner');
      final beforeMoney = obj(await observer.request('GET', reportPath));
      final date = DateTime.parse(original['dateOfBirth'] as String);
      final changedDate = DateTime(
        date.year,
        date.month,
        date.day == 15 ? 16 : 15,
      );
      String iso(DateTime d) =>
          '${d.year}-${d.month.toString().padLeft(2, '0')}-${d.day.toString().padLeft(2, '0')}';
      Future<void> wait(Finder f) async {
        for (var i = 0; i < 240 && f.evaluate().isEmpty; i++) {
          await t.pump(const Duration(milliseconds: 150));
        }
        expect(f, findsWidgets);
        await t.pumpAndSettle();
      }

      final appearance = Appearance();
      for (final theme in ['football', 'general']) {
        await appearance.completeRoleIntro('AcademyAdmin', theme);
      }
      var firstOpen = true;
      Future<void> show() async {
        if (firstOpen) {
          await t.pumpWidget(
            AcademyApp(auth: auth, environment: 'Demo', appearance: appearance),
          );
          await wait(find.text('اللاعبون'));
          await t.ensureVisible(find.text('اللاعبون').last);
          await t.tap(find.text('اللاعبون').last);
          await wait(find.byType(NativePlayerDirectory));
          final search = find.widgetWithText(
            TextFormField,
            'بحث بالاسم أو الكود أو هاتف ولي الأمر',
          );
          await t.enterText(search, 'FB-0011');
          await t.ensureVisible(find.text('بحث اللاعبين'));
          await t.tap(find.text('بحث اللاعبين'));
          await wait(find.text(original['arabicName'] as String));
          await t.ensureVisible(
            find.text(original['arabicName'] as String).last,
          );
          await t.tap(find.text(original['arabicName'] as String).last);
          await wait(find.byType(NativePlayerWorkspace));
          firstOpen = false;
        } else {
          await t.tap(find.byType(BackButton));
          await t.pumpAndSettle();
          await wait(find.byType(NativePlayerWorkspace));
        }
        await wait(find.text('تعديل بيانات اللاعب'));
        await t.ensureVisible(find.text('تعديل بيانات اللاعب'));
        await t.tap(find.text('تعديل بيانات اللاعب'));
        await wait(find.text('حفظ بيانات اللاعب'));
      }

      Finder field(String name) => find.widgetWithText(TextFormField, name);
      Future<void> save(String name, String english, DateTime birthday) async {
        await show();
        await t.enterText(field('اسم اللاعب'), name);
        await t.enterText(field('الاسم بالإنجليزية'), english);
        FocusManager.instance.primaryFocus?.unfocus();
        await t.pumpAndSettle();
        await t.ensureVisible(field('تاريخ الميلاد'));
        await t.tap(field('تاريخ الميلاد'));
        await t.pumpAndSettle();
        final calendar = find.byType(CalendarDatePicker);
        expect(calendar, findsOneWidget);
        await t.tap(
          find
              .descendant(of: calendar, matching: find.text('${birthday.day}'))
              .last,
        );
        await t.pumpAndSettle();
        final dialog = find.byType(DatePickerDialog);
        final ok = MaterialLocalizations.of(t.element(dialog)).okButtonLabel;
        await t.tap(find.text(ok));
        await t.pumpAndSettle();
        await t.ensureVisible(find.text('حفظ بيانات اللاعب'));
        await t.tap(find.text('حفظ بيانات اللاعب'));
        await wait(find.text('حُفظت بيانات اللاعب وأُعيدت قراءتها.'));
        final value = await read();
        expect(value['id'], id);
        expect(value['arabicName'], name);
        expect(value['englishName'] ?? '', english);
        expect(value['dateOfBirth'], iso(birthday));
        expect(value['address'], original['address']);
        expect(value['guardians'], original['guardians']);
        expect(value['enrollments'], original['enrollments']);
        expect(value['photoReference'], original['photoReference']);
      }

      try {
        await save(
          'لاعب قبول إداري اصطناعي11',
          'Synthetic Admin Player Eleven',
          changedDate,
        );
        await binding.takeScreenshot(
          'phase-approved-admin-player-identity-persisted',
        );
        await show();
        expect(
          t.widget<TextFormField>(field('اسم اللاعب')).controller!.text,
          'لاعب قبول إداري اصطناعي11',
        );
        expect(
          t.widget<TextFormField>(field('الاسم بالإنجليزية')).controller!.text,
          'Synthetic Admin Player Eleven',
        );
        expect(
          t.widget<TextFormField>(field('تاريخ الميلاد')).controller!.text,
          iso(changedDate),
        );
      } finally {
        try {
          await save(
            original['arabicName'] as String,
            (original['englishName'] ?? '') as String,
            date,
          );
          final restored = await read();
          final afterMoney = obj(await observer.request('GET', reportPath));
          expect(afterMoney['netAmount'], beforeMoney['netAmount']);
          expect(afterMoney['totalCount'], beforeMoney['totalCount']);
          expect(transport.writes, [
            'PUT /api/v1/people/players/$id',
            'PUT /api/v1/people/players/$id',
          ]);
          expect(restored['arabicName'], original['arabicName']);
          expect(restored['englishName'], original['englishName']);
          expect(restored['dateOfBirth'], original['dateOfBirth']);
          await binding.takeScreenshot(
            'phase-approved-admin-player-identity-restored',
          );
        } finally {
          final actual = await read();
          if ([
            'arabicName',
            'englishName',
            'dateOfBirth',
            'address',
          ].any((key) => actual[key] != original[key])) {
            await observer.request(
              'PUT',
              '/api/v1/people/players/$id',
              body: {
                for (final key in [
                  'arabicName',
                  'englishName',
                  'dateOfBirth',
                  'address',
                  'gender',
                  'heightCm',
                  'weightKg',
                  'preferredFoot',
                  'footballPosition',
                ])
                  key: original[key],
              },
            );
            final recovered = await read();
            for (final key in [
              'arabicName',
              'englishName',
              'dateOfBirth',
              'address',
            ]) {
              expect(recovered[key], original[key]);
            }
            debugPrint(
              'Failure cleanup restored original synthetic identity by API; not UI acceptance.',
            );
          }
        }
        await t.pumpWidget(const SizedBox());
        auth.dispose();
        observer.dispose();
      }
      expect(t.takeException(), isNull);
    },
  );
}
