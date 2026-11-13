import 'dart:async';
import 'dart:convert';
import 'dart:io';

import 'package:academy_mobile/core/api/api_client.dart';
import 'package:academy_mobile/core/auth/auth_controller.dart';
import 'package:academy_mobile/features/guardian/sport_store.dart';
import 'package:file_picker/file_picker.dart';
import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_test/flutter_test.dart';

import 'auth_controller_test.dart' show MemoryStore;
import 'private_media_picker_context_test.dart'
    show DeferredMediaPicker, SyntheticPickedFile;
import 'shared_ui_quality_test.dart' show capture;
import 'support/visibility.dart';
import 'v2_role_workspaces_test.dart' show app;

class _LiveProofTransport extends HttpApiTransport {
  _LiveProofTransport(super.config, this.io);
  final Zone io;
  final links = <Object?>[];
  int uploads = 0;

  @override
  Future<Object?> send(
    String method,
    String path, {
    Object? body,
    String? token,
    String? idempotencyKey,
  }) {
    if (path.endsWith('/proof') && method == 'POST') links.add(body);
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
  ) {
    uploads++;
    expect(contentType, 'image/png');
    expect(fields, {'purpose': 'Proof'});
    return io.run(() => super.upload(bytes, contentType, fields, token));
  }
}

void main() {
  const enabled = bool.fromEnvironment('QA_PRODUCT_PROOF_LIVE');
  const base = String.fromEnvironment('API_BASE_URL');
  const order = String.fromEnvironment('QA_PRODUCT_PROOF_ORDER');
  const captureDir = String.fromEnvironment('UI_QUALITY_CAPTURE_DIR');
  TestWidgetsFlutterBinding.ensureInitialized();
  if (enabled && captureDir.isNotEmpty) {
    setUpAll(() async {
      final arabic = FontLoader('AcademyArabic');
      for (final weight in [400, 500, 600, 700]) {
        arabic.addFont(
          rootBundle.load('assets/fonts/AcademyArabic-$weight.ttf'),
        );
      }
      await arabic.load();
    });
  }

  testWidgets('Guardian replaces a saved proof through the real demo API', (
    t,
  ) async {
    expect(base, 'http://127.0.0.1:5494');
    expect(order, matches(r'^[a-f0-9-]{36}$'));
    t.view.physicalSize = const Size(390, 844);
    t.view.devicePixelRatio = 1;
    addTearDown(t.view.reset);
    final previousOverrides = HttpOverrides.current;
    addTearDown(() => HttpOverrides.global = previousOverrides);
    final connection = await t.runAsync(() async {
      HttpOverrides.global = null;
      final transport = _LiveProofTransport(
        AppConfig('Demo', base),
        Zone.current,
      );
      final auth = AuthController(transport, MemoryStore())..restoring = false;
      const phone = '01099900102';
      await auth.verify(phone, await auth.requestOtp(phone), '246810');
      expect(
        auth.session?.selected?.academyId,
        '30000000-0000-0000-0000-000000000003',
      );
      expect(auth.session?.selected?.role, 'Guardian');
      final initial = await auth.request(
        'GET',
        '/api/v1/guardian/product-sales/$order',
      ) as Map;
      expect(initial['status'], 'Pending');
      expect(initial['hasProof'], true);
      return (auth, transport);
    });
    expect(connection, isNotNull);
    final (auth, transport) = connection!;
    addTearDown(transport.close);

    final originalPicker = FilePickerPlatform.instance;
    final picker = DeferredMediaPicker();
    FilePickerPlatform.instance = picker;
    addTearDown(() => FilePickerPlatform.instance = originalPicker);
    final proof = SyntheticPickedFile();
    proof.read.complete(
      base64Decode(
        'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+ip1sAAAAASUVORK5CYII=',
      ),
    );
    picker.result.complete(proof);

    Future<void> waitFor(Finder target) async {
      for (var i = 0; i < 120 && target.evaluate().isEmpty; i++) {
        await t.runAsync(
          () => Future<void>.delayed(const Duration(milliseconds: 30)),
        );
        await t.pump(const Duration(milliseconds: 32));
      }
      await revealTestTarget(t, target);
    }

    await t.pumpWidget(
      app(
        RepaintBoundary(
          key: const ValueKey('quality-capture'),
          child: GuardianProductOrder(auth: auth, id: order),
        ),
      ),
    );
    final received = find.text('إثبات الدفع قيد المراجعة');
    await waitFor(received);
    expect(find.text('التحويل للأكاديمية'), findsNothing);
    final replace = find.text('استبدال إثبات الدفع');
    await revealTestTarget(t, replace);
    await t.tap(replace);
    await t.pumpAndSettle();
    final upload = find.text('رفع إيصال الدفع');
    await revealTestTarget(t, upload);
    await t.tap(upload);
    await t.pump();
    final done = find.text('حسنًا');
    await waitFor(done);
    await t.tap(done);
    await t.pumpAndSettle();
    await revealTestTarget(t, received);
    expect(find.text('إعادة تأكيد نفس الإثبات'), findsNothing);
    expect(transport.uploads, 1);
    expect(transport.links.single, {'assetId': matches(r'^[a-f0-9-]{36}$')});
    final saved = await t.runAsync(
      () => auth.request('GET', '/api/v1/guardian/product-sales/$order'),
    );
    expect(saved, containsPair('hasProof', true));
    expect(saved, containsPair('status', 'Pending'));
    await capture(t, 'family-proof-received-390');
    expect(t.takeException(), isNull);
    await t.pumpWidget(const SizedBox());
    auth.dispose();
  }, skip: !enabled);
}
