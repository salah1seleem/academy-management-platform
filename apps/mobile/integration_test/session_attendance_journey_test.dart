import 'package:academy_mobile/core/api/api_client.dart';
import 'package:academy_mobile/core/auth/auth_controller.dart';
import 'package:academy_mobile/core/storage/refresh_store.dart';
import 'package:academy_mobile/features/coach/coach_attendance.dart';
import 'package:academy_mobile/features/guardian/guardian_profile.dart';
import 'package:academy_mobile/features/guardian/guardian_ui.dart';
import 'package:flutter/material.dart';
import 'package:flutter_localizations/flutter_localizations.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:integration_test/integration_test.dart';

// Writes one synthetic attendance record. Run ONLY against the explicitly
// configured, isolated Football Demo database, with a current group/0 session.
void main() {
  final binding = IntegrationTestWidgetsFlutterBinding.ensureInitialized();
  testWidgets(
    'Authorized administrator saves actual session; coach and guardian see the same saved attendance',
    (tester) async {
      expect(
        const bool.fromEnvironment('ISOLATED_DEMO'),
        isTrue,
        reason: 'This journey writes attendance. Explicit isolated Demo setup is required.',
      );
      final transport = HttpApiTransport(
        AppConfig('Demo', const String.fromEnvironment('API_BASE_URL')),
      );
      Future<AuthController> login(String phone, String key) async {
        final store = SecureRefreshStore(
          'academy.v3.attendance-integration.$key',
        );
        await store.clear();
        final auth = AuthController(transport, store);
        await auth.restore();
        final challenge = await auth.requestOtp(phone);
        await auth.verify(phone, challenge, '246810');
        return auth;
      }

      final admin = await login('01099900011', 'admin');
      final guardian = await login('01099900100', 'guardian');
      final home = obj(await guardian.request('GET', '/api/v1/guardian/home'));
      final child = rows(home['children']).single;
      final sessions = rows(
        await admin.request('GET', '/api/v1/attendance/sessions'),
      );
      Json? session, player;
      for (final s in sessions) {
        final data = obj(
          await admin.request(
            'GET',
            '/api/v1/attendance/sessions/${s['id']}/players',
          ),
        );
        final matches = rows(data['players'])
            .where((p) => p['playerId'] == child['id']);
        if (matches.isNotEmpty) {
          expect(data['canManageAttendance'], isTrue);
          session = s;
          player = matches.first;
          break;
        }
      }
      expect(
        session,
        isNotNull,
        reason: 'Prepare a session for group/0 on the real Cairo date in the isolated database.',
      );
      final selected = session!, target = player!;
      final nextStatus = target['attendanceStatus'] == 'Present'
          ? 'Absent'
          : 'Present';
      Widget page(Widget body) => MaterialApp(
        locale: const Locale('ar'),
        supportedLocales: const [Locale('ar')],
        localizationsDelegates: GlobalMaterialLocalizations.delegates,
        theme: guardianTheme(),
        debugShowCheckedModeBanner: false,
        home: body,
      );
      Future<void> waitFor(Finder finder) async {
        for (var i = 0; i < 180 && finder.evaluate().isEmpty; i++) {
          await tester.pump(const Duration(milliseconds: 150));
        }
        expect(finder, findsWidgets);
        await tester.pumpAndSettle();
      }

      Future<void> capture(String name) async {
        if (const bool.fromEnvironment('CAPTURE_VISUALS')) {
          await binding.takeScreenshot('phase-admin-$name');
        }
      }

      await tester.pumpWidget(
        page(CoachAttendance(auth: admin, sessionId: textOf(selected['id']))),
      );
      await waitFor(find.byType(TextField));
      await tester.enterText(
        find.byType(TextField),
        textOf(target['playerCode']),
      );
      await tester.testTextInput.receiveAction(TextInputAction.done);
      await tester.pumpAndSettle();
      await tester.scrollUntilVisible(
        find.text(label(nextStatus)),
        180,
        scrollable: find.byType(Scrollable).first,
      );
      await tester.ensureVisible(find.text(label(nextStatus)));
      await tester.pumpAndSettle();
      await capture('attendance-before');
      await tester.tap(find.widgetWithText(FilterChip, label(nextStatus)));
      await tester.pumpAndSettle();
      await tester.tap(find.text('حفظ الحضور'));
      await waitFor(find.textContaining('تم حفظ الحضور'));
      final saved = obj(
        await admin.request(
          'GET',
          '/api/v1/attendance/sessions/${selected['id']}/players',
        ),
      );
      expect(
        rows(saved['players']).singleWhere(
          (p) => p['sportEnrollmentId'] == target['sportEnrollmentId'],
        )['attendanceStatus'],
        nextStatus,
      );
      await tester.pumpWidget(const SizedBox());
      await tester.pumpWidget(
        page(CoachAttendance(auth: admin, sessionId: textOf(selected['id']))),
      );
      await waitFor(find.byType(TextField));
      expect(find.textContaining('تغييرات لم تُحفظ'), findsNothing);
      await capture('attendance-saved');
      final coach = await login('01099900020', 'coach');
      final coachRead = obj(
        await coach.request(
          'GET',
          '/api/v1/attendance/sessions/${selected['id']}/players',
        ),
      );
      expect(coachRead['canManageAttendance'], isFalse);
      expect(
        rows(coachRead['players']).singleWhere(
          (p) => p['sportEnrollmentId'] == target['sportEnrollmentId'],
        )['attendanceStatus'],
        nextStatus,
      );
      final profile = obj(
        await guardian.request(
          'GET',
          '/api/v1/guardian/children/${child['id']}/profile',
        ),
      );
      expect(
        rows(profile['attendance'])
            .where(
              (a) =>
                  a['sessionDate'] == selected['sessionDate'] &&
                  a['group'] == selected['group'],
            )
            .single['status'],
        nextStatus,
      );
      await tester.pumpWidget(const SizedBox());
      await tester.pumpWidget(
        page(
          ProfileRecords(
            auth: guardian,
            playerId: textOf(child['id']),
            section: 'attendance',
          ),
        ),
      );
      await waitFor(find.text(day(selected['sessionDate'])));
      expect(find.text(label(nextStatus)), findsWidgets);
      await capture('guardian-attendance');
      expect(tester.takeException(), isNull);
      await tester.pumpWidget(const SizedBox());
      for (final auth in [admin, guardian, coach]) {
        await auth.logout();
        auth.dispose();
      }
      transport.close();
    },
  );
}
