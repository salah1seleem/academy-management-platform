import '../test/introduction_test_helpers.dart';

import 'package:academy_mobile/app/app.dart';
import 'package:academy_mobile/core/api/api_client.dart';
import 'package:academy_mobile/core/auth/auth_controller.dart';
import 'package:academy_mobile/core/storage/refresh_store.dart';
import 'package:academy_mobile/core/theme/appearance.dart';
import 'package:academy_mobile/features/guardian/guardian_ui.dart';
import 'package:academy_mobile/features/owner/owner_dashboard.dart';
import 'package:academy_mobile/features/shared/page_video.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:integration_test/integration_test.dart';

import 'v45_single_renewal_saved_receipt_test.dart'
    show SavedRenewalReadTransport;

void main() {
  final binding = IntegrationTestWidgetsFlutterBinding.ensureInitialized();
  testWidgets(
    'Fresh owner app reads the saved disabled reports lesson and opens only the Arabic unavailable dialog',
    (t) async {
      expect(const bool.fromEnvironment('ISOLATED_DEMO'), true);
      const base = String.fromEnvironment('API_BASE_URL');
      expect(base, 'http://127.0.0.1:5278');
      final auth = AuthController(
        SavedRenewalReadTransport(AppConfig('Demo', base)),
        SecureRefreshStore('v47.5278.decision-assignment.owner10'),
      );
      await auth.restore();
      expect(auth.session?.selected?.role, 'AcademyOwner');
      final saved =
          rows(
            obj(await auth.request('GET', '/api/v1/manage/page-help'))['items'],
          ).singleWhere(
            (v) => v['role'] == 'AcademyOwner' && v['screen'] == 'reports',
          );
      expect(saved['title'], 'شرح اصطناعي لاختبار حفظ الإعداد');
      expect(saved['isActive'], false);
      final effect = obj(
        await auth.request('GET', '/api/v1/page-help/reports'),
      );
      expect(effect['available'], false);
      expect(effect['url'], null);
      Future<void> wait(Finder f) async {
        for (var i = 0; i < 240 && f.evaluate().isEmpty; i++) {
          if (find.text('تخطي').evaluate().isNotEmpty) {
            await finishRoleIntroduction(t);
          }
          await t.pump(const Duration(milliseconds: 150));
        }
        expect(f, findsWidgets);
        await t.pumpAndSettle();
      }

      await t.pumpWidget(
        AcademyApp(auth: auth, environment: 'Demo', appearance: Appearance()),
      );
      await wait(find.byType(OwnerDashboard));
      await t.tap(
        find.descendant(
          of: find.byType(NavigationBar),
          matching: find.text('التقارير'),
        ),
      );
      await t.pumpAndSettle();
      await wait(find.byTooltip('شرح الصفحة بالفيديو'));
      final buttons = t.widgetList<PageVideoButton>(
        find.byType(PageVideoButton),
      );
      expect(buttons.any((b) => b.screen == 'reports'), true);
      await t.tap(find.byTooltip('شرح الصفحة بالفيديو'));
      await t.pumpAndSettle();
      await wait(find.text('لم تضف الأكاديمية فيديو لهذه الصفحة بعد.'));
      expect(find.byType(PageVideoPlayer), findsNothing);
      await binding.takeScreenshot('phase-approved-page-video-disabled-owner');
      expect(t.takeException(), isNull);
      await t.pumpWidget(const SizedBox());
      auth.dispose();
    },
  );
}
