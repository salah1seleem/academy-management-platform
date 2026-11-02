import 'package:academy_mobile/core/api/api_client.dart';
import 'package:academy_mobile/core/auth/auth_controller.dart';
import 'package:academy_mobile/core/storage/refresh_store.dart';
import 'package:academy_mobile/core/theme/app_theme.dart';
import 'package:academy_mobile/features/admin/native_player_workspace.dart';
import 'package:academy_mobile/features/guardian/guardian_ui.dart';
import 'package:flutter/material.dart';
import 'package:flutter_localizations/flutter_localizations.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:integration_test/integration_test.dart';

void main() {
  final binding = IntegrationTestWidgetsFlutterBinding.ensureInitialized();
  testWidgets(
    'actual native player name English name and birth date persist and restore without changing enrollments',
    (t) async {
      expect(const bool.fromEnvironment('ISOLATED_DEMO'), true);
      const base = String.fromEnvironment('API_BASE_URL');
      expect(base, 'http://127.0.0.1:5278');
      final auth = AuthController(
        HttpApiTransport(AppConfig('Demo', base)),
        SecureRefreshStore('v45.5278.os-media-entry.01099900010'),
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
      const id = '90445c63-4f72-9320-a1d1-aa99990f6d02';
      Future<Json> read() async =>
          obj(await auth.request('GET', '/api/v1/people/players/$id'));
      final original = await read();
      expect(original['playerCode'], 'FB-0011');
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

      Future<void> show() async {
        await t.pumpWidget(const SizedBox());
        await t.pumpAndSettle();
        await t.pumpWidget(
          MaterialApp(
            locale: const Locale('ar'),
            supportedLocales: const [Locale('ar')],
            localizationsDelegates: GlobalMaterialLocalizations.delegates,
            theme: AcademyTheme.dark(),
            home: NativePlayerWorkspace(auth: auth, id: id),
          ),
        );
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
          'لاعب تعديل اصطناعي 11',
          'Synthetic Player Eleven',
          changedDate,
        );
        await binding.takeScreenshot(
          'phase-approved-player-identity-persisted',
        );
        await show();
        expect(
          t.widget<TextFormField>(field('اسم اللاعب')).controller!.text,
          'لاعب تعديل اصطناعي 11',
        );
        expect(
          t.widget<TextFormField>(field('الاسم بالإنجليزية')).controller!.text,
          'Synthetic Player Eleven',
        );
        expect(
          t.widget<TextFormField>(field('تاريخ الميلاد')).controller!.text,
          iso(changedDate),
        );
      } finally {
        await save(
          original['arabicName'] as String,
          (original['englishName'] ?? '') as String,
          date,
        );
        final restored = await read();
        expect(restored['arabicName'], original['arabicName']);
        expect(restored['englishName'], original['englishName']);
        expect(restored['dateOfBirth'], original['dateOfBirth']);
        await binding.takeScreenshot('phase-approved-player-identity-restored');
        await t.pumpWidget(const SizedBox());
        auth.dispose();
      }
      expect(t.takeException(), isNull);
    },
  );
}
