import 'package:academy_mobile/core/api/api_client.dart';
import 'package:academy_mobile/core/auth/auth_controller.dart';
import 'package:academy_mobile/core/storage/refresh_store.dart';
import 'package:academy_mobile/features/guardian/guardian_ui.dart';
import 'package:academy_mobile/features/owner/owner_followups.dart';
import 'package:flutter/material.dart';
import 'package:flutter_localizations/flutter_localizations.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:integration_test/integration_test.dart';

void main() {
  final binding = IntegrationTestWidgetsFlutterBinding.ensureInitialized();
  testWidgets(
    'Owner reads actual recorded absence evidence; closing never writes child attendance or money',
    (t) async {
      expect(const bool.fromEnvironment('ISOLATED_DEMO'), isTrue);
      expect(
        const String.fromEnvironment('API_BASE_URL'),
        'http://127.0.0.1:5240',
      );
      final auth = AuthController(
        HttpApiTransport(
          AppConfig('Demo', const String.fromEnvironment('API_BASE_URL')),
        ),
        SecureRefreshStore('academy.v3.recorded-absence.journey'),
      );
      await auth.restore();
      if (auth.session != null) {
        await auth.logout();
      }
      final challenge = await auth.requestOtp('01099900010');
      await auth.verify('01099900010', challenge, '246810');
      expect(
        auth.session!.selected!.academyId,
        '30000000-0000-0000-0000-000000000003',
      );
      const group = 'f6814615-520c-adaa-123b-6d8aed2c2eee';
      final options = obj(
        await auth.request('GET', '/api/v1/manage/owner-followups/options'),
      );
      final c = rows(options['candidates']).firstWhere(
        (c) =>
            c['condition'] == 'GroupRecordedAbsences' &&
            obj(c['source'])['groupId'] == group,
      );
      expect(obj(obj(c['source'])['recordedAbsences'])['playerCount'], 1);
      final financial = await auth.request(
        'GET',
        '/api/v1/reports/financial?from=2026-09-01&to=2026-09-28',
      );
      final created = c['activeFollowUpId'] == null
          ? obj(
              await auth.request(
                'POST',
                '/api/v1/manage/owner-followups/',
                body: {
                  'trainingGroupId': group,
                  'condition': c['condition'],
                  'requestId': followUpRequestId(),
                },
              ),
            )
          : <String, dynamic>{'id': c['activeFollowUpId']};
      final id = textOf(created['id']),
          before = obj(
            await auth.request(
              'GET',
              '/api/v1/manage/owner-followups/${created['id']}',
            ),
          );
      final evidence = obj(obj(before['item'])['evidence']),
          records = rows(evidence['absenceEvidence']);
      expect(records, hasLength(3));
      final session = textOf(records.first['SessionId']);
      final roster = await auth.request(
        'GET',
        '/api/v1/attendance/sessions/$session/players',
      );
      await t.pumpWidget(
        MaterialApp(
          locale: const Locale('ar'),
          supportedLocales: const [Locale('ar')],
          localizationsDelegates: GlobalMaterialLocalizations.delegates,
          theme: guardianTheme(),
          initialRoute: '/detail',
          routes: {
            '/': (_) => OwnerFollowUpsPage(auth: auth),
            '/detail': (_) => OwnerFollowUpPage(auth: auth, id: id),
          },
        ),
      );
      Future<void> wait(Finder f) async {
        for (var i = 0; i < 180 && f.evaluate().isEmpty; i++) {
          await t.pump(const Duration(milliseconds: 100));
        }
        expect(f, findsWidgets);
        await t.pumpAndSettle();
      }

      await wait(find.textContaining('1 لاعب فريد · 1 تسجيل'));
      if (const bool.fromEnvironment('CAPTURE_VISUALS')) {
        await binding.takeScreenshot('phase-absence-followup-ios');
      }
      final link = find.text('مراجعة كشف ${records.first['SessionDate']}');
      await t.ensureVisible(link);
      await t.pumpAndSettle();
      await t.tap(link);
      await wait(find.text('كشف الحضور من دليل المتابعة'));
      await wait(find.text('لاعب متابعة الغياب — بيانات اصطناعية'));
      expect(
        find.text('حفظ الحضور'),
        findsNothing,
      ); // Historical register uses the existing correction workflow.
      await t.tap(find.byType(BackButton));
      await t.pumpAndSettle();
      await t.ensureVisible(find.byType(DropdownButtonFormField<String>).first);
      await t.pumpAndSettle();
      await t.tap(find.byType(DropdownButtonFormField<String>).first);
      await t.pumpAndSettle();
      await t.tap(find.text('مغلق').last);
      await t.pumpAndSettle();
      final reason = find.widgetWithText(
        TextField,
        'سبب إغلاق أو رفض المتابعة',
      );
      await t.ensureVisible(reason);
      await t.enterText(reason, 'مراجعة بشرية اصطناعية دون تغيير سجل الطفل');
      await t.testTextInput.receiveAction(TextInputAction.done);
      await t.pumpAndSettle();
      await t.ensureVisible(find.text('حفظ المتابعة'));
      await t.pumpAndSettle();
      await t.tap(find.text('حفظ المتابعة'));
      await wait(find.text('متابعات المالك'));
      final saved = obj(
        await auth.request('GET', '/api/v1/manage/owner-followups/$id'),
      );
      expect(obj(saved['item'])['state'], 'Closed');
      expect(obj(saved['item'])['conditionActive'], true);
      expect(obj(obj(saved['item'])['evidence']), evidence);
      final afterRoster = obj(
        await auth.request(
          'GET',
          '/api/v1/attendance/sessions/$session/players',
        ),
      );
      expect(afterRoster['players'], obj(roster)['players']);
      expect(afterRoster['session'], obj(roster)['session']);
      expect(
        afterRoster['canManageAttendance'],
        obj(roster)['canManageAttendance'],
      );
      expect(
        await auth.request(
          'GET',
          '/api/v1/reports/financial?from=2026-09-01&to=2026-09-28',
        ),
        financial,
      );
      await auth.logout();
      await t.pumpAndSettle();
      expect(find.textContaining('1 لاعب فريد · 1 تسجيل'), findsNothing);
    },
  );
}
