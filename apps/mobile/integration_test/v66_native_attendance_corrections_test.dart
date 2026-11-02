import 'package:academy_mobile/app/app.dart';
import 'package:academy_mobile/core/api/api_client.dart';
import 'package:academy_mobile/core/auth/auth_controller.dart';
import 'package:academy_mobile/core/storage/refresh_store.dart';
import 'package:academy_mobile/core/theme/appearance.dart';
import 'package:academy_mobile/features/guardian/guardian_ui.dart';
import 'package:academy_mobile/features/shared/attendance_corrections.dart';
import 'package:academy_mobile/features/shared/notification_inbox.dart';
import 'package:academy_mobile/features/shared/role_navigation.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:integration_test/integration_test.dart';

class CorrectionAcceptanceTransport extends HttpApiTransport {
  CorrectionAcceptanceTransport(super.config);
  bool loseReadback = false;
  int approvals = 0;
  final responses = <String, Object?>{};
  @override
  Future<Object?> send(
    String method,
    String path, {
    Object? body,
    String? token,
    String? idempotencyKey,
  }) async {
    if (method == 'GET' &&
        path.startsWith('/api/v1/attendance/corrections/') &&
        loseReadback) {
      loseReadback = false;
      throw const ApiFailure(503, 'تعذرت قراءة القرار في اختبار الاستئناف.');
    }
    final result = await super.send(
      method,
      path,
      body: body,
      token: token,
      idempotencyKey: idempotencyKey,
    );
    if (method == 'GET') responses[path] = result;
    if (method == 'POST' && path.endsWith('/approve')) {
      approvals++;
      if (approvals == 1) loseReadback = true;
    }
    return result;
  }
}

void main() {
  final binding = IntegrationTestWidgetsFlutterBinding.ensureInitialized();
  testWidgets(
    'administrator requests a real past correction, owner reviews natively and requester opens the exact decision notice',
    (t) async {
      expect(const bool.fromEnvironment('ISOLATED_DEMO'), true);
      const base = String.fromEnvironment('API_BASE_URL');
      expect(base, 'http://127.0.0.1:5278');
      const width = int.fromEnvironment(
        'CORRECTION_EXPECTED_WIDTH',
        defaultValue: 440,
      );
      expect(
        (t.view.physicalSize / t.view.devicePixelRatio).width,
        width.toDouble(),
      );
      const session = 'ba4e72a7-e50b-0dea-4c49-851fba504377';
      const enrollment = '2ce7c148-f1eb-2198-f50a-6af291c06858';
      final transport = CorrectionAcceptanceTransport(AppConfig('Demo', base));
      final owner = AuthController(
        transport,
        SecureRefreshStore('v45.5278.os-media-entry.01099900010'),
      );
      final admin = AuthController(
        HttpApiTransport(AppConfig('Demo', base)),
        SecureRefreshStore('v66.native-corrections.admin11'),
      );
      Future<void> login(AuthController auth, String phone, String role) async {
        await auth.restore();
        if (auth.session == null) {
          final c = await auth.requestOtp(phone);
          await auth.verify(phone, c, '246810');
        }
        expect(auth.session!.selected!.role, role);
        expect(
          auth.session!.selected!.academyId,
          '30000000-0000-0000-0000-000000000003',
        );
      }

      await login(owner, '01099900010', 'AcademyOwner');
      await login(admin, '01099900011', 'AcademyAdmin');
      Future<Json> roster() async => obj(
        await admin.request(
          'GET',
          '/api/v1/attendance/sessions/$session/players',
        ),
      );
      Json row(Json value) =>
          rows(value['players'])
              .singleWhere((r) => r['sportEnrollmentId'] == enrollment);
      final original = row(await roster());
      expect(original['playerCode'], 'FB-0005');
      final existing = rows(
        await admin.request('GET', '/api/v1/attendance/corrections'),
      );
      Future<String> request(String status, String suffix) async {
        final reason =
            'قبول المراجعة الأصلية v66/$suffix لسجل FB-0005 الاصطناعي';
        final saved = existing.where((r) => r['reason'] == reason).toList();
        if (saved.isNotEmpty) return textOf(saved.single['id']);
        final source = row(await roster());
        final result = obj(
          await admin.request(
            'POST',
            '/api/v1/attendance/sessions/$session/corrections',
            idempotencyKey: 'v66-correction-$suffix-fb0005',
            body: {
              'kind': 'Players',
              'reason': reason,
              'items': [
                {
                  'subjectId': enrollment,
                  'status': status,
                  'expectedVersion': source['version'],
                },
              ],
            },
          ),
        );
        return textOf(result['id']);
      }

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
        await t.scrollUntilVisible(f, 180, scrollable: scroll, maxScrolls: 100);
        await t.ensureVisible(f);
        await t.pumpAndSettle();
      }

      final appearance = Appearance();
      for (final role in ['AcademyOwner', 'AcademyAdmin']) {
        for (final sport in ['football', 'general']) {
          await appearance.completeRoleIntro(role, sport);
        }
      }
      final ids = <String>[];
      try {
        for (final step in [
          ('NotRecorded', 'clear'),
          ('Absent', 'restore'),
          ('NotRecorded', 'reject'),
        ]) {
          final shouldApprove = step.$2 != 'reject';
          final id = await request(step.$1, step.$2);
          ids.add(id);
          await appearance.update(night: false);
          await t.pumpWidget(const SizedBox());
          await t.pumpAndSettle();
          await t.pumpWidget(
            AcademyApp(
              key: ValueKey(id),
              auth: owner,
              environment: 'Demo',
              appearance: appearance,
            ),
          );
          await wait(find.text('المزيد'));
          await t.tap(find.text('المزيد'));
          await t.pumpAndSettle();
          await wait(find.byType(NativeMore));
          await locate(find.text('طلبات تصحيح الحضور'));
          await t.tap(find.text('طلبات تصحيح الحضور'));
          await t.pumpAndSettle();
          await wait(find.byType(AttendanceCorrectionsPage));
          final link = find.byKey(ValueKey('attendance-correction-$id'));
          await locate(link);
          await t.tap(link);
          await t.pumpAndSettle();
          await wait(find.byType(AttendanceCorrectionDetail));
          expect(
            t
                .widget<AttendanceCorrectionDetail>(
                  find.byType(AttendanceCorrectionDetail),
                )
                .id,
            id,
          );
          final detail = obj(
            await owner.request('GET', '/api/v1/attendance/corrections/$id'),
          );
          final appliedNow = detail['state'] == 'Pending';
          if (appliedNow) {
            final action = shouldApprove
                ? 'اعتماد وتطبيق التصحيح'
                : 'عدم الاعتماد';
            await locate(find.text(action));
            await t.tap(find.text(action));
            await t.pumpAndSettle();
            await t.tap(find.text('تأكيد القرار'));
            await t.pumpAndSettle();
            if (transport.loseReadback) {
              fail('The deliberate readback loss was not exercised.');
            }
            if (find.text('إعادة التحقق من القرار').evaluate().isNotEmpty) {
              expect(
                find.text('اعتمد التصحيح وتأكد تطبيقه على السجل المحفوظ.'),
                findsNothing,
              );
              final before = transport.approvals;
              await locate(find.text('إعادة التحقق من القرار'));
              await t.tap(find.text('إعادة التحقق من القرار'));
              await t.pumpAndSettle();
              expect(transport.approvals, before);
            }
          }
          await locate(find.text(shouldApprove ? 'اعتمد وطُبق' : 'لم يُعتمد'));
          final actual = obj(
            await owner.request('GET', '/api/v1/attendance/corrections/$id'),
          );
          expect(actual['state'], shouldApprove ? 'Approved' : 'Rejected');
          expect(actual['appliedAtUtc'], shouldApprove ? isNotNull : isNull);
          expect(
            rows(actual['items'])
                .singleWhere((i) => i['subjectId'] == enrollment)['after'],
            step.$1,
          );
          if (appliedNow && shouldApprove) {
            expect(row(await roster())['attendanceStatus'], step.$1);
          }
          expect(
            row(await roster())['remainingSessions'],
            original['remainingSessions'],
          );
          await binding.takeScreenshot(
            'phase-approved-native-correction-${step.$2}-w$width-light',
          );
        }
        final before = obj(await admin.request('GET', '/api/v1/notifications'));
        final notices = rows(before['items'])
            .where(
              (n) =>
                  n['targetKind'] == 'AttendanceCorrection' &&
                  ids.contains(n['targetId']),
            )
            .toList();
        expect(notices.length, 3);
        for (final light in [true, false]) {
          await t.pumpWidget(const SizedBox());
          await t.pumpAndSettle();
          await appearance.update(night: !light);
          await t.pumpWidget(
            AcademyApp(
              key: ValueKey(light),
              auth: admin,
              environment: 'Demo',
              appearance: appearance,
            ),
          );
          await wait(find.byTooltip('التنبيهات'));
          await t.tap(find.byTooltip('التنبيهات'));
          await t.pumpAndSettle();
          await wait(find.byType(NotificationInbox));
          for (final notice in notices) {
            final tile = find.byKey(ValueKey('notice-${notice['id']}'));
            await locate(tile);
            final button = find.descendant(
              of: tile,
              matching: find.text('عرض طلب تصحيح الحضور'),
            );
            await t.ensureVisible(button);
            await t.tap(button);
            await t.pumpAndSettle();
            await wait(find.byType(AttendanceCorrectionDetail));
            expect(
              t
                  .widget<AttendanceCorrectionDetail>(
                    find.byType(AttendanceCorrectionDetail),
                  )
                  .id,
              notice['targetId'],
            );
            await locate(
              find.text(
                notice['targetId'] == ids.last ? 'لم يُعتمد' : 'اعتمد وطُبق',
              ),
            );
            expect(find.text('اعتماد وتطبيق التصحيح'), findsNothing);
            await binding.takeScreenshot(
              'phase-approved-native-correction-admin-${ids.indexOf(textOf(notice['targetId']))}-w$width-${light ? 'light' : 'dark'}',
            );
            await t.tap(find.byType(BackButton).first);
            await t.pumpAndSettle();
            await wait(find.byType(NotificationInbox));
          }
        }
        expect(
          obj(await admin.request('GET', '/api/v1/notifications')),
          before,
        );
        expect(row(await roster())['attendanceStatus'], 'Absent');
        debugPrint(
          'Native correction sources: ${ids.join(', ')}; approvals=${transport.approvals}',
        );
        expect(t.takeException(), isNull);
      } finally {
        if (row(await roster())['attendanceStatus'] != 'Absent') {
          final id = await request('Absent', 'restore');
          final current = obj(
            await owner.request('GET', '/api/v1/attendance/corrections/$id'),
          );
          if (current['state'] == 'Pending') {
            await owner.request(
              'POST',
              '/api/v1/attendance/corrections/$id/approve',
              body: {'expectedVersion': current['version'], 'reason': null},
            );
          }
        }
        owner.dispose();
        admin.dispose();
      }
    },
  );
}
