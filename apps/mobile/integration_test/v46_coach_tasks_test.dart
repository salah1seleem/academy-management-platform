import 'package:academy_mobile/core/api/api_client.dart';
import 'package:academy_mobile/core/auth/auth_controller.dart';
import 'package:academy_mobile/core/storage/refresh_store.dart';
import 'package:academy_mobile/core/theme/app_theme.dart';
import 'package:academy_mobile/features/guardian/guardian_ui.dart';
import 'package:academy_mobile/features/shared/coach_tasks.dart';
import 'package:academy_mobile/features/owner/owner_followups.dart'
    show followUpRequestId;
import 'package:flutter/material.dart';
import 'package:flutter_localizations/flutter_localizations.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:integration_test/integration_test.dart';

void main() {
  final binding = IntegrationTestWidgetsFlutterBinding.ensureInitialized();
  testWidgets(
    'Actual owner assigns and assigned coach progresses and closes an attendance task with saved reads',
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
      final coach = await login('01099900020', 'Coach');
      Widget app(Widget home) => MaterialApp(
        locale: const Locale('ar'),
        supportedLocales: const [Locale('ar')],
        localizationsDelegates: GlobalMaterialLocalizations.delegates,
        theme: AcademyTheme.dark(),
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
          140,
          scrollable: find.byType(Scrollable).first,
          maxScrolls: 60,
        );
        await t.pumpAndSettle();
        await t.tap(f.last);
        await t.pumpAndSettle();
      }

      final options = obj(
        await owner.request(
          'GET',
          '/api/v1/manage/coach-tasks/options?pageSize=100',
        ),
      );
      final open = obj(
        await owner.request(
          'GET',
          '/api/v1/manage/coach-tasks?state=Open&pageSize=100',
        ),
      );
      expect(open['hasMore'], false);
      final pair = rows(options['items']).firstWhere(
        (p) =>
            p['coachId'] == coach.session!.selected!.id &&
            !rows(open['items']).any(
              (x) =>
                  x['trainingGroupId'] == p['groupId'] &&
                  x['kind'] == 'Attendance',
            ),
      );
      String? createdId;
      await t.pumpWidget(
        app(
          Builder(
            builder: (context) => Scaffold(
              body: Center(
                child: FilledButton(
                  onPressed: () async {
                    createdId = await Navigator.of(context).push<String>(
                      MaterialPageRoute(
                        builder: (_) =>
                            CoachTasksPage(auth: owner, create: true),
                      ),
                    );
                  },
                  child: const Text('بدء الإسناد'),
                ),
              ),
            ),
          ),
        ),
      );
      await t.tap(find.text('بدء الإسناد'));
      await wait(find.text('${pair['group']} · ${pair['coach']}'));
      await tap('${pair['group']} · ${pair['coach']}');
      final note = find.widgetWithText(TextField, 'ملاحظة المتابعة');
      await t.scrollUntilVisible(
        note,
        150,
        scrollable: find.byType(Scrollable).first,
      );
      await t.enterText(note, 'رحلة قبول اصطناعية لمتابعة الحضور');
      await t.testTextInput.receiveAction(TextInputAction.done);
      await t.pumpAndSettle();
      await binding.takeScreenshot('phase-approved-coach-task-assignment');
      await tap('تأكيد الإسناد');
      for (var i = 0; i < 200 && createdId == null; i++) {
        await t.pump(const Duration(milliseconds: 150));
      }
      expect(createdId, isNotNull);
      try {
        final saved = obj(
          await owner.request('GET', '/api/v1/manage/coach-tasks/$createdId'),
        );
        expect(saved['assignedToMembershipId'], coach.session!.selected!.id);
        expect(saved['kind'], 'Attendance');
        expect(saved['state'], 'Assigned');
        await t.pumpWidget(app(CoachTasksPage(auth: coach, id: createdId)));
        await wait(find.text('فتح المجموعة للحضور والتقييم'));
        await tap('حفظ حالة المهمة');
        await wait(find.text('حُفظت المهمة وأُعيدت قراءتها.'));
        expect(
          obj(
            await owner.request('GET', '/api/v1/manage/coach-tasks/$createdId'),
          )['state'],
          'InProgress',
        );
        final state = find.widgetWithText(
          DropdownButtonFormField<String>,
          'تحديث الحالة',
        );
        await t.scrollUntilVisible(
          state,
          -150,
          scrollable: find.byType(Scrollable).first,
        );
        await t.tap(state);
        await t.pumpAndSettle();
        await t.tap(find.text('مغلق').last);
        await t.pumpAndSettle();
        final reason = find.widgetWithText(TextField, 'سبب الإغلاق أو الرفض');
        await t.scrollUntilVisible(
          reason,
          150,
          scrollable: find.byType(Scrollable).first,
        );
        await t.enterText(
          reason,
          'انتهت رحلة متابعة اصطناعية دون كتابة حضور تلقائي',
        );
        await t.testTextInput.receiveAction(TextInputAction.done);
        await t.pumpAndSettle();
        await tap('حفظ حالة المهمة');
        for (
          var i = 0;
          i < 200 && find.text('حفظ حالة المهمة').evaluate().isNotEmpty;
          i++
        ) {
          await t.pump(const Duration(milliseconds: 150));
        }
        final closed = obj(
          await owner.request('GET', '/api/v1/manage/coach-tasks/$createdId'),
        );
        expect(closed['state'], 'Closed');
        expect(rows(closed['history']).length, 3);
        expect(find.text('حفظ حالة المهمة'), findsNothing);
        await binding.takeScreenshot(
          'phase-approved-coach-task-closed-history',
        );
        expect(t.takeException(), isNull);
      } finally {
        final latest = obj(
          await owner.request('GET', '/api/v1/manage/coach-tasks/$createdId'),
        );
        if (!['Closed', 'Rejected'].contains(latest['state'])) {
          await owner.request(
            'POST',
            '/api/v1/manage/coach-tasks/$createdId',
            body: {
              'state': 'Closed',
              'version': latest['version'],
              'requestId': followUpRequestId(),
              'resolutionReason': 'إغلاق تجهيز رحلة القبول الاصطناعية',
            },
          );
        }
        await t.pumpWidget(const SizedBox());
        owner.dispose();
        coach.dispose();
      }
    },
  );
}
