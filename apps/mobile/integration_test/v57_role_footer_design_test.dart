import 'package:academy_mobile/app/app.dart';
import 'package:academy_mobile/features/coach/coach_home.dart';
import 'package:academy_mobile/core/api/api_client.dart';
import 'package:academy_mobile/core/auth/auth_controller.dart';
import 'package:academy_mobile/core/storage/refresh_store.dart';
import 'package:academy_mobile/core/theme/appearance.dart';
import 'package:academy_mobile/features/shared/role_navigation.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:integration_test/integration_test.dart';

class FooterReadTransport extends HttpApiTransport {
  FooterReadTransport(super.config);
  final writes = <String>[];
  @override
  Future<Object?> send(
    String method,
    String path, {
    Object? body,
    String? token,
    String? idempotencyKey,
  }) {
    if (method != 'GET' && !path.contains('/auth/')) {
      writes.add('$method $path');
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
  const roleFilter = String.fromEnvironment('ROLE_FOOTER_FILTER');
  testWidgets(
    'real role shells retain destinations and show approved states, filter=$roleFilter',
    (t) async {
      expect(const bool.fromEnvironment('ISOLATED_DEMO'), true);
      const base = String.fromEnvironment('API_BASE_URL');
      expect(base, 'http://127.0.0.1:5278');
      Future<void> wait(Finder f) async {
        for (var i = 0; i < 240 && f.evaluate().isEmpty; i++) {
          await t.pump(const Duration(milliseconds: 150));
        }
        expect(f, findsWidgets);
        await t.pumpAndSettle();
      }

      var checkedRoles = 0;
      for (final sample in [
        (
          'AcademyOwner',
          '01099900010',
          'owner',
          'ملخص الأكاديمية',
          'المباريات',
        ),
        ('AcademyAdmin', '01099900011', 'admin', 'حضور اللاعبين', 'الحضور'),
        ('Coach', '01099900020', 'coach', 'المجموعات المسندة', 'المباريات'),
        ('Guardian', '01099900100', 'guardian', 'أبنائي', 'المباريات'),
      ]) {
        if (roleFilter.isNotEmpty && sample.$1 != roleFilter) continue;
        checkedRoles++;
        final transport = FooterReadTransport(AppConfig('Demo', base));
        final auth = AuthController(
          transport,
          SecureRefreshStore('v45.5278.os-media-entry.${sample.$2}'),
        );
        await auth.restore();
        if (auth.session == null) {
          final c = await auth.requestOtp(sample.$2);
          await auth.verify(sample.$2, c, '246810');
        }
        expect(
          auth.session!.selected!.academyId,
          '30000000-0000-0000-0000-000000000003',
        );
        expect(auth.session!.selected!.role, sample.$1);
        final appearance = Appearance();
        for (final theme in ['football', 'general']) {
          await appearance.completeRoleIntro(sample.$1, theme);
        }
        for (final light in [true, false]) {
          await t.pumpWidget(const SizedBox());
          await t.pumpAndSettle();
          await appearance.update(night: !light);
          await t.pumpWidget(
            AcademyApp(
              key: ValueKey('${sample.$1}-$light'),
              auth: auth,
              environment: 'Demo',
              appearance: appearance,
            ),
          );
          await wait(find.byType(NativeRoleTabs));
          await wait(find.textContaining(sample.$4));
          expect(find.byTooltip('حسابي'), findsOneWidget);
          expect(find.byTooltip('التنبيهات'), findsOneWidget);
          final primary = light
              ? const Color(0xffc8102e)
              : const Color(0xff3b6cf0);
          expect(
            t
                .widget<ColoredBox>(
                  find.byKey(const ValueKey('role-nav-marker-0')),
                )
                .color,
            primary,
          );
          await t.tap(
            find.descendant(
              of: find.byType(NavigationBar),
              matching: find.text(sample.$5),
            ),
          );
          await t.pumpAndSettle();
          await wait(find.byKey(const ValueKey('role-nav-marker-2')));
          final bar = t.widget<NavigationBar>(find.byType(NavigationBar));
          expect(bar.selectedIndex, 2);
          expect(
            t
                .widget<ColoredBox>(
                  find.byKey(const ValueKey('role-nav-marker-2')),
                )
                .color,
            primary,
          );
          final destination = bar.destinations[2] as NavigationDestination;
          final football = sample.$1 == 'AcademyOwner' || sample.$1 == 'Coach';
          expect(
            (destination.selectedIcon as Icon).color,
            football
                ? (light ? const Color(0xff16803c) : const Color(0xff34d399))
                : primary,
          );
          expect(find.byTooltip('حسابي'), findsOneWidget);
          expect(find.byTooltip('التنبيهات'), findsOneWidget);
          expect(find.byType(AppBar), findsNothing);
          expect(
            find.text(sample.$1 == 'AcademyAdmin' ? 'حصص اليوم' : 'المباريات'),
            findsWidgets,
          );
          if (sample.$1 == 'AcademyAdmin') {
            final calendar =
                await auth.request('GET', '/api/v1/academy/calendar') as Map;
            final source = (await auth.request(
              'GET',
              '/api/v1/attendance/sessions',
            ) as List).cast<Map>();
            final session = source.firstWhere(
              (s) =>
                  s['sessionDate'] == calendar['today'] &&
                  s['status'] != 'Cancelled',
            );
            final line =
                '${session['sessionDate']} · ${CoachSessionCard.time(session['startTime'])} — ${CoachSessionCard.time(session['endTime'])}';
            expect(find.text(line), findsWidgets);
            expect(
              t.widget<Text>(find.text(line).first).textDirection,
              TextDirection.ltr,
            );
          }
          if (sample.$1 == 'AcademyOwner') {
            await wait(find.byTooltip('إدارة المباريات والنتائج'));
            await t.tap(find.byTooltip('إدارة المباريات والنتائج'));
            await wait(find.text('إضافة مباراة'));
            expect(find.byType(AppBar), findsOneWidget);
            expect(find.byType(BackButton), findsOneWidget);
            expect(find.byTooltip('شرح الصفحة بالفيديو'), findsOneWidget);
            await binding.takeScreenshot(
              'phase-approved-owner-detail-header-${light ? 'light' : 'dark'}',
            );
            await t.tap(find.byType(BackButton));
            await t.pumpAndSettle();
            expect(find.byType(AppBar), findsNothing);
            expect(
              t.widget<NavigationBar>(find.byType(NavigationBar)).selectedIndex,
              2,
            );
          }
          await binding.takeScreenshot(
            'phase-approved-${sample.$3}-shell-${light ? 'light' : 'dark'}',
          );
          expect(t.takeException(), isNull);
        }
        expect(transport.writes, isEmpty);
        await t.pumpWidget(const SizedBox());
        auth.dispose();
        appearance.dispose();
      }
      expect(checkedRoles, roleFilter.isEmpty ? 4 : 1);
    },
  );
}
