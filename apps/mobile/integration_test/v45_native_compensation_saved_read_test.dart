import 'package:academy_mobile/app/app.dart';
import 'package:academy_mobile/core/api/api_client.dart';
import 'package:academy_mobile/core/auth/auth_controller.dart';
import 'package:academy_mobile/core/storage/refresh_store.dart';
import 'package:academy_mobile/core/theme/appearance.dart';
import 'package:academy_mobile/features/admin/native_player_workspace.dart';
import 'package:academy_mobile/features/admin/native_player_placement.dart';
import 'package:academy_mobile/features/guardian/guardian_ui.dart';
import 'package:academy_mobile/features/owner/home_destinations.dart';
import 'package:academy_mobile/features/owner/refined_home_panels.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:integration_test/integration_test.dart';

import 'v45_native_compensation_test.dart' show CompensationAcceptanceTransport;

void main() {
  final binding = IntegrationTestWidgetsFlutterBinding.ensureInitialized();
  testWidgets(
    'resume the saved native compensation and released booking through read only production screens',
    (t) async {
      expect(const bool.fromEnvironment('ISOLATED_DEMO'), true);
      const base = String.fromEnvironment('API_BASE_URL');
      expect(base, 'http://127.0.0.1:5278');
      expect((t.view.physicalSize / t.view.devicePixelRatio).width, 375);
      final transport = CompensationAcceptanceTransport(
        AppConfig('Demo', base),
      );
      final auth = AuthController(
        transport,
        SecureRefreshStore('v45.5278.os-media-entry.01099900010'),
      );
      await auth.restore();
      expect(auth.session, isNotNull);
      expect(auth.session!.selected!.role, 'AcademyOwner');
      expect(
        auth.session!.selected!.academyId,
        '30000000-0000-0000-0000-000000000003',
      );
      const enrollment = '676f7a72-b5bc-3aff-7cee-2b48d877d80f';
      const source = '8354c7dc-3a13-0d71-db92-c18a0ded4a7f';
      final grants = rows(
        await auth.request(
          'GET',
          '/api/v1/people/enrollments/$enrollment/compensations',
        ),
      );
      final grant = grants.singleWhere(
        (g) => g['id'] == 'f370ba5f-1fa0-4cfe-bef4-ced37c21fc31',
      );
      expect(grant['sourceSessionId'], source);
      expect(grant['units'], 1);
      expect(grant['allocated'], 0);
      expect(grant['consumed'], 0);
      expect(grant['validTo'], '2026-10-29');
      expect(grant['cause'], 'ApprovedAbsence');
      final booking = rows(grant['bookings']).single;
      expect(booking['id'], '9b75c3e7-ece7-4244-ba7b-528205cfba3a');
      expect(
        booking['trainingSessionId'],
        'b45e1c26-6c69-4527-8882-2cd80e9e624e',
      );
      expect(booking['state'], 'Cancelled');
      expect(booking['consumed'], 0);
      final attendance = obj(
        await auth.request(
          'GET',
          '/api/v1/attendance/sessions/$source/players',
        ),
      );
      final sourcePlayer = rows(attendance['players'])
          .singleWhere((p) => p['sportEnrollmentId'] == enrollment);
      expect(sourcePlayer['attendanceStatus'], 'Absent');
      expect(sourcePlayer['sessionConsumed'], false);
      for (final p in [
        ('63359353-a864-b606-b49e-37eccdf5056c', '2026-09-20', '2026-10-19'),
        ('2c03dd3c-5d00-b5c8-0893-09890ab40597', '2026-08-21', '2026-09-20'),
      ]) {
        final period = obj(
          await auth.request('GET', '/api/v1/subscriptions/periods/${p.$1}'),
        );
        expect(period['startDate'], p.$2);
        expect(period['endDate'], p.$3);
        expect(period['priceSnapshot'], 900);
        expect(period['remainingSessions'], null);
        expect(period['status'], 'Expired');
        expect(period['adjustments'], isEmpty);
      }
      final before = obj(
        await auth.request(
          'GET',
          '/api/v1/reports/financial?allDates=true&pageSize=1',
        ),
      );
      expect(before['totalCount'], 71);
      expect(before['totalAmount'], 60285);
      expect(before['totalRefunds'], 1);
      expect(before['netAmount'], 60284);
      Future<void> wait(Finder f) async {
        for (var i = 0; i < 240 && f.evaluate().isEmpty; i++) {
          await t.pump(const Duration(milliseconds: 150));
        }
        expect(f, findsWidgets);
        await t.pumpAndSettle();
      }

      Future<void> reach(Finder f) async {
        final scroll = find.byType(Scrollable).first;
        t.state<ScrollableState>(scroll).position.jumpTo(0);
        await t.pumpAndSettle();
        await t.scrollUntilVisible(f, 150, scrollable: scroll, maxScrolls: 120);
        await t.ensureVisible(f.last);
        await t.pumpAndSettle();
      }

      Future<void> tap(Finder f) async {
        await reach(f);
        await t.tap(f.last);
        await t.pump(const Duration(milliseconds: 400));
      }

      final appearance = Appearance();
      for (final theme in ['football', 'general']) {
        await appearance.completeRoleIntro('AcademyOwner', theme);
      }
      await t.pumpWidget(
        AcademyApp(auth: auth, environment: 'Demo', appearance: appearance),
      );
      await wait(find.text('ملخص الأكاديمية'));
      await reach(find.byType(HomeSubscriptions));
      await tap(find.textContaining(RegExp(r'^\d+ إجمالي$')));
      await wait(find.byType(HomeSubscriptionPlayers));
      await tap(find.widgetWithText(ListTile, 'علي أحمد إبراهيم'));
      await wait(find.byType(NativePlayerWorkspace));
      await tap(find.widgetWithText(TextButton, 'المزيد'));
      await tap(find.text('التعويض والحصص البديلة'));
      await wait(find.byType(NativePlayerCompensations));
      final sourceLabel = find.textContaining('الحصة الأصلية 2026-10-14');
      await wait(sourceLabel);
      expect(find.textContaining('غياب أو اعتذار معتمد'), findsOneWidget);
      final card = find.ancestor(of: sourceLabel, matching: find.byType(GCard));
      await reach(card);
      await t.runAsync(
        () => Future<void>.delayed(const Duration(milliseconds: 500)),
      );
      await binding.takeScreenshot(
        'phase-approved-compensation-source-history-native-w375',
      );
      expect(find.text('حجز قائم'), findsNothing);
      expect(transport.commands, isEmpty);
      final after = obj(
        await auth.request(
          'GET',
          '/api/v1/reports/financial?allDates=true&pageSize=1',
        ),
      );
      for (final k in [
        'totalCount',
        'totalAmount',
        'totalRefunds',
        'netAmount',
      ]) {
        expect(after[k], before[k]);
      }
      expect(t.takeException(), isNull);
      await t.pumpWidget(const SizedBox.shrink());
      auth.dispose();
    },
  );
}
