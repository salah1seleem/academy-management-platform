import 'support/role_intro.dart';

import 'package:academy_mobile/app/app.dart';
import 'package:academy_mobile/core/api/api_client.dart';
import 'package:academy_mobile/core/auth/auth_controller.dart';
import 'package:academy_mobile/core/storage/refresh_store.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:integration_test/integration_test.dart';

// Requires an isolated Football Demo API with an assigned upcoming session.
// This journey reads data only; it neither resets seed nor records attendance.
void main() {
  final binding = IntegrationTestWidgetsFlutterBinding.ensureInitialized();
  testWidgets(
    'Coach real API home to actual read-only session and assigned group',
    (tester) async {
      final transport = HttpApiTransport(
        AppConfig('Demo', const String.fromEnvironment('API_BASE_URL')),
      );
      final store = SecureRefreshStore(
        'academy.v3.coach-workspace-integration',
      );
      await store.clear();
      final auth = AuthController(transport, store);
      await auth.restore();
      const phone = String.fromEnvironment(
        'COACH_PHONE',
        defaultValue: '01099900020',
      );
      final challenge = await auth.requestOtp(phone);
      await auth.verify(phone, challenge, '246810');
      final rows = await auth.request(
        'GET',
        '/api/v1/attendance/sessions?upcomingOnly=true',
      ) as List;
      expect(
        rows,
        isNotEmpty,
        reason: 'Prepare an upcoming synthetic session in the isolated Demo database.',
      );
      final nearest = rows.first as Map;
      expect(nearest['trainingFormatSource'], 'CurrentGroup');
      expect([
        'Group',
        'Private',
        'SemiPrivate',
      ], contains(nearest['trainingFormat']));
      expect(nearest['isSubstitute'], isA<bool>());
      final roster = await auth.request(
        'GET',
        '/api/v1/attendance/sessions/${nearest['id']}/players',
      ) as Map;
      expect(roster['canManageAttendance'], isFalse);
      expect(roster['canEvaluate'], isTrue);
      final context = roster['session'] as Map;
      expect(context['trainingFormat'], nearest['trainingFormat']);
      expect(context['trainingFormatSource'], nearest['trainingFormatSource']);
      expect(context['isSubstitute'], nearest['isSubstitute']);
      final players = roster['players'] as List;
      expect(players, isNotEmpty);
      await tester.pumpWidget(AcademyApp(auth: auth, environment: 'Demo'));
      await skipRoleIntroIfPresent(tester);
      Future<void> waitFor(Finder finder) async {
        for (var i = 0; i < 150 && finder.evaluate().isEmpty; i++) {
          await tester.pump(const Duration(milliseconds: 150));
        }
        expect(finder, findsWidgets);
        await tester.pumpAndSettle();
      }

      Future<void> capture(String name) async {
        if (const bool.fromEnvironment('CAPTURE_VISUALS')) {
          await binding.takeScreenshot('phase-coach-$name');
        }
      }

      await waitFor(find.text('حصتك الأقرب'));
      expect(find.text(nearest['group'] as String), findsWidgets);
      final formatLabel = {
        'Group': 'جماعية',
        'Private': 'برايفت',
        'SemiPrivate': 'شبه خاصة',
      }[nearest['trainingFormat']]!;
      expect(find.text(formatLabel), findsWidgets);
      expect(
        find.text('مدرب بديل'),
        nearest['isSubstitute'] == true ? findsWidgets : findsNothing,
      );
      await capture('home');
      await tester.tap(find.text('فتح الحصة'));
      await waitFor(find.text('تفاصيل الحصة'));
      expect(
        find.text((players.first as Map)['arabicName'] as String),
        findsOneWidget,
      );
      expect(find.text('حفظ الحضور'), findsNothing);
      expect(find.text(formatLabel), findsOneWidget);
      expect(
        find.text('نوع التدريب حسب إعداد المجموعة الحالي'),
        findsOneWidget,
      );
      expect(find.byType(ChoiceChip), findsNothing);
      expect(find.text('تقييم اللاعب'), findsWidgets);
      await capture('session-readonly');
      await tester.scrollUntilVisible(
        find.byType(TextField),
        180,
        scrollable: find.byType(Scrollable).first,
      );
      await tester.enterText(
        find.byType(TextField),
        (players.first as Map)['playerCode'] as String,
      );
      await tester.testTextInput.receiveAction(TextInputAction.done);
      await tester.pumpAndSettle();
      expect(find.text('1 من ${players.length} لاعب'), findsOneWidget);
      await tester.scrollUntilVisible(
        find.text('عرض الملف'),
        180,
        scrollable: find.byType(Scrollable).first,
      );
      await tester.ensureVisible(find.text('عرض الملف'));
      await tester.tap(find.text('عرض الملف'));
      await waitFor(find.text('ملف اللاعب'));
      await waitFor(find.text((players.first as Map)['arabicName'] as String));
      expect(find.text('حفظ الحضور'), findsNothing);
      await capture('player-session');
      await tester.tap(find.byType(BackButton).last);
      await tester.pumpAndSettle();
      await tester.tap(find.byType(BackButton).last);
      await tester.pumpAndSettle();
      await tester.scrollUntilVisible(
        find.text('المجموعات المسندة'),
        220,
        scrollable: find.byType(Scrollable).first,
      );
      await tester.tap(find.byIcon(Icons.chevron_left).first);
      await waitFor(find.text('فتح الحصة الأقرب'));
      await capture('group');
      await tester.scrollUntilVisible(
        find.byType(TextField),
        180,
        scrollable: find.byType(Scrollable).last,
      );
      await tester.enterText(
        find.byType(TextField),
        (players.first as Map)['playerCode'] as String,
      );
      await tester.testTextInput.receiveAction(TextInputAction.done);
      await tester.pumpAndSettle();
      await tester.scrollUntilVisible(
        find.text('عرض الملف'),
        180,
        scrollable: find.byType(Scrollable).last,
      );
      await capture('group-search');
      await tester.scrollUntilVisible(
        find.text('رفع صور وفيديوهات للحصة'),
        180,
        scrollable: find.byType(Scrollable).last,
      );
      expect(find.text('رفع صور وفيديوهات للحصة'), findsOneWidget);
      expect(tester.takeException(), isNull);
      await auth.logout();
      await tester.pumpWidget(const SizedBox());
      auth.dispose();
      transport.close();
    },
  );
}
