import 'dart:convert';
import 'dart:io';

import 'package:academy_mobile/app/app.dart';
import 'package:academy_mobile/core/api/api_client.dart';
import 'package:academy_mobile/core/auth/auth_controller.dart';
import 'package:academy_mobile/core/storage/refresh_store.dart';
import 'package:academy_mobile/core/theme/appearance.dart';
import 'package:academy_mobile/features/coach/coach_home.dart';
import 'package:academy_mobile/features/guardian/guardian_ui.dart';
import 'package:academy_mobile/features/owner/refined_home_panels.dart';
import 'package:academy_mobile/features/shared/owner_brand_navigation.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:integration_test/integration_test.dart';

/// Read-only inventory and screenshots from a real, isolated Football Demo.
void main() {
  final binding = IntegrationTestWidgetsFlutterBinding.ensureInitialized();
  const base = String.fromEnvironment('API_BASE_URL');
  const exercise = bool.fromEnvironment('QA_CASES');
  const roles = {
    'AcademyOwner': '01099900010',
    'AcademyAdmin': '01099900011',
    'Coach': '01099900020',
    'Guardian': '01099900101',
  };
  final output = Directory('${Directory.systemTemp.path}/academy-senior-qa')
    ..createSync(recursive: true);

  for (final entry in roles.entries) {
    testWidgets(
      '${entry.key} ${exercise ? 'focused cases' : 'visual review'}',
      (tester) async {
        expect(const bool.fromEnvironment('ISOLATED_DEMO'), isTrue);
        expect(Uri.parse(base).host, '127.0.0.1');
        final transport = HttpApiTransport(AppConfig('Demo', base));
        final storage = SecureRefreshStore(
          'academy.senior-qa.census.${entry.key}',
        );
        await storage.clear();
        final auth = AuthController(transport, storage);
        final appearance = Appearance();
        await auth.restore();
        final challenge = await auth.requestOtp(entry.value);
        await auth.verify(entry.value, challenge, '246810');
        expect(auth.session?.selected?.role, entry.key);
        expect(
          auth.session?.selected?.academyId,
          '30000000-0000-0000-0000-000000000003',
        );
        appearance.bindIntroUser(auth.session?.userId);
        await appearance.completeRoleIntro(entry.key, 'football');

        final captures = <Map<String, Object?>>[];
        final failures = <String>[];
        Future<void> pump([int count = 20]) async {
          for (var i = 0; i < count; i++) {
            await tester.pump(const Duration(milliseconds: 160));
          }
        }

        Future<void> capture(String name) async {
          await pump(9);
          final dump = tester.allWidgets.toList();
          final text = dump.whereType<Text>().map(
            (w) => w.data ?? w.textSpan?.toPlainText(),
          );
          final tooltips = dump.whereType<Tooltip>().map((w) => w.message);
          final actions = dump
              .whereType<Semantics>()
              .where((w) => w.properties.button == true)
              .map((w) => w.properties.label);
          final visible = <String>{
            ...text.whereType<String>(),
            ...tooltips.whereType<String>(),
            ...actions.whereType<String>(),
          }.where((s) => s.trim().isNotEmpty).toList();
          final id = 'phase-census-${entry.key.toLowerCase()}-$name';
          final bytes = await binding.takeScreenshot(id);
          await File('${output.path}/$id.png').writeAsBytes(bytes);
          captures.add({
            'screen': name,
            'image': '$id.png',
            'labels': visible,
            'logicalWidth':
                tester.view.physicalSize.width / tester.view.devicePixelRatio,
            'direction': Directionality.of(
              tester.element(find.byType(Text).first),
            ).name,
            'exception': tester.takeException()?.toString(),
          });
          await File('${output.path}/${entry.key}.json')
              .writeAsString(jsonEncode(captures));
        }

        Future<void> check(String id, Future<void> Function() body) async {
          try {
            await body();
            debugPrint('$id verified');
          } catch (error, trace) {
            failures.add('$id $error');
            debugPrint('$id failed: $error');
            debugPrintStack(stackTrace: trace, maxFrames: 6);
          }
        }

        Future<void> waitFor(Finder finder) async {
          for (var i = 0; i < 50 && finder.evaluate().isEmpty; i++) {
            await pump(1);
          }
          expect(finder, findsWidgets);
        }

        Future<void> tap(Finder finder) async {
          await waitFor(finder);
          await tester.ensureVisible(finder.first);
          await pump(3);
          expect(finder.hitTestable(), findsWidgets);
          await tester.tap(finder.hitTestable().first);
          await pump(14);
        }

        final vertical = find
            .byWidgetPredicate(
              (w) => w is Scrollable && w.axisDirection == AxisDirection.down,
            )
            .first;
        Future<void> reveal(Finder finder) async {
          for (
            var attempt = 0;
            attempt < 16 && finder.evaluate().isEmpty;
            attempt++
          ) {
            final scrollable = vertical.hitTestable();
            if (scrollable.evaluate().isNotEmpty) {
              await tester.drag(scrollable, const Offset(0, -380));
            }
            await pump(4);
          }
          await waitFor(finder);
          await tester.ensureVisible(finder.first);
          await pump(3);
        }

        Future<void> top() async {
          await waitFor(vertical);
          tester.state<ScrollableState>(vertical).position.jumpTo(0);
          await pump(5);
        }

        Future<void> visitNested(String label, String name) async {
          final target = find.text(label);
          if (target.evaluate().isEmpty) return;
          await tester.ensureVisible(target.first);
          await pump(3);
          if (target.hitTestable().evaluate().isEmpty) return;
          await tester.tap(target.hitTestable().first);
          await pump(12);
          final backs = find.byType(BackButton).hitTestable();
          if (backs.evaluate().isEmpty) return;
          await capture(name);
          await tester.tap(backs.last);
          await pump(9);
        }

        await tester.pumpWidget(
          AcademyApp(auth: auth, environment: 'Demo', appearance: appearance),
        );
        await pump(30);
        await capture('home');
        if (exercise && entry.key == 'AcademyOwner') {
          final panel = find.byType(AcademySummaryPanel);
          Future<void> overdue(num amount, int count) async {
            final target = find.descendant(
              of: panel,
              matching: find.byWidgetPredicate(
                (w) => w is MoneyText && num.tryParse('${w.value}') == amount,
              ),
            );
            await reveal(target);
            final label = find.text('لاعبون عليهم متأخرات');
            final tile = find
                .ancestor(of: label, matching: find.byType(InkWell))
                .first;
            expect(
              find.descendant(of: tile, matching: find.text('$count')),
              findsOneWidget,
            );
            final current = tester.widget<AcademySummaryPanel>(panel);
            expect(current.data['overdue']['players'], count);
            await reveal(label);
          }

          await check('NM-O01-ui', () async {
            await overdue(925, 2);
            await capture('overdue-925-two-players');
          });
          await check('NM-O02-partial', () async {
            final label = find.text('مستحقات متأخرة');
            await reveal(label);
            await tap(label);
            await waitFor(find.text('المستحقات المالية'));
            await capture('receivables-opened');
            await tap(find.byType(BackButton));
            await top();
            await tap(find.text('فرع مدينة نصر'));
            await overdue(800, 1);
            await capture('overdue-800-one-player');
            await top();
            await tap(find.text('إلغاء التصفية'));
            await overdue(925, 2);
          });
          await check('NM-O06-ui', () async {
            final statement = find.byType(HomeOperatingStatement);
            await reveal(statement);
            expect(
              tester
                  .widget<HomeOperatingStatement>(statement.first)
                  .data['totalExpenses'],
              123.45,
            );
            expect(
              tester
                  .widget<HomeOperatingStatement>(statement.first)
                  .data['surplus'],
              -123.45,
            );
            final deficit = find.text('العجز التشغيلي');
            await waitFor(deficit);
            await tester.ensureVisible(deficit.first);
            await capture('paid-expense-operating-deficit');
          });
        }
        if (exercise && entry.key == 'Coach') {
          await check('NM-S05-partial', () async {
            final groups = rows(
              await auth.request('GET', '/api/v1/coach/groups'),
            );
            expect(groups, hasLength(2));
            expect(
              groups
                  .map((g) => g['playerCount'] as num)
                  .reduce((a, b) => a + b),
              10,
            );
            await waitFor(find.text('مجموعات مسندة'));
            await tap(find.text('فتح المجموعة'));
            await waitFor(find.byType(CoachGroup));
            await capture('assigned-group-opened');
            await tap(find.byType(BackButton));
          });
          await check('NM-S07-ui', () async {
            final attendance = obj(
              await auth.request('GET', '/api/v1/staff/passkeys/attendance'),
            );
            expect(attendance['hasOpenAttendance'], isFalse);
            expect(
              rows(attendance['branches'])
                  .any((branch) => branch['enabled'] == true),
              isFalse,
            );
            expect(find.text('حضور وانصراف'), findsNothing);
            await waitFor(find.text('فتح المجموعة'));
            await auth.request('GET', '/api/v1/staff/work-periods');
            await capture('self-clock-disabled');
          });
        }
        if (exercise && entry.key == 'Guardian') {
          await check('NM-G01-partial', () async {
            final home = obj(
              await auth.request('GET', '/api/v1/guardian/home'),
            );
            final children = rows(home['children']);
            expect(children, hasLength(2));
            for (final child in children) {
              await waitFor(find.text(child['arabicName'] as String));
            }
            final second = children.last;
            await tap(find.text(second['arabicName'] as String));
            await waitFor(find.byKey(ValueKey('large-child-${second['id']}')));
            await capture('second-child-selected');
          });
        }
        final homeScroll = find.byType(Scrollable).hitTestable();
        if (!exercise && homeScroll.evaluate().isNotEmpty) {
          for (var i = 1; i <= 4; i++) {
            await tester.drag(homeScroll.first, const Offset(0, -610));
            await capture('home-scroll-$i');
          }
        }

        final owner = entry.key == 'AcademyOwner';
        final navigation = owner
            ? find.byType(OwnerBrandNavigation)
            : find.byType(NavigationBar);
        final tabs = owner
            ? const {1: 'operations', 2: 'matches', 3: 'reports', 4: 'more'}
            : switch (entry.key) {
                'AcademyAdmin' => const {
                  1: 'operations',
                  2: 'attendance',
                  3: 'more',
                },
                'Coach' => const {1: 'groups', 2: 'matches', 3: 'more'},
                _ => const {1: 'sessions', 2: 'matches', 3: 'evaluations'},
              };
        for (final tab in tabs.entries) {
          final destination = owner
              ? find.byKey(ValueKey('owner-nav-${tab.key}'))
              : find
                    .descendant(
                      of: navigation,
                      matching: find.byType(NavigationDestination),
                    )
                    .at(tab.key);
          if (destination.evaluate().isEmpty) continue;
          await tester.tap(destination);
          await pump(14);
          if (!exercise || tab.value != 'more') await capture(tab.value);
          if (exercise &&
              entry.key == 'AcademyAdmin' &&
              tab.value == 'attendance') {
            await check('NM-S03-empty', () async {
              await waitFor(find.text('لا توجد حصص اليوم.'));
              expect(find.text('0 حصة مرتبة حسب الوقت'), findsOneWidget);
            });
          }
          if (exercise && owner && tab.value == 'more') {
            await check('NM-O17-ui', () async {
              final action = find.text('إعدادات الرئيسية');
              await reveal(action);
              await tap(action);
              await waitFor(find.text('المظهر والرئيسية'));
              await waitFor(find.text('رسم محايد'));
              expect(find.text('إعادة تحميل الإعدادات الحالية'), findsNothing);
              await capture('owner-home-settings-defaults');
              await tap(find.byType(BackButton));
            });
          }
          if (!exercise && tab.value == 'more') {
            final scroll = find.byType(Scrollable).hitTestable();
            if (scroll.evaluate().isNotEmpty) {
              await tester.drag(scroll.first, const Offset(0, -425));
              await capture('more-scroll');
            }
            await visitNested('سجل دوامي', 'staff-work-periods');
            await visitNested('السلف والمبالغ الإضافية', 'staff-amounts');
            await visitNested('طلب إثبات حضوري', 'attendance-claim');
            await visitNested('إعدادات الرئيسية', 'owner-home-settings');
            await visitNested('الموظفون والصلاحيات', 'staff-access');
          }
        }
        await tester.pumpWidget(const SizedBox.shrink());
        await auth.logout();
        auth.dispose();
        transport.close();
        expect(failures, isEmpty, reason: failures.join('\n'));
      },
    );
  }
}
