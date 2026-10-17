import 'package:academy_mobile/core/api/api_client.dart';
import 'package:academy_mobile/core/auth/auth_controller.dart';
import 'package:academy_mobile/core/storage/refresh_store.dart';
import 'package:academy_mobile/features/guardian/guardian_ui.dart';
import 'package:academy_mobile/features/guardian/guardian_profile.dart';
import 'package:academy_mobile/features/shared/matches.dart';
import 'package:flutter/material.dart';
import 'package:flutter_localizations/flutter_localizations.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:integration_test/integration_test.dart';

void main() {
  final binding = IntegrationTestWidgetsFlutterBinding.ensureInitialized();
  testWidgets(
    'Guardian child profile opens only its published match history, including cancellation; logout clears it',
    (t) async {
      expect(const bool.fromEnvironment('ISOLATED_DEMO'), isTrue);
      final transport = HttpApiTransport(
        AppConfig('Demo', const String.fromEnvironment('API_BASE_URL')),
      );
      final auth = AuthController(
        transport,
        SecureRefreshStore('academy.v3.profile-matches.journey'),
      );
      await auth.restore();
      if (auth.session != null) await auth.logout();
      final challenge = await auth.requestOtp('01099900100');
      await auth.verify('01099900100', challenge, '246810');
      expect(
        auth.session!.selected!.academyId,
        '30000000-0000-0000-0000-000000000003',
      );
      final home = await auth.request('GET', '/api/v1/guardian/home') as Map;
      final mine = await auth.request('GET', '/api/v1/my-matches') as List;
      final selected =
          mine.firstWhere((m) => m['status'] == 'Cancelled') as Map;
      expect(selected['coachAssignmentSource'], 'Confirmed');
      expect(selected['coachName'], isA<String>());
      final childId = (selected['children'] as List).first as String;
      final child = (home['children'] as List).firstWhere(
        (c) => c['id'] == childId,
      ) as Map;
      final scoped = await auth.request(
        'GET',
        '/api/v1/my-matches?playerId=$childId',
      ) as List;
      expect(
        scoped.every((m) => (m['children'] as List).contains(childId)),
        isTrue,
      );
      await t.pumpWidget(
        MaterialApp(
          debugShowCheckedModeBanner: false,
          locale: const Locale('ar'),
          supportedLocales: const [Locale('ar')],
          localizationsDelegates: GlobalMaterialLocalizations.delegates,
          theme: guardianTheme(),
          home: ChildProfile(
            auth: auth,
            environment: 'Demo',
            playerId: childId,
            today: home['today'] as String,
          ),
        ),
      );
      Future<void> wait(Finder f) async {
        for (var i = 0; i < 180 && f.evaluate().isEmpty; i++) {
          await t.pump(const Duration(milliseconds: 100));
        }
        expect(f, findsWidgets);
        await t.pumpAndSettle();
      }

      await wait(find.text(child['arabicName'] as String));
      await t.scrollUntilVisible(
        find.text('المباريات'),
        160,
        scrollable: find.byType(Scrollable).first,
      );
      await t.tap(find.text('المباريات'));
      await wait(find.byType(MatchSchedule));
      await wait(find.text('ملغاة'));
      expect(
        find.text('${selected['team']} × ${selected['opponent']}'),
        findsOneWidget,
      );
      await t.scrollUntilVisible(
        find.text('${selected['team']} × ${selected['opponent']}'),
        160,
        scrollable: find.byType(Scrollable).first,
      );
      await t.scrollUntilVisible(
        find.text('مدرب المباراة: ${selected['coachName']}').last,
        120,
        scrollable: find.byType(Scrollable).first,
      );
      expect(find.text('مدرب المباراة: ${selected['coachName']}'), findsWidgets);
      if (const bool.fromEnvironment('CAPTURE_VISUALS')) {
        await t.pumpAndSettle();
        await binding.takeScreenshot('phase-profile-child-matches');
      }
      await auth.logout();
      await t.pumpAndSettle();
      expect(
        find.text('${selected['team']} × ${selected['opponent']}'),
        findsNothing,
      );
      await t.pumpWidget(const SizedBox());
      auth.dispose();
      transport.close();
    },
  );
}
