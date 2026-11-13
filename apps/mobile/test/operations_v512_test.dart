import 'package:academy_mobile/features/shared/operation_design.dart';
import 'package:academy_mobile/features/admin/native_academy_setup.dart';
import 'package:academy_mobile/features/coach/coach_evaluation.dart';
import 'package:academy_mobile/features/owner/owner_quick_operations.dart';
import 'package:academy_mobile/features/shared/directory_cards.dart';
import 'package:academy_mobile/core/api/api_client.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

import 'v2_role_workspaces_test.dart' as f;
import 'approved_native_forms_test.dart' show text, choose, tap;

const options = {
  'branches': [
    {'id': 'b', 'arabicName': 'فرع الرحاب'},
  ],
  'sports': [
    {'id': 's', 'arabicName': 'السباحة'},
  ],
  'categories': [
    {'id': 'a', 'arabicName': 'مواليد 2014'},
  ],
  'coaches': [
    {'id': 'c', 'arabicName': 'مدرب خيالي'},
  ],
};
void main() {
  test('conflict messages accept only known codes, never arbitrary response content', () {
    expect(
      evaluationConflictMessage(
        '{"code":"evaluation_criteria_missing","message":"unsafe"}',
      ),
      contains('معايير'),
    );
    expect(
      evaluationConflictMessage('{"message":"secret or stack trace"}'),
      isNull,
    );
    expect(evaluationConflictMessage('<html>'), isNull);
  });
  testWidgets(
    'owner operations expose core setup, while a read-only admin has no write shortcuts',
    (t) async {
      t.view.physicalSize = const Size(430, 3000);
      t.view.devicePixelRatio = 1;
      addTearDown(t.view.resetPhysicalSize);
      addTearDown(t.view.resetDevicePixelRatio);
      final owner = await f.authenticate(
        'AcademyOwner',
        (_, _, _, _) async => {},
      );
      await t.pumpWidget(f.app(OwnerQuickOperations(auth: owner)));
      await t.pumpAndSettle();
      for (final name in [
        'إضافة فرع',
        'إضافة رياضة',
        'إنشاء مجموعة',
        'إضافة باقة',
      ]) {
        final card = find.byWidgetPredicate(
          (w) => w is OperationActionTile && w.title == name,
        );

        expect(card, findsOneWidget);
      }
      final admin = await f.authenticate(
        'AcademyAdmin',
        (m, p, b, token) async => p.endsWith('my-permissions')
            ? <String>[]
            : {'useActionGrants': true, 'actions': []},
      );
      await t.pumpWidget(f.app(OwnerQuickOperations(auth: admin)));
      await t.pumpAndSettle();
      expect(find.text('إضافة باقة'), findsNothing);
      expect(find.text('إنشاء مجموعة'), findsNothing);
    },
  );
  testWidgets(
    'branch creation and plan review write only after explicit save',
    (t) async {
      final writes = <Map<String, dynamic>>[];
      final auth = await f.authenticate('AcademyOwner', (m, p, b, token) async {
        if (m == 'POST') {
          writes.add({'path': p, 'body': b});
          return {'id': 'saved'};
        }
        return options;
      });
      await t.pumpWidget(f.app(NativeNamedSetup(auth: auth, kind: 'branches')));
      await t.pumpAndSettle();
      await text(t, 'الاسم', 'فرع مصري خيالي');
      await tap(t, 'حفظ الفرع');
      expect(writes.length, 1);
      expect(writes.first['path'], '/api/v1/manage/branches');
      expect(find.text('تم الحفظ بنجاح.'), findsOneWidget);
      await t.pumpWidget(f.app(NativePlanCreate(auth: auth)));
      await t.pumpAndSettle();
      await text(t, 'اسم الباقة', 'باقة شهرية');
      await text(t, 'سعر الباقة', '1000');
      await tap(t, 'مراجعة الباقة');
      expect(writes.length, 1);
      await tap(t, 'تأكيد حفظ الباقة');
      expect(writes.length, 2);
      final body = writes.last['body'] as Map;
      expect(body['sessionCount'], 12);
      expect(body['durationMonths'], 1);
      expect(body['price'], '1000');
      expect(body['serviceKind'], 'Training');
      expect(find.text('الباقة جاهزة'), findsOneWidget);
      expect(t.takeException(), isNull);
    },
  );
  testWidgets('a seasonal plan reviews and saves exact calendar terms', (
    t,
  ) async {
    final writes = <Map>[];
    final auth = await f.authenticate('AcademyOwner', (
      method,
      path,
      body,
      _,
    ) async {
      if (method == 'POST') {
        expect(path, '/api/v1/subscriptions/plans');
        writes.add(body as Map);
        return {'id': 'season'};
      }
      return options;
    });
    await t.pumpWidget(f.app(NativePlanCreate(auth: auth)));
    await t.pumpAndSettle();
    await text(t, 'اسم الباقة', 'موسم السباحة التجريبي');
    await choose(t, 'نمط التدريب', 'موسمي');
    expect(find.text('عدد الحصص'), findsNothing);
    await text(t, 'أشهر الخدمة', '٥');
    await text(t, 'السعر الشهري المرجعي', '٦٠٠٫٢٥');
    await text(t, 'الأشهر المدفوعة', '٦');
    await tap(t, 'مراجعة الباقة');
    expect(find.text('أدخل عددًا من 1 إلى عدد أشهر الخدمة.'), findsOneWidget);
    expect(writes, isEmpty);
    await text(t, 'الأشهر المدفوعة', '٤');
    await tap(t, 'مراجعة الباقة');
    expect(find.text('متاحة في كل الفروع'), findsOneWidget);
    expect(writes, isEmpty);
    await tap(t, 'تأكيد حفظ الباقة');
    expect(writes.single, containsPair('trainingFormat', 'Season'));
    expect(writes.single, containsPair('planType', 'Duration'));
    expect(writes.single, containsPair('durationMonths', 5));
    expect(writes.single, containsPair('chargedMonths', 4));
    expect(writes.single, containsPair('referenceMonthlyPrice', '600.25'));
    expect(writes.single, containsPair('price', '2401.00'));
    expect(writes.single, containsPair('sessionCount', null));
    expect(t.takeException(), isNull);
    await t.pumpWidget(const SizedBox());
    auth.dispose();
  });
  testWidgets(
    'group wizard validates days then previews and saves group, coach and timetable together',
    (t) async {
      final writes = <String>[];
      Map? saved;

      final auth = await f.authenticate('AcademyOwner', (m, p, b, token) async {
        if (p.endsWith('structure/options')) return options;
        if (p.endsWith('training-levels')) {
          return [
            {'id': 'l', 'sportId': 's', 'arabicName': 'مبتدئ'},
          ];
        }
        if (p.endsWith('/facilities')) return {'resources': [], 'spaces': []};
        if (p.endsWith('/preview')) {
          writes.add('preview');
          return {
            'fingerprint': 'fp',
            'sessions': 13,
            'players': 0,
            'conflicts': [],
          };
        }
        if (m == 'POST') {
          writes.add('save');
          saved = b as Map;
          return {'id': 'group'};
        }
        return {};
      });
      await t.pumpWidget(f.app(NativeGroupCreate(auth: auth)));
      await t.pumpAndSettle();
      await text(t, 'اسم المجموعة', 'مجموعة ناشئين خيالية');
      await choose(t, 'الفئة العمرية', 'مواليد 2014');
      await choose(t, 'المستوى — اختياري', 'مبتدئ');
      await tap(t, 'متابعة للحصص والمدرب');
      await choose(t, 'المدرب', 'مدرب خيالي');
      await tap(t, 'معاينة المجموعة');
      expect(writes, isEmpty);
      expect(find.text('اختر أيام التدريب.'), findsOneWidget);
      final sunday = find.widgetWithText(FilterChip, 'الأحد');
      await Scrollable.ensureVisible(t.element(sunday), alignment: .2);
      await t.pumpAndSettle();
      await t.tap(sunday);
      await t.pumpAndSettle();
      await tap(t, 'معاينة المجموعة');
      expect(writes, ['preview']);
      await tap(t, 'تأكيد إنشاء المجموعة');
      expect(writes, ['preview', 'save']);
      expect(saved!['coachMembershipId'], 'c');
      expect(saved!['branchId'], 'b');
      expect((saved!['timetable'] as Map)['days'], [0]);
      expect(saved!['fingerprint'], 'fp');
      expect(find.text('المجموعة جاهزة'), findsOneWidget);

      expect(t.takeException(), isNull);
    },
  );
  testWidgets(
    'missing evaluation criteria is an explicit setup step and never publishes a score',
    (t) async {
      final calls = <String>[];
      final auth = await f.authenticate('AcademyOwner', (m, p, b, token) async {
        calls.add('$m $p');
        if (p == '/api/v1/academy/calendar') return {'today': '2026-11-05'};
        if (p.contains('/criteria/defaults')) {
          return m == 'GET'
              ? {
                  'sportId': 's',
                  'sport': 'السباحة',
                  'criteria': [
                    {'arabicName': 'تقنية التنفس'},
                    {'arabicName': 'التوازن'},
                  ],
                }
              : {'count': 2, 'created': 2};
        }
        if (p == '/api/v1/evaluations/criteria?sportId=s') return [];
        if (m == 'POST') return {'id': 'e'};
        return {
          'id': 'e',
          'sportEnrollmentId': 'en',
          'status': 'Draft',
          'version': 1,
          'player': 'طفل خيالي',
          'group': 'مجموعة ناشئين',
          'scores': [
            {'criterionId': 'c', 'name': 'التنفس', 'score': null},
          ],
        };
      });
      await t.pumpWidget(
        f.app(
          CoachEvaluation(
            auth: auth,
            enrollment: const {
              'id': 'en',
              'sportId': 's',
              'player': 'طفل خيالي',
              'group': 'مجموعة ناشئين',
              'criteriaReady': false,
            },
          ),
        ),
      );
      await t.pumpAndSettle();
      expect(calls, ['GET /api/v1/evaluations/criteria?sportId=s']);
      expect(find.text('لا توجد معايير لهذه الرياضة'), findsOneWidget);
      await tap(t, 'عرض المعايير المقترحة');
      await tap(t, 'إلغاء');
      expect(calls.where((c) => c.startsWith('POST')), isEmpty);
      await tap(t, 'عرض المعايير المقترحة');
      expect(calls.where((c) => c.startsWith('POST')), isEmpty);
      await tap(t, 'إضافة المعايير وبدء التقييم');
      expect(calls.where((c) => c.startsWith('POST')), [
        'POST /api/v1/evaluations/criteria/defaults',
        'POST /api/v1/evaluations',
      ]);
      expect(calls.any((c) => c.contains('/publish')), isFalse);
      expect(find.text('التنفس'), findsOneWidget);
      expect(t.takeException(), isNull);
    },
  );
  for (final width in [320.0, 430.0]) {
    testWidgets(
      'directory cards remain readable RTL at $width with large text',
      (t) async {
        t.view.physicalSize = Size(width, 1300);
        t.view.devicePixelRatio = 1;
        addTearDown(t.view.resetPhysicalSize);
        addTearDown(t.view.resetDevicePixelRatio);
        final auth = await f.authenticate(
          'AcademyOwner',
          (_, _, _, _) async => {},
        );
        await t.pumpWidget(
          f.app(
            Scaffold(
              body: SingleChildScrollView(
                child: Padding(
                  padding: const EdgeInsets.all(14),
                  child: Column(
                    children: [
                      PlayerDirectoryCard(
                        auth: auth,
                        player: const {
                          'arabicName': 'طفل مصري خيالي',
                          'playerCode': 'PL01',
                          'dateOfBirth': '2014-02-01',
                          'hasActiveSubscription': true,
                        },
                        enrollments: const [
                          {
                            'sport': 'السباحة',
                            'branch': 'فرع الرحاب',
                            'group': 'مجموعة ناشئين',
                            'subscription': {
                              'remainingSessions': 12,
                              'sessionBased': true,
                            },
                          },
                        ],
                        onOpen: () {},
                        onEvaluate: () {},
                      ),
                      GroupDirectoryCard(
                        group: const {
                          'arabicName': 'مجموعة ناشئين',
                          'sportName': 'السباحة',
                          'branchName': 'فرع الرحاب',
                          'playerCount': 15,
                          'coaches': ['مدرب مصري خيالي'],
                        },
                        onTap: () {},
                      ),
                    ],
                  ),
                ),
              ),
            ),
            scale: 1.6,
          ),
        );
        await t.pumpAndSettle();
        expect(find.text('متبقي 12 حصة'), findsOneWidget);
        expect(find.text('15 لاعب'), findsOneWidget);
        expect(find.text('المدرب: مدرب مصري خيالي'), findsOneWidget);
        expect(t.takeException(), isNull);
      },
    );
  }
}
