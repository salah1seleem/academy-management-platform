import 'package:academy_mobile/app/app.dart';
import 'package:academy_mobile/core/api/api_client.dart';
import 'package:academy_mobile/core/auth/auth_controller.dart';
import 'package:academy_mobile/core/storage/refresh_store.dart';
import 'package:academy_mobile/core/theme/appearance.dart';
import 'package:academy_mobile/features/guardian/coach_feedback.dart';
import 'package:academy_mobile/features/guardian/guardian_profile.dart';
import 'package:academy_mobile/features/guardian/guardian_ui.dart';
import 'package:academy_mobile/features/shared/notification_inbox.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:integration_test/integration_test.dart';

class AbsenceNoticeReadTransport extends HttpApiTransport {
  AbsenceNoticeReadTransport(super.config);
  final replies = <String, Object?>{};
  final allowedNoticeIds = <String>{};
  @override
  Future<Object?> send(
    String method,
    String path, {
    Object? body,
    String? token,
    String? idempotencyKey,
  }) async {
    final mark =
        method == 'PUT' &&
        allowedNoticeIds.any((id) => path == '/api/v1/notifications/$id/read');
    if (method != 'GET' && !path.startsWith('/api/v1/mobile/auth/') && !mark) {
      throw StateError(
        'Guardian acceptance permits reads and exact notice read markers only.',
      );
    }
    final value = await super.send(
      method,
      path,
      body: body,
      token: token,
      idempotencyKey: idempotencyKey,
    );
    if (method == 'GET') replies[path] = value;
    return value;
  }
}

void main() {
  final binding = IntegrationTestWidgetsFlutterBinding.ensureInitialized();
  testWidgets(
    'committed synthetic absence correction delivers actual session and threshold notices to the linked family only',
    (t) async {
      expect(const bool.fromEnvironment('ISOLATED_DEMO'), true);
      const base = String.fromEnvironment('API_BASE_URL');
      expect(base, 'http://127.0.0.1:5278');
      const width = int.fromEnvironment(
        'ABSENCE_EXPECTED_WIDTH',
        defaultValue: 440,
      );
      expect(
        (t.view.physicalSize / t.view.devicePixelRatio).width,
        width.toDouble(),
      );
      const session = 'ba4e72a7-e50b-0dea-4c49-851fba504377';
      const enrollment = '2ce7c148-f1eb-2198-f50a-6af291c06858';
      final controllers = <AuthController>[];
      Future<AuthController> login(
        String phone,
        String key,
        ApiTransport transport,
        String role,
      ) async {
        final auth = AuthController(transport, SecureRefreshStore(key));
        controllers.add(auth);
        await auth.restore();
        if (auth.session == null) {
          final challenge = await auth.requestOtp(phone);
          await auth.verify(phone, challenge, '246810');
        }
        expect(auth.session!.selected!.role, role);
        expect(
          auth.session!.selected!.academyId,
          '30000000-0000-0000-0000-000000000003',
        );
        return auth;
      }

      final owner = await login(
        '01099900010',
        'v45.5278.os-media-entry.01099900010',
        HttpApiTransport(AppConfig('Demo', base)),
        'AcademyOwner',
      );
      final transport = AbsenceNoticeReadTransport(AppConfig('Demo', base));
      final guardian = await login(
        '01099900103',
        'v65.absence.guardian103',
        transport,
        'Guardian',
      );
      final otherTransport = AbsenceNoticeReadTransport(
        AppConfig('Demo', base),
      );
      final other = await login(
        '01099900100',
        'v46.urgent-summary.guardian0',
        otherTransport,
        'Guardian',
      );
      Future<Json> roster() async => obj(
        await owner.request(
          'GET',
          '/api/v1/attendance/sessions/$session/players',
        ),
      );
      Json row(Json r) =>
          rows(r['players'])
              .singleWhere((p) => p['sportEnrollmentId'] == enrollment);
      final originalRoster = await roster();
      final original = row(originalRoster);
      expect(original['playerCode'], 'FB-0005');
      expect(original['attendanceStatus'], 'Absent');
      expect(obj(originalRoster['session'])['sessionDate'], '2026-10-21');
      expect(originalRoster['canRequestCorrection'], true);
      final player = textOf(original['playerId']);
      final profileBefore = obj(
        await guardian.request(
          'GET',
          '/api/v1/guardian/children/$player/profile',
        ),
      );
      final otherBefore = obj(
        await other.request('GET', '/api/v1/notifications'),
      );
      Future<void> correction(String status, String key) async {
        final current = row(await roster());
        if (current['attendanceStatus'] == status) return;
        final request = obj(
          await owner.request(
            'POST',
            '/api/v1/attendance/sessions/$session/corrections',
            idempotencyKey: key,
            body: {
              'kind': 'Players',
              'reason': 'قبول اصطناعي لتنبيه الغياب؛ إعادة الحالة الأصلية بلا أثر مالي',
              'items': [
                {
                  'subjectId': enrollment,
                  'status': status,
                  'expectedVersion': current['version'],
                },
              ],
            },
          ),
        );
        final id = textOf(request['id']);
        final detail = obj(
          await owner.request('GET', '/api/v1/attendance/corrections/$id'),
        );
        final decision = {
          'expectedVersion': detail['version'],
          'reason': 'قبول معزول لسجل FB-0005 الاصطناعي مع حفظ الرصيد',
        };
        final approved = obj(
          await owner.request(
            'POST',
            '/api/v1/attendance/corrections/$id/approve',
            body: decision,
          ),
        );
        expect(approved['state'], 'Approved');
        // Replaying the exact approval must neither apply twice nor emit again.
        expect(
          obj(
            await owner.request(
              'POST',
              '/api/v1/attendance/corrections/$id/approve',
              body: decision,
            ),
          ),
          approved,
        );
        expect(row(await roster())['attendanceStatus'], status);
        debugPrint('Synthetic absence correction $id -> $status');
      }

      final restoreMarkers = <Json>[];
      try {
        var inbox = obj(await guardian.request('GET', '/api/v1/notifications'));
        final existing = rows(inbox['items'])
            .where(
              (n) =>
                  n['eventKey'] == 'RecordedAbsence' && n['targetId'] == player,
            )
            .toList();
        if (existing.isEmpty) {
          await correction('NotRecorded', 'v65-absence-clear-fb0005-20261021');
          await correction('Absent', 'v65-absence-restore-fb0005-20261021');
          inbox = obj(await guardian.request('GET', '/api/v1/notifications'));
        }
        final notices = [
          for (final event in ['RecordedAbsence', 'Absence'])
            rows(inbox['items']).singleWhere(
              (n) => n['eventKey'] == event && n['targetId'] == player,
            ),
        ];
        final recorded = notices.first;
        final threshold = notices.last;
        expect(recorded['targetKind'], 'Attendance');
        expect(threshold['targetKind'], 'Contact');
        for (final notice in notices) {
          expect(
            textOf(notice['message']),
            contains(textOf(original['arabicName'])),
          );
          expect(textOf(notice['message']), isNot(contains('{')));
          transport.allowedNoticeIds.add(textOf(notice['id']));
          otherTransport.allowedNoticeIds.add(textOf(notice['id']));
          restoreMarkers.add(notice);
          try {
            await other.request(
              'PUT',
              '/api/v1/notifications/${notice['id']}/read',
              body: {'isRead': true},
            );
            fail('An unrelated family cannot change the notice.');
          } on ApiFailure catch (e) {
            expect(e.status, 404);
          }
        }
        expect(textOf(recorded['message']), contains('2026-10-21'));
        expect(textOf(threshold['message']), contains('3'));
        expect(
          obj(await other.request('GET', '/api/v1/notifications')),
          otherBefore,
        );
        try {
          await other.request(
            'GET',
            '/api/v1/guardian/children/$player/profile',
          );
          fail('An unrelated family cannot read the child.');
        } on ApiFailure catch (e) {
          expect(e.status, 404);
        }
        final afterRoster = row(await roster());
        expect(afterRoster['attendanceStatus'], original['attendanceStatus']);
        expect(afterRoster['remainingSessions'], original['remainingSessions']);
        expect(afterRoster['sessionConsumed'], original['sessionConsumed']);
        final profileAfter = obj(
          await guardian.request(
            'GET',
            '/api/v1/guardian/children/$player/profile',
          ),
        );
        expect(
          profileAfter['attendanceSummary'],
          profileBefore['attendanceSummary'],
        );
        expect(
          rows(profileAfter['attendance']),
          rows(profileBefore['attendance']),
        );
        Future<void> wait(Finder f) async {
          for (var i = 0; i < 240 && f.evaluate().isEmpty; i++) {
            await t.pump(const Duration(milliseconds: 150));
          }
          expect(f, findsWidgets);
          await t.pumpAndSettle();
        }

        Future<void> locate(Finder f) async {
          final scroll = find.byType(Scrollable).first;
          t.state<ScrollableState>(scroll).position.jumpTo(0);
          await t.pumpAndSettle();
          await t.scrollUntilVisible(
            f,
            180,
            scrollable: scroll,
            maxScrolls: 100,
          );
          await t.ensureVisible(f);
          await t.pumpAndSettle();
        }

        final appearance = Appearance();
        for (final sport in ['football', 'general']) {
          await appearance.completeRoleIntro('Guardian', sport);
        }
        for (final light in [true, false]) {
          await t.pumpWidget(const SizedBox());
          await t.pumpAndSettle();
          await appearance.update(night: !light);
          await t.pumpWidget(
            AcademyApp(
              key: ValueKey(light),
              auth: guardian,
              environment: 'Demo',
              appearance: appearance,
            ),
          );
          await wait(find.byTooltip('التنبيهات'));
          await t.tap(find.byTooltip('التنبيهات'));
          await wait(find.byType(NotificationInbox));
          for (final notice in notices) {
            final message = find.text(textOf(notice['message']));
            await locate(message);
            final tile = find
                .ancestor(of: message, matching: find.byType(GCard))
                .first;
            final recordedEvent = notice['eventKey'] == 'RecordedAbsence';
            final action = find.descendant(
              of: tile,
              matching: find.text(
                recordedEvent ? 'عرض سجل الحضور' : 'التواصل أو تقديم ملاحظة',
              ),
            );
            await t.ensureVisible(action);
            await t.tap(action);
            await t.pumpAndSettle();
            final page = find.byType(
              recordedEvent ? ProfileRecords : CoachFeedbackPage,
            );
            await wait(page);
            if (recordedEvent) {
              expect(t.widget<ProfileRecords>(page).playerId, player);
              expect(t.widget<ProfileRecords>(page).section, 'attendance');
              await wait(find.text('2026-10-21'));
              expect(
                rows(
                  obj(
                    transport
                        .replies['/api/v1/guardian/children/$player/profile'],
                  )['attendance'],
                ).any(
                  (a) =>
                      a['sessionDate'] == '2026-10-21' &&
                      a['status'] == 'Absent',
                ),
                true,
              );
            } else {
              expect(t.widget<CoachFeedbackPage>(page).playerId, player);
            }
            await binding.takeScreenshot(
              'phase-approved-guardian-absence-${recordedEvent ? 'record' : 'threshold'}-w$width-${light ? 'light' : 'dark'}',
            );
            await t.tap(find.byType(BackButton).first);
            await t.pumpAndSettle();
            await wait(find.byType(NotificationInbox));
          }
          await locate(find.text(textOf(recorded['message'])));
          final tile = find
              .ancestor(
                of: find.text(textOf(recorded['message'])),
                matching: find.byType(GCard),
              )
              .first;
          final mark = find.descendant(
            of: tile,
            matching: find.text(
              recorded['isRead'] == true ? 'تحديد كغير مقروء' : 'تمت القراءة',
            ),
          );
          await t.ensureVisible(mark);
          await t.tap(mark);
          await t.pumpAndSettle();
          final reread = obj(
            await guardian.request('GET', '/api/v1/notifications'),
          );
          expect(
            rows(reread['items'])
                .singleWhere((n) => n['id'] == recorded['id'])['isRead'],
            recorded['isRead'] != true,
          );
          await guardian.request(
            'PUT',
            '/api/v1/notifications/${recorded['id']}/read',
            body: {'isRead': recorded['isRead'] == true},
          );
        }
        final finalInbox = obj(
          await guardian.request('GET', '/api/v1/notifications'),
        );
        expect(finalInbox, inbox);
        debugPrint(
          'Absence delivery accepted: $player, ${notices.map((n) => n['id']).join(', ')}',
        );
        expect(t.takeException(), isNull);
      } finally {
        if (row(await roster())['attendanceStatus'] != 'Absent') {
          await correction('Absent', 'v65-absence-restore-fb0005-20261021');
        }
        for (final notice in restoreMarkers) {
          await guardian.request(
            'PUT',
            '/api/v1/notifications/${notice['id']}/read',
            body: {'isRead': notice['isRead'] == true},
          );
        }
        for (final auth in controllers) {
          auth.dispose();
        }
      }
    },
  );
}
