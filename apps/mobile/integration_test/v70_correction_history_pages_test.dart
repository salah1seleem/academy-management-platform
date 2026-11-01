import 'package:academy_mobile/core/api/api_client.dart';
import 'package:academy_mobile/core/auth/auth_controller.dart';
import 'package:academy_mobile/core/storage/refresh_store.dart';
import 'package:academy_mobile/core/theme/app_theme.dart';
import 'package:academy_mobile/features/shared/attendance_corrections.dart';
import 'package:academy_mobile/features/guardian/guardian_ui.dart';
import 'package:flutter/material.dart';
import 'package:flutter_localizations/flutter_localizations.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:integration_test/integration_test.dart';

class HistoryReadOnlyTransport extends HttpApiTransport {
  HistoryReadOnlyTransport(super.config);
  final pages = <Json>[];
  @override
  Future<Object?> send(
    String method,
    String path, {
    Object? body,
    String? token,
    String? idempotencyKey,
  }) async {
    if (method != 'GET' && !path.startsWith('/api/v1/mobile/auth/')) {
      throw StateError('History acceptance must not mutate business data.');
    }
    final result = await super.send(
      method,
      path,
      body: body,
      token: token,
      idempotencyKey: idempotencyKey,
    );
    if (path.startsWith('/api/v1/attendance/corrections?page=')) {
      pages.add(obj(result));
    }
    return result;
  }
}

void main() {
  final binding = IntegrationTestWidgetsFlutterBinding.ensureInitialized();
  testWidgets(
    'real correction history page refreshes and opens the authorized stored source without a decision',
    (t) async {
      expect(const bool.fromEnvironment('ISOLATED_DEMO'), true);
      const base = String.fromEnvironment('API_BASE_URL');
      expect(base, 'http://127.0.0.1:5278');
      final transport = HistoryReadOnlyTransport(AppConfig('Demo', base));
      final auth = AuthController(
        transport,
        SecureRefreshStore('v45.5278.os-media-entry.01099900010'),
      );
      await auth.restore();
      if (auth.session == null) {
        final c = await auth.requestOtp('01099900010');
        await auth.verify('01099900010', c, '246810');
      }
      expect(auth.session!.selected!.role, 'AcademyOwner');
      expect(
        auth.session!.selected!.academyId,
        '30000000-0000-0000-0000-000000000003',
      );
      Future<void> wait(Finder f) async {
        for (var i = 0; i < 240 && f.evaluate().isEmpty; i++) {
          await t.pump(const Duration(milliseconds: 150));
        }
        expect(f, findsWidgets);
        await t.pumpAndSettle();
      }

      for (final dark in [false, true]) {
        await t.pumpWidget(
          MaterialApp(
            locale: const Locale('ar'),
            supportedLocales: const [Locale('ar')],
            localizationsDelegates: GlobalMaterialLocalizations.delegates,
            theme: dark ? AcademyTheme.dark() : AcademyTheme.light(),
            home: AttendanceCorrectionsPage(key: ValueKey(dark), auth: auth),
          ),
        );
        await wait(find.text('الصفحة التالية'));
        final page = transport.pages.last;
        expect(page['page'], 1);
        expect(page['pageSize'], 25);
        expect(page['total'], greaterThan(0));
        final first = rows(page['items']).first;
        await binding.takeScreenshot(
          'phase-approved-correction-history-${dark ? 'dark' : 'light'}',
        );
        await t.tap(find.text('تحديث الطلبات'));
        await t.pumpAndSettle();
        await wait(
          find.byKey(ValueKey('attendance-correction-${first['id']}')),
        );
        await t.scrollUntilVisible(
          find.byKey(ValueKey('attendance-correction-${first['id']}')),
          120,
          scrollable: find.byType(Scrollable).first,
        );
        await t.tap(
          find.byKey(ValueKey('attendance-correction-${first['id']}')),
        );
        await t.pumpAndSettle();
        await wait(find.byType(AttendanceCorrectionDetail));
        expect(
          t
              .widget<AttendanceCorrectionDetail>(
                find.byType(AttendanceCorrectionDetail),
              )
              .id,
          first['id'],
        );
        await wait(find.text(first['reason'].toString()));
        await binding.takeScreenshot(
          'phase-approved-correction-history-source-${dark ? 'dark' : 'light'}',
        );
        final backTooltip = MaterialLocalizations.of(
          t.element(find.byType(AttendanceCorrectionDetail)),
        ).backButtonTooltip;
        await t.tap(find.byTooltip(backTooltip));
        await t.pumpAndSettle();
        await wait(find.text('الصفحة التالية'));
        expect(
          find.text('الصفحة 1 · إجمالي الطلبات ${page['total']}'),
          findsOneWidget,
        );
        expect(t.takeException(), isNull);
      }
      await t.pumpWidget(const SizedBox());
      auth.dispose();
    },
  );
}
