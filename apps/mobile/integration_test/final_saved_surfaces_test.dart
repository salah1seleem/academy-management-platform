import 'package:academy_mobile/app/app.dart';
import 'package:academy_mobile/core/api/api_client.dart';
import 'package:academy_mobile/core/auth/auth_controller.dart';
import 'package:academy_mobile/core/storage/refresh_store.dart';
import 'package:academy_mobile/core/theme/appearance.dart';
import 'package:academy_mobile/features/admin/native_enrollment.dart';
import 'package:academy_mobile/features/admin/native_operation_form.dart';
import 'package:academy_mobile/features/guardian/guardian_report.dart';
import 'package:academy_mobile/features/guardian/guardian_ui.dart';
import 'package:academy_mobile/features/shared/native_academy_content.dart';
import 'package:academy_mobile/features/shared/role_navigation.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:integration_test/integration_test.dart';

class SavedSurfaceTransport extends HttpApiTransport {
  SavedSurfaceTransport(super.config);
  final quotes = <Json>[];
  @override
  Future<Object?> send(
    String method,
    String path, {
    Object? body,
    String? token,
    String? idempotencyKey,
  }) async {
    final quote =
        method == 'POST' && path == '/api/v1/people/registration-quote';
    if (method != 'GET' && !quote && !path.startsWith('/api/v1/mobile/auth/')) {
      throw StateError(
        'Only saved reads, authentication and price preview: $method $path',
      );
    }
    final value = await super.send(
      method,
      path,
      body: body,
      token: token,
      idempotencyKey: idempotencyKey,
    );
    if (quote) {
      debugPrint('Preview intent: ${obj(body)['exceptionalDiscount']}');
      quotes.add(obj(value));
    }
    return value;
  }
}

class SavedSurfaceAppearance extends Appearance {
  @override
  Future<bool> hasRoleIntro(String role, String sportTheme) async => true;
}

void main() {
  final binding = IntegrationTestWidgetsFlutterBinding.ensureInitialized();
  testWidgets(
    'saved content and evaluation plus fixed/percentage preview at440',
    (t) async {
      expect(const bool.fromEnvironment('ISOLATED_DEMO'), true);
      const base = String.fromEnvironment('API_BASE_URL');
      expect(base, 'http://127.0.0.1:5278');
      expect((t.view.physicalSize / t.view.devicePixelRatio).width, 440);
      t.platformDispatcher.accessibilityFeaturesTestValue =
          const FakeAccessibilityFeatures(disableAnimations: true);
      addTearDown(t.platformDispatcher.clearAccessibilityFeaturesTestValue);
      final transport = SavedSurfaceTransport(AppConfig('Demo', base));
      final controllers = <AuthController>[];
      final appearance = SavedSurfaceAppearance();
      Future<AuthController> login(String role, String phone) async {
        final a = AuthController(
          transport,
          SecureRefreshStore('v73.5278.approved-intro.$role'),
        );
        controllers.add(a);
        await a.restore();
        if (a.session == null) {
          final c = await a.requestOtp(phone);
          await a.verify(phone, c, '246810');
        }
        expect(a.session!.selected!.role, role);
        expect(
          a.session!.selected!.academyId,
          '30000000-0000-0000-0000-000000000003',
        );
        return a;
      }

      Future<void> wait(Finder f) async {
        for (var i = 0; i < 240 && f.evaluate().isEmpty; i++) {
          await t.pump(const Duration(milliseconds: 150));
        }
        expect(f, findsWidgets);
        await t.pumpAndSettle();
      }

      Future<void> reach(Finder f) async {
        if (f.evaluate().isEmpty) {
          final scroll = find.byType(Scrollable).first;
          t.state<ScrollableState>(scroll).position.jumpTo(0);
          await t.pumpAndSettle();
          await t.scrollUntilVisible(
            f,
            160,
            scrollable: scroll,
            maxScrolls: 100,
          );
        }
        await t.ensureVisible(f.first);
        await t.pumpAndSettle();
      }

      Future<void> tap(String label) async {
        final f = find.text(label);
        await reach(f);
        await t.tap(f.first);
        await t.pumpAndSettle();
      }

      Future<void> field(String label, String value) async {
        final f = find.widgetWithText(TextFormField, label);
        await reach(f);
        // Re-focus through the real field after a dropdown or explicit unfocus.
        // WidgetTester's cached focusedEditable may still be this same widget.
        await t.tap(f);
        await t.pumpAndSettle();
        await t.enterText(f, value);
        await t.pumpAndSettle();
        expect(t.widget<TextFormField>(f).controller?.text, value);
      }

      Future<void> choose(String label, String value) async {
        final f = find.widgetWithText(DropdownButtonFormField<String>, label);
        await reach(f);
        await t.tap(f);
        await t.pumpAndSettle();
        final option = find.text(value).last;
        if (option.evaluate().isEmpty) {
          await t.scrollUntilVisible(
            option,
            100,
            scrollable: find.byType(Scrollable).last,
          );
        }
        await t.ensureVisible(option);
        await t.pumpAndSettle();
        await t.tap(option);
        await t.pumpAndSettle();
      }

      Future<void> show(AuthController a, bool dark, Widget page) async {
        await t.pumpWidget(const SizedBox.shrink());
        await t.pumpAndSettle();
        appearance.dark = dark;
        await t.pumpWidget(
          AcademyApp(auth: a, appearance: appearance, environment: 'Demo'),
        );
        await wait(find.byType(NativeRoleTabs));
        openPage(t.element(find.byType(NativeRoleTabs)), page);
        await t.pump();
      }

      Future<void> capture(String name) async {
        expect(t.takeException(), isNull);
        await binding.takeScreenshot('phase-approved-final-saved-440-$name');
      }

      try {
        final owner = await login('AcademyOwner', '01099900010');
        final financial = obj(
          await owner.request(
            'GET',
            '/api/v1/reports/financial?allDates=true&pageSize=1',
          ),
        );
        if (!const bool.fromEnvironment('RESUME_QUOTE_ONLY')) {
          final news = obj(
            await owner.request(
              'GET',
              '/api/v1/content/news/?paged=true&page=1',
            ),
          );
          expect(rows(news['items']), isNotEmpty);
          for (final dark in [false, true]) {
            await show(owner, dark, NativeAcademyContent(auth: owner));
            await wait(find.text(textOf(rows(news['items']).first['title'])));
            await capture('content-${dark ? 'dark' : 'light'}');
          }

          final family = await login('Guardian', '01099900100');
          final children = rows(
            obj(
              await family.request('GET', '/api/v1/guardian/home'),
            )['children'],
          );
          Json? report;
          String? reportId;
          for (final child in children) {
            final history = rows(
              await family.request(
                'GET',
                '/api/v1/guardian/children/${child['id']}/evaluations',
              ),
            );
            if (history.isNotEmpty) {
              reportId = textOf(history.first['id']);
              report = obj(
                await family.request(
                  'GET',
                  '/api/v1/guardian/evaluations/$reportId/report',
                ),
              );
              break;
            }
          }
          expect(
            reportId,
            isNotNull,
            reason: 'Use a published saved evaluation, never publish for a screenshot.',
          );
          for (final dark in [false, true]) {
            await show(family, dark, PlayerReport(auth: family, id: reportId!));
            await wait(find.text('التقييم العام'));
            expect(find.text(textOf(report!['player'])), findsWidgets);
            await capture('report-${dark ? 'dark' : 'light'}-header');
            await reach(find.text('المعايير التفصيلية'));
            await capture('report-${dark ? 'dark' : 'light'}-criteria');
          }
          expect(
            await family.request(
              'GET',
              '/api/v1/guardian/evaluations/$reportId/report',
            ),
            report,
          );
        }
        final structure = obj(
          await owner.request('GET', '/api/v1/manage/structure/options'),
        );
        final plans = rows(
          await owner.request('GET', '/api/v1/subscriptions/plans'),
        );
        Json? group, plan;
        for (final g in rows(structure['groups'])) {
          if (g['registrationFormat'] != 'Group') continue;
          final p = plans
              .where(
                (p) =>
                    p['sportId'] == g['sportId'] &&
                    p['isActive'] == true &&
                    p['serviceKind'] == 'Training' &&
                    (p['trainingFormat'] == null ||
                        p['trainingFormat'] == 'Group') &&
                    (p['allBranches'] == true ||
                        (p['branchIds'] as List? ?? []).contains(
                          g['branchId'],
                        )) &&
                    num.parse('${p['price']}') > 100,
              )
              .firstOrNull;
          if (p != null) {
            group = g;
            plan = p;
            break;
          }
        }
        expect(group, isNotNull);
        expect(plan, isNotNull);
        final g = group!, p = plan!;
        await show(owner, false, NativeNewEnrollment(auth: owner));
        await wait(find.widgetWithText(TextFormField, 'رقم الهاتف'));
        await field('رقم الهاتف', '01099900887');
        await choose(
          'الفرع',
          textOf(
            rows(structure['branches'])
                .singleWhere((b) => b['id'] == g['branchId'])['arabicName'],
          ),
        );
        await choose(
          'الرياضة',
          textOf(
            rows(structure['sports'])
                .singleWhere((s) => s['id'] == g['sportId'])['arabicName'],
          ),
        );
        await choose('المجموعة', textOf(g['arabicName']));
        await choose(
          'الباقة',
          '${p['arabicName']} · ${money(p['price'], p['currency'])}',
        );
        await tap('إضافة خصم استثنائي');
        await field('سبب الخصم', 'معاينة خصم اصطناعية فقط — دون إنشاء اشتراك');
        for (final percent in [false, true]) {
          await choose('نوع الخصم', percent ? 'نسبة مئوية' : 'قيمة ثابتة');
          await field('قيمة الخصم', percent ? '15' : '100');
          FocusManager.instance.primaryFocus?.unfocus();
          await t.pumpAndSettle();
          final beforeQuote = transport.quotes.length;
          await tap('حساب المبلغ المستحق');
          for (
            var i = 0;
            i < 240 && transport.quotes.length == beforeQuote;
            i++
          ) {
            await t.pump(const Duration(milliseconds: 150));
          }
          expect(transport.quotes.length, beforeQuote + 1);
          await wait(find.text('المبلغ النهائي'));
          final q = transport.quotes.last;
          debugPrint('Price preview percent=$percent result=$q');
          expect(storedAmountMatches(q['originalAmount'], p['price']), true);
          final price = num.parse('${p['price']}');
          expect(
            storedAmountMatches(
              q['discountAmount'],
              percent ? (price * .15).toStringAsFixed(2) : '100.00',
            ),
            true,
          );
          expect(
            storedAmountMatches(
              q['finalAmount'],
              (percent ? price * .85 : price - 100).toStringAsFixed(2),
            ),
            true,
          );
          await reach(find.text('المبلغ النهائي'));
          await capture(
            'discount-${percent ? 'percent' : 'fixed'}-light-preview',
          );
        }
        expect(transport.quotes, hasLength(2));
        final after = obj(
          await owner.request(
            'GET',
            '/api/v1/reports/financial?allDates=true&pageSize=1',
          ),
        );
        expect(after['netAmount'], financial['netAmount']);
        expect(after['totalCount'], financial['totalCount']);
        expect(
          await owner.request('GET', '/api/v1/subscriptions/plans'),
          plans,
        );
      } finally {
        await t.pumpWidget(const SizedBox.shrink());
        await t.pumpAndSettle();
        for (final a in controllers) {
          a.dispose();
        }
        appearance.dispose();
      }
    },
  );
}
