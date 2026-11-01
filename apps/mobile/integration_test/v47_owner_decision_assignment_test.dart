import '../test/introduction_test_helpers.dart';

import 'dart:convert';

import 'package:academy_mobile/app/app.dart';
import 'package:academy_mobile/core/api/api_client.dart';
import 'package:academy_mobile/core/auth/auth_controller.dart';
import 'package:academy_mobile/core/storage/refresh_store.dart';
import 'package:academy_mobile/core/theme/appearance.dart';
import 'package:academy_mobile/features/admin/native_structure_operations.dart';
import 'package:academy_mobile/features/guardian/guardian_ui.dart';
import 'package:academy_mobile/features/owner/owner_dashboard.dart';
import 'package:academy_mobile/features/owner/owner_operations.dart';
import 'package:academy_mobile/features/owner/refined_home_panels.dart';
import 'package:academy_mobile/features/shared/experience_ui.dart';
import 'package:flutter/material.dart';
import 'package:flutter_secure_storage/flutter_secure_storage.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:integration_test/integration_test.dart';

class ScopedAssignmentAcceptanceTransport extends HttpApiTransport {
  ScopedAssignmentAcceptanceTransport(super.config);
  String? assignmentPath, coachId, sourceId;
  @override
  Future<Object?> send(
    String method,
    String path, {
    Object? body,
    String? token,
    String? idempotencyKey,
  }) async {
    final assign =
        method == 'POST' &&
        path == assignmentPath &&
        body is Map &&
        body['membershipId'] == coachId;
    final close =
        method == 'PUT' &&
        sourceId != null &&
        path == '/api/v1/manage/group-assignments/$sourceId/status' &&
        body is Map &&
        body['isActive'] == false;
    if (method != 'GET' &&
        !path.startsWith('/api/v1/mobile/auth/') &&
        !assign &&
        !close) {
      throw StateError(
        'Only the selected synthetic assignment may be changed.',
      );
    }
    final result = await super.send(
      method,
      path,
      body: body,
      token: token,
      idempotencyKey: idempotencyKey,
    );
    if (assign && result is String) sourceId = result;
    return result;
  }
}

void main() {
  final binding = IntegrationTestWidgetsFlutterBinding.ensureInitialized();
  testWidgets(
    'Actual owner home decision keeps branch/period, saves exact assignment and refreshes count before safe web replay',
    (t) async {
      expect(const bool.fromEnvironment('ISOLATED_DEMO'), true);
      const base = String.fromEnvironment('API_BASE_URL');
      expect(base, 'http://127.0.0.1:5278');
      final transport = ScopedAssignmentAcceptanceTransport(
        AppConfig('Demo', base),
      );
      final auth = AuthController(
        transport,
        SecureRefreshStore('v47.5278.decision-assignment.owner10'),
      );
      await auth.restore();
      if (auth.session == null) {
        final challenge = await auth.requestOtp('01099900010');
        await auth.verify('01099900010', challenge, '246810');
      }
      expect(auth.session!.selected!.role, 'AcademyOwner');
      expect(
        auth.session!.selected!.academyId,
        '30000000-0000-0000-0000-000000000003',
      );
      final calendar = obj(
            await auth.request('GET', '/api/v1/academy/calendar'),
          ),
          options = obj(
            await auth.request('GET', '/api/v1/manage/structure/options'),
          );
      final period = {
        'from': textOf(calendar['trailingMonthFrom']),
        'to': textOf(calendar['today']),
      };
      ({Json coach, Json group, Json branch})? selected;
      search:
      for (final branch in rows(options['branches'])) {
        final query = Uri(
          queryParameters: {...period, 'branchId': textOf(branch['id'])},
        ).query;
        final summary = obj(
          await auth.request('GET', '/api/v1/reports/owner-summary?$query'),
        );
        for (final coach in rows(
          obj(summary['operations'])['underAssignedCoaches'],
        )) {
          if ((coach['groupCount'] as num) + 1 >= (coach['target'] as num)) {
            continue;
          }
          final profile = obj(
            await auth.request(
              'GET',
              '/api/v1/manage/structure/coaches/${coach['id']}',
            ),
          );
          final assigned = rows(profile['assignments'])
              .where((a) => a['isActive'] == true)
              .map((a) => a['trainingGroupId'])
              .toSet();
          final sports = rows(options['groups'])
              .where(
                (g) =>
                    assigned.contains(g['id']) && g['branchId'] == branch['id'],
              )
              .map((g) => g['sportId'])
              .toSet();
          final eligible = rows(options['groups']).where(
            (g) =>
                g['branchId'] == branch['id'] &&
                sports.contains(g['sportId']) &&
                !assigned.contains(g['id']),
          );
          if (eligible.isNotEmpty) {
            selected = (coach: coach, group: eligible.first, branch: branch);
            break search;
          }
        }
      }
      expect(selected, isNotNull);
      final candidate = selected!;
      final coachId = textOf(candidate.coach['id']),
          groupId = textOf(candidate.group['id']),
          branchId = textOf(candidate.branch['id']);
      final beforeCount = candidate.coach['groupCount'] as num,
          target = candidate.coach['target'] as num;
      final query = Uri(queryParameters: {...period, 'branchId': branchId})
          .query;
      transport.assignmentPath = '/api/v1/manage/groups/$groupId/coaches';
      transport.coachId = coachId;
      var passed = false;
      Future<void> wait(Finder f) async {
        for (var i = 0; i < 240 && f.evaluate().isEmpty; i++) {
          if (find.text('تخطي').evaluate().isNotEmpty) {
            await finishRoleIntroduction(t);
            await t.pumpAndSettle();
          }
          await t.pump(const Duration(milliseconds: 150));
        }
        expect(f, findsWidgets);
        await t.pumpAndSettle();
      }

      Future<void> tap(Finder f) async {
        await t.scrollUntilVisible(
          f,
          220,
          scrollable: find.byType(Scrollable).first,
          maxScrolls: 100,
        );
        await t.pumpAndSettle();
        await t.tap(f);
        await t.pumpAndSettle();
      }

      try {
        final appearance = Appearance(storage: const FlutterSecureStorage());
        await appearance.load();
        await t.pumpWidget(
          AcademyApp(auth: auth, environment: 'Demo', appearance: appearance),
        );
        await wait(find.byType(OwnerDashboard));
        await wait(find.byType(ScopeBranchTabs));
        final branchLabel = find.descendant(
          of: find.byType(ScopeBranchTabs),
          matching: find.text(textOf(candidate.branch['arabicName'])),
        );
        await t.ensureVisible(branchLabel);
        await t.tap(branchLabel);
        await t.pump(const Duration(milliseconds: 300));
        await wait(find.byType(ScopeBranchTabs));
        expect(
          t.widget<ScopeBranchTabs>(find.byType(ScopeBranchTabs)).selected,
          branchId,
        );
        await tap(find.text('كل القرارات'));
        await wait(find.byType(OwnerDecisionsPanel));
        expect(
          t.widget<OwnerDecisionsPage>(find.byType(OwnerDecisionsPage)).filters,
          {...period, 'branchId': branchId},
        );
        await tap(
          find.text(
            '${candidate.coach['name']} · $beforeCount مجموعات من أصل $target',
          ),
        );
        await wait(find.byType(NativeCoachAssignment));
        final form = t.widget<NativeCoachAssignment>(
          find.byType(NativeCoachAssignment),
        );
        expect(form.coachId, coachId);
        expect(form.branchId, branchId);
        await wait(
          find.widgetWithText(DropdownButtonFormField<String>, 'المجموعة'),
        );
        await t.tap(
          find.widgetWithText(DropdownButtonFormField<String>, 'المجموعة'),
        );
        await t.pumpAndSettle();
        final groupLabel = find.text(textOf(candidate.group['arabicName']));
        await t.scrollUntilVisible(
          groupLabel,
          160,
          scrollable: find.byType(Scrollable).last,
          maxScrolls: 100,
        );
        await t.tap(groupLabel.last);
        await t.pumpAndSettle();
        await binding.takeScreenshot(
          'phase-approved-owner-decision-assignment-form',
        );
        await tap(find.text('حفظ الإسناد'));
        await wait(find.text('تم حفظ الإسناد.'));
        final stored = obj(
          await auth.request('GET', '${transport.assignmentPath}/$coachId'),
        );
        expect(stored['id'], transport.sourceId);
        expect(stored['trainingGroupId'], groupId);
        expect(stored['academyMembershipId'], coachId);
        expect(stored['isActive'], true);
        await binding.takeScreenshot(
          'phase-approved-owner-decision-assignment-saved',
        );
        Navigator.of(t.element(find.byType(NativeCoachAssignment))).pop();
        await wait(find.byType(OwnerDecisionsPanel));
        await wait(
          find.text(
            '${candidate.coach['name']} · ${beforeCount + 1} مجموعات من أصل $target',
          ),
        );
        Navigator.of(t.element(find.byType(OwnerDecisionsPage))).pop();
        await wait(find.byType(OwnerDashboard));
        await t.scrollUntilVisible(
          find.byType(HomeDecisionCards),
          220,
          scrollable: find.byType(Scrollable).first,
          maxScrolls: 100,
        );
        await t.pumpAndSettle();
        final refreshed = t.widget<HomeDecisionCards>(
          find.byType(HomeDecisionCards),
        );
        expect(
          rows(refreshed.data['underAssignedCoaches'])
              .singleWhere((c) => c['id'] == coachId)['groupCount'],
          beforeCount + 1,
        );
        final summary = obj(
          await auth.request('GET', '/api/v1/reports/owner-summary?$query'),
        );
        expect(
          rows(obj(summary['operations'])['underAssignedCoaches'])
              .singleWhere((c) => c['id'] == coachId)['groupCount'],
          beforeCount + 1,
        );
        await binding.takeScreenshot(
          'phase-approved-owner-decision-assignment-return',
        );
        debugPrint(
          'V47_ASSIGNMENT=${jsonEncode({'id': stored['id'], 'coachId': coachId, 'coachName': candidate.coach['name'], 'groupId': groupId, 'groupName': candidate.group['arabicName'], 'branchId': branchId, 'branchName': candidate.branch['arabicName'], ...period, 'before': beforeCount, 'after': beforeCount + 1, 'target': target})}',
        );
        expect(t.takeException(), isNull);
        passed = true;
      } finally {
        if (!passed || !const bool.fromEnvironment('KEEP_ASSIGNMENT_FOR_WEB')) {
          try {
            final row = obj(
              await auth.request('GET', '${transport.assignmentPath}/$coachId'),
            );
            transport.sourceId = textOf(row['id']);
            await auth.request(
              'PUT',
              '/api/v1/manage/group-assignments/${row['id']}/status',
              body: {'isActive': false},
            );
          } on ApiFailure catch (e) {
            if (e.status != 404) rethrow;
          }
        }
        await t.pumpWidget(const SizedBox());
        auth.dispose();
      }
    },
  );
}
