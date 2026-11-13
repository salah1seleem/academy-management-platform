import 'package:academy_mobile/core/api/api_client.dart';
import 'package:academy_mobile/core/auth/auth_controller.dart';
import 'package:academy_mobile/core/theme/app_theme.dart';
import 'package:academy_mobile/features/admin/admin_home.dart';
import 'package:academy_mobile/features/shared/role_navigation.dart';
import 'package:academy_mobile/features/shared/owner_brand_navigation.dart';
import 'package:flutter/material.dart';
import 'package:flutter_localizations/flutter_localizations.dart';
import 'package:flutter_test/flutter_test.dart';

import 'auth_controller_test.dart';

Future<AuthController> authenticate(
  String role,
  Handler handler, {
  String? userId,
}) async {
  final auth = AuthController(
    FakeApi((m, p, b, t) async {
      if (p.contains('/auth/')) {
        return {
          ...credentials(),
          'userId': ?userId,
          'membershipId': 'member',
          'memberships': [
            {
              'id': 'member',
              'academyId': 'academy',
              'academyName': 'أكاديمية اختبار',
              'role': role,
            },
          ],
        };
      }
      return handler(m, p, b, t);
    }),
    MemoryStore(),
  )..restoring = false;
  await auth.verify('phone', 'challenge', 'code');
  return auth;
}

Widget app(Widget page, {double scale = 1}) => MaterialApp(
  locale: const Locale('ar'),
  supportedLocales: const [Locale('ar')],
  localizationsDelegates: GlobalMaterialLocalizations.delegates,
  theme: AcademyTheme.light(),
  builder: (context, child) => MediaQuery(
    data: MediaQuery.of(
      context,
    ).copyWith(textScaler: TextScaler.linear(scale), disableAnimations: true),
    child: child!,
  ),
  home: page,
);

void main() {
  const expected = {
    'Guardian': ['الرئيسية', 'الحصص', 'المباريات', 'التقييمات'],
    'Coach': ['الرئيسية', 'المجموعات', 'المباريات', 'المزيد'],
    'AcademyOwner': ['العمليات', 'المباريات', 'الرئيسية', 'التقارير', 'المزيد'],
    'AcademyAdmin': ['الرئيسية', 'العمليات', 'الحضور', 'المزيد'],
  };
  for (final role in expected.keys) {
    for (final width in [360.0, 390.0, 430.0]) {
      testWidgets(
        '$role has its approved role-specific destinations at $width with 200% text',
        (tester) async {
          tester.view.physicalSize = Size(width, 900);
          tester.view.devicePixelRatio = 1;
          addTearDown(tester.view.resetPhysicalSize);
          addTearDown(tester.view.resetDevicePixelRatio);
          final calls = <String>[];
          final auth = await authenticate(role, (m, p, b, t) async {
            calls.add('$m $p');
            return [];
          });
          await tester.pumpWidget(
            app(
              NativeRoleTabs(
                auth: auth,
                role: role,
                home: const Scaffold(body: Text('محتوى الدور')),
                account: const Scaffold(body: Text('إعدادات الحساب')),
              ),
              scale: 2,
            ),
          );
          await tester.pumpAndSettle();
          final destinations = role == 'AcademyOwner'
              ? tester
                    .widgetList<Semantics>(
                      find.descendant(
                        of: find.byType(OwnerBrandNavigation),
                        matching: find.byType(Semantics),
                      ),
                    )
                    .where((w) => w.properties.button == true)
                    .map((w) => w.properties.label)
                    .whereType<String>()
                    .where((v) => v.isNotEmpty)
                    .toList()
              : tester
                    .widgetList<NavigationDestination>(
                      find.byType(NavigationDestination),
                    )
                    .map((d) => d.label)
                    .toList();
          expect(destinations, expected[role]);
          expect(tester.takeException(), isNull);
          expect(find.byTooltip('حسابي'), findsOneWidget);
          expect(find.byTooltip('التنبيهات'), findsOneWidget);
          expect(find.byTooltip('شرح الصفحة بالفيديو'), findsNothing);
          if (role == 'Coach') {
            await tester.tap(find.text('المجموعات'));
            await tester.pumpAndSettle();
            expect(find.byType(CoachGroupsPage), findsOneWidget);
            expect(find.text('لا توجد مجموعات مسندة إليك.'), findsOneWidget);
            expect(calls.where((call) => call.contains('/api/v1/coach/')), [
              'GET /api/v1/coach/groups',
            ]);
            await tester.tap(find.text('الرئيسية'));
            await tester.pumpAndSettle();
            expect(find.text('محتوى الدور'), findsOneWidget);
          }
          if (role != 'Guardian') {
            await tester.tap(find.text('المزيد'));
            await tester.pumpAndSettle();
            expect(find.byTooltip('حسابي'), findsOneWidget);
            expect(find.byTooltip('التنبيهات'), findsOneWidget);
            expect(find.byTooltip('شرح الصفحة بالفيديو'), findsNothing);
            expect(find.text('فيديوهات شرح الصفحات'), findsNothing);
            expect(find.text('الحساب والإعدادات'), findsNothing);
            expect(find.text('حضوري وانصرافي'), findsNothing);
          }
          await tester.pumpWidget(const SizedBox());
          expect(tester.takeException(), isNull);
          auth.dispose();
        },
      );
    }
  }
  for (final matchAccess in ['denied', 'forbidden', 'granted']) {
    testWidgets(
      'Admin home with $matchAccess match access keeps attendance and finance within permission',
      (tester) async {
        final calls = <String>[];
        final auth = await authenticate('AcademyAdmin', (m, p, b, t) async {
          calls.add(p);
          if (p == '/api/v1/my-permissions') return ['payment.read'];
          if (p == '/api/v1/my-match-access') {
            if (matchAccess == 'forbidden') {
              throw const ApiFailure(403, 'لا توجد صلاحية.');
            }
            return {
              'canRead': matchAccess == 'granted',
              'canAdd': false,
              'canEdit': false,
            };
          }
          return {'pending': 0, 'items': []};
        });
        await tester.pumpWidget(app(AdminHome(auth: auth)));
        await tester.pumpAndSettle();
        expect(find.text('طلبات الدفع'), findsOneWidget);
        expect(find.text('حضور اللاعبين'), findsNothing);
        expect(find.text('تسجيل الحضور'), findsNothing);
        expect(
          find.text('إدارة المباريات والنتائج'),
          matchAccess == 'granted' ? findsOneWidget : findsNothing,
        );
        expect(calls.toSet(), {
          '/api/v1/my-permissions',
          '/api/v1/my-access',
          '/api/v1/my-match-access',
          '/api/v1/manage/payment-requests?status=Pending&allDates=true',
          '/api/v1/staff/passkeys/attendance',
          '/api/v1/news',
        });
        expect(
          calls.any(
            (p) =>
                p.contains('/attendance/') ||
                p.contains('/reports/') ||
                p.contains('/manage/matches'),
          ),
          isFalse,
        );
        expect(auth.session!.selected!.label, 'إداري');
        await tester.pumpWidget(const SizedBox());
        auth.dispose();
      },
    );
  }
}
