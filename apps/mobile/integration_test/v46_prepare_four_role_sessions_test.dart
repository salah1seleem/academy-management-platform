import '../test/introduction_test_helpers.dart';

import 'package:academy_mobile/app/app.dart';
import 'package:academy_mobile/core/api/api_client.dart';
import 'package:academy_mobile/core/auth/auth_controller.dart';
import 'package:academy_mobile/core/storage/refresh_store.dart';
import 'package:academy_mobile/core/theme/appearance.dart';
import 'package:academy_mobile/features/shared/role_navigation.dart';
import 'package:flutter_secure_storage/flutter_secure_storage.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:integration_test/integration_test.dart';

void main() {
  final binding = IntegrationTestWidgetsFlutterBinding.ensureInitialized();
  testWidgets(
    'Prepare the requested isolated simulator role through actual authentication in normal app storage',
    (t) async {
      expect(const bool.fromEnvironment('ISOLATED_DEMO'), true);
      const base = String.fromEnvironment('API_BASE_URL');
      expect(base, 'http://127.0.0.1:5278');
      const roles = {
        '8123D59D-B8B5-418A-8B7A-68E53E07154E': (
          '01099900010',
          'AcademyOwner',
          'owner',
        ),
        '48933B05-60BD-4464-91D3-D80C9357CEBF': (
          '01099900011',
          'AcademyAdmin',
          'admin',
        ),
        '487B7A96-8204-4ADC-A5CB-EAE1B885160F': (
          '01099900020',
          'Coach',
          'coach',
        ),
        '0A112EFC-366F-4E22-8B74-CB4DCCC3F2D4': (
          '01099900100',
          'Guardian',
          'guardian',
        ),
      };
      final role = roles[const String.fromEnvironment('DEMO_SIMULATOR_ID')];
      expect(role, isNotNull);
      final config = AppConfig('Demo', base);
      final auth = AuthController(
        HttpApiTransport(config),
        SecureRefreshStore(config.storageKey),
      );
      await auth.restore();
      if (auth.session != null && auth.session!.selected?.role != role!.$2) {
        await auth.logout();
      }
      if (auth.session == null) {
        final challenge = await auth.requestOtp(role!.$1);
        await auth.verify(role.$1, challenge, '246810');
      }
      expect(auth.session!.selected!.role, role!.$2);
      expect(
        auth.session!.selected!.academyId,
        '30000000-0000-0000-0000-000000000003',
      );
      final appearance = Appearance(storage: const FlutterSecureStorage());
      await appearance.load();
      await t.pumpWidget(
        AcademyApp(auth: auth, environment: 'Demo', appearance: appearance),
      );
      for (
        var i = 0;
        i < 200 && find.byType(NativeRoleTabs).evaluate().isEmpty;
        i++
      ) {
        if (find.text('تخطي').evaluate().isNotEmpty) {
          await finishRoleIntroduction(t);
          await t.pumpAndSettle();
        }
        await t.pump(const Duration(milliseconds: 150));
      }
      expect(find.byType(NativeRoleTabs), findsOneWidget);
      await t.pumpAndSettle();
      await binding.takeScreenshot('phase-approved-normal-role-${role.$3}');
      expect(t.takeException(), isNull);
      // Deliberately retain the normal namespace refresh session for the requested demo.
    },
  );
}
