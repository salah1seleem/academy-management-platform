import 'package:academy_mobile/app/app.dart';
import 'package:academy_mobile/core/api/api_client.dart';
import 'package:academy_mobile/core/auth/auth_controller.dart';
import 'package:academy_mobile/core/storage/refresh_store.dart';
import 'package:academy_mobile/core/theme/appearance.dart';
import 'package:academy_mobile/features/admin/admin_home.dart';
import 'package:academy_mobile/features/coach/coach_home.dart';
import 'package:academy_mobile/features/guardian/guardian_home.dart';
import 'package:academy_mobile/features/owner/owner_welcome_banner.dart';
import 'package:academy_mobile/features/shared/role_navigation.dart';
import 'package:flutter/material.dart';
import 'package:flutter_secure_storage/flutter_secure_storage.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:integration_test/integration_test.dart';

import 'support/role_intro.dart';

void main() {
  final binding = IntegrationTestWidgetsFlutterBinding.ensureInitialized();
  const roles = [
    ('owner', 'AcademyOwner', '01099900010', OwnerWelcomeBanner, 'التقارير'),
    ('admin', 'AcademyAdmin', '01099900011', AdminHome, 'العمليات'),
    ('coach', 'Coach', '01099900020', CoachHome, 'الحضور والتقييم'),
    ('guardian', 'Guardian', '01099900100', GuardianHome, 'التقييمات'),
  ];

  for (final (label, role, phone, home, tab) in roles) {
    testWidgets('$role opens its own home with the isolated Demo API', (
      tester,
    ) async {
      expect(const bool.fromEnvironment('ACADEMY_ISOLATED_IOS_QA'), isTrue);
      const base = String.fromEnvironment('API_BASE_URL');
      expect(base, 'http://127.0.0.1:5397');
      final auth = AuthController(
        HttpApiTransport(AppConfig('Demo', base)),
        SecureRefreshStore('academy.ios-cloud.$label'),
      );
      final challenge = await auth.requestOtp(phone);
      await auth.verify(phone, challenge, '246810');
      expect(auth.session?.selected?.role, role);
      expect(
        auth.session?.selected?.academyId,
        '30000000-0000-0000-0000-000000000003',
      );

      final appearance = Appearance(storage: const FlutterSecureStorage());
      await appearance.load();
      await tester.pumpWidget(
        AcademyApp(auth: auth, environment: 'Demo', appearance: appearance),
      );
      await skipRoleIntroIfPresent(tester);
      final nav = find.byType(NativeRoleTabs);
      for (var tick = 0; tick < 120 && nav.evaluate().isEmpty; tick++) {
        await tester.pump(const Duration(milliseconds: 150));
      }
      expect(tester.widget<NativeRoleTabs>(nav).role, role);
      expect(find.byType(home), findsOneWidget);
      expect(find.text(tab), findsWidgets);
      await binding.takeScreenshot('phase-cloud-$label');
      expect(tester.takeException(), isNull);

      await tester.pumpWidget(const SizedBox.shrink());
      await auth.logout();
      expect(auth.session, isNull);
      auth.dispose();
      appearance.dispose();
    });
  }
}
