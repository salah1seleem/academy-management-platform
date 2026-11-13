import 'dart:async';
import 'dart:io';
import 'dart:ui' as ui;

import 'package:academy_mobile/core/api/api_client.dart';
import 'package:academy_mobile/core/auth/auth_controller.dart';
import 'package:academy_mobile/core/theme/approved_role_introduction.dart';
import 'package:academy_mobile/core/theme/app_theme.dart';
import 'package:academy_mobile/core/widgets/gentle_carousel.dart';
import 'package:academy_mobile/features/auth/login_page.dart';
import 'package:academy_mobile/features/guardian/guardian_home.dart';
import 'package:academy_mobile/features/owner/owner_dashboard.dart';
import 'package:academy_mobile/features/owner/owner_home_scope.dart';
import 'package:academy_mobile/features/shared/role_navigation.dart';
import 'package:academy_mobile/features/shared/staff_attendance_exceptions.dart';
import 'package:academy_mobile/features/shared/staff_work_periods.dart';
import 'package:flutter/material.dart';
import 'package:flutter/rendering.dart';
import 'package:flutter/services.dart';
import 'package:flutter_localizations/flutter_localizations.dart';
import 'package:flutter_test/flutter_test.dart';

import 'auth_controller_test.dart' show FakeApi, MemoryStore;
import 'guardian_home_workspace_test.dart' as family;
import 'owner_test.dart' as owner;
import 'support/visibility.dart';
import 'v2_role_workspaces_test.dart' show authenticate;

Widget qualityFrame(Widget child) => MaterialApp(
  locale: const Locale('ar'),
  supportedLocales: const [Locale('ar')],
  localizationsDelegates: GlobalMaterialLocalizations.delegates,
  theme: AcademyTheme.light(),
  home: RepaintBoundary(key: const ValueKey('quality-capture'), child: child),
);

Future<void> capture(WidgetTester t, String name) async {
  const folder = String.fromEnvironment('UI_QUALITY_CAPTURE_DIR');
  if (folder.isEmpty) return;
  await t.runAsync(() async {
    final boundary = t.renderObject<RenderRepaintBoundary>(
      find.byKey(const ValueKey('quality-capture')),
    );
    final image = await boundary.toImage(pixelRatio: 1);
    final bytes = await image.toByteData(format: ui.ImageByteFormat.png);
    await Directory(folder).create(recursive: true);
    await File('$folder/$name.png').writeAsBytes(bytes!.buffer.asUint8List());
    image.dispose();
  });
}

void main() {
  TestWidgetsFlutterBinding.ensureInitialized();
  if (const String.fromEnvironment('UI_QUALITY_CAPTURE_DIR').isNotEmpty) {
    setUpAll(() async {
      final arabic = FontLoader('AcademyArabic');
      for (final weight in [400, 500, 600, 700]) {
        arabic.addFont(
          rootBundle.load('assets/fonts/AcademyArabic-$weight.ttf'),
        );
      }
      final icons = FontLoader('MaterialIcons')
        ..addFont(rootBundle.load('fonts/MaterialIcons-Regular.otf'));
      final roboto = FontLoader('Roboto');
      const materialFonts = String.fromEnvironment('UI_QUALITY_MATERIAL_FONTS');
      for (final weight in ['Regular', 'Black']) {
        if (materialFonts.isNotEmpty) {
          roboto.addFont(
            File('$materialFonts/Roboto-$weight.ttf')
                .readAsBytes()
                .then((bytes) => ByteData.sublistView(bytes)),
          );
        }
      }
      await Future.wait([arabic.load(), icons.load(), roboto.load()]);
    });
  }
  testWidgets('Guardian restores the selected linked child after a tab visit', (
    t,
  ) async {
    t.view.physicalSize = const Size(390, 844);
    t.view.devicePixelRatio = 1;
    addTearDown(t.view.reset);
    var homeReads = 0;
    var linked = true;
    final secondChild = family.child('b');
    final football =
        (secondChild['sports'] as List).single as Map<String, dynamic>;
    secondChild['sports'] = [
      football,
      {...football, 'id': 'enrollment-swimming', 'sport': 'السباحة'},
    ];
    final auth = await authenticate('Guardian', (_, path, _, _) async {
      if (path == '/api/v1/guardian/home') {
        homeReads++;
        return {
          ...family.home(),
          'children': [family.child('a'), if (linked) secondChild],
        };
      }
      return family.response(path);
    });
    await t.pumpWidget(
      qualityFrame(
        NativeRoleTabs(
          auth: auth,
          role: 'Guardian',
          home: GuardianHome(
            auth: auth,
            environment: 'Demo',
            accountPage: const SizedBox(),
          ),
          account: const SizedBox(),
        ),
      ),
    );
    await t.pumpAndSettle();
    final second = find.byKey(const ValueKey('child-tab-b'));
    await revealTestTarget(t, second);
    await t.tap(second);
    await t.pumpAndSettle();
    final enrollment = find.byKey(
      const ValueKey('card-enrollment-b-enrollment-b'),
    );
    await revealTestTarget(t, enrollment);
    await t.tap(enrollment);
    await t.pumpAndSettle();
    await t.tap(find.textContaining('السباحة').last);
    await t.pumpAndSettle();
    final nav = find.byType(NavigationBar);
    Finder tab(String label) =>
        find.descendant(of: nav, matching: find.text(label));
    await t.tap(tab('المباريات'));
    await t.pumpAndSettle();
    final beforeReturn = homeReads;
    await t.tap(tab('الرئيسية'));
    await t.pumpAndSettle();
    await capture(t, 'mobile-nav-guardian-return');
    expect(homeReads, beforeReturn + 1);
    expect(t.widget<ChoiceChip>(second).selected, isTrue);
    expect(
      find.byKey(const ValueKey('card-enrollment-b-enrollment-swimming')),
      findsOneWidget,
    );

    await t.tap(tab('المباريات'));
    await t.pumpAndSettle();
    final beforeUnlinkReturn = homeReads;
    linked = false;
    await t.tap(tab('الرئيسية'));
    await t.pumpAndSettle();
    expect(homeReads, beforeUnlinkReturn + 1);
    expect(second, findsNothing);
    expect(find.byKey(const ValueKey('child-a')), findsOneWidget);
    expect(t.takeException(), isNull);
    await t.pumpWidget(const SizedBox());
    auth.dispose();
  });

  testWidgets(
    'Owner restores scope after a tab visit and drops a removed branch',
    (t) async {
      t.view.physicalSize = const Size(390, 844);
      t.view.devicePixelRatio = 1;
      addTearDown(t.view.reset);
      var branchReads = 0;
      var allowed = true;
      final reportBranches = <String?>[];
      final auth = await owner.homeOwner((_, path, _, _) async {
        if (path.contains('/reports/owner-summary?')) {
          reportBranches.add(Uri.parse(path).queryParameters['branchId']);
        }
        if (path.endsWith('/manage/structure/options')) {
          branchReads++;
          return {
            'branches': [
              if (allowed) {'id': 'demo-branch', 'arabicName': 'فرع العرض'},
            ],
          };
        }
        return owner.summary();
      });
      await t.pumpWidget(
        qualityFrame(
          NativeRoleTabs(
            auth: auth,
            role: 'AcademyOwner',
            home: OwnerDashboard(auth: auth, accountPage: const SizedBox()),
            account: const SizedBox(),
          ),
        ),
      );
      await t.pumpAndSettle();
      final branch = find.text('فرع العرض');
      await revealTestTarget(t, branch);
      await t.tap(branch);
      await t.pumpAndSettle();
      final summary = find.byKey(const ValueKey('owner-period-summary'));
      final today = find.descendant(
        of: summary,
        matching: find.text('هذا اليوم'),
      );
      await revealTestTarget(t, today);
      await t.tap(today);
      await t.pumpAndSettle();

      Future<void> visitMoreAndReturn() async {
        await t.tap(find.byKey(const ValueKey('owner-nav-4')));
        await t.pumpAndSettle();
        await t.tap(find.byKey(const ValueKey('owner-nav-0')));
        await t.pumpAndSettle();
      }

      await visitMoreAndReturn();
      await capture(t, 'mobile-nav-owner-return');
      expect(branchReads, 2);
      expect(reportBranches.last, 'demo-branch');
      expect(
        t.widget<OwnerBranchRail>(find.byType(OwnerBranchRail)).branchId,
        'demo-branch',
      );
      await revealTestTarget(t, summary);
      expect(
        t.widget<OwnerPeriodControls>(summary).period.preset,
        OwnerPeriodPreset.today,
      );

      allowed = false;
      reportBranches.clear();
      await visitMoreAndReturn();
      expect(branchReads, 3);
      expect(reportBranches, [null]);
      expect(
        t.widget<OwnerBranchRail>(find.byType(OwnerBranchRail)).branchId,
        isEmpty,
      );
      expect(t.takeException(), isNull);
      await t.pumpWidget(const SizedBox());
      auth.dispose();
    },
  );

  for (final example in [
    ('AcademyOwner', 1, 'owner-followups'),
    ('Coach', 2, 'coach-attendance'),
    ('Guardian', 1, 'guardian-schedule'),
  ]) {
    testWidgets('role introduction is readable: ${example.$3}', (t) async {
      t.view.physicalSize = const Size(375, 812);
      t.view.devicePixelRatio = 1;
      addTearDown(t.view.reset);
      await t.pumpWidget(
        qualityFrame(
          ApprovedRoleIntroduction(
            role: example.$1,
            sportTheme: 'swimming',
            training: true,
            academyName: 'أكاديمية الاختبار للسباحة',
            crest: const Icon(Icons.pool),
            initialPage: example.$2,
            onComplete: () async {},
          ),
        ),
      );
      await t.pumpAndSettle();
      expect(t.takeException(), isNull);
      if (example.$1 == 'Coach') {
        final instruction = find.textContaining('إذا أسند إليك الإداري');
        expect(instruction, findsOneWidget);
        expect(
          t.getBottomLeft(instruction).dy,
          lessThan(t.getTopLeft(find.byType(FilledButton)).dy),
        );
      }
      await capture(t, example.$3);
    });
  }

  testWidgets('login disclosure keeps the demo status explicit', (t) async {
    t.view.physicalSize = const Size(375, 812);
    t.view.devicePixelRatio = 1;
    addTearDown(t.view.reset);
    final auth = AuthController(
      FakeApi((m, p, b, token) async => null),
      MemoryStore(),
    )..restoring = false;
    await t.pumpWidget(
      qualityFrame(LoginPage(auth: auth, environment: 'Demo')),
    );
    await t.pumpAndSettle();
    await capture(t, 'login-demo');
    expect(
      find.text('بيئة عرض تجريبي. لا تُرسل رسائل تحقق فعلية.'),
      findsOneWidget,
    );
    await t.pumpWidget(
      qualityFrame(LoginPage(auth: auth, environment: 'Production')),
    );
    await t.pumpAndSettle();
    expect(find.textContaining('بيئة عرض تجريبي'), findsNothing);
    expect(find.text('وصول آمن إلى حسابك وبيانات أكاديميتك.'), findsNothing);
    await capture(t, 'login-production');
    await t.pumpWidget(const SizedBox());
    auth.dispose();
  });

  testWidgets('Coach groups handles a failed read', (t) async {
    t.view.physicalSize = const Size(375, 812);
    t.view.devicePixelRatio = 1;
    addTearDown(t.view.reset);
    final pending = Completer<Object?>();
    final auth = await authenticate('Coach', (method, path, _, _) async {
      if (path == '/api/v1/coach/groups') return pending.future;
      return [];
    });
    await t.pumpWidget(qualityFrame(CoachGroupsPage(auth: auth)));
    await t.pump(const Duration(milliseconds: 250));
    await capture(t, 'coach-groups-loading');
    pending.completeError(
      const ApiFailure(503, 'تعذر الاتصال. تحقق من الشبكة.'),
    );
    await t.pumpAndSettle();
    await capture(t, 'coach-groups-error');
    expect(t.takeException(), isNull);
    await t.pumpWidget(const SizedBox());
    auth.dispose();
  });

  testWidgets('Admin More separates personal attendance from staff review', (
    t,
  ) async {
    t.view.physicalSize = const Size(375, 812);
    t.view.devicePixelRatio = 1;
    addTearDown(t.view.reset);
    final permissions = Completer<List<String>>(),
        access = Completer<Object?>();
    final paths = <String>[];
    final auth = await authenticate('AcademyAdmin', (method, path, _, _) async {
      paths.add(path);
      return switch (path) {
        '/api/v1/my-permissions' => permissions.future,
        '/api/v1/my-access' => access.future,
        '/api/v1/my-match-access' => {'canRead': false},
        '/api/v1/staff/work-periods' => {'configured': false},
        _ when path.startsWith(manageStaffExceptions) => {'items': []},
        _ => [],
      };
    });
    await t.pumpWidget(
      qualityFrame(
        NativeRoleTabs(
          auth: auth,
          role: 'AcademyAdmin',
          home: const Text('محتوى الدور'),
          account: const SizedBox(),
        ),
      ),
    );
    await t.pumpAndSettle();
    await t.tap(find.text('المزيد'));
    await t.pump(const Duration(milliseconds: 300));
    await t.pump();
    await capture(t, 'admin-more-loading');
    permissions.complete([
      'followups.read',
      'staff.provision',
      'payment.read',
      'facilities.read',
    ]);
    access.complete({'useActionGrants': false});
    await t.pumpAndSettle();
    await capture(t, 'admin-more-loaded');
    expect(find.text('مهام الأكاديمية'), findsOneWidget);
    final review = find.text('مراجعة إثبات حضور الموظفين');
    final personal = find.text('سجل دوامي');
    await revealTestTarget(t, review);
    await t.tap(review);
    await t.pumpAndSettle();
    expect(
      t
          .widget<StaffAttendanceExceptionPage>(
            find.byType(StaffAttendanceExceptionPage),
          )
          .review,
      isTrue,
    );
    expect(paths.any((path) => path.startsWith(manageStaffExceptions)), isTrue);
    await t.tap(find.byType(BackButton));
    await t.pumpAndSettle();
    await revealTestTarget(t, personal);
    expect(find.text('دوامي ومستحقاتي'), findsOneWidget);
    await capture(t, 'admin-more-personal');
    await t.tap(personal);
    await t.pumpAndSettle();
    expect(find.byType(StaffWorkPeriodsPage), findsOneWidget);
    expect(paths, contains('/api/v1/staff/work-periods'));
    expect(t.takeException(), isNull);
    await t.pumpWidget(const SizedBox());
    auth.dispose();
  });

  testWidgets('Admin More retries unavailable tools independently of matches', (
    t,
  ) async {
    t.view.physicalSize = const Size(360, 812);
    t.view.devicePixelRatio = 1;
    addTearDown(t.view.reset);
    var permissionReads = 0;
    var matchReads = 0;
    final auth = await authenticate('AcademyAdmin', (_, path, _, _) async {
      if (path == '/api/v1/my-permissions') {
        if (++permissionReads == 1) {
          throw const ApiFailure(503, 'تعذر التحميل.');
        }
        return <String>[];
      }
      if (path == '/api/v1/my-match-access') {
        return {'canRead': ++matchReads == 1};
      }
      if (path == '/api/v1/my-access') return {'useActionGrants': false};
      return [];
    });
    await t.pumpWidget(
      qualityFrame(
        NativeRoleTabs(
          auth: auth,
          role: 'AcademyAdmin',
          home: const SizedBox(),
          account: const SizedBox(),
        ),
      ),
    );
    await t.pumpAndSettle();
    await t.tap(find.text('المزيد'));
    await t.pumpAndSettle();
    final matches = find.text('إدارة المباريات والنتائج');
    final retry = find.text('تعذر تحميل بعض الأدوات · إعادة المحاولة');
    expect(matches, findsOneWidget);
    expect(retry, findsOneWidget);
    await t.tap(retry);
    await t.pumpAndSettle();
    expect(permissionReads, 2);
    expect(matchReads, 1);
    expect(matches, findsOneWidget);
    expect(retry, findsNothing);

    await t.tap(find.text('الرئيسية'));
    await t.pumpAndSettle();
    await t.tap(find.text('المزيد'));
    await t.pumpAndSettle();
    expect(permissionReads, 3);
    expect(matchReads, 2);
    expect(find.text('مهام الأكاديمية'), findsNothing);
    expect(find.text('متابعاتي المسندة'), findsNothing);
    expect(matches, findsNothing);
    expect(find.text('سجل دوامي'), findsOneWidget);
    await capture(t, 'admin-more-no-academy-tools');
    expect(t.takeException(), isNull);
    await t.pumpWidget(const SizedBox());
    auth.dispose();
  });

  for (final interaction in ['touch', 'keyboard']) {
    testWidgets(
      'automatic rail stops after $interaction; swipe remains available',
      (t) async {
        t.view.physicalSize = const Size(375, 812);
        t.view.devicePixelRatio = 1;
        addTearDown(t.view.reset);
        var actions = 0;
        final action = FocusNode();
        addTearDown(action.dispose);
        await t.pumpWidget(
          qualityFrame(
            Scaffold(
              body: GentleCarousel(
                count: 4,
                width: 240,
                height: 100,
                itemBuilder: (context, index) => TextButton(
                  focusNode: index == 0 ? action : null,
                  onPressed: () => actions++,
                  child: Text('إجراء $index'),
                ),
              ),
            ),
          ),
        );
        await t.pumpAndSettle();
        final position = t
            .state<ScrollableState>(find.byType(Scrollable))
            .position;
        if (interaction == 'touch') {
          await t.tap(find.text('إجراء 0'));
          expect(actions, 1);
        } else {
          await t.sendKeyEvent(LogicalKeyboardKey.tab);
          await t.pump();
          expect(action.hasFocus, isTrue);
          action.unfocus();
        }
        await t.pump(const Duration(seconds: 12));
        await t.pump(const Duration(seconds: 1));
        expect(position.pixels, 0);
        await t.drag(find.byType(ListView), const Offset(170, 0));
        await t.pumpAndSettle();
        expect(position.pixels, greaterThan(0));
        await t.pumpWidget(const SizedBox());
      },
    );
  }
}
