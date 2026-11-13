import 'package:academy_mobile/core/api/api_client.dart';
import 'package:academy_mobile/features/guardian/guardian_payments.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

import 'shared_ui_quality_test.dart' show capture, qualityFrame;
import 'v2_role_workspaces_test.dart' show authenticate;

const externalPaymentId = '54efb32a-20a0-4c58-8d52-1b7b3bc26774';
const season = <String, dynamic>{
  'id': 'season',
  'arabicName': 'موسم كرة القدم',
  'price': 1050.75,
  'currency': 'EGP',
  'trainingFormat': 'Season',
  'durationMonths': 5,
  'chargedMonths': 3,
  'referenceMonthlyPrice': 350.25,
};
const days = <String, dynamic>{
  'id': 'days',
  'arabicName': 'اشتراك شهري',
  'price': 900,
  'currency': 'EGP',
  'durationDays': 30,
};
const countOnly = <String, dynamic>{
  'id': 'count',
  'arabicName': 'باقة الحصص',
  'price': 100,
  'currency': 'EGP',
  'sessionCount': 12,
};
const combined = <String, dynamic>{
  'id': 'combined',
  'arabicName': 'باقة المدة والحصص',
  'price': 200,
  'currency': 'EGP',
  'durationMonths': 3,
  'sessionCount': 8,
};

Map<String, dynamic> review(
  List<Map<String, dynamic>> plans, {
  Map<String, dynamic>? open,
}) => {
  'enrollment': {
    'id': 'training',
    'player': 'لاعب اصطناعي',
    'sport': 'كرة القدم',
    'branch': 'فرع الاختبار',
  },
  'plans': plans,
  'openRenewal': open,
};

Finder choose(String name) => find.descendant(
  of: find.ancestor(of: find.text(name), matching: find.byType(Card)).first,
  matching: find.widgetWithText(FilledButton, 'اختيار الباقة'),
);

void main() {
  testWidgets('calendar season terms carry into the narrow renewal review', (
    t,
  ) async {
    var writes = 0;
    final auth = await authenticate('Guardian', (method, path, _, _) async {
      if (method == 'POST') writes++;
      return review([season]);
    });
    t.view.physicalSize = const Size(320, 680);
    t.view.devicePixelRatio = 1;
    t.platformDispatcher.textScaleFactorTestValue = 1.3;
    addTearDown(t.view.reset);
    addTearDown(t.platformDispatcher.clearTextScaleFactorTestValue);
    addTearDown(auth.dispose);
    await t.pumpWidget(
      qualityFrame(
        PlanPicker(
          auth: auth,
          environment: 'Demo',
          childName: 'لاعب اصطناعي',
          enrollmentId: 'training',
        ),
      ),
    );
    await t.pumpAndSettle();
    expect(find.text('5 أشهر تقويمية · الأشهر المدفوعة: 3'), findsOneWidget);
    expect(find.textContaining('1,050.75'), findsOneWidget);
    await t.tap(choose('موسم كرة القدم'));
    await t.pumpAndSettle();
    expect(find.text('مراجعة التجديد'), findsOneWidget);
    expect(find.text('سعر الباقة قبل الخصم'), findsOneWidget);
    expect(find.text('5 أشهر تقويمية · الأشهر المدفوعة: 3'), findsOneWidget);
    expect(find.textContaining('350.25'), findsOneWidget);
    expect(find.text('متابعة إلى الدفع'), findsOneWidget);
    expect(writes, 0);
    expect(t.takeException(), isNull);
  });

  testWidgets(
    'back keeps the registration while day and session terms stay distinct',
    (t) async {
      final legacy = {...season}..remove('chargedMonths');
      final auth = await authenticate(
        'Guardian',
        (_, _, _, _) async => review([days, countOnly, combined, legacy]),
      );
      addTearDown(auth.dispose);
      await t.pumpWidget(
        qualityFrame(
          PlanPicker(
            auth: auth,
            environment: 'Demo',
            childName: 'لاعب اصطناعي',
            enrollmentId: 'training',
          ),
        ),
      );
      await t.pumpAndSettle();
      expect(find.text('30 يومًا'), findsOneWidget);
      expect(find.text('12 حصة'), findsOneWidget);
      expect(find.text('3 أشهر تقويمية · 8 حصص'), findsOneWidget);
      await t.tap(choose('اشتراك شهري'));
      await t.pumpAndSettle();
      expect(find.text('30 يومًا'), findsOneWidget);
      await t.tap(find.byTooltip('رجوع'));
      await t.pumpAndSettle();
      await t.tap(choose('باقة الحصص'));
      await t.pumpAndSettle();
      expect(find.text('لاعب اصطناعي'), findsOneWidget);
      expect(find.text('12 حصة'), findsOneWidget);
      expect(find.text('30 يومًا'), findsNothing);
      await t.tap(find.byTooltip('رجوع'));
      await t.pumpAndSettle();
      await t.scrollUntilVisible(find.text('5 أشهر تقويمية'), 160);
      expect(find.text('5 أشهر تقويمية'), findsOneWidget);
      expect(find.textContaining('الأشهر المدفوعة'), findsNothing);
    },
  );

  testWidgets('a failed catalog read can recover into the actual empty state', (
    t,
  ) async {
    var reads = 0;
    final auth = await authenticate('Guardian', (_, _, _, _) async {
      if (++reads == 1) throw const ApiFailure(503, 'تعذر تحميل الباقات');
      return review([]);
    });
    addTearDown(auth.dispose);
    await t.pumpWidget(
      qualityFrame(
        PlanPicker(
          auth: auth,
          environment: 'Demo',
          childName: 'لاعب اصطناعي',
          enrollmentId: 'training',
        ),
      ),
    );
    await t.pumpAndSettle();
    expect(find.text('لا توجد باقات متاحة.'), findsNothing);
    await t.tap(find.text('إعادة المحاولة'));
    await t.pumpAndSettle();
    expect(find.text('لا توجد باقات متاحة.'), findsOneWidget);
    expect(reads, 2);
  });

  testWidgets(
    'an unavailable own plan returns to a fresh list instead of retrying the write',
    (t) async {
      var reads = 0, writes = 0;
      final auth = await authenticate('Guardian', (method, path, _, _) async {
        if (method == 'POST') {
          expect(path, endsWith('/renewals'));
          writes++;
          throw const ApiFailure(404, 'غير متاح');
        }
        return review(++reads == 1 ? [season, days] : [days]);
      });
      addTearDown(auth.dispose);
      await t.pumpWidget(
        qualityFrame(
          PlanPicker(
            auth: auth,
            environment: 'Demo',
            childName: 'لاعب اصطناعي',
            enrollmentId: 'training',
          ),
        ),
      );
      await t.pumpAndSettle();
      await t.tap(choose('موسم كرة القدم'));
      await t.pumpAndSettle();
      await t.tap(find.text('متابعة إلى الدفع'));
      await t.pumpAndSettle();
      expect(find.text('الباقة أو التسجيل غير متاح الآن.'), findsOneWidget);
      expect(find.text('متابعة إلى الدفع'), findsNothing);
      await t.tap(find.text('تحديث الباقات'));
      await t.pumpAndSettle();
      expect(find.text('موسم كرة القدم'), findsNothing);
      expect(find.text('اشتراك شهري'), findsOneWidget);
      expect(reads, 2);
      expect(writes, 1);
    },
  );

  testWidgets(
    'a pending payment owned by the academy has no plan or payment action',
    (t) async {
      final auth = await authenticate(
        'Guardian',
        (_, _, _, _) async =>
            review([season], open: {'kind': 'Payment', 'paymentId': null}),
      );
      addTearDown(auth.dispose);
      await t.pumpWidget(
        qualityFrame(
          PlanPicker(
            auth: auth,
            environment: 'Demo',
            childName: 'لاعب اصطناعي',
            enrollmentId: 'training',
          ),
        ),
      );
      await t.pumpAndSettle();
      expect(find.text('تواصل مع الأكاديمية لمتابعة الطلب.'), findsOneWidget);
      expect(find.text('اختيار الباقة'), findsNothing);
      expect(find.text('متابعة طلب الدفع'), findsNothing);
    },
  );

  testWidgets(
    'a conflict returns to the authorized current payment instead of repeating the write',
    (t) async {
      var reads = 0, writes = 0;
      final auth = await authenticate('Guardian', (method, path, _, _) async {
        if (method == 'POST') {
          writes++;
          throw const ApiFailure(
            409,
            'يوجد طلب تجديد مفتوح.',
            code: 'renewal_already_open',
          );
        }
        if (path.endsWith('/renewal-review')) {
          return ++reads == 1
              ? review([days])
              : review([], open: {'kind': 'Payment', 'paymentId': 'existing'});
        }
        return {
          'id': 'existing',
          'status': 'Pending',
          'player': 'لاعب اصطناعي',
          'plan': 'اشتراك شهري',
          'sport': 'كرة القدم',
          'amount': 900,
          'currency': 'EGP',
          'isCurrent': true,
        };
      });
      addTearDown(auth.dispose);
      await t.pumpWidget(
        qualityFrame(
          PlanPicker(
            auth: auth,
            environment: 'Demo',
            childName: 'لاعب اصطناعي',
            enrollmentId: 'training',
          ),
        ),
      );
      await t.pumpAndSettle();
      await t.tap(choose('اشتراك شهري'));
      await t.pumpAndSettle();
      await t.tap(find.text('متابعة إلى الدفع'));
      await t.pumpAndSettle();
      await t.tap(find.text('عرض حالة التجديد'));
      await t.pumpAndSettle();
      expect(find.text('اختيار الباقة'), findsNothing);
      await t.tap(find.text('متابعة طلب الدفع'));
      await t.pumpAndSettle();
      expect(t.widget<CheckoutPage>(find.byType(CheckoutPage)).id, 'existing');
      expect(reads, 2);
      expect(writes, 1);
    },
  );

  for (final (failure, description) in [
    (const ApiFailure(404, 'الاختيار منتهي'), 'expired'),
    (
      const ApiFailure(409, 'رفض الطلب', code: 'renewal_already_open'),
      'blocked',
    ),
  ]) {
    testWidgets(
      'a $description external choice returns to the authorized search',
      (t) async {
        var searches = 0, writes = 0;
        final auth = await authenticate('Guardian', (method, path, _, _) async {
          if (path.endsWith('/search')) {
            searches++;
            return [
              {
                'name': 'لاعب تجريبي',
                'sport': 'كرة القدم',
                'branch': 'فرع أول',
                'reference': 'reference',
              },
            ];
          }
          if (path.endsWith('/resolve')) {
            return {
              'playerDisplayName': 'لاعب تجريبي',
              'plans': [season],
              if (writes != 0 && failure.status == 409)
                'openRenewal': {'kind': 'Unavailable'},
            };
          }
          expect(path, endsWith('/external/renewals'));
          writes++;
          throw failure;
        });
        addTearDown(auth.dispose);
        await t.pumpWidget(
          qualityFrame(ExternalRenewal(auth: auth, environment: 'Demo')),
        );
        await t.enterText(find.byType(TextField), 'لاعب');
        await t.tap(find.text('بحث عن لاعب'));
        await t.pumpAndSettle();
        await t.tap(find.text('لاعب تجريبي'));
        await t.pumpAndSettle();
        await t.tap(choose('موسم كرة القدم'));
        await t.pumpAndSettle();
        expect(find.text('هذا التجديد لا يربط اللاعب بحسابك.'), findsOneWidget);
        await t.tap(find.text('متابعة إلى الدفع'));
        await t.pumpAndSettle();
        if (failure.status == 409) {
          expect(find.text('طلب تجديد مفتوح'), findsOneWidget);
          expect(
            find.text('تواصل مع الأكاديمية لمتابعة الطلب.'),
            findsOneWidget,
          );
          expect(find.text('موسم كرة القدم'), findsNothing);
          expect(find.text('متابعة إلى الدفع'), findsNothing);
        }
        await t.tap(find.text('العودة إلى البحث'));
        await t.pumpAndSettle();
        expect(find.text('تجديد اشتراك لشخص آخر'), findsOneWidget);
        expect(find.text('موسم كرة القدم'), findsNothing);
        expect(find.text('لاعب تجريبي'), findsOneWidget);
        expect(searches, 1);
        expect(writes, 1);
      },
    );
  }

  testWidgets(
    'external selection resumes only the open checkout and returns to search',
    (t) async {
      final paths = <String>[];
      final auth = await authenticate('Guardian', (method, path, _, _) async {
        paths.add(path);
        if (path.endsWith('/search')) {
          return [
            {
              'name': 'لاعب تجريبي',
              'sport': 'كرة القدم',
              'branch': 'فرع أول',
              'reference': 'reference',
            },
          ];
        }
        if (path.endsWith('/resolve')) {
          return {
            'playerDisplayName': 'لاعب تجريبي',
            'sport': 'كرة القدم',
            'branch': 'فرع أول',
            'plans': [days],
            'openRenewal': {'kind': 'Payment', 'paymentId': externalPaymentId},
          };
        }
        expect(method, 'GET');
        expect(path, endsWith('/payments/$externalPaymentId'));
        return {
          'id': externalPaymentId,
          'status': 'Pending',
          'player': 'لاعب تجريبي',
          'plan': 'اشتراك شهري',
          'sport': 'كرة القدم',
          'amount': 900,
          'currency': 'EGP',
          'isCurrent': true,
        };
      });
      addTearDown(auth.dispose);
      await t.pumpWidget(
        qualityFrame(ExternalRenewal(auth: auth, environment: 'Demo')),
      );
      await t.enterText(find.byType(TextField), 'أب');
      await t.tap(find.text('بحث عن لاعب'));
      await t.pumpAndSettle();
      expect(
        find.text('اكتب ثلاثة أحرف على الأقل من اسم اللاعب.'),
        findsOneWidget,
      );
      expect(
        t.widget<EditableText>(find.byType(EditableText)).focusNode.hasFocus,
        isTrue,
      );
      expect(paths.where((path) => path.endsWith('/external/search')), isEmpty);
      await t.enterText(find.byType(TextField), 'لاعب');
      await t.testTextInput.receiveAction(TextInputAction.search);
      await t.pumpAndSettle();
      expect(
        t.widget<EditableText>(find.byType(EditableText)).focusNode.hasFocus,
        isFalse,
      );
      await t.tap(find.text('لاعب تجريبي'));
      await t.pumpAndSettle();
      expect(find.text('اختيار الباقة'), findsNothing);
      await t.tap(find.text('متابعة طلب الدفع'));
      await t.pumpAndSettle();
      expect(find.text('طلب دفع التجديد'), findsOneWidget);
      expect(find.text('المبلغ المطلوب'), findsOneWidget);
      await t.binding.handlePopRoute();
      await t.pumpAndSettle();
      expect(find.text('تجديد اشتراك لشخص آخر'), findsOneWidget);
      expect(
        paths.where((path) => path.endsWith('/external/renewals')),
        isEmpty,
      );
    },
  );

  testWidgets(
    'external conflict reads the saved request after a temporary error',
    (t) async {
      var reads = 0, writes = 0;
      final auth = await authenticate('Guardian', (method, path, _, _) async {
        if (path.endsWith('/external/renewals')) {
          writes++;
          throw const ApiFailure(
            409,
            'يوجد طلب مفتوح.',
            code: 'renewal_already_open',
          );
        }
        if (path.endsWith('/resolve')) {
          if (++reads == 1) throw const ApiFailure(503, 'تعذر قراءة الحالة.');
          return {
            'plans': [days],
            'openRenewal': {'kind': 'Payment', 'paymentId': externalPaymentId},
          };
        }
        throw StateError(path);
      });
      addTearDown(auth.dispose);
      await t.pumpWidget(
        qualityFrame(
          RenewalReview(
            auth: auth,
            environment: 'Demo',
            childName: 'لاعب تجريبي',
            plan: days,
            reference: 'reference',
          ),
        ),
      );
      await t.tap(find.text('متابعة إلى الدفع'));
      await t.pumpAndSettle();
      expect(find.text('تعذر التحقق من حالة الطلب.'), findsOneWidget);
      expect(find.text('متابعة إلى الدفع'), findsNothing);
      await t.tap(find.text('إعادة التحقق'));
      await t.pumpAndSettle();
      expect(find.text('متابعة طلب الدفع'), findsOneWidget);
      expect(writes, 1);
    },
  );

  testWidgets('external pending actions fit at 320 and doubled text', (
    t,
  ) async {
    t.view.devicePixelRatio = 1;
    t.view.physicalSize = const Size(320, 700);
    addTearDown(t.view.resetPhysicalSize);
    addTearDown(t.view.resetDevicePixelRatio);
    final auth = await authenticate('Guardian', (method, path, _, _) async {
      throw StateError('An existing notice should not send a request: $path');
    });
    addTearDown(auth.dispose);
    for (final open in [
      {'kind': 'Payment', 'paymentId': externalPaymentId},
      {'kind': 'Unavailable'},
    ]) {
      await t.pumpWidget(
        qualityFrame(
          Builder(
            builder: (context) {
              return MediaQuery(
                data: MediaQuery.of(context)
                    .copyWith(textScaler: const TextScaler.linear(2)),
                child: PlanPicker(
                  auth: auth,
                  environment: 'Demo',
                  childName: 'لاعب تجريبي باسم عربي طويل',
                  reference: 'reference',
                  externalRegistration: 'كرة القدم · فرع مدينة نصر',
                  plans: const [days],
                  externalOpen: open,
                ),
              );
            },
          ),
        ),
      );
      await t.pumpAndSettle();
      final own = open['kind'] == 'Payment';
      if (own) {
        await t.ensureVisible(find.text('متابعة طلب الدفع'));
        await t.pumpAndSettle();
      }
      expect(
        find.text('متابعة طلب الدفع').hitTestable(),
        own ? findsOneWidget : findsNothing,
      );
      expect(
        find.text('تواصل مع الأكاديمية لمتابعة الطلب.'),
        own ? findsNothing : findsOneWidget,
      );
      expect(find.text('اختيار الباقة'), findsNothing);
      expect(t.takeException(), isNull);
      await capture(t, 'external-resume-${own ? "payer" : "private"}-320');
    }
  });
}
