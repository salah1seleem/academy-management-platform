import 'package:academy_mobile/app/app.dart';
import 'package:academy_mobile/core/api/api_client.dart';
import 'package:academy_mobile/core/auth/auth_controller.dart';
import 'package:academy_mobile/core/storage/refresh_store.dart';
import 'package:academy_mobile/core/theme/appearance.dart';
import 'package:academy_mobile/features/admin/native_enrollment_payment_read.dart';
import 'package:academy_mobile/features/guardian/guardian_ui.dart';
import 'package:academy_mobile/features/shared/notification_inbox.dart';
import 'package:academy_mobile/features/shared/private_media.dart';
import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:integration_test/integration_test.dart';

class EnrollmentSourceTransport extends HttpApiTransport {
  EnrollmentSourceTransport(super.config, {this.guardianSetup = false});
  final bool guardianSetup;
  String? sourceId, noticeId;
  final replies = <String, Object?>{};
  final assetsRead = <String>{};
  int uploads = 0, creations = 0, proofs = 0, rejections = 0;
  @override
  Future<Object?> send(
    String method,
    String path, {
    Object? body,
    String? token,
    String? idempotencyKey,
  }) async {
    final create =
        guardianSetup &&
        method == 'POST' &&
        path == '/api/v1/guardian/enrollment-requests';
    final proof =
        guardianSetup &&
        method == 'POST' &&
        sourceId != null &&
        path == '/api/v1/guardian/enrollment-requests/$sourceId/proof';
    final reject =
        !guardianSetup &&
        method == 'POST' &&
        sourceId != null &&
        path == '/api/v1/enrollment-requests/$sourceId/reject';
    final marker =
        method == 'PUT' &&
        noticeId != null &&
        path == '/api/v1/notifications/$noticeId/read';
    if (method != 'GET' &&
        !path.startsWith('/api/v1/mobile/auth/') &&
        !create &&
        !proof &&
        !reject &&
        !marker) {
      throw StateError(
        'Source acceptance forbids collection, activation and unrelated writes.',
      );
    }
    final result = await super.send(
      method,
      path,
      body: body,
      token: token,
      idempotencyKey: idempotencyKey,
    );
    if (method == 'GET') replies[path] = result;
    if (create) creations++;
    if (proof) proofs++;
    if (reject) rejections++;
    return result;
  }

  @override
  Future<Object?> upload(
    Uint8List bytes,
    String contentType,
    Map<String, String> fields,
    String token,
  ) async {
    if (!guardianSetup ||
        sourceId == null ||
        fields['purpose'] != 'Proof' ||
        uploads != 0) {
      throw StateError(
        'Only one clearly synthetic proof fixture may be uploaded.',
      );
    }
    final result = await super.upload(bytes, contentType, fields, token);
    uploads++;
    return result;
  }

  @override
  Future<Uint8List> asset(String path, String token) async {
    final result = await super.asset(path, token);
    assetsRead.add(path);
    return result;
  }
}

void main() {
  final binding = IntegrationTestWidgetsFlutterBinding.ensureInitialized();
  testWidgets(
    'owner opens committed enrollment proof notice and retains its exact rejected historical source without collecting',
    (t) async {
      expect(const bool.fromEnvironment('ISOLATED_DEMO'), true);
      const base = String.fromEnvironment('API_BASE_URL');
      expect(base, 'http://127.0.0.1:5278');
      const width = int.fromEnvironment(
        'ENROLLMENT_NOTICE_EXPECTED_WIDTH',
        defaultValue: 440,
      );
      expect(
        (t.view.physicalSize / t.view.devicePixelRatio).width,
        width.toDouble(),
      );
      const name = 'طفل قبول مصدر التنبيه 69';
      const reason =
          'طلب قبول اصطناعي لمصدر التنبيه؛ لا تحويل ولا تفعيل اشتراك.';
      final ownerTransport = EnrollmentSourceTransport(AppConfig('Demo', base));
      final guardianTransport = EnrollmentSourceTransport(
        AppConfig('Demo', base),
        guardianSetup: true,
      );
      final otherTransport = EnrollmentSourceTransport(AppConfig('Demo', base));
      final controllers = <AuthController>[];
      Future<AuthController> login(
        String phone,
        String key,
        String role,
        EnrollmentSourceTransport transport,
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
        'AcademyOwner',
        ownerTransport,
      );
      final guardian = await login(
        '01099900100',
        'v46.urgent-summary.guardian0',
        'Guardian',
        guardianTransport,
      );
      final other = await login(
        '01200000007',
        'v45.5278.guardian-workspace.01200000007',
        'Guardian',
        otherTransport,
      );
      final beforeHome = obj(
        await guardian.request('GET', '/api/v1/guardian/home'),
      );
      final list = rows(
        await guardian.request('GET', '/api/v1/guardian/enrollment-requests'),
      );
      final saved = list.where((r) => r['child'] == name).toList();
      String id;
      if (saved.isNotEmpty) {
        id = textOf(saved.single['id']);
      } else {
        final options = obj(
          await guardian.request(
            'GET',
            '/api/v1/guardian/enrollment-request-options',
          ),
        );
        final sport = rows(options['sports'])
            .firstWhere((r) => textOf(r['name']).contains('كرة القدم'));
        final plan = rows(options['plans']).firstWhere(
          (p) =>
              p['sportId'] == sport['id'] &&
              p['trainingFormat'] == 'Group' &&
              (p['allBranches'] == true || (p['branchIds'] as List).isNotEmpty),
        );
        final branch = rows(options['branches']).firstWhere(
          (b) =>
              plan['allBranches'] == true ||
              (plan['branchIds'] as List).contains(b['id']),
        );
        final created = obj(
          await guardian.request(
            'POST',
            '/api/v1/guardian/enrollment-requests',
            idempotencyKey: 'v69-owner-enrollment-notice-source',
            body: {
              'requestType': 'NewChild',
              'newChildArabicName': name,
              'newChildDateOfBirth': '2018-01-01',
              'newChildGender': 'Male',
              'sportId': sport['id'],
              'preferredBranchId': branch['id'],
              'subscriptionPlanId': plan['id'],
              'notes': reason,
            },
          ),
        );
        id = textOf(created['id']);
      }
      expect(id, isNotEmpty);
      ownerTransport.sourceId = id;
      guardianTransport.sourceId = id;
      final path = '/api/v1/manage/enrollment-requests/$id/payment';
      Future<Json> detail() async => obj(await owner.request('GET', path));
      var source = await detail();
      expect(source['child'], name);
      expect(textOf(source['guardian']), isNotEmpty);
      expect(textOf(source['sport']), isNotEmpty);
      expect(textOf(source['branch']), isNotEmpty);
      expect(source['amount'], isNotNull);
      expect(source['createdSportEnrollmentId'], isNull);
      expect(source['paymentRequestId'], isNull);
      if (source['proofUrl'] == null) {
        expect(source['status'], 'Pending');
        // Existing academy logo is an unmistakable synthetic proof fixture.
        // This exercises the API commit, not the OS picker or an actual transfer.
        final bytes = await rootBundle.load(
          'assets/branding/professional-academy.png',
        );
        final asset = obj(
          await guardian.upload(bytes.buffer.asUint8List(), 'image/png', {
            'purpose': 'Proof',
          }),
        );
        final proofBody = {'assetId': asset['id']};
        await guardian.request(
          'POST',
          '/api/v1/guardian/enrollment-requests/$id/proof',
          body: proofBody,
        );
        await guardian.request(
          'POST',
          '/api/v1/guardian/enrollment-requests/$id/proof',
          body: proofBody,
        );
        source = await detail();
      }
      expect(isPrivateAsset(source['proofUrl']), true);
      final inbox = obj(await owner.request('GET', '/api/v1/notifications'));
      final notices = rows(inbox['items'])
          .where((n) => n['eventKey'] == 'Payment' && n['targetId'] == id)
          .toList();
      expect(notices.length, 1);
      final notice = notices.single;
      expect(notice['targetKind'], 'PaymentQueue');
      expect(notice['paymentReviewType'], 'Enrollment');
      final noticeId = textOf(notice['id']);
      ownerTransport.noticeId = noticeId;
      otherTransport.noticeId = noticeId;
      final otherInbox = obj(
        await other.request('GET', '/api/v1/notifications'),
      );
      expect(rows(otherInbox['items']).any((r) => r['id'] == noticeId), false);
      for (final check in [
        (path, 403),
        ('/api/v1/guardian/enrollment-requests/$id/payment', 404),
      ]) {
        try {
          await other.request('GET', check.$1);
          fail('An unrelated family must not read the source.');
        } on ApiFailure catch (e) {
          expect(e.status, check.$2);
        }
      }
      try {
        await other.request(
          'PUT',
          '/api/v1/notifications/$noticeId/read',
          body: {'isRead': true},
        );
        fail('An unrelated family cannot mark the owner notice.');
      } on ApiFailure catch (e) {
        expect(e.status, 404);
      }
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
        await t.scrollUntilVisible(f, 160, scrollable: scroll, maxScrolls: 100);
        await t.ensureVisible(f);
        await t.pumpAndSettle();
      }

      final appearance = Appearance();
      for (final sport in ['football', 'general']) {
        await appearance.completeRoleIntro('AcademyOwner', sport);
      }
      Future<void> showSource(bool light, String phase) async {
        await t.pumpWidget(const SizedBox());
        await t.pumpAndSettle();
        await appearance.update(night: !light);
        await t.pumpWidget(
          AcademyApp(
            key: ValueKey('$phase-$light'),
            auth: owner,
            environment: 'Demo',
            appearance: appearance,
          ),
        );
        await wait(find.byTooltip('التنبيهات'));
        await t.tap(find.byTooltip('التنبيهات'));
        await wait(find.byType(NotificationInbox));
        final tile = find.byKey(ValueKey('notice-$noticeId'));
        await reach(tile);
        final action = find.descendant(
          of: tile,
          matching: find.text('عرض طلب الدفع ومراجعته'),
        );
        await t.ensureVisible(action);
        await t.tap(action);
        await wait(find.byType(NativeEnrollmentPaymentRead));
        expect(
          t
              .widget<NativeEnrollmentPaymentRead>(
                find.byType(NativeEnrollmentPaymentRead),
              )
              .id,
          id,
        );
        await wait(find.text(name));
        expect(find.text('ولي الأمر: ${source['guardian']}'), findsOneWidget);
        expect(
          find.text('${source['sport']} · ${source['branch']}'),
          findsOneWidget,
        );
        expect(
          find.text(money(source['amount'], source['currency'])),
          findsOneWidget,
        );
        final proof = find.byType(PrivateImage);
        await reach(proof);
        await wait(find.descendant(of: proof, matching: find.byType(Image)));
        expect(ownerTransport.assetsRead, contains(textOf(source['proofUrl'])));
        await reach(find.text(name));
        await binding.takeScreenshot(
          'phase-approved-owner-enrollment-notice-$phase-w$width-${light ? 'light' : 'dark'}',
        );
        await t.tap(find.byType(BackButton).first);
        await wait(find.byType(NotificationInbox));
      }

      Future<void> rejectIfPending() async {
        final current = await detail();
        if (['Pending', 'UnderReview'].contains(current['status'])) {
          await owner.request(
            'POST',
            '/api/v1/enrollment-requests/$id/reject',
            body: {
              'guardianVisibleReason': reason,
              'adminNotes': 'انتهاء قبول مصدر الإشعار دون تحصيل أو إنشاء لاعب.',
            },
          );
        }
      }

      try {
        if (source['status'] != 'Rejected') {
          for (final light in [true, false]) {
            await showSource(light, 'pending');
          }
        }
        await rejectIfPending();
        source = await detail();
        expect(source['status'], 'Rejected');
        expect(source['guardianVisibleReason'], reason);
        expect(source['createdSportEnrollmentId'], isNull);
        expect(source['paymentRequestId'], isNull);
        for (final light in [true, false]) {
          await showSource(light, 'history');
        }
        expect(obj(await owner.request('GET', '/api/v1/notifications')), inbox);
        expect(
          obj(await other.request('GET', '/api/v1/notifications')),
          otherInbox,
        );
        final afterHome = obj(
          await guardian.request('GET', '/api/v1/guardian/home'),
        );
        expect(afterHome['children'], beforeHome['children']);
        expect(afterHome['enrollments'], beforeHome['enrollments']);
        expect(ownerTransport.rejections, lessThanOrEqualTo(1));
        expect(guardianTransport.creations, lessThanOrEqualTo(1));
        expect(guardianTransport.proofs, lessThanOrEqualTo(2));
        expect(t.takeException(), isNull);
        debugPrint(
          'Enrollment notice $noticeId -> $id, Rejected with original money and proof; no player, enrollment or payment created.',
        );
      } finally {
        await rejectIfPending();
        for (final auth in controllers) {
          auth.dispose();
        }
      }
    },
  );
}
