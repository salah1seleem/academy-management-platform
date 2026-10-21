import 'package:academy_mobile/core/auth/auth_controller.dart';
import 'package:academy_mobile/core/theme/app_theme.dart';
import 'package:academy_mobile/features/admin/admin_home.dart';
import 'package:academy_mobile/features/shared/role_navigation.dart';
import 'package:flutter/material.dart';
import 'package:flutter_localizations/flutter_localizations.dart';
import 'package:flutter_test/flutter_test.dart';

import 'auth_controller_test.dart';

Future<AuthController> authenticate(String role, Handler handler) async {
  final auth = AuthController(
    FakeApi((m, p, b, t) async {
      if (p.contains('/auth/')) {
        return {
          ...credentials(),
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
    data: MediaQuery.of(context).copyWith(textScaler: TextScaler.linear(scale)),
    child: child!,
  ),
  home: page,
);

void main() {
  const expected = {
    'Guardian': ['الرئيسية', 'أبنائي', 'التقويم', 'المزيد'],
    'Coach': ['الرئيسية', 'جدولي', 'اللاعبون', 'المباريات', 'المزيد'],
    'AcademyOwner': ['الرئيسية', 'العمليات', 'المباريات', 'التقارير', 'المزيد'],
    'AcademyAdmin': ['الرئيسية', 'الحضور', 'الطلبات', 'المزيد'],
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
          final auth = await authenticate(role, (m, p, b, t) async => []);
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
          final destinations = tester
              .widgetList<NavigationDestination>(
                find.byType(NavigationDestination),
              )
              .map((d) => d.label)
              .toList();
          expect(destinations, expected[role]);
          expect(tester.takeException(), isNull);
          await tester.tap(find.text('المزيد'));
          await tester.pumpAndSettle();
          expect(find.text('فيديوهات شرح الصفحات'), findsOneWidget);
          if (role == 'AcademyOwner') {
            expect(find.text('الحساب والإعدادات'), findsNothing);
            expect(find.text('حضوري وانصرافي'), findsNothing);
          } else {
            await tester.scrollUntilVisible(
              find.text('الحساب والإعدادات'),
              200,
              scrollable: find.byType(Scrollable).first,
            );
            expect(find.text('الحساب والإعدادات'), findsOneWidget);
          }
          expect(tester.takeException(), isNull);
          auth.dispose();
        },
      );
    }
  }
  testWidgets(
    'Admin home hides attendance when its permission is denied and never reads owner finances',
    (tester) async {
      final calls = <String>[];
      final auth = await authenticate('AcademyAdmin', (m, p, b, t) async {
        calls.add(p);
        return p == '/api/v1/my-permissions'
            ? ['payment.read']
            : {'pending': 0, 'items': []};
      });
      await tester.pumpWidget(app(AdminHome(auth: auth)));
      await tester.pumpAndSettle();
      expect(find.text('طلبات الدفع'), findsOneWidget);
      expect(find.text('حضور اللاعبين'), findsNothing);
      expect(calls, [
        '/api/v1/my-permissions',
        '/api/v1/manage/payment-requests?status=Pending&allDates=true',
        '/api/v1/notifications',
      ]);
      expect(
        calls.any((p) => p.contains('/attendance/') || p.contains('/reports/')),
        isFalse,
      );
      expect(auth.session!.selected!.label, 'إداري');
      auth.dispose();
    },
  );
}
