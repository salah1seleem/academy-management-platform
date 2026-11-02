import 'package:academy_mobile/core/api/api_client.dart';
import 'package:academy_mobile/core/auth/auth_controller.dart';
import 'package:academy_mobile/core/storage/refresh_store.dart';
import 'package:academy_mobile/features/guardian/guardian_ui.dart';
import 'package:academy_mobile/features/shared/session_lesson.dart';
import 'package:flutter/material.dart';
import 'package:flutter_localizations/flutter_localizations.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:integration_test/integration_test.dart';

void main() {
  final binding = IntegrationTestWidgetsFlutterBinding.ensureInitialized();
  testWidgets(
    'Coach reads the archived actual lesson source with warning and preserved history, not a new assignment',
    (t) async {
      expect(const bool.fromEnvironment('ISOLATED_DEMO'), true);
      expect(
        const String.fromEnvironment('API_BASE_URL'),
        'http://127.0.0.1:5253',
      );
      final auth = AuthController(
        HttpApiTransport(
          AppConfig('Demo', const String.fromEnvironment('API_BASE_URL')),
        ),
        SecureRefreshStore('academy.v3.lesson.archive.coach'),
      );
      await auth.restore();
      if (auth.session != null) await auth.logout();
      final challenge = await auth.requestOtp('01099900022');
      await auth.verify('01099900022', challenge, '246810');
      try {
        expect(
          auth.session!.selected!.academyId,
          '30000000-0000-0000-0000-000000000003',
        );
        final sessions = rows(
          await auth.request(
            'GET',
            '/api/v1/attendance/sessions?allDates=true',
          ),
        );
        final s = sessions.firstWhere(
          (x) => textOf(x['group']).startsWith('مجموعة خطط اصطناعية'),
        );
        final id = textOf(s['id']),
            path = '/api/v1/evaluations/lessons/sessions/$id';
        final before = obj(await auth.request('GET', path));
        expect(obj(before['active'])['contentRetired'], true);
        expect(rows(before['available']), isEmpty);
        await t.pumpWidget(
          MaterialApp(
            key: UniqueKey(),
            locale: const Locale('ar'),
            supportedLocales: const [Locale('ar')],
            localizationsDelegates: GlobalMaterialLocalizations.delegates,
            theme: guardianTheme(),
            home: SessionLessonPage(auth: auth, sessionId: id),
          ),
        );
        for (
          var i = 0;
          i < 180 &&
              find.textContaining('للمرجع التاريخي فقط').evaluate().isEmpty;
          i++
        ) {
          await t.pump(const Duration(milliseconds: 100));
        }
        await t.pumpAndSettle();
        await t.scrollUntilVisible(
          find.textContaining('للمرجع التاريخي فقط'),
          160,
        );
        expect(find.textContaining('للمرجع التاريخي فقط'), findsOneWidget);
        if (const bool.fromEnvironment('CAPTURE_VISUALS')) {
          await binding.takeScreenshot('phase-archive-coach-warning-ios');
        }
        await t.scrollUntilVisible(
          find.text('تمرين مؤرشف — لا يستخدم في خطة جديدة'),
          180,
          scrollable: find.byType(Scrollable).first,
        );
        expect(
          find.text('تمرين مؤرشف — لا يستخدم في خطة جديدة'),
          findsOneWidget,
        );
        await t.scrollUntilVisible(
          find.text('سجل إسناد الخطة'),
          180,
          scrollable: find.byType(Scrollable).first,
        );
        await t.tap(find.text('سجل إسناد الخطة'));
        await t.pumpAndSettle();
        expect(find.textContaining('مراجعة 1'), findsOneWidget);
        if (const bool.fromEnvironment('CAPTURE_VISUALS')) {
          await binding.takeScreenshot('phase-archive-coach-history-ios');
        }
        expect(find.text('تأكيد أرشفة النسخة'), findsNothing);
        final after = obj(await auth.request('GET', path));
        expect(after['history'], before['history']);
        expect(after['revision'], before['revision']);
        expect(
          obj(obj(after['active'])['item'])['id'],
          obj(obj(before['active'])['item'])['id'],
        );
        expect(t.takeException(), isNull);
      } finally {
        await auth.logout();
        await t.pumpWidget(const SizedBox.shrink());
      }
    },
  );
}
