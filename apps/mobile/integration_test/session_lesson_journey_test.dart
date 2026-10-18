import 'package:academy_mobile/core/api/api_client.dart';
import 'package:academy_mobile/core/auth/auth_controller.dart';
import 'package:academy_mobile/core/storage/refresh_store.dart';
import 'package:academy_mobile/features/guardian/guardian_ui.dart';
import 'package:academy_mobile/features/shared/player_goals.dart'
    show goalCommandId;
import 'package:academy_mobile/features/shared/session_lesson.dart';
import 'package:flutter/material.dart';
import 'package:flutter_localizations/flutter_localizations.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:integration_test/integration_test.dart';

void main() {
  final binding = IntegrationTestWidgetsFlutterBinding.ensureInitialized();
  testWidgets(
    'Coach uses the actual session plan and saves a reviewed replacement without touching attendance or money',
    (t) async {
      expect(const bool.fromEnvironment('ISOLATED_DEMO'), true);
      expect(
        const String.fromEnvironment('API_BASE_URL'),
        'http://127.0.0.1:5247',
      );
      Future<AuthController> login(String phone, String role) async {
        final a = AuthController(
          HttpApiTransport(
            AppConfig('Demo', const String.fromEnvironment('API_BASE_URL')),
          ),
          SecureRefreshStore('academy.v3.lessons.$role'),
        );
        await a.restore();
        if (a.session != null) await a.logout();
        final challenge = await a.requestOtp(phone);
        await a.verify(phone, challenge, '246810');
        expect(
          a.session!.selected!.academyId,
          '30000000-0000-0000-0000-000000000003',
        );
        return a;
      }

      final owner = await login('01099900010', 'owner'),
          coach = await login('01099900022', 'coach');
      try {
        final sessions = rows(
          await coach.request(
            'GET',
            '/api/v1/attendance/sessions?allDates=true',
          ),
        );
        final s = sessions.firstWhere(
          (x) => textOf(x['group']).startsWith('مجموعة خطط اصطناعية'),
        );
        final id = textOf(s['id']);
        final path = '/api/v1/evaluations/lessons/sessions/$id';
        final d = obj(await owner.request('GET', path));
        final active = obj(d['active']), p = obj(active['item']);
        expect(active['computedMinutes'], 60);
        final money = await owner.request(
          'GET',
          '/api/v1/reports/financial?from=2026-09-01&to=2026-09-28',
        );
        final roster = await coach.request(
          'GET',
          '/api/v1/attendance/sessions/$id/players',
        );
        final title = 'خطة بديلة للموبايل — ${goalCommandId().substring(0, 5)}';
        final created = obj(
          await owner.request(
            'POST',
            '/api/v1/evaluations/lessons/templates',
            body: {
              'trainingLevelId': p['trainingLevelId'],
              'title': title,
              'declaredMinutes': 60,
              'copiedFromId': p['id'],
              'steps': rows(active['steps'])
                  .map(
                    (s) => {
                      'exerciseId': obj(s['exercise'])['id'],
                      'minutes': s['minutes'],
                    },
                  )
                  .toList(),
              'requestId': goalCommandId(),
            },
          ),
        );
        final copied = obj(
          await owner.request(
            'GET',
            '/api/v1/evaluations/lessons/templates/${created['id']}',
          ),
        );
        await owner.request(
          'POST',
          '/api/v1/evaluations/lessons/templates/${created['id']}/review',
          body: {
            'version': obj(copied['item'])['version'],
            'confirmTechnicalReview': true,
            'requestId': goalCommandId(),
          },
        );
        await t.pumpWidget(
          MaterialApp(
            key: UniqueKey(),
            locale: const Locale('ar'),
            supportedLocales: const [Locale('ar')],
            localizationsDelegates: GlobalMaterialLocalizations.delegates,
            theme: guardianTheme(),
            home: SessionLessonPage(auth: coach, sessionId: id),
          ),
        );
        for (
          var i = 0;
          i < 180 &&
              find.byType(DropdownButtonFormField<String>).evaluate().isEmpty;
          i++
        ) {
          await t.pump(const Duration(milliseconds: 100));
        }
        await t.pumpAndSettle();
        expect(find.byType(DropdownButtonFormField<String>), findsOneWidget);
        await t.tap(find.byType(DropdownButtonFormField<String>));
        await t.pumpAndSettle();
        await t.tap(find.text(title).last);
        await t.pumpAndSettle();
        await t.ensureVisible(find.text('استبدال مع حفظ السابق'));
        await t.tap(find.text('استبدال مع حفظ السابق'));
        for (
          var i = 0;
          i < 180 &&
              find
                  .text('تم حفظ خطة الحصة مع تاريخ الإسناد.')
                  .evaluate()
                  .isEmpty;
          i++
        ) {
          await t.pump(const Duration(milliseconds: 100));
        }
        await t.pumpAndSettle();
        expect(find.text('تم حفظ خطة الحصة مع تاريخ الإسناد.'), findsOneWidget);
        await t.scrollUntilVisible(
          find.text('60 دقيقة محسوبة · المعلن 60'),
          180,
        );
        expect(find.text('60 دقيقة محسوبة · المعلن 60'), findsOneWidget);
        if (const bool.fromEnvironment('CAPTURE_VISUALS')) {
          await binding.takeScreenshot('phase-session-lesson-coach-ios');
        }
        final after = obj(await owner.request('GET', path));
        expect(obj(obj(after['active'])['item'])['id'], created['id']);
        expect(after['revision'], (d['revision'] as int) + 1);
        expect(rows(after['history']).length, greaterThan(1));
        final afterRoster = obj(
          await coach.request('GET', '/api/v1/attendance/sessions/$id/players'),
        );
        Json stable(Object? source) => {...obj(source)}
          ..removeWhere(
            (k, v) => [
              'draftLease',
              'draftExpiresAtUtc',
              'lastSynchronizedAtUtc',
            ].contains(k),
          );
        expect(stable(afterRoster), stable(roster));
        expect(textOf(afterRoster['draftLease']).length, greaterThan(20));
        expect(
          DateTime.parse(textOf(afterRoster['lastSynchronizedAtUtc'])).isBefore(
            DateTime.parse(textOf(obj(roster)['lastSynchronizedAtUtc'])),
          ),
          false,
        );
        expect(
          DateTime.parse(textOf(afterRoster['draftExpiresAtUtc'])).isAfter(
            DateTime.parse(textOf(afterRoster['lastSynchronizedAtUtc'])),
          ),
          true,
        );
        expect(
          await owner.request(
            'GET',
            '/api/v1/reports/financial?from=2026-09-01&to=2026-09-28',
          ),
          money,
        );
      } finally {
        await coach.logout();
        await owner.logout();
        await t.pumpWidget(const SizedBox());
      }
    },
  );
}
