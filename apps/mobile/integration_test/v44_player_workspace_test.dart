import 'package:academy_mobile/core/api/api_client.dart';
import 'package:academy_mobile/core/auth/auth_controller.dart';
import 'package:academy_mobile/core/storage/refresh_store.dart';
import 'package:academy_mobile/core/theme/app_theme.dart';
import 'package:academy_mobile/features/admin/native_player_workspace.dart';
import 'package:academy_mobile/features/guardian/guardian_ui.dart';
import 'package:flutter/material.dart';
import 'package:flutter_localizations/flutter_localizations.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:integration_test/integration_test.dart';

class PlayerEditAcceptanceTransport extends HttpApiTransport {
  PlayerEditAcceptanceTransport(super.config);
  final addresses = <Object?>[];
  @override
  Future<Object?> send(
    String method,
    String path, {
    Object? body,
    String? token,
    String? idempotencyKey,
  }) async {
    if (method == 'PUT' && path.contains('/people/players/')) {
      addresses.add((body as Map)['address']);
      debugPrint('Synthetic player edit address write: ${addresses.last}');
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
    'isolated actual player profile edits registration shirt, rereads and restores it on a small iPhone',
    (t) async {
      expect(const bool.fromEnvironment('ISOLATED_DEMO'), isTrue);
      const base = String.fromEnvironment('API_BASE_URL');
      expect(base, 'http://127.0.0.1:5278');
      final transport = PlayerEditAcceptanceTransport(AppConfig('Demo', base));
      final auth = AuthController(
        transport,
        SecureRefreshStore('v44.5278.player.workspace'),
      );
      await auth.restore();
      if (auth.session == null) {
        final challenge = await auth.requestOtp('01099900010');
        await auth.verify('01099900010', challenge, '246810');
      }
      expect(
        auth.session!.selected!.academyId,
        '30000000-0000-0000-0000-000000000003',
      );
      expect(auth.session!.selected!.role, 'AcademyOwner');
      final list = obj(
        await auth.request(
          'GET',
          '/api/v1/people/players?page=1&pageSize=25&sort=name',
        ),
      );
      final p = rows(list['items']).first;
      final initial = obj(
        await auth.request('GET', '/api/v1/people/players/${p['id']}'),
      );
      final enrollment = rows(initial['enrollments']).first;
      final original = enrollment['shirtNumber'];
      final changed = original == 9 ? 10 : 9;
      Future<void> waitFor(Finder f) async {
        for (var i = 0; i < 240 && f.evaluate().isEmpty; i++) {
          await t.pump(const Duration(milliseconds: 150));
        }
        expect(f, findsWidgets);
        await t.pumpAndSettle();
      }

      Future<void> tap(String label) async {
        FocusManager.instance.primaryFocus?.unfocus();
        await t.pumpAndSettle();
        final scroll = find.byType(Scrollable).first;
        t.state<ScrollableState>(scroll).position.jumpTo(0);
        await t.pumpAndSettle();
        final f = find.text(label);
        await t.scrollUntilVisible(f, 140, scrollable: scroll);
        await t.ensureVisible(f.last);
        await t.pumpAndSettle();
        await t.ensureVisible(f.last);
        await t.tap(f.last);
        await t.pump(const Duration(milliseconds: 350));
      }

      await t.pumpWidget(
        MaterialApp(
          locale: const Locale('ar'),
          supportedLocales: const [Locale('ar')],
          localizationsDelegates: GlobalMaterialLocalizations.delegates,
          theme: AcademyTheme.dark(),
          home: NativePlayerWorkspace(auth: auth, id: textOf(p['id'])),
        ),
      );
      await waitFor(find.text(textOf(initial['arabicName'])));
      expect(find.text('الاشتراكات والماليات'), findsOneWidget);
      await binding.takeScreenshot('phase-approved-player-profile-small');
      await tap('تعديل بيانات اللاعب');
      await waitFor(find.widgetWithText(TextFormField, 'العنوان'));
      final originalAddress = textOf(initial['address'] ?? '');
      const changedAddress = 'عنوان اختبار اصطناعي — قبول تعديل ملف اللاعب';
      try {
        await t.enterText(
          find.widgetWithText(TextFormField, 'العنوان'),
          changedAddress,
        );
        await tap('حفظ بيانات اللاعب');
        expect(transport.addresses, [changedAddress]);
        await waitFor(find.text('حُفظت بيانات اللاعب وأُعيدت قراءتها.'));
        await waitFor(find.widgetWithText(FilledButton, 'حفظ بيانات اللاعب'));
        final edited = obj(
          await auth.request('GET', '/api/v1/people/players/${p['id']}'),
        );
        expect(edited['address'], changedAddress);
        expect(edited['arabicName'], initial['arabicName']);
        expect(edited['dateOfBirth'], initial['dateOfBirth']);
        await binding.takeScreenshot('phase-approved-player-address-persisted');
      } finally {
        await t.ensureVisible(find.widgetWithText(TextFormField, 'العنوان'));
        await t.pumpAndSettle();
        await t.tap(find.widgetWithText(TextFormField, 'العنوان'));
        await t.pumpAndSettle();
        await t.enterText(
          find.widgetWithText(TextFormField, 'العنوان'),
          originalAddress,
        );
        await t.pumpAndSettle();
        expect(
          t
              .widget<TextFormField>(
                find.widgetWithText(TextFormField, 'العنوان'),
              )
              .controller!
              .text,
          originalAddress,
        );
        await tap('حفظ بيانات اللاعب');
        debugPrint('Synthetic player restore writes: ${transport.addresses}');
        for (var i = 0; i < 80; i++) {
          final latest = obj(
            await auth.request('GET', '/api/v1/people/players/${p['id']}'),
          );
          if (textOf(latest['address'] ?? '') == originalAddress) break;
          await t.pump(const Duration(milliseconds: 150));
        }
        await waitFor(find.text('حُفظت بيانات اللاعب وأُعيدت قراءتها.'));
        final restored = obj(
          await auth.request('GET', '/api/v1/people/players/${p['id']}'),
        );
        expect(textOf(restored['address'] ?? ''), originalAddress);
      }
      await t.tap(find.byType(BackButton));
      await t.pumpAndSettle();
      // Horizontal tabs are native and can be reached independently of vertical scroll.
      final more = find.text('المزيد');
      await t.ensureVisible(more);
      await t.pumpAndSettle();
      await t.tap(more);
      await t.pumpAndSettle();
      await tap('رقم القميص لهذا التسجيل');
      await waitFor(find.widgetWithText(TextFormField, 'رقم القميص'));
      await t.enterText(
        find.widgetWithText(TextFormField, 'رقم القميص'),
        '$changed',
      );
      await tap('حفظ رقم القميص');
      await waitFor(find.text('حُفظ رقم القميص لهذا التسجيل الرياضي.'));
      var read = obj(
        await auth.request('GET', '/api/v1/people/players/${p['id']}'),
      );
      expect(
        rows(read['enrollments'])
            .firstWhere((e) => e['id'] == enrollment['id'])['shirtNumber'],
        changed,
      );
      await binding.takeScreenshot(
        'phase-approved-player-shirt-persisted-small',
      );
      await t.tap(find.widgetWithText(TextFormField, 'رقم القميص'));
      await t.pumpAndSettle();
      await t.enterText(
        find.widgetWithText(TextFormField, 'رقم القميص'),
        original?.toString() ?? '',
      );
      await tap('حفظ رقم القميص');
      await waitFor(find.text('حُفظ رقم القميص لهذا التسجيل الرياضي.'));
      read = obj(
        await auth.request('GET', '/api/v1/people/players/${p['id']}'),
      );
      expect(
        rows(read['enrollments'])
            .firstWhere((e) => e['id'] == enrollment['id'])['shirtNumber'],
        original,
      );
      expect(t.takeException(), isNull);
      await t.pumpWidget(const SizedBox());
      await auth.logout();
      auth.dispose();
    },
  );
}
