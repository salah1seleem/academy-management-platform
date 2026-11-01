import 'package:academy_mobile/core/api/api_client.dart';
import 'package:academy_mobile/core/auth/auth_controller.dart';
import 'package:academy_mobile/core/storage/refresh_store.dart';
import 'package:academy_mobile/core/theme/app_theme.dart';
import 'package:academy_mobile/features/admin/native_player_workspace.dart';
import 'package:academy_mobile/features/guardian/guardian_ui.dart';
import 'package:flutter/material.dart';
import 'package:flutter_localizations/flutter_localizations.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:integration_test/integration_test.dart';

void main() {
  final binding = IntegrationTestWidgetsFlutterBinding.ensureInitialized();
  testWidgets(
    'actual iOS picker links a synthetic photo to the intended player',
    (t) async {
      expect(const bool.fromEnvironment('ISOLATED_DEMO'), true);
      const base = String.fromEnvironment('API_BASE_URL');
      expect(base, 'http://127.0.0.1:5278');
      final auth = AuthController(
        HttpApiTransport(AppConfig('Demo', base)),
        SecureRefreshStore('v45.5278.os-media-entry.01099900010'),
      );
      await auth.restore();
      if (auth.session == null) {
        final challenge = await auth.requestOtp('01099900010');
        await auth.verify('01099900010', challenge, '246810');
      }
      expect(auth.session?.selected?.role, 'AcademyOwner');
      expect(
        auth.session!.selected!.academyId,
        '30000000-0000-0000-0000-000000000003',
      );
      const id = '90445c63-4f72-9320-a1d1-aa99990f6d02';
      final before = obj(
        await auth.request('GET', '/api/v1/people/players/$id'),
      );
      expect(before['playerCode'], 'FB-0011');
      // Do not repeat an upload after an interrupted acceptance journey.
      expect(
        before['photoReference'],
        isNot(startsWith('/api/v1/assets/')),
        reason: 'An uploaded photo is already linked. Inspect it instead of uploading again.',
      );
      Future<void> wait(Finder f) async {
        for (var i = 0; i < 240 && f.evaluate().isEmpty; i++) {
          await t.pump(const Duration(milliseconds: 150));
        }
        expect(f, findsWidgets);
        await t.pumpAndSettle();
      }

      await t.pumpWidget(
        MaterialApp(
          locale: const Locale('ar'),
          supportedLocales: const [Locale('ar')],
          localizationsDelegates: GlobalMaterialLocalizations.delegates,
          theme: AcademyTheme.dark(),
          home: NativePlayerWorkspace(auth: auth, id: id),
        ),
      );
      await wait(find.text('تغيير صورة اللاعب'));
      final scroll = find.byType(Scrollable).first;
      await t.scrollUntilVisible(
        find.text('تغيير صورة اللاعب'),
        140,
        scrollable: scroll,
      );
      await t.tap(find.text('تغيير صورة اللاعب'));
      await t.pump(const Duration(milliseconds: 500));
      debugPrint(
        'Choose only I-Academy-Photo-Test.png in the real iOS file picker.',
      );
      // The OS picker is operated externally; the real widget performs upload and readback.
      for (
        var i = 0;
        i < 1600 &&
            find.text('حُفظت صورة اللاعب وأُعيدت قراءتها.').evaluate().isEmpty;
        i++
      ) {
        await t.pump(const Duration(milliseconds: 150));
        if (i % 20 == 0 &&
            find.byType(CircularProgressIndicator).evaluate().isEmpty) {
          t
              .state<ScrollableState>(scroll)
              .position
              .jumpTo(
                t.state<ScrollableState>(scroll).position.maxScrollExtent,
              );
        }
      }
      await wait(find.text('حُفظت صورة اللاعب وأُعيدت قراءتها.'));
      final after = obj(
        await auth.request('GET', '/api/v1/people/players/$id'),
      );
      expect(after['photoReference'], startsWith('/api/v1/assets/'));
      expect(after['photoReference'], isNot(before['photoReference']));
      expect(after['arabicName'], before['arabicName']);
      expect(after['enrollments'], before['enrollments']);
      debugPrint(
        'Saved synthetic player photo: player=$id reference=${after['photoReference']}',
      );
      t.state<ScrollableState>(scroll).position.jumpTo(0);
      await t.pumpAndSettle();
      await binding.takeScreenshot('phase-approved-player-os-photo-persisted');
      expect(t.takeException(), isNull);
      await t.pumpWidget(const SizedBox());
      auth.dispose();
    },
  );
}
