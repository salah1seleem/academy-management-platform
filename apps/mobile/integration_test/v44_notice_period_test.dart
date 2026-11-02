import 'package:academy_mobile/core/api/api_client.dart';
import 'package:academy_mobile/core/auth/auth_controller.dart';
import 'package:academy_mobile/core/storage/refresh_store.dart';
import 'package:academy_mobile/core/theme/app_theme.dart';
import 'package:academy_mobile/features/admin/native_player_workspace.dart';
import 'package:academy_mobile/features/shared/notification_inbox.dart';
import 'package:academy_mobile/features/shared/staff_capabilities.dart';
import 'package:academy_mobile/features/guardian/guardian_profile.dart';
import 'package:academy_mobile/features/guardian/guardian_ui.dart';
import 'package:flutter/material.dart';
import 'package:flutter_localizations/flutter_localizations.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:integration_test/integration_test.dart';

void main() {
  final binding = IntegrationTestWidgetsFlutterBinding.ensureInitialized();
  testWidgets(
    'real committed day events reach the linked guardian inbox and open the exact audited period',
    (t) async {
      expect(const bool.fromEnvironment('ISOLATED_DEMO'), isTrue);
      const base = String.fromEnvironment('API_BASE_URL');
      expect(base, 'http://127.0.0.1:5278');
      Future<AuthController> login(String phone, String suffix) async {
        final auth = AuthController(
          HttpApiTransport(AppConfig('Demo', base)),
          SecureRefreshStore('v44.5278.notice.period.$suffix'),
        )..restoring = false;
        await auth.restore();
        if (auth.session == null) {
          final c = await auth.requestOtp(phone);
          await auth.verify(phone, c, '246810');
        }
        expect(
          auth.session!.selected!.academyId,
          '30000000-0000-0000-0000-000000000003',
        );
        return auth;
      }

      final owner = await login('01099900010', 'owner'),
          guardian = await login('01099900102', 'guardian102');
      expect(owner.session!.selected!.role, 'AcademyOwner');
      expect(guardian.session!.selected!.role, 'Guardian');
      final enrollments = rows(
        await guardian.request(
          'GET',
          '/api/v1/guardian/subscriptions/enrollments',
        ),
      );
      Json? candidate;
      for (final enrollment in enrollments) {
        if (enrollment['period'] is! Map) continue;
        final read = obj(
          await owner.request(
            'GET',
            '/api/v1/subscriptions/periods/${enrollment['period']['id']}',
          ),
        );
        if (['Duration', 'Combined'].contains(read['planType']) &&
            read['endDate'] != null &&
            !['Frozen', 'Cancelled'].contains(read['status'])) {
          candidate = read;
          break;
        }
      }
      expect(
        candidate,
        isNotNull,
        reason: 'Only dated Duration/Combined periods support day adjustments.',
      );
      final before = candidate!;
      final periodId = textOf(before['id']);
      final beforeEnd = textOf(before['endDate']),
          beforeRemaining = before['remainingSessions'];
      final ids = <String>[];
      final beforeInbox = obj(
        await guardian.request('GET', '/api/v1/notifications'),
      );
      ids.addAll(rows(beforeInbox['items']).map((n) => textOf(n['id'])));
      Future<void> idle() async {
        for (
          var i = 0;
          i < 240 &&
              find.byType(CircularProgressIndicator).evaluate().isNotEmpty;
          i++
        ) {
          await t.pump(const Duration(milliseconds: 150));
        }
        await t.pumpAndSettle();
      }

      Future<void> waitFor(Finder f) async {
        for (var i = 0; i < 240 && f.evaluate().isEmpty; i++) {
          await t.pump(const Duration(milliseconds: 150));
        }
        expect(f, findsWidgets);
        await t.pumpAndSettle();
      }

      Future<void> tap(String label) async {
        final scroll = find.byType(Scrollable).first;
        t.state<ScrollableState>(scroll).position.jumpTo(0);
        await t.pumpAndSettle();
        final f = find.text(label);
        await t.scrollUntilVisible(f, 140, scrollable: scroll);
        await t.ensureVisible(f.last);
        await t.pumpAndSettle();
        await t.ensureVisible(f.last);
        await t.tap(f.last);
        await t.pump(const Duration(milliseconds: 400));
      }

      Future<void> show(Widget screen, String key) async {
        await t.pumpWidget(
          MaterialApp(
            key: ValueKey(key),
            debugShowCheckedModeBanner: false,
            locale: const Locale('ar'),
            supportedLocales: const [Locale('ar')],
            localizationsDelegates: GlobalMaterialLocalizations.delegates,
            theme: AcademyTheme.dark(),
            home: screen,
          ),
        );
        await t.pump(const Duration(milliseconds: 300));
        await idle();
      }

      await show(
        NativeSubscriptionPeriod(
          auth: owner,
          id: periodId,
          access: const StaffCapabilities(
            owner: true,
            permissions: [],
            scope: {},
          ),
        ),
        'owner',
      );
      Future<void> adjust(String label) async {
        final select = find.widgetWithText(
          DropdownButtonFormField<String>,
          'الإجراء',
        );
        await t.scrollUntilVisible(
          select,
          140,
          scrollable: find.byType(Scrollable).first,
        );
        await t.ensureVisible(select);
        await t.tap(select);
        await t.pumpAndSettle();
        await t.tap(find.text(label).last);
        await t.pumpAndSettle();
        await t.enterText(
          find.widgetWithText(TextFormField, 'عدد الأيام'),
          '1',
        );
        await t.enterText(
          find.widgetWithText(TextFormField, 'سبب التعديل'),
          'قبول التنبيهات ببيانات عرض اصطناعية',
        );
        await tap('راجعت أثر التعديل على الاشتراك وأؤكد تنفيذه.');
        await tap('تأكيد تعديل الاشتراك');
        await idle();
        expect(
          owner.session?.selected?.role,
          'AcademyOwner',
          reason: 'Owner authorization must remain the opened context.',
        );
        expect(find.byType(NativeSubscriptionPeriod), findsOneWidget);
        final position = t
            .state<ScrollableState>(find.byType(Scrollable).first)
            .position;
        position.jumpTo(position.maxScrollExtent);
        await t.pumpAndSettle();
        await t.pumpAndSettle();
        expect(
          find.text('حُفظ التعديل وأُعيدت قراءة سجل الاشتراك.'),
          findsOneWidget,
        );
      }

      // V4.5 replaces the main Add Days action with sessions. Legacy API and history remain covered.
      await owner.request(
        'POST',
        '/api/v1/subscriptions/periods/$periodId/days',
        body: {
          'direction': 'Add',
          'days': 1,
          'reason': 'قبول توافق سجل الأيام القديم',
          'expectedVersion': before['version'],
        },
        idempotencyKey: 'legacy-days-${DateTime.now().microsecondsSinceEpoch}',
      );
      await show(
        NativeSubscriptionPeriod(
          auth: owner,
          id: periodId,
          access: const StaffCapabilities(
            owner: true,
            permissions: [],
            scope: {},
          ),
        ),
        'owner-legacy-read',
      );
      await waitFor(find.text('سجل التعديلات'));
      var saved = obj(
        await owner.request('GET', '/api/v1/subscriptions/periods/$periodId'),
      );
      expect(
        saved['endDate'],
        DateTime.parse(beforeEnd)
            .add(const Duration(days: 1))
            .toIso8601String()
            .substring(0, 10),
      );
      expect(saved['remainingSessions'], beforeRemaining);
      await binding.takeScreenshot('phase-approved-days-audit-native');
      t
          .state<ScrollableState>(find.byType(Scrollable).first)
          .position
          .jumpTo(0);
      await t.pumpAndSettle();
      await adjust('خصم أيام');
      saved = obj(
        await owner.request('GET', '/api/v1/subscriptions/periods/$periodId'),
      );
      expect(saved['endDate'], beforeEnd);
      expect(saved['remainingSessions'], beforeRemaining);
      final inbox = obj(await guardian.request('GET', '/api/v1/notifications'));
      final notices = rows(inbox['items'])
          .where(
            (n) =>
                !ids.contains(n['id']) &&
                n['targetKind'] == 'SubscriptionPeriod' &&
                n['targetId'] == periodId &&
                ['DaysAdded', 'DaysDeducted'].contains(n['eventKey']),
          )
          .toList();
      expect(notices, hasLength(2));
      expect(notices.every((n) => n['isRead'] == false), isTrue);
      expect(notices.every((n) => !textOf(n['message']).contains('{')), isTrue);
      await show(NotificationInbox(auth: guardian), 'guardian-inbox');
      await waitFor(find.text(textOf(notices.first['message'])));
      await binding.takeScreenshot('phase-approved-guardian-days-inbox');
      final noticeCard = find
          .ancestor(
            of: find.text(textOf(notices.first['message'])),
            matching: find.byType(GCard),
          )
          .first;
      final mark = find.descendant(
        of: noticeCard,
        matching: find.text('تمت القراءة'),
      );
      await t.ensureVisible(mark);
      await t.tap(mark);
      await t.pump(const Duration(milliseconds: 400));
      await idle();
      final read = obj(await guardian.request('GET', '/api/v1/notifications'));
      expect(
        rows(read['items'])
            .firstWhere((n) => n['id'] == notices.first['id'])['isRead'],
        isTrue,
      );
      final card = find
          .ancestor(
            of: find.text(textOf(notices.first['message'])),
            matching: find.byType(GCard),
          )
          .first;
      final link = find.descendant(
        of: card,
        matching: find.text('عرض الاشتراك وتعديلاته'),
      );
      await t.ensureVisible(link);
      await t.tap(link);
      await t.pump(const Duration(milliseconds: 400));
      await idle();
      expect(t.widget<PeriodPage>(find.byType(PeriodPage)).id, periodId);
      expect(find.text('حالة الاشتراك'), findsOneWidget);
      await binding.takeScreenshot('phase-approved-guardian-exact-period');
      expect(t.takeException(), isNull);
      await t.pumpWidget(const SizedBox());
      await owner.logout();
      await guardian.logout();
      owner.dispose();
      guardian.dispose();
    },
  );
}
