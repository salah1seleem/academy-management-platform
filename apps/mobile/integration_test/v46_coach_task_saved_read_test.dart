import 'package:academy_mobile/core/api/api_client.dart';
import 'package:academy_mobile/core/auth/auth_controller.dart';
import 'package:academy_mobile/core/storage/refresh_store.dart';
import 'package:academy_mobile/core/theme/app_theme.dart';
import 'package:academy_mobile/features/guardian/guardian_ui.dart';
import 'package:academy_mobile/features/shared/coach_tasks.dart';
import 'package:flutter/material.dart';
import 'package:flutter_localizations/flutter_localizations.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:integration_test/integration_test.dart';

import 'v45_single_renewal_saved_receipt_test.dart'
    show SavedRenewalReadTransport;

void main() {
  final binding = IntegrationTestWidgetsFlutterBinding.ensureInitialized();
  testWidgets(
    'Owner and assigned coach read exact saved closed task and three audit changes without repeating writes',
    (t) async {
      expect(const bool.fromEnvironment('ISOLATED_DEMO'), true);
      const base = String.fromEnvironment('API_BASE_URL');
      expect(base, 'http://127.0.0.1:5278');
      const id = '718cfe8c-20c8-43f9-a77d-1956335fafaa';
      for (final person in [
        ('01099900010', 'AcademyOwner'),
        ('01099900020', 'Coach'),
      ]) {
        final auth = AuthController(
          SavedRenewalReadTransport(AppConfig('Demo', base)),
          SecureRefreshStore('v46.coach-tasks.${person.$1}'),
        );
        await auth.restore();
        if (auth.session == null) {
          final challenge = await auth.requestOtp(person.$1);
          await auth.verify(person.$1, challenge, '246810');
        }
        expect(auth.session!.selected!.role, person.$2);
        expect(
          auth.session!.selected!.academyId,
          '30000000-0000-0000-0000-000000000003',
        );
        final route = person.$2 == 'Coach'
            ? '/api/v1/my-coach-tasks/$id'
            : '/api/v1/manage/coach-tasks/$id';
        final saved = obj(await auth.request('GET', route));
        expect(saved['id'], id);
        expect(saved['kind'], 'Attendance');
        expect(saved['state'], 'Closed');
        expect(rows(saved['history']).length, 3);
        await t.pumpWidget(
          MaterialApp(
            locale: const Locale('ar'),
            supportedLocales: const [Locale('ar')],
            localizationsDelegates: GlobalMaterialLocalizations.delegates,
            theme: AcademyTheme.dark(),
            home: CoachTasksPage(key: ValueKey(person.$1), auth: auth, id: id),
          ),
        );
        for (
          var i = 0;
          i < 200 && find.text('سجل المهمة').evaluate().isEmpty;
          i++
        ) {
          await t.pump(const Duration(milliseconds: 150));
        }
        await t.pumpAndSettle();
        expect(find.text('سجل المهمة'), findsOneWidget);
        expect(find.text('حفظ حالة المهمة'), findsNothing);
        await t.scrollUntilVisible(
          find.text(textOf(obj(saved['group'])['arabicName'])),
          -250,
          scrollable: find.byType(Scrollable).first,
        );
        await binding.takeScreenshot(
          'phase-approved-coach-task-saved-${person.$2 == 'Coach' ? 'coach' : 'owner'}',
        );
        await t.scrollUntilVisible(
          find.text('انتهت رحلة متابعة اصطناعية دون كتابة حضور تلقائي').last,
          200,
          scrollable: find.byType(Scrollable).first,
        );
        await binding.takeScreenshot(
          'phase-approved-coach-task-history-${person.$2 == 'Coach' ? 'coach' : 'owner'}',
        );
        final after = obj(await auth.request('GET', route));
        expect(after['version'], saved['version']);
        expect(rows(after['history']).length, 3);
        expect(t.takeException(), isNull);
        await t.pumpWidget(const SizedBox());
        auth.dispose();
      }
    },
  );
}
