import 'dart:async';
import 'dart:convert';
import 'dart:io';
import 'dart:typed_data';

import 'package:academy_mobile/core/api/api_client.dart';
import 'package:academy_mobile/core/auth/auth_controller.dart';
import 'package:academy_mobile/features/admin/native_enrollment_payment_read.dart';
import 'package:academy_mobile/features/guardian/guardian_ui.dart';
import 'package:crypto/crypto.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:integration_test/integration_test.dart';

import '../test/auth_controller_test.dart' show MemoryStore;
import '../test/v2_role_workspaces_test.dart' show app;

const _academy = '10000000-0000-0000-0000-000000000001';
const _otherAcademy = '20000000-0000-0000-0000-000000000002';
const _finance = '/api/v1/reports/financial?allDates=true';

class _Transport extends HttpApiTransport {
  _Transport(this.io) : super(AppConfig('Demo', 'http://127.0.0.1:5397'));
  final Zone io;
  final writes = <String>[];
  final imageHashes = <String>[];
  int imageReads = 0, detailReads = 0;
  String? fault;
  Completer<Uint8List>? pending;
  Uint8List? delayed;

  @override
  Future<Object?> send(
    String method,
    String path, {
    Object? body,
    String? token,
    String? idempotencyKey,
  }) {
    if (method != 'GET' && !path.startsWith('/api/v1/mobile/auth/')) {
      writes.add('$method $path');
    }
    if (path.startsWith('/api/v1/manage/enrollment-requests/')) detailReads++;
    return io.run(
      () => super.send(
        method,
        path,
        body: body,
        token: token,
        idempotencyKey: idempotencyKey,
      ),
    );
  }

  @override
  Future<Object?> upload(
    Uint8List bytes,
    String contentType,
    Map<String, String> fields,
    String token,
  ) => io.run(() => super.upload(bytes, contentType, fields, token));

  @override
  Future<Uint8List> asset(String path, String token) async {
    imageReads++;
    final mode = fault;
    fault = null;
    if (mode == 'unavailable') {
      throw const ApiFailure(503, 'تعذر الاتصال');
    }
    if (mode == 'corrupt') return Uint8List.fromList([1, 2, 3]);
    final bytes = await io.run(() => super.asset(path, token));
    imageHashes.add(sha256.convert(bytes).toString());
    if (mode == 'delayed') {
      delayed = bytes;
      pending = Completer<Uint8List>();
      return pending!.future;
    }
    return bytes;
  }
}

void main() {
  final binding = IntegrationTestWidgetsFlutterBinding.ensureInitialized();
  testWidgets(
    'installed iOS enrollment proof: recovery gestures context and real API isolation',
    (t) async {
      expect(const bool.fromEnvironment('ACADEMY_ISOLATED_IOS_QA'), true);
      expect(
        const String.fromEnvironment('API_BASE_URL'),
        'http://127.0.0.1:5397',
      );
      final previous = HttpOverrides.current;
      addTearDown(() => HttpOverrides.global = previous);
      final done = <String>[];
      void passed(String id) {
        done.add(id);
        debugPrint('IOS-PROOF $id executed successfully');
      }

      final setup = (await t.runAsync(() async {
        HttpOverrides.global = null;
        final api = _Transport(Zone.current);
        addTearDown(api.close);
        Future<AuthController> login(
          String phone,
          String role,
          String academy,
        ) async {
          final auth = AuthController(api, MemoryStore())..restoring = false;
          await auth.verify(phone, await auth.requestOtp(phone), '246810');
          if (auth.session?.selected == null) {
            final member = auth.session!.memberships.singleWhere(
              (m) => m.role == role && m.academyId == academy,
            );
            await auth.selectRole(member.id);
          }
          expect(auth.session?.selected?.role, role);
          expect(auth.session?.selected?.academyId, academy);
          addTearDown(auth.dispose);
          return auth;
        }

        final owner = await login('01000000010', 'AcademyOwner', _academy);
        final admin = await login('01000000011', 'AcademyAdmin', _academy);
        final guardian = await login('01000000001', 'Guardian', _academy);
        final other = await login('01000000002', 'Guardian', _academy);
        final foreign = await login(
          '01000000003',
          'AcademyOwner',
          _otherAcademy,
        );
        final before = await owner.request('GET', _finance);
        final options = obj(
          await guardian.request(
            'GET',
            '/api/v1/guardian/enrollment-request-options',
          ),
        );
        final plan = rows(options['plans'])
            .firstWhere((p) => (p['price'] as num) > 0);
        final branches = rows(options['branches']);
        final branch = branches.firstWhere(
          (b) =>
              plan['allBranches'] == true ||
              (plan['branchIds'] as List).contains(b['id']),
        );
        Future<Json> create(String suffix, {bool priced = true}) async => obj(
          await guardian.request(
            'POST',
            '/api/v1/guardian/enrollment-requests',
            idempotencyKey: 'ios-proof-cloud-$suffix',
            body: {
              'requestType': 'NewChild',
              'newChildArabicName': 'لاعب اختبار إثبات الدفع $suffix',
              'newChildDateOfBirth': '2018-01-15',
              'newChildGender': 1,
              'sportId': plan['sportId'],
              'preferredBranchId': branch['id'],
              'subscriptionPlanId': priced ? plan['id'] : null,
            },
          ),
        );
        final request = await create('الأول');
        final empty = await create('الثاني', priced: false);
        final bytes = base64Decode(_png);
        final uploaded = obj(
          await guardian.upload(bytes, 'image/png', {'purpose': 'Proof'}),
        );
        await guardian.request(
          'POST',
          '/api/v1/guardian/enrollment-requests/${request['id']}/proof',
          body: {'assetId': uploaded['id']},
        );
        final detail = obj(
          await owner.request(
            'GET',
            '/api/v1/manage/enrollment-requests/${request['id']}/payment',
          ),
        );
        expect(detail['proofUrl'], '/api/v1/assets/${uploaded['id']}');
        expect(detail['amount'], request['finalAmount']);
        expect(detail['currency'], request['currency']);
        expect(detail['status'], 'Pending');
        api.writes.clear();
        return (
          api,
          owner,
          admin,
          guardian,
          other,
          foreign,
          before,
          request,
          empty,
          detail,
          sha256.convert(bytes).toString(),
        );
      }))!;
      final (
        api,
        owner,
        admin,
        guardian,
        other,
        foreign,
        before,
        request,
        empty,
        detail,
        hash,
      ) = setup;
      final id = request['id'] as String;
      final path = '/api/v1/manage/enrollment-requests/$id/payment';
      final asset = detail['proofUrl'] as String;
      Future<void> wait(Finder finder) async {
        final clock = Stopwatch()..start();
        while (finder.evaluate().isEmpty && clock.elapsed.inSeconds < 35) {
          await t.runAsync(
            () => Future<void>.delayed(const Duration(milliseconds: 30)),
          );
          await t.pump(const Duration(milliseconds: 50));
        }
        expect(finder, findsWidgets);
        await t.pump(const Duration(milliseconds: 100));
      }

      Future<void> reach(Finder finder) async {
        await wait(finder);
        await t.scrollUntilVisible(
          finder.first,
          160,
          scrollable: find.byType(Scrollable).last,
          maxScrolls: 30,
        );
        await t.ensureVisible(finder.first);
        await t.pumpAndSettle();
      }

      Future<void> capture(String name) async {
        expect(t.takeException(), isNull);
        await binding.takeScreenshot('phase-iosproof-$name');
      }

      Future<void> open(
        AuthController auth, {
        String? order,
        double scale = 1,
      }) async {
        await t.pumpWidget(const SizedBox());
        await t.pumpAndSettle();
        await t.pumpWidget(
          app(
            NativeEnrollmentPaymentRead(auth: auth, id: order ?? id),
            scale: scale,
          ),
        );
        await wait(find.text('طلب الاشتراك الجديد'));
      }

      InteractiveViewer viewer() =>
          t.widget<InteractiveViewer>(find.byType(InteractiveViewer));
      Future<void> zoom() async {
        await reach(find.text('تكبير'));
        await t.tap(find.text('تكبير').hitTestable());
        await t.pumpAndSettle();
        expect(viewer().transformationController!.value.getMaxScaleOnAxis(), 2);
      }

      Future<void> deny(Future<Object?> Function() call) async {
        await t.runAsync(
          () => expectLater(
            call(),
            throwsA(
              isA<ApiFailure>().having(
                (e) => e.status,
                'status',
                anyOf(403, 404),
              ),
            ),
          ),
        );
      }

      api.fault = 'unavailable';
      await open(owner);
      await wait(find.text(detail['child'] as String));
      expect(find.text('قيد الانتظار'), findsWidgets);
      expect(
        find.text(money(detail['amount'], detail['currency'])),
        findsWidgets,
      );
      expect(find.text('عرض الإيصال'), findsNothing);
      passed('IP01');
      await reach(find.text('إعادة تحميل الصورة'));
      expect(find.text('تكبير'), findsNothing);
      expect(find.text('لم يصل إثبات دفع.'), findsNothing);
      await capture('unavailable');
      passed('IP02');
      final reads = api.detailReads;
      await t.tap(find.text('إعادة تحميل الصورة').hitTestable());
      await reach(find.text('تكبير'));
      expect(api.imageHashes.last, hash);
      expect(api.detailReads, reads);
      passed('IP03');
      await capture('decoded');
      final imageReads = api.imageReads;
      await zoom();
      expect(api.imageReads, imageReads);
      passed('IP04');
      final matrix = viewer().transformationController!.value.clone();
      await t.drag(find.byType(InteractiveViewer), const Offset(48, -35));
      await t.pumpAndSettle();
      expect(viewer().transformationController!.value, isNot(matrix));
      expect(viewer().transformationController!.value.getMaxScaleOnAxis(), 2);
      expect(api.imageReads, imageReads);
      await capture('panned');
      passed('IP05');
      await t.tap(find.text('الحجم الأصلي').hitTestable());
      await t.pumpAndSettle();
      expect(viewer().transformationController!.value.isIdentity(), true);
      expect(api.imageReads, imageReads);
      passed('IP06');
      await open(owner);
      await reach(find.text('تكبير'));
      expect(api.imageHashes.last, hash);
      expect(viewer().transformationController!.value.isIdentity(), true);
      passed('IP07');
      api.fault = 'corrupt';
      await open(owner);
      await reach(find.text('إعادة تحميل الصورة'));
      expect(find.text('تكبير'), findsNothing);
      expect(t.takeException(), isNull);
      passed('IP08');
      await t.tap(find.text('إعادة تحميل الصورة').hitTestable());
      await reach(find.text('تكبير'));
      expect(api.imageHashes.last, hash);
      passed('IP09');
      await open(admin);
      await wait(find.text(detail['child'] as String));
      await reach(find.text('تكبير'));
      expect(
        find.text(money(detail['amount'], detail['currency'])),
        findsWidgets,
      );
      expect(api.imageHashes.last, hash);
      await capture('admin');
      passed('IP10');
      await deny(
        () => other.request(
          'GET',
          '/api/v1/guardian/enrollment-requests/$id/payment',
        ),
      );
      passed('IP11');
      await deny(() => other.asset(asset));
      passed('IP12');
      await deny(() => foreign.request('GET', path));
      passed('IP13');
      await deny(() => foreign.asset(asset));
      passed('IP14');
      final assetBefore = api.imageReads;
      await open(owner, order: empty['id'] as String);
      await wait(find.text('لم تُحدد قيمة الدفع لهذا الطلب بعد.'));
      expect(find.text('لم يصل إثبات دفع.'), findsOneWidget);
      expect(api.imageReads, assetBefore);
      await capture('unset');
      passed('IP15');
      await open(owner);
      await zoom();
      final firstMembership = owner.session!.selected!.id;
      final secondMembership = owner.session!.memberships
          .singleWhere((m) => m.academyId == _otherAcademy)
          .id;
      await t.runAsync(() => owner.selectRole(secondMembership));
      await t.pumpAndSettle();
      expect(find.byType(InteractiveViewer), findsNothing);
      expect(find.text(detail['child'] as String), findsNothing);
      expect(find.text('الحجم الأصلي'), findsNothing);
      await capture('context-cleared');
      passed('IP16');
      await deny(() => owner.request('GET', path));
      await deny(() => owner.asset(asset));
      passed('IP17');
      await t.runAsync(() => owner.selectRole(firstMembership));
      await open(owner);
      await reach(find.text('تكبير'));
      expect(api.imageHashes.last, hash);
      expect(viewer().transformationController!.value.isIdentity(), true);
      passed('IP18');
      api.fault = 'unavailable';
      await open(owner, scale: 2);
      await reach(find.text('إعادة تحميل الصورة'));
      await capture('large-text-unavailable');
      await t.tap(find.text('إعادة تحميل الصورة').hitTestable());
      await reach(find.text('تكبير'));
      expect(t.takeException(), isNull);
      await capture('large-text-decoded');
      passed('IP24');
      final after = await t.runAsync(() => owner.request('GET', _finance));
      expect(after, before);
      passed('IP21');
      expect(api.writes, isEmpty);
      passed('IP23');
      api.fault = 'delayed';
      await open(owner);
      for (var tick = 0; api.pending == null && tick < 350; tick++) {
        await t.runAsync(
          () => Future<void>.delayed(const Duration(milliseconds: 20)),
        );
        await t.pump(const Duration(milliseconds: 30));
      }
      expect(api.pending, isNotNull);
      await t.runAsync(owner.logout);
      api.pending!.complete(api.delayed!);
      await t.pumpAndSettle();
      expect(find.byType(InteractiveViewer), findsNothing);
      expect(find.text(detail['child'] as String), findsNothing);
      expect(find.text('تكبير'), findsNothing);
      await capture('logout-cleared');
      passed('IP19');
      expect(t.takeException(), isNull);
      binding.reportData = {
        'proofReview': {
          'executedPassed': done,
          'visualReviewRequired': 'IP20',
          'sqlComparisonRequired': 'IP22',
          'source': 'installed-ios-with-real-isolated-api',
          'requestId': id,
          'emptyRequestId': empty['id'],
          'amount': detail['amount'],
          'currency': detail['currency'],
          'originalPngSha256': hash,
          'successfulAssetHashes': api.imageHashes,
          'businessWritesAfterFixture': api.writes,
        },
      };
      await t.pumpWidget(const SizedBox());
    },
  );
}

const _png =
    'iVBORw0KGgoAAAANSUhEUgAAAQAAAAFACAIAAADcSDVdAAADjklEQVR4nO3cMRECURQEwTOBKzQiBQckaLkEIiSQAPcf01UtYJNJd3s+dsjaDl8ABxIAaQIgTQCkCYA0AZAmANIEQJoASBMAaQIgTQCkCYA0AZAmANIEQJoASBMAaQIgTQCkCYA0AZAmANIEQJoASBMAaQIgTQCkCYA0AZAmANIEQJoASBMAaQIg7cMBnM4X+CoBkCYA0gRAmgBIEwBpAiBNAKQJgDQBkCYA0gRAmgBIEwBpAiBNAKQJgDQBkCYA0gRAmgBIEwBpAiBNAKQJgDQBkCYA0gRAmgBIEwBpAiBNAKQtHQDMIgDSBECaAEgTAGkCIE0ApAmANAGQJgDSBECaAEgTAGkCIE0ApAmANAGQJgDSBECaAEgTAGkCIE0ApPkFYhgBkCYA0pYOAGYRAGkCIE0ApAmANAGQJgDSBECaAEgTAGkCIE0ApAmANAGQJgDSBECaAEgTAGkCIE0ApAmANAGQJgDSBECaAEgTAGmuERlGAKQJgDQBkLZ0ADCLAEgTAGkCIE0ApAmANAGQJgDSBECaAEgTAGkCIE0ApAmANAGQJgDSBECaAEgTAGkCIE0ApAmANAGQJgDSBECaAEhzjcgwAiBNAKQJgLSlA4BZBECaAEgTAGkCIE0ApAmANAGQJgDSBECaAEgTAGkCIE0ApAmANAGQJgDSBECaAEgTAGkCIE0ApAmANAGQJgDSBECaa0SGEQBpAiBNAKQtHQDMIgDSBECaAEgTAGkCIE0ApAmANAGQJgDSBECaAEgTAGkCIE0ApAmANAGQJgDSBECaAEgTAGkCIE0ApAmANAGQJgDSXCMyjABIEwBpAiBt6QBgFgGQJgDSBECaAEgTAGkCIE0ApAmANAGQJgDSBECaAEgTAGkCIE0ApAmANAGQJgDSBECaAEgTAGkCIE0ApAmANAGQJgDSBECaAEgTAGkCIE0ApAmANAGQJgDSBECaAEgTAGkCIE0ApAmANAGQ9tMArrc7vCUA0gRAmgBIEwBpAiBNAKQJgDQBkCYA0gRAmgBIEwBpAiBNAKQJgDQBkCYA0gRA2t8GAKsRAGkCIE0ApAmANAGQJgDSBECaAEgTAGkCIE0ApAmANAGQJgDSBECaAEgTAGkCIE0ApAmANAGQJgDSBECaAEgTAGkCIE0ApAmANAGQJgDSBECaAEgTAGkCIE0ApAmANAGQJgDSBECaAEgTAGkCIE0ApAmAtBeQHxxFVj1j4QAAAABJRU5ErkJggg==';
