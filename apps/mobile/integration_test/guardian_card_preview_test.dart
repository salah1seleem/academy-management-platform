import 'package:academy_mobile/core/auth/auth_controller.dart';
import 'package:academy_mobile/core/auth/session.dart';
import 'package:academy_mobile/core/theme/app_theme.dart';
import 'package:academy_mobile/features/guardian/guardian_home.dart';
import 'package:flutter/material.dart';
import 'package:flutter_localizations/flutter_localizations.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:integration_test/integration_test.dart';

import '../test/auth_controller_test.dart'
    show FakeApi, MemoryStore, credentials;
import '../test/guardian_home_workspace_test.dart' show home, response;

// Explicit fictional preview of the production card, not an actual child's rating.
void main() {
  final binding = IntegrationTestWidgetsFlutterBinding.ensureInitialized();
  testWidgets(
    'published mean card and synchronized child tabs native preview',
    (t) async {
      final data = home();
      final kids = data['children'] as List;
      final axes = [
        {'axis': 'Speed', 'value': 86},
        {'axis': 'Shooting', 'value': 88},
        {'axis': 'Passing', 'value': 85},
        {'axis': 'Dribbling', 'value': 90},
        {'axis': 'Defending', 'value': 76},
        {'axis': 'Physical', 'value': 86},
      ];
      final criteria = [
        {'name': 'سرعة', 'value': 86},
        {'name': 'تسديد', 'value': 88},
        {'name': 'تمرير', 'value': 85},
        {'name': 'مراوغة', 'value': 90},
        {'name': 'دفاع', 'value': 76},
        {'name': 'لياقة', 'value': 86},
      ];
      kids[0]['arabicName'] = 'لاعب عرض خيالي';
      kids[0]['photoReference'] = '/demo-assets/portraits/child-01.jpg';
      kids[0]['sports'][0]['performance'] = {
        'evaluationId': 'report-a',
        'meanPerformance': 85.2,
        'overallScore': 85.2,
        'scoredCriteria': 6,
        'applicableCriteria': 6,
        'isFootballReport': true,
        'axes': axes,
        'criteria': criteria,
      };
      kids[1]['arabicName'] = 'طفل آخر — معاينة';
      kids[1]['sports'][0]['sport'] = 'السباحة';
      kids[1]['sports'][0]['performance'] = {
        'evaluationId': 'report-b',
        'meanPerformance': 0,
        'overallScore': null,
        'scoredCriteria': 1,
        'applicableCriteria': 2,
        'isFootballReport': false,
        'criteria': [
          {'name': 'انسياب الماء', 'value': 0},
        ],
      };
      final auth = AuthController(
        FakeApi(
          (m, p, b, token) async =>
              p == '/api/v1/guardian/home' ? data : response(p),
        ),
        MemoryStore(),
      )..session = Session.fromJson(credentials());
      Future<void> tick(int ms) async {
        for (var elapsed = 0; elapsed < ms; elapsed += 50) {
          await t.pump(const Duration(milliseconds: 50));
        }
      }

      Future<void> mount({bool large = false}) async {
        await t.pumpWidget(
          MaterialApp(
            key: ValueKey(large),
            locale: const Locale('ar'),
            supportedLocales: const [Locale('ar')],
            localizationsDelegates: GlobalMaterialLocalizations.delegates,
            theme: AcademyTheme.light(),
            builder: (context, child) => MediaQuery(
              data: MediaQuery.of(context)
                  .copyWith(textScaler: TextScaler.linear(large ? 2 : 1)),
              child: child!,
            ),
            home: Scaffold(
              appBar: AppBar(
                title: const Text('بطاقة الأداء · معاينة اصطناعية'),
              ),
              body: GuardianHome(
                auth: auth,
                environment: 'Demo',
                accountPage: const SizedBox(),
              ),
            ),
          ),
        );
        await tick(1000);
      }

      await mount();
      expect(find.text('85.2'), findsWidgets);
      await binding.takeScreenshot('phase-guardian-mean-card-preview');
      await t.tap(find.byKey(const ValueKey('child-tab-b')));
      await tick(600);
      expect(find.text('0'), findsWidgets);
      await binding.takeScreenshot('phase-guardian-swim-zero-preview');
      await mount(large: true);
      await binding.takeScreenshot('phase-guardian-mean-large-preview');
      expect(t.takeException(), isNull);
    },
  );
}
