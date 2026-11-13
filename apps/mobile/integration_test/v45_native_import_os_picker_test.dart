import 'dart:typed_data';

import 'package:academy_mobile/core/api/api_client.dart';
import 'package:academy_mobile/core/auth/auth_controller.dart';
import 'package:academy_mobile/core/storage/refresh_store.dart';
import 'package:academy_mobile/core/theme/app_theme.dart';
import 'package:academy_mobile/core/theme/ui_text.dart';
import 'package:academy_mobile/features/admin/native_operation_form.dart';
import 'package:academy_mobile/features/owner/native_academy_import.dart';
import 'package:academy_mobile/features/guardian/guardian_ui.dart';
import 'package:flutter/material.dart';
import 'package:flutter_localizations/flutter_localizations.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:integration_test/integration_test.dart';

class PickerImportTransport extends HttpApiTransport {
  PickerImportTransport(super.config);
  Json? preview, commitResult;
  final downloads = <String, int>{};
  @override
  Future<Uint8List> asset(String path, String token) async {
    final bytes = await super.asset(path, token);
    downloads[path] = bytes.length;
    debugPrint('Actual workbook download: $path / ${bytes.length} bytes');
    return bytes;
  }

  @override
  Future<Object?> importWorkbook(
    Uint8List bytes,
    bool commit,
    Map<String, String> fields,
    String token, {
    bool correctionRows = false,
    String? idempotencyKey,
    String? validRowsFromHash,
  }) async {
    final result = await super.importWorkbook(
      bytes,
      commit,
      fields,
      token,
      idempotencyKey: idempotencyKey,
      validRowsFromHash: validRowsFromHash,
      correctionRows: correctionRows,
    );
    if (validRowsFromHash != null) return result;
    if (commit) {
      commitResult = obj(result);
      debugPrint(
        'Actual OS workbook commit: ${commitResult!['id']} / replay ${commitResult!['replayedRequest']}',
      );
    } else {
      preview = obj(result);
      debugPrint(
        'Actual OS workbook preview: ${preview!['playerCount']} players / ${preview!['rowCount']} enrollments',
      );
    }
    return result;
  }
}

void main() {
  final binding = IntegrationTestWidgetsFlutterBinding.ensureInitialized();
  testWidgets(
    'actual iOS save and open pickers download both workbooks and safely replay the linked example without resetting session grants',
    (t) async {
      expect(const bool.fromEnvironment('ISOLATED_DEMO'), isTrue);
      const base = String.fromEnvironment('API_BASE_URL');
      expect(base, 'http://127.0.0.1:5278');
      expect((t.view.physicalSize / t.view.devicePixelRatio).width, 375);
      final transport = PickerImportTransport(AppConfig('Demo', base));
      final auth = AuthController(
        transport,
        SecureRefreshStore('v45.5278.os-import.owner'),
      );
      await auth.restore();
      if (auth.session == null) {
        final c = await auth.requestOtp('01099900010');
        await auth.verify('01099900010', c, '246810');
      }
      expect(
        auth.session!.selected!.academyId,
        '30000000-0000-0000-0000-000000000003',
      );
      expect(auth.session!.selected!.role, 'AcademyOwner');
      final money = obj(
        await auth.request(
          'GET',
          '/api/v1/reports/financial?allDates=true&pageSize=1',
        ),
      );
      const ids = [
        '02f8772b-4afe-4446-9f52-4af1f96d11c6',
        '6cc8b201-8f82-4a4d-a459-365b228cd075',
      ];
      final periods = <String, Json>{};
      for (final id in ids) {
        periods[id] = obj(
          await auth.request('GET', '/api/v1/subscriptions/periods/$id'),
        );
        expect(periods[id]!['remainingSessions'], 7);
      }
      Future<void> waitFor(Finder f) async {
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
        await t.scrollUntilVisible(f, 200, scrollable: scroll, maxScrolls: 100);
        await t.ensureVisible(f.last);
        await t.pumpAndSettle();
      }

      final page = NativeAcademyImport(auth: auth);
      expect(page.pickWorkbook, isNull);
      expect(page.saveWorkbook, isNull);
      await t.pumpWidget(
        MaterialApp(
          debugShowCheckedModeBanner: false,
          locale: const Locale('ar'),
          supportedLocales: const [Locale('ar')],
          localizationsDelegates: GlobalMaterialLocalizations.delegates,
          theme: AcademyTheme.dark(),
          home: page,
        ),
      );
      await waitFor(find.text('تنزيل قالب فارغ'));
      NativeOperationState<NativeAcademyImport> state() =>
          t.state<NativeOperationState<NativeAcademyImport>>(
            find.byType(NativeAcademyImport),
          );
      Future<void> finishNative(String marker) async {
        debugPrint('NATIVE_PICKER_WAIT: $marker');
        for (var i = 0; i < 1800 && state().busy; i++) {
          await t.pump(const Duration(milliseconds: 150));
        }
        expect(
          state().busy,
          isFalse,
          reason: 'Complete the real OS file picker within the bounded acceptance window.',
        );
        expect(state().error, isNull, reason: state().error);
        await t.pumpAndSettle();
      }

      Future<void> save(String label, String marker) async {
        final button = find.text(label);
        await reach(button);
        await t.tap(button);
        await t.pump(const Duration(milliseconds: 350));
        await finishNative(marker);
        expect(state().message, 'تم حفظ ملف XLSX.');
        await reach(find.text('تم حفظ ملف XLSX.'));
        await binding.takeScreenshot(
          'phase-approved-os-${marker.toLowerCase()}-saved-w375',
        );
      }

      await save('تنزيل قالب فارغ', 'TEMPLATE');
      await save('تنزيل نموذج مكتمل', 'SAMPLE');
      expect(
        transport.downloads['/api/v1/people/import/connected-template'],
        greaterThan(1000),
      );
      expect(
        transport.downloads['/api/v1/people/import/connected-sample'],
        greaterThan(1000),
      );
      final choose = find.text('اختيار ملف XLSX ومعاينته');
      await reach(choose);
      await t.tap(choose);
      await t.pump(const Duration(milliseconds: 350));
      await finishNative('OPEN_SAMPLE');
      expect(transport.preview, isNotNull);
      expect(transport.preview!['canApprove'], isTrue);
      expect(transport.preview!['playerCount'], 10);
      expect(transport.preview!['rowCount'], 11);
      await reach(find.text('اكتملت المعاينة دون أخطاء'));
      await binding.takeScreenshot('phase-approved-os-file-preview-w375');
      final matches = rows(transport.preview!['guardianMatches'] ?? []);
      for (final match in matches) {
        expect((match['candidates'] as List).length, 1);
        final checkbox = find.byWidgetPredicate(
          (w) =>
              w is CheckboxListTile &&
              w.value != true &&
              w.title is UiText &&
              (w.title as UiText).data ==
                  'ربط هذه الأسرة بالحساب المطابق القائم',
        );
        await reach(checkbox);
        await t.tap(checkbox.first);
        await t.pumpAndSettle();
      }
      for (final text in [
        'راجعت علاقات أولياء الأمور وأعتمد ربط الدفعة.',
        'راجعت الأرصدة الافتتاحية وسندها إن وجدت؛ ليست قبض اليوم.',
      ]) {
        final f = find.text(text);
        await reach(f);
        await t.tap(f);
        await t.pumpAndSettle();
      }
      expect(transport.preview!['financiallyIncomplete'] ?? 0, 0);
      final approve = find.text('اعتماد وبدء الاستيراد');
      await reach(approve);
      await t.tap(approve);
      await t.pump(const Duration(milliseconds: 350));
      await finishNative('COMMIT');
      expect(transport.commitResult, isNotNull);
      await waitFor(find.text('اكتمل الاستيراد'));
      await reach(find.text('اكتمل الاستيراد'));
      await binding.takeScreenshot('phase-approved-os-file-replay-w375');
      for (final id in ids) {
        final read = obj(
          await auth.request('GET', '/api/v1/subscriptions/periods/$id'),
        );
        expect(read['remainingSessions'], periods[id]!['remainingSessions']);
        expect(read['endDate'], periods[id]!['endDate']);
        expect(read['initialSessions'], periods[id]!['initialSessions']);
      }
      final after = obj(
        await auth.request(
          'GET',
          '/api/v1/reports/financial?allDates=true&pageSize=1',
        ),
      );
      expect(money['netAmount'], isNotNull);
      expect(after['netAmount'], money['netAmount']);
      expect(after['totalCount'], money['totalCount']);
      expect(t.takeException(), isNull);
      await t.pumpWidget(const SizedBox());
      await auth.logout();
      auth.dispose();
    },
  );
}
