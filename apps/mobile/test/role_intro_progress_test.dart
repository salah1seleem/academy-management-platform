import 'package:academy_mobile/core/theme/appearance.dart';
import 'package:academy_mobile/features/shared/role_intro_gate.dart';
import 'package:flutter/material.dart';
import 'package:flutter_secure_storage/flutter_secure_storage.dart';
import 'package:flutter_test/flutter_test.dart';

import 'v2_role_workspaces_test.dart' show authenticate, app;
import 'introduction_test_helpers.dart';

void main() {
  test('tutorial completion and resumable page belong to user/role/version, independent of academy sport', () async {
    FlutterSecureStorage.setMockInitialValues({});
    final prefs = Appearance(storage: const FlutterSecureStorage());
    prefs.bindIntroUser('synthetic-user-a');
    await prefs.saveRoleIntroPage('Guardian', 'football', 1);
    final reopened = Appearance(storage: const FlutterSecureStorage())
      ..bindIntroUser('synthetic-user-a');
    expect(await reopened.roleIntroPage('Guardian', 'swimming'), 1);
    expect(await reopened.hasRoleIntro('Guardian', 'football'), false);
    reopened.bindIntroUser('synthetic-user-b');
    expect(await reopened.roleIntroPage('Guardian', 'football'), 0);
    expect(await reopened.hasRoleIntro('Guardian', 'football'), false);
    reopened.bindIntroUser('synthetic-user-a');
    await reopened.completeRoleIntro('Guardian', 'swimming');
    expect(await reopened.hasRoleIntro('Guardian', 'general'), true);
    expect(await reopened.hasRoleIntro('Coach', 'football'), false);
    reopened.bindIntroUser('synthetic-user-b');
    expect(await reopened.hasRoleIntro('Guardian', 'swimming'), false);
    prefs.dispose();
    reopened.dispose();
  });
  testWidgets(
    'gate resumes a manual page, with no skip and no automatic advance',
    (t) async {
      final auth = await authenticate('Guardian', (m, p, b, token) async => []);
      final prefs = Appearance();
      await prefs.saveRoleIntroPage('Guardian', 'football', 1);
      await t.pumpWidget(
        app(
          RoleIntroGate(
            auth: auth,
            appearance: prefs,
            child: const Text('الرئيسية الفعلية'),
          ),
        ),
      );
      await t.pumpAndSettle();
      expect(find.bySemanticsLabel('الخطوة 2 من 3'), findsOneWidget);
      expect(find.text('تخطي'), findsNothing);
      await t.pump(const Duration(seconds: 10));
      expect(find.bySemanticsLabel('الخطوة 2 من 3'), findsOneWidget);
      await finishRoleIntroduction(t);
      await t.pumpAndSettle();
      expect(find.text('الرئيسية الفعلية'), findsOneWidget);
      expect(await prefs.hasRoleIntro('Guardian', 'general'), true);
      await t.pumpWidget(const SizedBox());
      auth.dispose();
      prefs.dispose();
    },
  );
}
