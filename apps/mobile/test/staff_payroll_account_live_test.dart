import 'dart:async';
import 'dart:io';

import 'package:academy_mobile/core/api/api_client.dart';
import 'package:academy_mobile/core/auth/auth_controller.dart';
import 'package:academy_mobile/features/shared/payroll_statements.dart';
import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_test/flutter_test.dart';

import 'auth_controller_test.dart' show MemoryStore;
import 'coach_test.dart' show shell;
import 'shared_ui_quality_test.dart' show capture;

class _PayrollReads extends HttpApiTransport {
  _PayrollReads(super.config, this.ioZone);
  final Zone ioZone;
  final reads = <String>[];
  bool signedIn = false;

  @override
  Future<Object?> send(
    String method,
    String path, {
    Object? body,
    String? token,
    String? idempotencyKey,
  }) {
    if (signedIn) {
      expect(method, 'GET');
      reads.add(path);
    }
    return ioZone.run(
      () => super.send(
        method,
        path,
        body: body,
        token: token,
        idempotencyKey: idempotencyKey,
      ),
    );
  }
}

void main() {
  const enabled = bool.fromEnvironment('QA_STAFF_PAYROLL_LIVE');
  const base = String.fromEnvironment('API_BASE_URL');
  const phone = String.fromEnvironment('QA_STAFF_PAYROLL_PHONE');
  const folder = String.fromEnvironment('UI_QUALITY_CAPTURE_DIR');
  TestWidgetsFlutterBinding.ensureInitialized();

  if (enabled && folder.isNotEmpty) {
    setUpAll(() async {
      final arabic = FontLoader('AcademyArabic');
      for (final weight in [400, 500, 600, 700]) {
        arabic.addFont(
          rootBundle.load('assets/fonts/AcademyArabic-$weight.ttf'),
        );
      }
      final icons = FontLoader('MaterialIcons')
        ..addFont(rootBundle.load('fonts/MaterialIcons-Regular.otf'));
      await Future.wait([arabic.load(), icons.load()]);
    });
  }

  testWidgets('Coach reads their settled monthly payroll over the demo API', (
    t,
  ) async {
    expect(base, 'http://127.0.0.1:5494');
    expect(phone, matches(r'^0109\d{7}$'));
    t.view.physicalSize = const Size(390, 844);
    t.view.devicePixelRatio = 1;
    addTearDown(t.view.reset);
    final previousOverrides = HttpOverrides.current;
    addTearDown(() => HttpOverrides.global = previousOverrides);
    final connection = await t.runAsync(() async {
      HttpOverrides.global = null;
      final transport = _PayrollReads(AppConfig('Demo', base), Zone.current);
      final auth = AuthController(transport, MemoryStore())..restoring = false;
      await auth.verify(phone, await auth.requestOtp(phone), '246810');
      expect(
        auth.session?.selected?.academyId,
        '30000000-0000-0000-0000-000000000003',
      );
      expect(auth.session?.selected?.role, 'Coach');
      transport.signedIn = true;
      return (auth, transport);
    });
    expect(connection, isNotNull);
    final (auth, transport) = connection!;
    await t.pumpWidget(
      shell(
        RepaintBoundary(
          key: const ValueKey('quality-capture'),
          child: StaffPayrollStatements(auth: auth),
        ),
      ),
    );
    final account = find.text('حساب مستحقاتك');
    for (var i = 0; i < 100 && account.evaluate().isEmpty; i++) {
      await t.runAsync(
        () => Future<void>.delayed(const Duration(milliseconds: 30)),
      );
      await t.pump(const Duration(milliseconds: 32));
    }
    expect(account, findsOneWidget);
    expect(find.text('إجمالي المسدد: 3,600 ج.م'), findsOneWidget);
    expect(find.text('المتبقي المعروف: 0 ج.م'), findsOneWidget);
    expect(find.text('السلف المسجلة'), findsNothing);
    expect(find.text('نوفمبر ٢٠٢٦'), findsOneWidget);
    expect(find.text('صرف مسجل: 2,500 ج.م'), findsOneWidget);
    expect(find.text('صرف مسجل: 100 ج.م'), findsOneWidget);
    expect(find.text('صرف مسجل: 1,000 ج.م'), findsOneWidget);
    expect(find.text('سند اصطناعي'), findsNothing);
    expect(transport.reads, ['/api/v1/staff/payroll/account']);
    await capture(t, 'staff-payroll-390');
    expect(t.takeException(), isNull);
  }, skip: !enabled);
}
