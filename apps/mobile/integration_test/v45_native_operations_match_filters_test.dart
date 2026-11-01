import 'package:academy_mobile/app/app.dart';
import 'package:academy_mobile/core/api/api_client.dart';
import 'package:academy_mobile/core/auth/auth_controller.dart';
import 'package:academy_mobile/core/storage/refresh_store.dart';
import 'package:academy_mobile/core/theme/appearance.dart';
import 'package:academy_mobile/features/coach/coach_evaluation.dart';
import 'package:academy_mobile/features/guardian/guardian_ui.dart';
import 'package:academy_mobile/features/owner/owner_quick_operations.dart';
import 'package:academy_mobile/features/shared/experience_ui.dart';
import 'package:academy_mobile/features/shared/match_experience.dart';
import 'package:academy_mobile/features/shared/matches.dart';
import 'package:academy_mobile/features/shared/role_navigation.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:integration_test/integration_test.dart';

class OperationsReadTransport extends HttpApiTransport {
  OperationsReadTransport(super.config);
  final writes = <String>[], reads = <String>[];
  @override
  Future<Object?> send(
    String method,
    String path, {
    Object? body,
    String? token,
    String? idempotencyKey,
  }) async {
    if (!path.contains('/auth/')) {
      (method == 'GET' ? reads : writes).add('$method $path');
    }
    return super.send(
      method,
      path,
      body: body,
      token: token,
      idempotencyKey: idempotencyKey,
    );
  }
}

void main() {
  final binding = IntegrationTestWidgetsFlutterBinding.ensureInitialized();
  const filter = String.fromEnvironment('READ_SCENARIO');
  for (final scenario in [
    'owner-operations',
    'admin-operations',
    'owner-filters',
    'family-filters',
    'coach-filters',
  ].where((s) => filter.isEmpty || s == filter)) {
    testWidgets(
      'actual $scenario preserves native source and role filters without creating records',
      (t) async {
        expect(const bool.fromEnvironment('ISOLATED_DEMO'), true);
        const base = String.fromEnvironment('API_BASE_URL');
        expect(base, 'http://127.0.0.1:5278');
        expect((t.view.physicalSize / t.view.devicePixelRatio).width, 375);
        final transport = OperationsReadTransport(AppConfig('Demo', base));
        final role = scenario.startsWith('admin')
            ? 'AcademyAdmin'
            : scenario.startsWith('family')
            ? 'Guardian'
            : scenario.startsWith('coach')
            ? 'Coach'
            : 'AcademyOwner';
        final key = role == 'AcademyAdmin'
            ? 'v45.5278.os-media-entry.01099900011'
            : role == 'Guardian'
            ? 'v45.5278.guardian-workspace.01099900100'
            : role == 'Coach'
            ? 'v45.5278.web-settings.coach20'
            : 'v45.5278.os-media-entry.01099900010';
        final auth = AuthController(transport, SecureRefreshStore(key));
        await auth.restore();
        expect(auth.session, isNotNull);
        expect(auth.session!.selected!.role, role);
        expect(
          auth.session!.selected!.academyId,
          '30000000-0000-0000-0000-000000000003',
        );
        Future<void> wait(Finder f) async {
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
          await t.scrollUntilVisible(
            f,
            150,
            scrollable: scroll,
            maxScrolls: 120,
          );
          await t.ensureVisible(f.first);
          await t.pumpAndSettle();
        }

        Future<void> tap(Finder f) async {
          await reach(f);
          await t.tap(f.first);
          await t.pump(const Duration(milliseconds: 400));
        }

        Future<void> capture(String name) async {
          await t.pumpAndSettle();
          await t.runAsync(
            () => Future<void>.delayed(const Duration(milliseconds: 500)),
          );
          await binding.takeScreenshot(name);
        }

        final appearance = Appearance();
        for (final theme in ['football', 'general']) {
          await appearance.completeRoleIntro(role, theme);
        }
        await t.pumpWidget(
          AcademyApp(auth: auth, environment: 'Demo', appearance: appearance),
        );
        await wait(find.byType(NativeRoleTabs));
        Future<void> tab(String label) async {
          await t.tap(
            find.descendant(
              of: find.byType(NavigationBar),
              matching: find.text(label),
            ),
          );
          await t.pump(const Duration(milliseconds: 400));
        }

        if (scenario.endsWith('operations')) {
          await tab('العمليات');
          await wait(find.byType(NativeOperationActions));
          final names = scenario == 'owner-operations'
              ? [
                  'اشتراك جديد',
                  'تجديد اشتراك',
                  'إضافة مدرب',
                  'إنشاء تقييم',
                  'تسجيل الحضور',
                  'إسناد إلى مجموعة',
                  'تسكين لاعب',
                  'تعديل مجموعة',
                ]
              : ['إنشاء تقييم', 'تسجيل الحضور'];
          for (final name in names) {
            await reach(find.text(name));
            expect(find.text(name), findsOneWidget);
          }
          expect(find.text('التقرير المالي'), findsNothing);
          expect(find.text('طلبات الدفع'), findsNothing);
          final source = rows(
            obj(
              await auth.request('GET', '/api/v1/evaluations/options'),
            )['enrollments'],
          ).first;
          await tap(find.text('إنشاء تقييم'));
          await wait(find.byType(NativeEvaluationPicker));
          await wait(find.byType(ListTile));
          await reach(find.byType(ListTile).first);
          await t.tap(find.byType(ListTile).first);
          await t.pump(const Duration(milliseconds: 400));
          await wait(find.byType(CoachEvaluation));
          expect(
            t
                .widget<CoachEvaluation>(find.byType(CoachEvaluation))
                .enrollment['id'],
            source['id'],
          );
          await wait(find.text('بدء تقييم جديد'));
          await capture('phase-approved-$scenario-evaluation-entry-w375');
        } else {
          await tab('المباريات');
          await wait(find.byType(MatchSchedule));
          await wait(find.byType(MatchExperience));
          const savedId = '76a0bdc5-bf2e-462f-a2db-fc2b0d850cee';
          final feed = rows(await auth.request('GET', '/api/v1/my-matches'));
          final match = feed.singleWhere((m) => m['id'] == savedId);
          final target = find.byWidgetPredicate(
            (w) => w is MatchResultCard && w.data['id'] == savedId,
          );
          if (scenario == 'owner-filters') {
            await tap(find.widgetWithText(ChoiceChip, textOf(match['branch'])));
            expect(
              t
                  .widget<ChoiceChip>(
                    find.widgetWithText(ChoiceChip, textOf(match['branch'])),
                  )
                  .selected,
              true,
            );
            await tap(find.widgetWithText(ChoiceChip, textOf(match['team'])));
            expect(
              t
                  .widget<ChoiceChip>(
                    find.widgetWithText(ChoiceChip, textOf(match['team'])),
                  )
                  .selected,
              true,
            );
            for (final name in ['من', 'إلى']) {
              await tap(
                find.descendant(
                  of: find.byType(ScopeDateTiles),
                  matching: find.text(name),
                ),
              );
              await wait(find.byType(DatePickerDialog));
              final local = MaterialLocalizations.of(
                t.element(find.byType(DatePickerDialog)),
              );
              await t.tap(find.byIcon(Icons.edit_outlined));
              await t.pumpAndSettle();
              await t.enterText(
                find.descendant(
                  of: find.byType(DatePickerDialog),
                  matching: find.byType(TextField),
                ),
                local.formatCompactDate(
                  DateTime.parse(textOf(match['matchDate'])),
                ),
              );
              await t.tap(find.text(local.okButtonLabel));
              await t.pumpAndSettle();
            }
            final dates = t.widget<ScopeDateTiles>(find.byType(ScopeDateTiles));
            expect(dates.from, match['matchDate']);
            expect(dates.to, match['matchDate']);
            await reach(target);
            for (final card in t.widgetList<MatchResultCard>(
              find.byType(MatchResultCard),
            )) {
              expect(card.data['branchId'], match['branchId']);
              expect(card.data['teamId'], match['teamId']);
              expect(card.data['matchDate'], match['matchDate']);
            }
            await capture(
              'phase-approved-owner-match-branch-team-day-filter-w375',
            );
            await tap(find.byTooltip('مسح الفلاتر'));
            await reach(find.byType(ScopeDateTiles));
            expect(
              t.widget<ScopeDateTiles>(find.byType(ScopeDateTiles)).from,
              '',
            );
            expect(
              t.widget<ScopeDateTiles>(find.byType(ScopeDateTiles)).to,
              '',
            );
            await reach(find.widgetWithText(ChoiceChip, 'كل الفروع'));
            expect(
              t
                  .widget<ChoiceChip>(
                    find.widgetWithText(ChoiceChip, 'كل الفروع'),
                  )
                  .selected,
              true,
            );
            await reach(find.widgetWithText(ChoiceChip, 'كل الفرق'));
            expect(
              t
                  .widget<ChoiceChip>(
                    find.widgetWithText(ChoiceChip, 'كل الفرق'),
                  )
                  .selected,
              true,
            );
          } else if (scenario == 'family-filters') {
            final children = rows(
              obj(
                await auth.request('GET', '/api/v1/guardian/home'),
              )['children'],
            );
            final child = children.first;
            final choice = find.widgetWithText(
              DropdownButtonFormField<String>,
              'الأبناء',
            );
            await t.tap(choice);
            await t.pumpAndSettle();
            await t.tap(find.text(textOf(child['arabicName'])).last);
            await t.pump(const Duration(milliseconds: 400));
            await reach(target);
            expect(
              transport.reads,
              contains('GET /api/v1/my-matches?playerId=${child['id']}'),
            );
            await capture('phase-approved-guardian-match-child-filter-w375');
            await t.tap(choice);
            await t.pumpAndSettle();
            await t.tap(find.text('كل الأبناء').last);
            await t.pump(const Duration(milliseconds: 400));
            await reach(target);
            expect(find.text('كل الأبناء'), findsOneWidget);
          } else {
            expect(find.text('كل الفروع'), findsNothing);
            expect(
              find.widgetWithText(DropdownButtonFormField<String>, 'الأبناء'),
              findsNothing,
            );
            await tap(find.widgetWithText(ChoiceChip, textOf(match['team'])));
            await reach(target);
            expect(
              t.widget<MatchResultCard>(target).data['coachAssignmentSource'],
              'Confirmed',
            );
            await capture('phase-approved-coach-assigned-match-filter-w375');
          }
        }
        expect(transport.writes, isEmpty);
        expect(t.takeException(), isNull);
        await t.pumpWidget(const SizedBox.shrink());
        auth.dispose();
      },
    );
  }
}
