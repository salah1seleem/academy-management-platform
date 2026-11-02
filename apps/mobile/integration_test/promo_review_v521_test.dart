import 'package:academy_mobile/app/app.dart';
import 'package:academy_mobile/core/auth/auth_controller.dart';
import 'package:academy_mobile/core/storage/refresh_store.dart';
import 'package:academy_mobile/core/theme/appearance.dart';
import 'package:academy_mobile/features/shared/role_navigation.dart';
import 'package:academy_mobile/features/guardian/guardian_ui.dart';
import 'package:academy_mobile/features/guardian/guardian_content.dart';
import 'package:academy_mobile/features/guardian/guardian_profile.dart';
import 'package:academy_mobile/features/owner/owner_action_rail.dart';
import 'package:academy_mobile/features/owner/refined_home_panels.dart';
import 'package:academy_mobile/features/owner/owner_reports.dart';
import 'package:academy_mobile/features/owner/owner_followups.dart';
import 'package:academy_mobile/features/owner/inventory_preview.dart';
import 'package:academy_mobile/features/admin/native_renewal_proof_review.dart';
import 'package:academy_mobile/features/coach/coach_evaluation.dart';
import 'package:academy_mobile/features/shared/matches.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

import 'promo_capture_v520_test.dart' show FilmApi, FilmBinding, mark;

void main() {
  final binding = FilmBinding();
  testWidgets('V5.21 reviewed screens with actual theme and analysis changes', (
    t,
  ) async {
    expect(const bool.fromEnvironment('ISOLATED_MARKETING_CAPTURE'), true);
    expect(
      const String.fromEnvironment('API_BASE_URL'),
      'http://127.0.0.1:5289',
    );
    Future<void> pause([int ms = 800]) async {
      for (var n = 0; n < ms; n += 50) {
        await t.runAsync(
          () => Future<void>.delayed(const Duration(milliseconds: 50)),
        );
        await t.pump();
      }
    }

    Future<void> wait(Finder f) async {
      for (var n = 0; n < 200 && f.evaluate().isEmpty; n++) {
        await pause(100);
      }
      expect(f, findsWidgets);
    }

    Future<void> tap(Finder f) async {
      await wait(f);
      await t.ensureVisible(f.first);
      await pause(250);
      final p = t.getCenter(f.first);
      mark('tap', {'x': p.dx, 'y': p.dy});
      await t.tap(f.first);
      await pause(1000);
    }

    Future<void> snap(String id) async {
      await pause(500);
      mark('scene', {'id': id, 'edge': 'start'});
      await binding.takeScreenshot('phase-review-$id');
      await pause(1400);
      mark('scene', {'id': id, 'edge': 'end'});
    }

    NavigatorState? nav;
    Appearance? appearance;
    final sessions = <AuthController>[];
    Future<AuthController> mount(
      String role,
      String phone,
      VisualModel model,
    ) async {
      await t.pumpWidget(const SizedBox());
      await pause(250);
      final auth = AuthController(
        FilmApi(),
        SecureRefreshStore('academy.marketing.capture-v520.$role.5289'),
      );
      sessions.add(auth);
      await t.runAsync(auth.restore);
      if (auth.session == null) {
        final c = await t.runAsync(() => auth.requestOtp(phone));
        await t.runAsync(() => auth.verify(phone, c!, '246810'));
      }
      expect(
        auth.session!.selected!.academyId,
        '30000000-0000-0000-0000-000000000003',
      );
      expect(auth.session!.selected!.role, role);
      final a = Appearance();
      appearance = a;
      a.bindIntroUser(auth.session!.userId);
      await a.completeRoleIntro(role, 'football');
      await a.update(visualModel: model);
      await t.pumpWidget(
        AcademyApp(
          auth: auth,
          environment: 'Production',
          appearance: a,
          key: UniqueKey(),
        ),
      );
      await wait(find.byType(NativeRoleTabs));
      nav = Navigator.of(t.element(find.byType(NativeRoleTabs)));
      await pause(2600);
      return auth;
    }

    Future<void> open(Widget page) async {
      nav!.push(MaterialPageRoute<void>(builder: (_) => page));
      await pause(1800);
    }

    Future<void> home() async {
      nav!.popUntil((r) => r.isFirst);
      await pause(900);
    }

    Future<void> show(Finder f) async {
      for (var n = 0; n < 20 && f.evaluate().isEmpty; n++) {
        await t.drag(find.byType(Scrollable).first, const Offset(0, -380));
        await pause(400);
      }
      await wait(f);
      await t.ensureVisible(f.first);
      await pause(500);
    }

    Color homeColor() =>
        Theme.of(t.element(find.byType(NativeRoleTabs)))
            .scaffoldBackgroundColor;
    Future<Object> read(AuthController auth, String path) async =>
        (await auth.request('GET', path))!;
    mark('ready', {});
    await pause(3500);
    final owner = await mount(
      'AcademyOwner',
      '01099900010',
      VisualModel.premium,
    );
    if (const String.fromEnvironment('PROMO_PHASE') == 'refinement') {
      await tap(find.byKey(const ValueKey('subscription-breakdown-toggle')));
      await pause(1500);
      await show(find.widgetWithText(ChoiceChip, 'المجموعات'));
      await snap('analysis-groups');
      await home();
      await open(OwnerFollowUpsPage(auth: owner));
      await snap('owner-followups');
      await tap(find.byTooltip('عن المتابعات'));
      await snap('followup-help');
      await tap(find.text('فهمت'));
      await tap(find.text('تصفية المتابعات'));
      await snap('followup-filters');
      await t.pumpWidget(const SizedBox());
      await pause(200);
      for (final s in sessions) {
        s.dispose();
      }
      mark('verified', {
        'refinementScreens': 4,
        'businessWrites': FilmApi.writes,
      });
      return;
    }
    await wait(find.text('اشتراكات الأكاديمية'));
    await snap('owner-gold');
    final colors = <int>{};
    for (final model in [
      VisualModel.light,
      VisualModel.dark,
      VisualModel.pitch,
      VisualModel.premium,
    ]) {
      await tap(find.byKey(const ValueKey('header-appearance-toggle')));
      await tap(find.text(model.label).last);
      await pause(1000);
      expect(appearance!.model, model);
      colors.add(homeColor().toARGB32());
      mark('verified', {
        'model': model.name,
        'homeBackground': homeColor().toARGB32(),
      });
      await snap('owner-theme-${model.name}');
    }
    expect(colors.length, 4);
    await show(find.byType(AcademySummaryPanel));
    await snap('owner-collections');
    t.state<ScrollableState>(find.byType(Scrollable).first).position.jumpTo(0);
    await pause(500);
    await tap(find.byKey(const ValueKey('subscription-scope-toggle')));
    await tap(find.text('فرع مدينتي').first);
    await pause(1200);
    await snap('owner-branch');
    await tap(find.text('كل الفروع').last);
    await tap(find.byKey(const ValueKey('subscription-breakdown-toggle')));
    expect(find.text('الرياضات'), findsNothing);
    for (final label in ['المجموعات', 'أيام التدريب', 'الفئات العمرية']) {
      final chip = find.widgetWithText(ChoiceChip, label);
      if (label != 'المجموعات') {
        await tap(chip);
      } else {
        await show(chip);
      }
      await pause(1000);
      await snap(
        label == 'المجموعات'
            ? 'analysis-groups'
            : label == 'أيام التدريب'
            ? 'analysis-days'
            : 'analysis-ages',
      );
    }
    await tap(find.byKey(const ValueKey('subscription-breakdown-toggle')));
    final cockpit = obj(
      await t.runAsync(() => read(owner, '/api/v1/reports/owner-cockpit')),
    );
    expect(obj(cockpit['todayPlan'])['openFollowUps'], greaterThan(0));
    expect(obj(cockpit['todayPlan'])['recordedAbsenceGroups'], greaterThan(0));
    await show(find.text('خطة اليوم'));
    await snap('today-plan');
    final deck = find.byType(HomeDecisionCards);
    await t.drag(deck, const Offset(290, 0));
    await pause(900);
    await snap('today-plan-next');
    await show(find.byType(OwnerActionRail));
    final rail = t.widget<ListView>(
      find.byKey(const ValueKey('owner-shortcut-scroll')),
    );
    final before = rail.controller!.offset;
    await pause(6000);
    expect(rail.controller!.offset, isNot(before));
    await snap('owner-actions');
    await tap(find.text('كل العمليات'));
    await snap('owner-all-operations');
    await home();
    await open(OwnerFollowUpsPage(auth: owner));
    await snap('owner-followups');
    await home();
    await open(OwnerReports(auth: owner, initialReport: 'operating'));
    await snap('owner-report');
    await home();
    await open(
      GuardianPage(
        title: 'المنتجات الرياضية',
        child: gList([OwnerInventoryPreview(auth: owner)]),
      ),
    );
    await snap('owner-stock');
    await home();
    final admin = await mount('AcademyAdmin', '01099900011', VisualModel.light);
    await snap('admin-light');
    await open(
      NativeRenewalProofReview(
        auth: admin,
        id: 'f294038c-9194-4b58-ae23-feadee0b41f6',
      ),
    );
    await wait(find.text('تكبير'));
    await show(find.text('تكبير'));
    await snap('admin-proof');
    await tap(find.text('تكبير'));
    await wait(find.byType(InteractiveViewer));
    await snap('admin-proof-full');
    await tap(find.byTooltip('إغلاق'));
    await home();
    final guardian = await mount('Guardian', '01099900100', VisualModel.dark);
    await snap('guardian-blue');
    final children = rows(
      await t.runAsync(() => read(guardian, '/api/v1/guardian/children')),
    );
    final child = children.firstWhere(
      (c) => textOf(c['arabicName'] ?? c['name']).startsWith('يوسف'),
      orElse: () => children.first,
    );
    final player = textOf(child['id'] ?? child['playerId']);
    await open(
      ChildProfile(
        auth: guardian,
        environment: 'Production',
        playerId: player,
        today: '2026-10-30',
      ),
    );
    await snap('guardian-profile');
    await home();
    await open(
      NutritionLibrary(
        auth: guardian,
        playerId: player,
        today: '2026-10-30',
        playerName: 'يوسف',
      ),
    );
    await wait(find.text('خطة اليوم'));
    await snap('nutrition-tabs');
    await tap(find.text('خطة اليوم'));
    await snap('nutrition-plan');
    await tap(find.text('خطة اليوم'));
    await tap(find.text('الغداء'));
    final meals = rows(
      await t.runAsync(
        () => read(guardian, '/api/v1/guardian/nutrition?category=Lunch'),
      ),
    );
    await open(MealDetails(auth: guardian, id: meals.first['id']));
    await snap('nutrition-meal');
    await home();
    await open(MatchSchedule(auth: guardian, playerId: player));
    await snap('guardian-matches');
    await home();
    final coach = await mount('Coach', '01099900020', VisualModel.pitch);
    await snap('coach-pitch');
    final options = obj(
      await t.runAsync(() => read(coach, '/api/v1/evaluations/options')),
    );
    await open(
      CoachEvaluation(
        auth: coach,
        enrollment: rows(options['enrollments']).first,
      ),
    );
    await wait(find.byType(Slider));
    await snap('coach-evaluation');
    await home();
    await t.pumpWidget(const SizedBox());
    await pause(200);
    for (final s in sessions) {
      s.dispose();
    }
    mark('verified', {
      'fourActualHomeColors': colors.length,
      'reviewedRoles': 4,
      'businessWrites': FilmApi.writes,
    });
  });
}
