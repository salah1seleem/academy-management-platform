import 'dart:async';

import 'package:academy_mobile/app/app.dart';
import 'package:academy_mobile/core/api/api_client.dart';
import 'package:academy_mobile/core/auth/auth_controller.dart';
import 'package:academy_mobile/core/storage/refresh_store.dart';
import 'package:academy_mobile/core/theme/appearance.dart';
import 'package:academy_mobile/features/admin/native_renewal.dart';
import 'package:academy_mobile/features/shared/role_navigation.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:integration_test/integration_test.dart';

class _Session implements RefreshStore {
  String? value;
  @override
  Future<String?> read() async => value;
  @override
  Future<void> write(String token) async {
    value = token;
  }

  @override
  Future<void> clear() async {
    value = null;
  }
}

class _ReadOnly extends HttpApiTransport {
  _ReadOnly() : super(AppConfig('Demo', 'http://127.0.0.1:5278'));
  final commands = <String>[];
  @override
  Future<Object?> send(
    String method,
    String path, {
    Object? body,
    String? token,
    String? idempotencyKey,
  }) {
    if (method != 'GET' && !path.startsWith('/api/v1/mobile/auth/')) {
      commands.add(path);
      throw StateError('Business writes blocked in this journey');
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
  testWidgets(
    'Existing demo renewal displays cash setup state without a failed page or collection',
    (t) async {
      expect(const bool.fromEnvironment('READ_ONLY_LIVE_QA'), true);
      final transport = _ReadOnly();
      final auth = AuthController(transport, _Session())..restoring = false;
      final appearance = Appearance();
      Future<void> wait(Finder f) async {
        for (var i = 0; i < 300 && f.evaluate().isEmpty; i++) {
          await t.pump(const Duration(milliseconds: 100));
        }
        expect(f, findsWidgets);
      }

      try {
        final challenge = await auth.requestOtp('01000204010');
        await auth.verify('01000204010', challenge, '246810');
        expect(auth.session!.selected!.role, 'AcademyOwner');
        appearance.bindIntroUser(auth.session!.userId);
        await appearance.completeRoleIntro('AcademyOwner', 'football');
        await appearance.update(visualModel: VisualModel.pitch);
        await t.pumpWidget(
          AcademyApp(auth: auth, environment: 'Demo', appearance: appearance),
        );
        await wait(find.byType(NativeRoleTabs));
        unawaited(
          Navigator.of(t.element(find.byType(NativeRoleTabs))).push<void>(
            MaterialPageRoute(
              builder: (_) => NativeRenewalPayment(
                auth: auth,
                id: '069dd4ad-ab36-449f-8176-686fc8e85ba1',
              ),
            ),
          ),
        );
        await t.pump();
        final method = find.widgetWithText(
          DropdownButtonFormField<String>,
          'طريقة التحصيل',
        );
        await wait(method);
        await t.ensureVisible(method);
        await t.tap(method);
        await t.pumpAndSettle();
        await t.tap(find.text('نقدي من ورديتي').last);
        await t.pump();
        await wait(find.text('التحصيل النقدي غير مفعّل'));
        await t.ensureVisible(find.text('التحصيل النقدي غير مفعّل'));
        await t.pumpAndSettle();
        expect(find.text('تعذر تحميل البيانات'), findsNothing);
        expect(find.textContaining('تغيرت البيانات'), findsNothing);
        expect(
          t
              .widget<FilledButton>(
                find.widgetWithText(
                  FilledButton,
                  'تأكيد التحصيل وإصدار الإيصال',
                ),
              )
              .onPressed,
          isNull,
        );
        expect(transport.commands, isEmpty);
        expect(t.takeException(), isNull);
        await binding.takeScreenshot('phase-cash-disabled-renewal');
        binding.reportData = {
          ...?binding.reportData,
          'source': 'V5.17 existing demo renewal;cash disabled is a setup state;no collection commands',
        };
      } finally {
        await t.pumpWidget(const SizedBox());
        await auth.logout();
        auth.dispose();
        appearance.dispose();
      }
    },
    timeout: const Timeout(Duration(minutes: 5)),
  );
}
