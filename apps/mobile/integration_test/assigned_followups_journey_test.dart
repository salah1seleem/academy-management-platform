import 'dart:convert';

import 'package:academy_mobile/core/api/api_client.dart';
import 'package:academy_mobile/core/auth/auth_controller.dart';
import 'package:academy_mobile/core/storage/refresh_store.dart';
import 'package:academy_mobile/features/guardian/guardian_ui.dart';
import 'package:academy_mobile/features/owner/owner_followups.dart';
import 'package:academy_mobile/features/shared/assigned_followups.dart';
import 'package:flutter/material.dart';
import 'package:flutter_localizations/flutter_localizations.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:integration_test/integration_test.dart';

void main() {
  final binding = IntegrationTestWidgetsFlutterBinding.ensureInitialized();
  testWidgets(
    'Real Admin OTP updates assigned scoped task and Owner sees closure; revocation removes native data',
    (t) async {
      expect(const bool.fromEnvironment('ISOLATED_DEMO'), true);
      expect(
        const String.fromEnvironment('API_BASE_URL'),
        'http://127.0.0.1:5236',
      );
      Future<AuthController> login(String phone, String role) async {
        final a = AuthController(
          HttpApiTransport(
            AppConfig('Demo', const String.fromEnvironment('API_BASE_URL')),
          ),
          SecureRefreshStore('academity.f1b2.$role'),
        );
        await a.restore();
        if (a.session != null) await a.logout();
        final c = await a.requestOtp(phone);
        await a.verify(phone, c, '246810');
        expect(a.session!.selected!.role, role);
        expect(
          a.session!.selected!.academyId,
          '30000000-0000-0000-0000-000000000003',
        );
        return a;
      }

      final owner = await login('01099900010', 'AcademyOwner'),
          admin = await login('01099900041', 'AcademyAdmin');
      final member = admin.session!.selected!.id;
      final access = obj(
        await owner.request('GET', '/api/v1/manage/staff/$member/access'),
      );
      Map<String, dynamic> scope(bool allowed) => {
        'allBranches': access['allBranches'],
        'branches': access['selectedBranches'],
        'allSports': access['allSports'],
        'sports': access['selectedSports'],
        'actions': [
          for (final module in access['modules'] as List)
            module == 'followups'
                ? {
                    'module': module,
                    'read': allowed,
                    'edit': allowed,
                    'add': false,
                    'delete': false,
                  }
                : rows(access['actions']).firstWhere(
                    (a) => a['module'] == module,
                    orElse: () => {
                      'module': module,
                      'read': false,
                      'edit': false,
                      'add': false,
                      'delete': false,
                    },
                  ),
        ],
      };
      try {
        final options = obj(
          await owner.request('GET', '/api/v1/manage/owner-followups/options'),
        );
        Json? candidate, group;
        for (final c in rows(options['candidates']).where(
          (c) =>
              c['condition'] == 'GroupLowOccupancy' &&
              c['activeFollowUpId'] == null,
        )) {
          final g = obj(
            await owner.request(
              'GET',
              '/api/v1/manage/groups/${obj(c['source'])['groupId']}/details',
            ),
          );
          if ((access['selectedBranches'] as List).contains(
                obj(g['group'])['branchId'],
              ) &&
              (access['selectedSports'] as List).contains(
                obj(g['group'])['sportId'],
              )) {
            candidate = c;
            group = g;
            break;
          }
        }
        expect(candidate, isNotNull);
        final groupId = obj(candidate!['source'])['groupId'];
        final report = jsonEncode(
          await owner.request(
            'GET',
            '/api/v1/reports/financial?from=2026-09-01&to=2026-09-28',
          ),
        );
        final created = obj(
          await owner.request(
            'POST',
            '/api/v1/manage/owner-followups/',
            body: {
              'trainingGroupId': groupId,
              'condition': candidate['condition'],
              'assignedToMembershipId': member,
              'requestId': followUpRequestId(),
            },
          ),
        );
        final id = created['id'];
        Future<void> wait(Finder f) async {
          for (var i = 0; i < 200 && f.evaluate().isEmpty; i++) {
            await t.pump(const Duration(milliseconds: 100));
          }
          expect(f, findsWidgets);
          await t.pumpAndSettle();
        }

        await t.pumpWidget(
          MaterialApp(
            locale: const Locale('ar'),
            supportedLocales: const [Locale('ar')],
            localizationsDelegates: GlobalMaterialLocalizations.delegates,
            theme: guardianTheme(),
            home: AssignedFollowUpsPage(auth: admin, id: id),
          ),
        );
        await wait(find.text('حفظ تحديث المتابعة'));
        expect(find.text('المسؤول'), findsNothing);
        final note = find.widgetWithText(TextField, 'ملاحظة اختيارية');
        await t.ensureVisible(note);
        await t.enterText(note, 'مراجعة تشغيل اصطناعية من الموبايل');
        await t.testTextInput.receiveAction(TextInputAction.done);
        await t.pumpAndSettle();
        await t.ensureVisible(find.text('حفظ تحديث المتابعة'));
        await t.tap(find.text('حفظ تحديث المتابعة'));
        await t.pumpAndSettle();
        await wait(find.text('حفظ تحديث المتابعة'));
        expect(
          obj(
            obj(
              await owner.request('GET', '/api/v1/manage/owner-followups/$id'),
            )['item'],
          )['state'],
          'InProgress',
        );
        final stateField = find.byType(DropdownButtonFormField<String>);
        await t.ensureVisible(stateField);
        await t.pumpAndSettle();
        await t.tap(stateField);
        await t.pumpAndSettle();
        await wait(find.text('مغلق'));
        await t.tap(find.text('مغلق').last);
        await t.pumpAndSettle();
        final reason = find.widgetWithText(
          TextField,
          'سبب إغلاق أو رفض المتابعة',
        );
        await t.ensureVisible(reason);
        await t.enterText(reason, 'إغلاق متابعة بشرية فقط؛ المصدر لم يتغير');
        await t.testTextInput.receiveAction(TextInputAction.done);
        await t.pumpAndSettle();
        await t.ensureVisible(find.text('حفظ تحديث المتابعة'));
        await t.tap(find.text('حفظ تحديث المتابعة'));
        await t.pumpAndSettle();
        await wait(
          find.text(
            'للقراءة فقط أو متابعة مغلقة. الإسناد وإعادة الفتح لدى المالك.',
          ),
        );
        final result = obj(
          await owner.request('GET', '/api/v1/manage/owner-followups/$id'),
        );
        expect(obj(result['item'])['state'], 'Closed');
        expect(obj(result['item'])['conditionActive'], true);
        expect(rows(result['history']).length, 3);
        expect(
          await owner.request('GET', '/api/v1/manage/groups/$groupId/details'),
          group,
        );
        expect(
          jsonEncode(
            await owner.request(
              'GET',
              '/api/v1/reports/financial?from=2026-09-01&to=2026-09-28',
            ),
          ),
          report,
        );
        await t.fling(find.byType(ListView).first, const Offset(0, 2000), 2000);
        await t.pumpAndSettle();
        if (const bool.fromEnvironment('CAPTURE_VISUALS')) {
          await binding.takeScreenshot('phase-assigned-followup-detail-ios');
        }
        await t.scrollUntilVisible(
          find.text('سجل المتابعة'),
          250,
          scrollable: find.byType(Scrollable).first,
        );
        if (const bool.fromEnvironment('CAPTURE_VISUALS')) {
          await binding.takeScreenshot('phase-assigned-followup-closed-ios');
        }
        await owner.request(
          'PUT',
          '/api/v1/manage/staff/$member/access',
          body: scope(false),
        );
        binding.handleAppLifecycleStateChanged(AppLifecycleState.inactive);
        await t.pump();
        binding.handleAppLifecycleStateChanged(AppLifecycleState.resumed);
        await wait(
          find.text('المتابعات غير متاحة ضمن إسنادك وصلاحياتك الحالية.'),
        );
        expect(find.text('مراجعة تشغيل اصطناعية من الموبايل'), findsNothing);
        expect(find.text('حفظ تحديث المتابعة'), findsNothing);
        expect(t.takeException(), isNull);
      } finally {
        await owner.request(
          'PUT',
          '/api/v1/manage/staff/$member/access',
          body: scope(true),
        );
        await t.pumpWidget(const SizedBox.shrink());
        await admin.logout();
        await owner.logout();
        admin.dispose();
        owner.dispose();
      }
    },
  );
}
