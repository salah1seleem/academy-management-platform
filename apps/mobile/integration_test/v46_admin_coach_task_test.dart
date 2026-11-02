import 'package:academy_mobile/core/api/api_client.dart';
import 'package:academy_mobile/core/auth/auth_controller.dart';
import 'package:academy_mobile/core/storage/refresh_store.dart';
import 'package:academy_mobile/core/theme/app_theme.dart';
import 'package:academy_mobile/features/guardian/guardian_ui.dart';
import 'package:academy_mobile/features/owner/owner_followups.dart'
    show followUpRequestId;
import 'package:academy_mobile/features/shared/coach_tasks.dart';
import 'package:flutter/material.dart';
import 'package:flutter_localizations/flutter_localizations.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:integration_test/integration_test.dart';

void main() {
  final binding = IntegrationTestWidgetsFlutterBinding.ensureInitialized();
  testWidgets(
    'Actual administrator needs explicit grant, assigns from native form and loses entry after original grants are restored',
    (t) async {
      expect(const bool.fromEnvironment('ISOLATED_DEMO'), true);
      const base = String.fromEnvironment('API_BASE_URL');
      expect(base, 'http://127.0.0.1:5278');
      Future<AuthController> login(String phone, String role) async {
        final auth = AuthController(
          HttpApiTransport(AppConfig('Demo', base)),
          SecureRefreshStore('v46.coach-tasks.$phone'),
        );
        await auth.restore();
        if (auth.session == null) {
          final challenge = await auth.requestOtp(phone);
          await auth.verify(phone, challenge, '246810');
        }
        expect(auth.session!.selected!.role, role);
        expect(
          auth.session!.selected!.academyId,
          '30000000-0000-0000-0000-000000000003',
        );
        return auth;
      }

      final owner = await login('01099900010', 'AcademyOwner');
      final admin = await login('01099900993', 'AcademyAdmin');
      final path = '/api/v1/manage/staff/${admin.session!.selected!.id}/access';
      final original = obj(await owner.request('GET', path));
      expect(original['useActionGrants'], true);
      Json accessBody(Json v) => {
        'allBranches': v['allBranches'],
        'branches': v['selectedBranches'],
        'allSports': v['allSports'],
        'sports': v['selectedSports'],
        'actions': rows(v['actions'])
          ..sort((a, b) => textOf(a['module']).compareTo(textOf(b['module']))),
        'sensitivePermissions': v['selectedSensitivePermissions'],
        'workflowActions': rows(v['workflowActions'])
            .where((a) => a['allowed'] == true)
            .map((a) => a['key'])
            .toList(),
      };
      final restore = accessBody(original);
      expect(
        (restore['workflowActions'] as List).contains(coachTaskPermission),
        false,
      );
      Widget app(Widget home) => MaterialApp(
        locale: const Locale('ar'),
        supportedLocales: const [Locale('ar')],
        localizationsDelegates: GlobalMaterialLocalizations.delegates,
        theme: AcademyTheme.light(),
        home: home,
      );
      Future<void> wait(Finder f) async {
        for (var i = 0; i < 200 && f.evaluate().isEmpty; i++) {
          await t.pump(const Duration(milliseconds: 150));
        }
        expect(f, findsWidgets);
        await t.pumpAndSettle();
      }

      Future<void> tap(String label) async {
        final f = find.text(label);
        await t.scrollUntilVisible(
          f,
          150,
          scrollable: find.byType(Scrollable).first,
          maxScrolls: 60,
        );
        await t.pumpAndSettle();
        await t.tap(f.last);
        await t.pumpAndSettle();
      }

      String? id;
      try {
        await t.pumpWidget(app(Scaffold(body: CoachTasksEntry(auth: admin))));
        for (var i = 0; i < 30; i++) {
          await t.pump(const Duration(milliseconds: 150));
        }
        expect(find.text('مهام الحضور والتقييم'), findsNothing);
        final actions = rows(restore['actions'])
            .map(
              (a) => <String, dynamic>{
                ...a,
                if (a['module'] == 'followups') ...{
                  'read': true,
                  'add': false,
                  'edit': true,
                },
              },
            )
            .toList();
        await owner.request(
          'PUT',
          path,
          body: {
            ...restore,
            'actions': actions,
            'workflowActions': [
              ...restore['workflowActions'] as List,
              coachTaskPermission,
            ],
          },
        );
        final options = obj(
          await admin.request(
            'GET',
            '/api/v1/manage/coach-tasks/options?pageSize=100',
          ),
        );
        final open = obj(
          await admin.request(
            'GET',
            '/api/v1/manage/coach-tasks?state=Open&pageSize=100',
          ),
        );
        expect(open['hasMore'], false);
        final pair = rows(options['items']).firstWhere(
          (p) => !rows(open['items']).any(
            (x) =>
                x['trainingGroupId'] == p['groupId'] &&
                x['kind'] == 'Attendance',
          ),
        );
        await t.pumpWidget(const SizedBox());
        await t.pumpWidget(app(Scaffold(body: CoachTasksEntry(auth: admin))));
        await wait(find.text('مهام الحضور والتقييم'));
        await t.tap(find.text('مهام الحضور والتقييم'));
        await wait(find.text('إسناد مهمة للمدرب'));
        await tap('إسناد مهمة للمدرب');
        await wait(find.text('${pair['group']} · ${pair['coach']}'));
        await tap('${pair['group']} · ${pair['coach']}');
        final note = find.widgetWithText(TextField, 'ملاحظة المتابعة');
        await t.scrollUntilVisible(
          note,
          150,
          scrollable: find.byType(Scrollable).first,
        );
        await t.enterText(note, 'إسناد إداري اصطناعي مخول لمتابعة الحضور');
        await t.testTextInput.receiveAction(TextInputAction.done);
        await t.pumpAndSettle();
        await binding.takeScreenshot(
          'phase-approved-admin-coach-task-assignment',
        );
        await tap('تأكيد الإسناد');
        await wait(find.widgetWithText(FilledButton, 'إسناد مهمة للمدرب'));
        await t.scrollUntilVisible(
          find.text('إسناد إداري اصطناعي مخول لمتابعة الحضور'),
          150,
          scrollable: find.byType(Scrollable).first,
        );
        final list = obj(
          await admin.request(
            'GET',
            '/api/v1/manage/coach-tasks?state=Open&pageSize=100',
          ),
        );
        final saved = rows(list['items']).singleWhere(
          (x) =>
              x['trainingGroupId'] == pair['groupId'] &&
              x['kind'] == 'Attendance',
        );
        id = textOf(saved['id']);
        expect(saved['assignedToMembershipId'], pair['coachId']);
        final details = obj(
          await owner.request('GET', '/api/v1/manage/coach-tasks/$id'),
        );
        expect(details['note'], 'إسناد إداري اصطناعي مخول لمتابعة الحضور');
        expect(rows(details['history']).length, 1);
        await binding.takeScreenshot('phase-approved-admin-coach-task-saved');
        expect(t.takeException(), isNull);
      } finally {
        if (id != null) {
          final latest = obj(
            await owner.request('GET', '/api/v1/manage/coach-tasks/$id'),
          );
          if (!['Closed', 'Rejected'].contains(latest['state'])) {
            await owner.request(
              'POST',
              '/api/v1/manage/coach-tasks/$id',
              body: {
                'state': 'Closed',
                'version': latest['version'],
                'requestId': followUpRequestId(),
                'resolutionReason': 'إغلاق مهمة تجهيز قبول الإداري الاصطناعية',
              },
            );
          }
        }
        await owner.request('PUT', path, body: restore);
        final restored = accessBody(obj(await owner.request('GET', path)));
        expect(restored, restore);
        await t.pumpWidget(const SizedBox());
        await t.pumpWidget(app(Scaffold(body: CoachTasksEntry(auth: admin))));
        for (var i = 0; i < 30; i++) {
          await t.pump(const Duration(milliseconds: 150));
        }
        expect(find.text('مهام الحضور والتقييم'), findsNothing);
        await t.pumpWidget(const SizedBox());
        owner.dispose();
        admin.dispose();
      }
    },
  );
}
