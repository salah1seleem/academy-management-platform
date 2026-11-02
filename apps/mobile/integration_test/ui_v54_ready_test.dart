import 'startup_motion_journey_test.dart' show StartupReadTransport;

import 'package:academy_mobile/app/app.dart';
import 'package:academy_mobile/core/api/api_client.dart';
import 'package:academy_mobile/core/auth/auth_controller.dart';
import 'package:academy_mobile/core/storage/refresh_store.dart';
import 'package:academy_mobile/core/theme/appearance.dart';
import 'package:academy_mobile/core/theme/academy_brand.dart';
import 'package:academy_mobile/features/shared/experience_ui.dart';
import 'package:flutter_secure_storage/flutter_secure_storage.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:integration_test/integration_test.dart';

void main() {
  final binding = IntegrationTestWidgetsFlutterBinding.ensureInitialized();
  testWidgets('isolated ordinary preview restores owner and saved portrait', (
    t,
  ) async {
    const base = String.fromEnvironment('API_BASE_URL');
    expect(base, 'http://127.0.0.1:5378');
    expect(const bool.fromEnvironment('ISOLATED_DEMO'), true);
    final config = AppConfig('Demo', base);
    final auth = AuthController(
      StartupReadTransport(config),
      SecureRefreshStore(config.storageKey),
    );
    await auth.restore();
    if (auth.session == null ||
        !auth.session!.memberships.any(
          (m) =>
              m.academyId == '507a3fde-42f9-43fa-bbbb-5a6009365d35' &&
              m.role == 'AcademyOwner',
        )) {
      final challenge = await auth.requestOtp('01084720300');
      await auth.verify('01084720300', challenge, '246810');
    }
    const academy = '507a3fde-42f9-43fa-bbbb-5a6009365d35';
    final member = auth.session!.memberships.firstWhere(
      (m) => m.academyId == academy && m.role == 'AcademyOwner',
    );
    if (auth.session?.membershipId != member.id) {
      await auth.selectRole(member.id);
    }
    final prefs = Appearance(storage: const FlutterSecureStorage());
    await prefs.load();
    prefs.bindIntroUser(auth.session!.userId);
    await prefs.completeRoleIntro('AcademyOwner', 'neutral');
    await prefs.update(night: false);
    auth.restoring = false;
    await t.pumpWidget(
      AcademyApp(auth: auth, environment: 'Demo', appearance: prefs),
    );
    for (var n = 0; n < 400 && find.text('255').evaluate().isEmpty; n++) {
      await t.pump(const Duration(milliseconds: 100));
    }
    expect(find.text('255'), findsOneWidget);
    final brand = BrandScope.maybeOf(t.element(find.byType(ExperienceHeader)))!;
    await brand.loadProfile();
    expect(brand.profilePhoto, isNotNull);
    await t.pump(const Duration(milliseconds: 400));
    await binding.takeScreenshot('phase-refine-owner-restored');
    expect(t.takeException(), isNull);
  });
}
