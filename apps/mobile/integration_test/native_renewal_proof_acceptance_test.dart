import 'dart:async';
import 'dart:io';
import 'dart:typed_data';
import 'dart:ui' as ui;

import 'package:academy_mobile/app/app.dart';
import 'package:academy_mobile/core/api/api_client.dart';
import 'package:academy_mobile/core/auth/auth_controller.dart';
import 'package:academy_mobile/core/theme/appearance.dart';
import 'package:academy_mobile/features/admin/native_renewal_proof_review.dart';
import 'package:academy_mobile/features/guardian/guardian_payments.dart';
import 'package:academy_mobile/features/guardian/guardian_ui.dart';
import 'package:academy_mobile/features/shared/private_media.dart';
import 'package:academy_mobile/features/shared/role_navigation.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:integration_test/integration_test.dart';

import '../test/auth_controller_test.dart' show MemoryStore;

const _base = 'http://127.0.0.1:5499';
const _academy = '10000000-0000-0000-0000-000000000001';
const _family = '/api/v1/guardian/subscriptions';
const _report = '/api/v1/reports/financial?allDates=true';

Future<Uint8List> _proofImage(Color accent) async {
  final recorder = ui.PictureRecorder();
  final canvas = Canvas(recorder);
  canvas.drawColor(const Color(0xFFF9F7EF), BlendMode.src);
  final header = Paint()..color = accent;
  canvas.drawRect(const Rect.fromLTWH(18, 12, 144, 20), header);
  final line = Paint()..color = const Color(0xFF93A4AB);
  for (final y in [44.0, 57.0, 70.0]) {
    canvas.drawRect(Rect.fromLTWH(20, y, 134, 2), line);
  }
  canvas.drawRect(const Rect.fromLTWH(96, 84, 58, 12), header);
  final picture = recorder.endRecording();
  final image = await picture.toImage(180, 110);
  try {
    final png = (await image.toByteData(format: ui.ImageByteFormat.png))!;
    return png.buffer.asUint8List(png.offsetInBytes, png.lengthInBytes);
  } finally {
    image.dispose();
    picture.dispose();
  }
}

class _ReturningRole extends Appearance {
  @override
  Future<bool> hasRoleIntro(String role, String sportTheme) async => true;
}

class _ProofTransport extends HttpApiTransport {
  _ProofTransport(this.io) : super(AppConfig('Demo', _base));

  final Zone io;
  final decisions = <Json>[];
  final results = <Object>[];
  final queueReads = <(Uri, Json)>[];
  String? proofToFail, reviewPath;
  bool loseNextReply = false;

  Future<T> _connected<T>(Future<T> Function() work) {
    final answer = Completer<T>();
    io.run(() async {
      try {
        answer.complete(await work());
      } catch (error, trace) {
        answer.completeError(error, trace);
      }
    });
    return answer.future;
  }

  @override
  Future<Object?> send(
    String method,
    String path, {
    Object? body,
    String? token,
    String? idempotencyKey,
  }) => _connected(() async {
    final review = method == 'POST' && path == reviewPath;
    if (review) decisions.add(obj(body));
    Object? value;
    try {
      value = await super.send(
        method,
        path,
        body: body,
        token: token,
        idempotencyKey: idempotencyKey,
      );
    } on ApiFailure catch (failure) {
      if (review) results.add(failure.status);
      rethrow;
    }
    if (review) {
      results.add(obj(value)['replay'] == true ? 'replay' : 'saved');
      if (loseNextReply) {
        loseNextReply = false;
        throw const ApiFailure(0, 'انقطع الاتصال بعد إرسال المراجعة.');
      }
    }
    if (method == 'GET' &&
        path.startsWith('/api/v1/manage/payment-requests?')) {
      queueReads.add((Uri.parse(path), obj(value)));
    }
    return value;
  });

  @override
  Future<Uint8List> asset(String path, String token) {
    if (path == proofToFail) {
      proofToFail = null;
      return Future.error(const ApiFailure(503, 'تعذر تحميل الصورة.'));
    }
    return _connected(() => super.asset(path, token));
  }
}

Future<AuthController> _login(
  _ProofTransport transport,
  String phone,
  String role,
) async {
  final auth = AuthController(transport, MemoryStore())..restoring = false;
  await auth.verify(phone, await auth.requestOtp(phone), '246810');
  expect(auth.session?.selected?.role, role);
  return auth;
}

void main() {
  final binding = IntegrationTestWidgetsFlutterBinding.ensureInitialized();
  testWidgets(
    'iOS owner approves the reviewed renewal proof once and family opens its receipt',
    (t) async {
      expect(const bool.fromEnvironment('QA_ISOLATED_RENEWAL_PROOF'), isTrue);
      final overrides = HttpOverrides.current;
      addTearDown(() => HttpOverrides.global = overrides);
      final setup = (await t.runAsync(() async {
        HttpOverrides.global = null;
        final api = _ProofTransport(Zone.current);
        addTearDown(api.close);
        final owner = await _login(api, '01000000010', 'AcademyOwner');
        final family = await _login(api, '01000000001', 'Guardian');
        final foreign = await _login(api, '01000000902', 'AcademyOwner');
        expect(owner.session?.selected?.academyId, _academy);
        expect(family.session?.selected?.academyId, _academy);
        expect(foreign.session?.selected?.academyId, isNot(_academy));
        final settings = obj(
          await owner.request('GET', '/api/v1/payment-settings'),
        );
        expect(
          settings['isActive'],
          isTrue,
          reason: 'Use the prepared isolated QA database.',
        );
        final before = obj(await owner.request('GET', _report));
        const resume = String.fromEnvironment('QA_RESUME_PAYMENT');
        String id = resume;
        if (id.isEmpty) {
          final enrollments = rows(
            await family.request('GET', '$_family/enrollments'),
          );
          final enrollment = enrollments.firstWhere(
            (row) => row['sport'] == 'كرة القدم',
          );
          final plans = rows(
            await family.request(
              'GET',
              '$_family/enrollments/${enrollment['id']}/plans',
            ),
          );
          final plan = plans.firstWhere(
            (row) => num.tryParse('${row['price']}') == 900,
          );
          final renewal = obj(
            await family.request(
              'POST',
              '$_family/renewals',
              body: {
                'sportEnrollmentId': enrollment['id'],
                'subscriptionPlanId': plan['id'],
              },
              idempotencyKey: commandKey(),
            ),
          );
          id = renewal['paymentId'] as String;
        }
        final paymentPath = '/api/v1/subscriptions/payments/$id';
        final staffProof = '/api/v1/manage/payments/$id/proof';
        final familyProof = '/api/v1/guardian/payments/$id/proof';
        Future<Json> attach(Color accent) async {
          final asset = obj(
            await family.upload(await _proofImage(accent), 'image/png', {
              'purpose': 'Proof',
            }),
          );
          await family.request(
            'POST',
            familyProof,
            body: {'assetId': asset['id']},
          );
          return obj(await owner.request('GET', staffProof));
        }

        final first = resume.isEmpty
            ? await attach(const Color(0xFF226982))
            : obj(await owner.request('GET', staffProof));
        expect(first['status'], 'Pending');
        final source = obj(await owner.request('GET', paymentPath));
        expect(source['groupId'], isNotNull);
        expect(source['status'], 'Pending');
        expect(source['receiptId'], isNull);
        expect(source['periodId'], isNull);
        await expectLater(
          foreign.request('GET', staffProof),
          throwsA(isA<ApiFailure>().having((e) => e.status, 'status', 404)),
        );
        await expectLater(
          foreign.asset(first['url'] as String),
          throwsA(isA<ApiFailure>().having((e) => e.status, 'status', 404)),
        );
        api.proofToFail = first['url'] as String;
        api.reviewPath = '/api/v1/manage/payments/$id/review';
        debugPrint('iOS renewal queue prepared payment=$id');
        return (
          api,
          owner,
          family,
          before,
          id,
          paymentPath,
          staffProof,
          first,
          source,
          attach,
        );
      }))!;
      final (
        api,
        owner,
        family,
        before,
        id,
        paymentPath,
        staffProof,
        first,
        source,
        attach,
      ) = setup;
      final verticalScrolls = find.byWidgetPredicate(
        (w) => w is Scrollable && w.axisDirection == AxisDirection.down,
      );
      Future<void> wait(Finder finder) async {
        for (
          var attempt = 0;
          attempt < 320 && finder.evaluate().isEmpty;
          attempt++
        ) {
          await t.runAsync(
            () => Future<void>.delayed(const Duration(milliseconds: 30)),
          );
          await t.pump(const Duration(milliseconds: 45));
        }
        expect(finder, findsWidgets);
      }

      Future<void> reveal(Finder finder) async {
        for (
          var attempt = 0;
          attempt < 30 && finder.evaluate().isEmpty;
          attempt++
        ) {
          await wait(verticalScrolls);
          await t.drag(verticalScrolls.first, const Offset(0, -230));
          await t.pump(const Duration(milliseconds: 150));
        }
        expect(finder, findsWidgets);
        await t.ensureVisible(finder.first);
      }

      Future<void> tap(Finder finder, {bool scrollTo = true}) async {
        if (scrollTo) {
          await reveal(finder);
        } else {
          await wait(finder);
        }
        await t.pump(const Duration(milliseconds: 250));
        final visible = finder.hitTestable();
        expect(visible, findsWidgets);
        await t.tap(visible.first);
        await t.pump(const Duration(milliseconds: 600));
      }

      Future<void> shot(String name) async {
        await t.pump(const Duration(milliseconds: 650));
        await binding.takeScreenshot('phase-iosproof-$name');
      }

      Future<void> show(AuthController auth) async {
        await t.pumpWidget(const SizedBox());
        await t.pump();
        await t.pumpWidget(
          AcademyApp(
            auth: auth,
            environment: 'Demo',
            appearance: _ReturningRole()..dark = true,
          ),
        );
        await wait(find.byType(NativeRoleTabs));
      }

      Future<Json> get(AuthController auth, String path) async =>
          obj(await t.runAsync<Object?>(() => auth.request('GET', path)));
      Future<void> openNotice(
        AuthController auth,
        String kind,
        String action,
      ) async {
        final notices = rows(
          (await get(auth, '/api/v1/notifications'))['items'],
        );
        final notice = notices.firstWhere(
          (row) => row['targetKind'] == kind && row['targetId'] == id,
        );
        await tap(find.byTooltip('التنبيهات'), scrollTo: false);
        await tap(
          find.descendant(
            of: find.byKey(ValueKey('notice-${notice['id']}')),
            matching: find.text(action),
          ),
        );
      }

      Future<void> beforePayment() async {
        final value = await get(owner, paymentPath);
        expect(value['status'], 'Pending');
        expect(value['receiptId'], isNull);
        expect(value['periodId'], isNull);
        final unchanged = await get(owner, _report);
        for (final field in ['totalAmount', 'totalCount', 'totalRefunds']) {
          expect(unchanged[field], before[field]);
        }
      }

      await show(owner);
      await tap(find.text('كل القرارات'));
      await tap(find.text('مراجعة الطلبات'));
      final queue = find.byType(NativePaymentRequests);
      await wait(queue);
      await wait(find.textContaining('طلبات معلّقة ('));
      final (queuePath, initialQueue) = api.queueReads.single;
      final initialCount = initialQueue['pending'] as int;
      final initialItems = rows(initialQueue['items']);
      final card = find.byKey(ValueKey('payment-request:Renewal:$id'));
      final openReview = find.descendant(
        of: card,
        matching: find.text('مراجعة إثبات التجديد'),
      );
      await reveal(card);
      expect(
        find.descendant(
          of: card,
          matching: find.text(money(source['finalAmount'], source['currency'])),
        ),
        findsOneWidget,
      );
      await tap(openReview);
      final detail = find.byType(NativeRenewalProofReview);
      await wait(detail);
      expect(t.widget<NativeRenewalProofReview>(detail).id, id);
      final retryImage = find.text('إعادة تحميل الصورة');
      final accept = find.text('قبول وتفعيل التجديد');
      final reject = find.text('رفض الإثبات');
      final zoom = find.text('تكبير');
      final backToQueue = find.text('العودة لقائمة طلبات الدفع');
      await wait(retryImage);
      expect(zoom, findsNothing);
      expect(accept, findsNothing);
      expect(reject, findsNothing);
      expect(api.decisions, isEmpty);
      await beforePayment();
      await t.ensureVisible(retryImage);
      await t.pump();
      final preview = find.ancestor(
        of: retryImage,
        matching: find.byType(PrivateImage),
      );
      expect(t.getSize(preview).height, 120);
      await shot('image-unavailable');
      await tap(retryImage);
      await wait(zoom);
      await tap(zoom);
      await wait(find.byType(InteractiveViewer));
      await shot('proof-zoom');
      await tap(find.byTooltip('إغلاق'), scrollTo: false);

      final note = find.byType(TextFormField);
      final noteEditor = find.descendant(
        of: note,
        matching: find.byType(EditableText),
      );
      Future<void> enterNote(
        String value, {
        bool captureKeyboard = false,
      }) async {
        await reveal(note);
        await t.pump(const Duration(milliseconds: 350));
        expect(noteEditor.hitTestable(), findsOneWidget);
        await t.tap(noteEditor.hitTestable());
        await t.pump(const Duration(milliseconds: 350));
        await wait(noteEditor.hitTestable());
        if (captureKeyboard) {
          await t.pump(const Duration(milliseconds: 250));
          final view = MediaQuery.of(t.element(detail));
          expect(view.viewInsets.bottom, greaterThan(0));
          expect(
            t.getBottomRight(note).dy,
            lessThanOrEqualTo(view.size.height - view.viewInsets.bottom),
          );
          await shot('note-keyboard');
        }
        await t.enterText(note, value);
        await t.pump(const Duration(milliseconds: 300));
        expect(t.widget<EditableText>(noteEditor).controller.text, value);
        FocusManager.instance.primaryFocus?.unfocus();
        await t.pump(const Duration(milliseconds: 450));
        expect(t.widget<EditableText>(noteEditor).controller.text, value);
      }

      final noteLimit = find.text('اكتب ملاحظة من 500 حرف أو أقل.');
      final dialog = find.byType(AlertDialog);
      final cancelDialog = find.descendant(
        of: dialog,
        matching: find.text('رجوع'),
      );
      await enterNote(List.filled(501, 'أ').join());
      for (final action in [accept, reject]) {
        await tap(action);
        await wait(noteLimit);
        expect(dialog, findsNothing);
        expect(api.decisions, isEmpty);
      }
      await shot('note-too-long');
      await enterNote(
        ' ${List.filled(500, 'أ').join()} ',
        captureKeyboard: true,
      );
      await tap(reject);
      await wait(dialog);
      expect(find.text('رفض إثبات التجديد'), findsOneWidget);
      expect(noteLimit, findsNothing);
      await tap(cancelDialog, scrollTo: false);
      await beforePayment();

      // The contextual action returns to the list. The header still goes home.
      await t.drag(verticalScrolls.first, const Offset(0, 1200));
      await t.pump(const Duration(milliseconds: 350));
      await tap(backToQueue);
      await wait(find.text('طلبات معلّقة ($initialCount)'));
      expect(api.queueReads, hasLength(2));
      expect(api.queueReads.last.$1, queuePath);
      expect(rows(api.queueReads.last.$2['items']), initialItems);
      expect(api.decisions, isEmpty);
      await shot('queue-pending');
      await tap(openReview);
      await wait(zoom);

      await tap(accept);
      await wait(dialog);
      expect(
        find.descendant(
          of: dialog,
          matching: find.textContaining(
            money(source['finalAmount'], source['currency']),
          ),
        ),
        findsOneWidget,
      );
      await tap(cancelDialog, scrollTo: false);
      expect(api.decisions, isEmpty);
      await tap(accept);
      final second = (await t.runAsync(() => attach(const Color(0xFF23805F))))!;
      expect(second['assetId'], isNot(first['assetId']));
      await tap(find.text('تأكيد المراجعة'), scrollTo: false);
      final reload = find.text('إعادة تحميل الطلب');
      await wait(reload);
      expect(accept, findsNothing);
      expect(api.results, [409]);
      expect((await get(owner, staffProof))['assetId'], second['assetId']);
      await beforePayment();
      await shot('changed-proof');
      await tap(reload);
      await wait(zoom);
      await tap(accept);
      api.loseNextReply = true;
      await tap(find.text('تأكيد المراجعة'), scrollTo: false);
      final resume = find.text('استكمال مراجعة الطلب نفسه');
      await wait(resume);
      expect(api.results, [409, 'saved']);
      await shot('approval-response-lost');
      await tap(resume);
      await wait(find.text('تم قبول التجديد وإصدار الإيصال.'));
      expect(api.results, [409, 'saved', 'replay']);
      expect(api.decisions.first['expectedProofAssetId'], first['assetId']);
      expect(api.decisions[1], api.decisions[2]);
      expect(api.decisions.last['expectedProofAssetId'], second['assetId']);
      expect(api.decisions.last['groupId'], source['groupId']);
      final saved = await get(owner, paymentPath);
      final receiptId = saved['receiptId'] as String;
      final periodId = saved['periodId'] as String;
      final staffReceipt = await get(
        owner,
        '/api/v1/subscriptions/receipts/$receiptId',
      );
      final period = await get(
        owner,
        '/api/v1/subscriptions/periods/$periodId',
      );
      final after = await get(owner, _report);
      expect(after['totalCount'], (before['totalCount'] as num) + 1);
      expect(
        after['totalAmount'],
        closeTo(
          (before['totalAmount'] as num) + (saved['finalAmount'] as num),
          0.001,
        ),
      );
      expect(after['totalRefunds'], before['totalRefunds']);
      expect(period['receiptId'], receiptId);
      expect(period['sportEnrollmentId'], source['sportEnrollmentId']);
      expect(staffReceipt['amount'], saved['finalAmount']);
      final number = staffReceipt['receiptNumber'] as String;
      await shot('owner-approved');

      await tap(backToQueue);
      await wait(find.text('طلبات معلّقة (${initialCount - 1})'));
      expect(api.queueReads, hasLength(3));
      expect(api.queueReads.last.$1, queuePath);
      final remaining = rows(api.queueReads.last.$2['items']);
      expect(remaining.any((row) => row['id'] == id), isFalse);
      expect(
        remaining,
        containsAll(initialItems.where((row) => row['id'] != id)),
      );
      expect(card, findsNothing);
      await shot('queue-after-approval');
      await tap(find.byType(BackButton), scrollTo: false);
      await wait(find.byType(NativeRoleTabs));
      expect(queue, findsNothing);

      const staffNoticeAction = 'عرض طلب الدفع ومراجعته';
      await openNotice(owner, 'PaymentQueue', staffNoticeAction);
      await wait(detail);
      expect(t.widget<NativeRenewalProofReview>(detail).id, id);
      expect(backToQueue, findsNothing);
      await tap(find.byType(BackButton), scrollTo: false);
      await wait(find.byType(NativeRoleTabs));
      expect(detail, findsNothing);
      expect(api.decisions, hasLength(3));
      await openNotice(owner, 'PaymentQueue', staffNoticeAction);
      await wait(detail);
      await tap(find.text('عرض الإيصال'));
      await wait(find.text(number));
      await shot('owner-receipt');

      await show(family);
      await openNotice(family, 'Payment', 'عرض طلب الدفع');
      await wait(find.byType(CheckoutPage));
      expect(t.widget<CheckoutPage>(find.byType(CheckoutPage)).id, id);
      await tap(find.text('عرض الإيصال'));
      await wait(find.text(number));
      await shot('family-receipt');
      final familyReceipt = await get(family, '$_family/receipts/$receiptId');
      expect(familyReceipt['receiptNumber'], number);
      expect(familyReceipt['amount'], saved['finalAmount']);
      debugPrint(
        'iOS renewal queue verified: image=120, note=501/500, pending=$initialCount→${initialCount - 1}, queue reads=${api.queueReads.length}, path=$queuePath',
      );
      debugPrint(
        'iOS renewal proof verified payment=$id proof=${second['assetId']} receipt=$receiptId period=$periodId amount=${saved['finalAmount']} total=${before['totalAmount']}→${after['totalAmount']}',
      );
    },
  );
}
