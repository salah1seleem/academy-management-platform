import 'package:academy_mobile/core/api/api_client.dart';
import 'package:academy_mobile/core/auth/auth_controller.dart';
import 'package:academy_mobile/core/storage/refresh_store.dart';
import 'package:academy_mobile/features/guardian/guardian_ui.dart';
import 'package:academy_mobile/features/shared/player_goals.dart'
    show goalCommandId;
import 'package:academy_mobile/features/shared/player_technical_reviews.dart';
import 'package:academy_mobile/features/shared/technical_promotion.dart';
import 'package:flutter/material.dart';
import 'package:flutter_localizations/flutter_localizations.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:integration_test/integration_test.dart';

void main() {
  final binding = IntegrationTestWidgetsFlutterBinding.ensureInitialized();
  testWidgets(
    'Owner previews and confirms actual reviewed promotion; linked family reads application and new schedule without financial change',
    (t) async {
      expect(const bool.fromEnvironment('ISOLATED_DEMO'), true);
      expect(
        const String.fromEnvironment('API_BASE_URL'),
        'http://127.0.0.1:5251',
      );
      Future<AuthController> login(String phone, String role) async {
        final a = AuthController(
          HttpApiTransport(
            AppConfig('Demo', const String.fromEnvironment('API_BASE_URL')),
          ),
          SecureRefreshStore('academy.v3.promotion.$role'),
        );
        await a.restore();
        if (a.session != null) await a.logout();
        final c = await a.requestOtp(phone);
        await a.verify(phone, c, '246810');
        expect(
          a.session!.selected!.academyId,
          '30000000-0000-0000-0000-000000000003',
        );
        return a;
      }

      Widget shell(Widget child) => MaterialApp(
        key: UniqueKey(),
        locale: const Locale('ar'),
        supportedLocales: const [Locale('ar')],
        localizationsDelegates: GlobalMaterialLocalizations.delegates,
        theme: guardianTheme(),
        home: child,
      );
      Future<void> wait(Finder f) async {
        for (var i = 0; i < 180 && f.evaluate().isEmpty; i++) {
          await t.pump(const Duration(milliseconds: 100));
        }
        expect(f, findsWidgets);
        await t.pumpAndSettle();
      }

      final owner = await login('01099900010', 'owner'),
          family = await login('01099900107', 'guardian');
      try {
        Future<Json> read(String path) async =>
            obj(await owner.request('GET', path));
        Future<Object?> write(
          String path,
          Json body, [
          String method = 'POST',
        ]) => owner.request(
          method,
          path,
          body: body,
          idempotencyKey: goalCommandId(),
        );
        final child = textOf(
          rows(
            obj(
              await family.request('GET', '/api/v1/guardian/home'),
            )['children'],
          ).first['id'],
        );
        final e = rows(
              (await read('/api/v1/evaluations/options'))['enrollments'],
            ).singleWhere((e) => e['playerId'] == child),
            eid = textOf(e['id']);
        final current = await read(
              '/api/v1/people/enrollments/$eid/placement-level',
            ),
            journey = await read(
              '/api/v1/evaluations/skills/enrollments/$eid/journey',
            );
        final tag = goalCommandId().substring(0, 6);
        if (current['trainingLevelId'] == null) {
          final initial = obj(
            await write('/api/v1/manage/training-levels', {
              'sportId': e['sportId'],
              'arabicName': 'بداية iOS اصطناعية $tag',
              'rank': 50,
            }),
          );
          await write('/api/v1/people/enrollments/$eid/placement-level', {
            'levelId': initial['id'],
          }, 'PUT');
          current['trainingLevelId'] = initial['id'];
        }
        final entitlement = obj(
          await write('/api/v1/subscriptions/plans', {
            'arabicName': 'استحقاق iOS اصطناعي $tag',
            'sportId': e['sportId'],
            'planType': 'Duration',
            'price': '100.00',
            'durationDays': 365,
            'sessionCount': null,
            'allBranches': true,
            'serviceKind': 'Training',
            'trainingFormat': 'Group',
          }),
        );
        final renewal = obj(
          await write('/api/v1/subscriptions/admin-renewals', {
            'sportEnrollmentId': eid,
            'subscriptionPlanId': entitlement['id'],
          }),
        );
        await write(
          '/api/v1/subscriptions/payments/${renewal['paymentId']}/simulate',
          {'outcome': 'Success'},
        );
        // Real isolated preparation commands, not claims of a human production review.
        final c = obj(
          await write('/api/v1/evaluations/skills/curricula', {
            'trainingLevelId': current['trainingLevelId'],
            'title': 'خطوة تالية لاختبار iOS $tag',
            'requirements': [
              {
                'name': 'تحكم اصطناعي مراجع',
                'unit': 'Checklist',
                'protocol': 'ملاحظة فنية اصطناعية في التمرين نفسه',
              },
            ],
            'requestId': goalCommandId(),
          }),
        );
        final cd = await read(
          '/api/v1/evaluations/skills/curricula/${c['id']}',
        );
        await write('/api/v1/evaluations/skills/curricula/${c['id']}/publish', {
          'version': obj(cd['item'])['version'],
          'requestId': goalCommandId(),
        });
        final previous = journey['active'] == null
            ? <String, dynamic>{}
            : obj(obj(journey['active'])['plan']);
        final p = obj(
          await write(
            '/api/v1/evaluations/skills/enrollments/$eid/skill-plan',
            {
              'curriculumId': c['id'],
              'previousPlanId': previous['id'],
              'previousVersion': previous['version'] ?? 0,
              'requestId': goalCommandId(),
            },
          ),
        );
        final pid = textOf(p['id']);
        final plan = await read('/api/v1/evaluations/skills/plans/$pid');
        for (final skill in rows(plan['skills'])) {
          await write('/api/v1/evaluations/skills/plans/$pid/reviews', {
            'requirementId': obj(skill['requirement'])['id'],
            'state': 'Mastered',
            'measuredValue': null,
            'observedOn': plan['asOfDate'],
            'note': 'دليل اختبار iOS معزول',
            'requestId': goalCommandId(),
          });
        }
        final path = '/api/v1/evaluations/skills/plans/$pid/technical-reviews',
            v = await read(path);
        final r = obj(
              await write(path, {
                'planVersion': obj(v['plan'])['version'],
                'sourceReviewSequence': obj(v['source'])['head'],
                'previousRequestId': null,
                'requestId': goalCommandId(),
              }),
            ),
            rid = textOf(r['id']);
        final next = obj(
          await write('/api/v1/manage/training-levels', {
            'sportId': e['sportId'],
            'arabicName': 'مستوى نقل iOS اصطناعي $tag',
            'rank': 70,
          }),
        );
        final detail = await read('/api/v1/evaluations/technical-reviews/$rid');
        await write('/api/v1/evaluations/technical-reviews/$rid/decision', {
          'version': obj(detail['request'])['version'],
          'outcome': 'ReadyForPlacement',
          'recommendedLevelId': next['id'],
          'familyNote': 'توصية فنية اصطناعية لنقل iOS $tag',
          'requestId': goalCommandId(),
        });
        final profile = await read('/api/v1/people/players/$child'),
            old = rows(profile['enrollments'])
                .singleWhere((x) => x['id'] == eid),
            options = await read('/api/v1/manage/structure/options');
        final source = rows(options['groups'])
                .singleWhere((g) => g['id'] == old['trainingGroupId']),
            coach = rows(options['coaches'])
                .singleWhere((c) => c['arabicName'] == 'حسام شريف عادل');
        final group = textOf(
          await write('/api/v1/manage/groups', {
            'arabicName': 'مجموعة نقل iOS $tag',
            'branchId': source['branchId'],
            'sportId': source['sportId'],
            'ageCategoryId': source['ageCategoryId'],
            'capacity': 15,
          }),
        );
        await write('/api/v1/manage/groups/$group/placement-level', {
          'levelId': next['id'],
        }, 'PUT');
        await write('/api/v1/manage/groups/$group/coaches', {
          'membershipId': coach['id'],
        });
        final occupied = rows(
          await owner.request(
            'GET',
            '/api/v1/attendance/sessions?allDates=true',
          ),
        );
        var day = DateTime.parse(textOf(plan['asOfDate']))
            .add(const Duration(days: 41));
        while (occupied.any(
          (s) => s['sessionDate'] == day.toIso8601String().substring(0, 10),
        )) {
          day = day.add(const Duration(days: 1));
        }
        final date = day.toIso8601String().substring(0, 10);
        final session = obj(
          await write('/api/v1/attendance/sessions', {
            'trainingGroupId': group,
            'sessionDate': date,
            'startTime': '07:15:00',
            'endTime': '08:15:00',
            'notes': 'موعد iOS اصطناعي',
          }),
        );
        final financial = await owner.request(
          'GET',
          '/api/v1/reports/financial?from=2026-09-01&to=2026-09-28',
        );
        await t.pumpWidget(
          shell(TechnicalPromotionPage(auth: owner, requestId: rid)),
        );
        await wait(find.byType(DropdownButtonFormField<String>));
        await t.tap(find.byType(DropdownButtonFormField<String>));
        await t.pumpAndSettle();
        await t.tap(find.text('مجموعة نقل iOS $tag').last);
        await t.pumpAndSettle();
        await t.tap(find.text('معاينة المستوى والمواعيد'));
        await wait(find.text('اعتماد المستوى ونقل المجموعة'));
        await t.scrollUntilVisible(
          find.text('اعتماد المستوى ونقل المجموعة'),
          180,
          scrollable: find.byType(Scrollable).first,
        );
        if (const bool.fromEnvironment('CAPTURE_VISUALS')) {
          await binding.takeScreenshot('phase-promotion-owner-preview-ios');
        }
        await t.tap(find.text('اعتماد المستوى ونقل المجموعة'));
        await wait(
          find.textContaining('تم اعتماد المستوى ونقل المواعيد مع حفظ التاريخ'),
        );
        final saved = await read(
          '/api/v1/people/enrollments/$eid/placement-level',
        );
        expect(saved['trainingLevelId'], next['id']);
        expect(
          obj(
            rows(obj(saved['history'])['items']).first,
          )['technicalDecisionId'],
          isNotNull,
        );
        await t.pumpWidget(
          shell(
            PlayerTechnicalReviewsPage(
              auth: family,
              planId: pid,
              playerId: child,
            ),
          ),
        );
        await wait(find.textContaining('تم تنفيذ المستوى بمراجعة إدارية'));
        await t.scrollUntilVisible(
          find.textContaining('تم تنفيذ المستوى بمراجعة إدارية'),
          180,
          scrollable: find.byType(Scrollable).first,
        );
        if (const bool.fromEnvironment('CAPTURE_VISUALS')) {
          await binding.takeScreenshot('phase-promotion-family-applied-ios');
        }
        final familyProfile = obj(
          await family.request(
            'GET',
            '/api/v1/guardian/children/$child/profile',
          ),
        );
        final related = rows(familyProfile['enrollments'])
            .singleWhere((x) => x['id'] == eid);
        expect(
          rows(related['upcoming']).any((s) => s['id'] == session['id']),
          true,
        );
        final after = rows(
          (await read('/api/v1/people/players/$child'))['enrollments'],
        ).singleWhere((x) => x['id'] == eid);
        expect(after['subscription'], old['subscription']);
        expect(
          await owner.request(
            'GET',
            '/api/v1/reports/financial?from=2026-09-01&to=2026-09-28',
          ),
          financial,
        );
        expect(find.text('اعتماد المستوى ونقل المجموعة'), findsNothing);
      } finally {
        await owner.logout();
        await family.logout();
        await t.pumpWidget(const SizedBox.shrink());
      }
    },
  );
}
