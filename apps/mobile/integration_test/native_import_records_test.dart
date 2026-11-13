import 'dart:io';

import 'package:academy_mobile/core/api/api_client.dart';
import 'package:academy_mobile/core/auth/auth_controller.dart';
import 'package:academy_mobile/core/theme/app_theme.dart';
import 'package:academy_mobile/features/guardian/guardian_ui.dart';
import 'package:academy_mobile/features/owner/native_academy_import.dart';
import 'package:flutter/material.dart';
import 'package:flutter_localizations/flutter_localizations.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:integration_test/integration_test.dart';

import '../test/auth_controller_test.dart' show MemoryStore;
import '../test/support/visibility.dart';

class _ReadOnlyAfterLogin extends HttpApiTransport {
  _ReadOnlyAfterLogin(super.config);
  bool readOnly = false;
  final reads = <String>[];

  @override
  Future<Object?> send(
    String method,
    String path, {
    Object? body,
    String? token,
    String? idempotencyKey,
  }) {
    if (readOnly) {
      expect(method, 'GET', reason: 'Reviewing saved imports cannot write.');
      reads.add(path);
    }
    return super.send(
      method,
      path,
      body: body,
      token: token,
      idempotencyKey: idempotencyKey,
    );
  }
}

void main() {
  final binding = IntegrationTestWidgetsFlutterBinding.ensureInitialized();
  testWidgets('Owner reads the last row of a real isolated 50-row import', (
    t,
  ) async {
    expect(const bool.fromEnvironment('ISOLATED_DEMO'), isTrue);
    const base = String.fromEnvironment('API_BASE_URL');
    expect(base, 'http://127.0.0.1:5496');
    const phone = String.fromEnvironment('QA_IMPORT_OWNER_PHONE');
    const academy = String.fromEnvironment('QA_IMPORT_ACADEMY');
    const expectedJob = String.fromEnvironment('QA_IMPORT_JOB');
    final transport = _ReadOnlyAfterLogin(AppConfig('Demo', base));
    final auth = AuthController(transport, MemoryStore())..restoring = false;
    final challenge = await auth.requestOtp(phone);
    await auth.verify(phone, challenge, '246810');
    expect(auth.session!.selected!.academyId, academy);
    expect(auth.session!.selected!.role, 'AcademyOwner');
    transport.readOnly = true;

    const endpoint = '/api/v1/people/import/jobs';
    final jobs = rows(await auth.request('GET', endpoint));
    expect(jobs.first['id'], expectedJob);
    expect(jobs.first['status'], 'Completed');
    expect(jobs.first['publishedRows'], 2);
    expect(jobs.first['replayedRows'], 48);
    final records = rows(
      await auth.request('GET', '$endpoint/$expectedJob/records'),
    );
    expect(records, hasLength(50));
    expect(records.map((row) => row['id']).toSet(), hasLength(50));
    final last = records.last;
    expect(last['playerCode'], isA<String>());

    await t.pumpWidget(
      MaterialApp(
        debugShowCheckedModeBanner: false,
        locale: const Locale('ar'),
        supportedLocales: const [Locale('ar')],
        localizationsDelegates: GlobalMaterialLocalizations.delegates,
        theme: AcademyTheme.dark(),
        home: NativeAcademyImport(auth: auth),
      ),
    );
    final section = find.text('التسجيلات المحفوظة · 50');
    for (var i = 0; i < 100 && section.evaluate().isEmpty; i++) {
      await t.pump(const Duration(milliseconds: 100));
    }
    await revealTestTarget(t, section);
    await t.tap(section);
    await t.pumpAndSettle();
    final search = find.byKey(const ValueKey('import-record-search'));
    await revealTestTarget(t, search);
    await t.enterText(search, last['playerCode'] as String);
    FocusManager.instance.primaryFocus?.unfocus();
    await t.pumpAndSettle();
    expect(find.text('1–1 من 1'), findsOneWidget);
    expect(find.text(last['player'] as String), findsOneWidget);
    expect(
      transport.reads.where((path) => path == '$endpoint/$expectedJob/records'),
      hasLength(2),
    );
    expect(t.takeException(), isNull);
    final image = await binding.takeScreenshot('native-import-last-record');
    await File('${Directory.systemTemp.path}/qa-native-import-last-record.png')
        .writeAsBytes(image);
    await t.pumpWidget(const SizedBox());
    auth.dispose();
  });
}
