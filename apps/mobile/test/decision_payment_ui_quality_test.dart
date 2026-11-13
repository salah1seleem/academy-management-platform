import 'package:academy_mobile/core/api/api_client.dart';
import 'package:academy_mobile/features/admin/native_enrollment_payment_read.dart';
import 'package:academy_mobile/features/admin/native_renewal_proof_review.dart';
import 'package:academy_mobile/features/guardian/guardian_ui.dart';
import 'package:academy_mobile/features/owner/decision_center.dart';
import 'package:academy_mobile/features/owner/group_merge.dart';
import 'package:academy_mobile/features/shared/role_navigation.dart';
import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_test/flutter_test.dart';

import 'owner_test.dart' show owner, shell;
import 'shared_ui_quality_test.dart' show capture;
import 'v2_role_workspaces_test.dart' show app;

const captureDir = String.fromEnvironment('UI_QUALITY_CAPTURE_DIR');
Widget frame(Widget child) => captureDir.isEmpty
    ? child
    : RepaintBoundary(key: const ValueKey('quality-capture'), child: child);

void main() {
  TestWidgetsFlutterBinding.ensureInitialized();
  setUpAll(() async {
    final arabic = FontLoader('AcademyArabic');
    for (final weight in [400, 500, 600, 700]) {
      arabic.addFont(rootBundle.load('assets/fonts/AcademyArabic-$weight.ttf'));
    }
    await arabic.load();
    if (captureDir.isNotEmpty) {
      final icons = FontLoader('MaterialIcons')
        ..addFont(rootBundle.load('fonts/MaterialIcons-Regular.otf'));
      await icons.load();
    }
  });
  testWidgets(
    'payment reference search keeps scope, retries and rechecks enrollment on return',
    (t) async {
      t.view.physicalSize = const Size(320, 844);
      t.view.devicePixelRatio = 1;
      addTearDown(t.view.reset);
      const reference = 'a1b2c3d4e5f6';
      const id = 'a1b2c333-4444-5555-6666-987654d4e5f6';
      const request = {
        'id': id,
        'type': 'Enrollment',
        'reference': 'A1B2C3D4E5F6',
        'createdOn': '2026-11-13',
        'player': 'لاعب عرض اصطناعي',
        'plan': 'باقة شهرية',
        'sport': 'كرة القدم',
        'branch': 'فرع العرض',
        'amount': 720,
        'currency': 'EGP',
        'canOpen': true,
        'hasProof': true,
      };
      final queries = <Uri>[];
      var fail = true, approved = false;
      final auth = await owner((method, path, body, key) async {
        expect(method, 'GET');
        if (path == '/api/v1/manage/enrollment-requests/$id/payment') {
          approved = true;
          return {
            ...request,
            'child': request['player'],
            'status': 'Pending',
            'originalAmount': 800,
            'discountAmount': 80,
          };
        }
        final uri = Uri.parse(path);
        queries.add(uri);
        expect(uri.queryParameters['branchId'], 'branch');
        expect(uri.queryParameters['from'], '2026-11-01');
        expect(uri.queryParameters['status'], 'Pending');
        final term = uri.queryParameters['reference'];
        if (term == reference && fail) {
          fail = false;
          throw const ApiFailure(503, 'تعذر تحميل طلبات الدفع.');
        }
        final matches =
            term == null || term == id || term == reference && !approved;
        return {
          'pending': 7,
          'total': matches ? 1 : 0,
          'items': matches ? [request] : [],
        };
      });
      await t.pumpWidget(
        frame(
          app(
            NativePaymentRequests(
              auth: auth,
              filters: const {
                'branchId': 'branch',
                'from': '2026-11-01',
                'to': '2026-11-13',
              },
            ),
            scale: 2,
          ),
        ),
      );
      await t.pumpAndSettle();
      final field = find.byType(TextField);
      await t.enterText(field, '123');
      await t.tap(find.byTooltip('بحث'));
      await t.pumpAndSettle();
      expect(
        find.text('أدخل رقم الطلب كاملًا كما يظهر في تفاصيله.'),
        findsOneWidget,
      );
      expect(queries, hasLength(1));
      await t.enterText(field, ' $reference ');
      await t.testTextInput.receiveAction(TextInputAction.search);
      await t.pumpAndSettle();
      expect(find.text('تعذر تحميل طلبات الدفع.'), findsOneWidget);
      expect(
        find.byKey(const ValueKey('payment-request:Enrollment:$id')),
        findsNothing,
      );
      await t.tap(find.text('إعادة المحاولة'));
      await t.pumpAndSettle();
      expect(queries.last, queries[queries.length - 2]);
      expect(find.text('نتائج البحث (1)'), findsOneWidget);
      await capture(t, 'payment-reference-result-320');
      final link = find.text('عرض طلب الاشتراك');
      await t.scrollUntilVisible(
        link,
        150,
        scrollable: find.byType(Scrollable).last,
      );
      await Scrollable.ensureVisible(t.element(link), alignment: .5);
      await t.pumpAndSettle();
      expect(link.hitTestable(), findsOneWidget);
      await t.tap(link.hitTestable());
      await t.pumpAndSettle();
      expect(
        t
            .widget<NativeEnrollmentPaymentRead>(
              find.byType(NativeEnrollmentPaymentRead),
            )
            .id,
        id,
      );
      expect(find.textContaining('A1B2C3D4E5F6'), findsOneWidget);
      final amount = find.text('720 ج.م', findRichText: true);
      await t.scrollUntilVisible(
        amount,
        140,
        scrollable: find.byType(Scrollable).last,
      );
      expect(amount, findsOneWidget);
      await t.tap(find.byTooltip('رجوع'));
      await t.pumpAndSettle();
      expect(
        find.text('لا توجد طلبات معلقة بهذا الرقم ضمن النطاق المختار.'),
        findsOneWidget,
      );
      expect(t.widget<TextField>(field).controller!.text, reference);
      await t.enterText(field, id);
      await t.tap(find.byTooltip('بحث'));
      await t.pumpAndSettle();
      expect(queries.last.queryParameters['reference'], id);
      await t.tap(find.text('مسح البحث'));
      await t.pumpAndSettle();
      expect(queries.last.queryParameters.containsKey('reference'), isFalse);
      expect(find.text('طلبات معلّقة (7)'), findsOneWidget);
      expect(t.takeException(), isNull);
      await t.pumpWidget(const SizedBox());
      auth.dispose();
    },
  );
  testWidgets(
    'decision list uses only its explicit page controls at 320px and large text',
    (t) async {
      t.view.physicalSize = const Size(320, 812);
      t.view.devicePixelRatio = 1;
      addTearDown(t.view.reset);
      final pages = <String>[];
      final auth = await owner((method, path, body, key) async {
        expect(method, 'GET');
        final query = Uri.parse(path).queryParameters;
        expect(query['branchId'], 'b');
        if (query['kind'] == null) return {'expired': 51};
        expect(query['kind'], 'Expired');
        final page = query['page']!;
        pages.add(page);
        return {
          'total': 51,
          'items': [
            {
              'name': 'لاعب العرض في الصفحة $page',
              'branch': 'فرع العرض',
              'sport': 'السباحة',
              'playerId': 'p$page',
              'date': '2026-11-04',
            },
          ],
        };
      });
      await t.pumpWidget(
        frame(
          app(
            Scaffold(
              body: SingleChildScrollView(
                child: OwnerDecisionCenter(
                  auth: auth,
                  branchId: 'b',
                  pendingPayments: 0,
                  onPayments: () async {},
                ),
              ),
            ),
            scale: 2,
          ),
        ),
      );
      await t.pumpAndSettle();
      await capture(t, 'decision-center-320-large-text');
      final expired = find.text('لاعبون انتهى اشتراكهم');
      await t.ensureVisible(expired);
      expect(expired.hitTestable(), findsOneWidget);
      await t.tap(expired);
      await t.pumpAndSettle();
      await t.drag(find.byType(Scrollable).first, const Offset(0, -240));
      await t.pumpAndSettle();
      final next = find.text('التالي');
      await t.scrollUntilVisible(
        next,
        240,
        scrollable: find.byType(Scrollable).first,
      );
      await t.pumpAndSettle();
      expect(pages, ['1']);
      expect(
        t
            .widget<TextButton>(find.widgetWithText(TextButton, 'السابق'))
            .onPressed,
        isNull,
      );
      await capture(t, 'decision-list-320-large-text');
      await t.tap(next);
      await t.pumpAndSettle();
      expect(pages, ['1', '2']);
      expect(find.text('لاعب العرض في الصفحة 1'), findsNothing);
      expect(find.text('لاعب العرض في الصفحة 2'), findsOneWidget);
      final previous = find.text('السابق');
      await t.scrollUntilVisible(
        previous,
        240,
        scrollable: find.byType(Scrollable).first,
      );
      await t.pumpAndSettle();
      expect(
        t
            .widget<TextButton>(find.widgetWithText(TextButton, 'التالي'))
            .onPressed,
        isNull,
      );
      await capture(t, 'decision-list-second-320-large-text');
      await t.tap(previous);
      await t.pumpAndSettle();
      expect(pages, ['1', '2', '1']);
      expect(find.text('لاعب العرض في الصفحة 1'), findsOneWidget);
      expect(find.text('لاعب العرض في الصفحة 2'), findsNothing);
      expect(t.takeException(), isNull);
      await t.pumpWidget(const SizedBox());
      auth.dispose();
    },
  );

  testWidgets(
    'returning from a renewal review reads the current queue, with a true empty state',
    (t) async {
      t.view.physicalSize = const Size(375, 812);
      t.view.devicePixelRatio = 1;
      addTearDown(t.view.reset);
      var queueReads = 0;
      final auth = await owner((method, path, body, key) async {
        expect(method, 'GET');
        if (!path.startsWith('/api/v1/manage/payment-requests?')) {
          return <Object>[];
        }
        final query = Uri.parse(path).queryParameters;
        expect(query['branchId'], 'b');
        expect(query['status'], 'Pending');
        queueReads++;
        return {
          'pending': queueReads == 1 ? 1 : 0,
          'items': queueReads == 1
              ? [
                  {
                    'id': 'payment',
                    'type': 'Renewal',
                    'player': 'لاعب العرض',
                    'sport': 'السباحة',
                    'branch': 'فرع العرض',
                    'plan': 'باقة السباحة',
                    'hasProof': true,
                    'amount': 450,
                    'currency': 'EGP',
                  },
                ]
              : [],
        };
      });
      await t.pumpWidget(
        frame(
          shell(
            NativePaymentRequests(auth: auth, filters: const {'branchId': 'b'}),
          ),
        ),
      );
      await t.pumpAndSettle();
      await capture(t, 'payment-queue');
      await t.tap(find.text('مراجعة إثبات التجديد'));
      await t.pumpAndSettle();
      expect(find.byType(NativeRenewalProofReview), findsOneWidget);
      await t.tap(find.text('العودة لقائمة طلبات الدفع'));
      await t.pumpAndSettle();
      expect(queueReads, 2);
      await capture(t, 'payment-queue-empty');
      expect(find.text('لاعب العرض'), findsNothing);
      expect(
        find.text('لا توجد طلبات دفع معلقة ضمن النطاق المختار.'),
        findsOneWidget,
      );
      expect(t.takeException(), isNull);
      await t.pumpWidget(const SizedBox());
      auth.dispose();
    },
  );

  testWidgets(
    'payment queue distinguishes absent, zero and identical renewal rows at 320 and large text',
    (t) async {
      t.view.physicalSize = const Size(320, 812);
      t.view.devicePixelRatio = 1;
      addTearDown(t.view.reset);
      const common = {
        'player': 'لاعب العرض',
        'sport': 'السباحة',
        'branch': 'فرع العرض',
        'plan': 'اشتراك شهري',
        'hasProof': true,
      };
      const examples = [
        (
          'Enrollment',
          'unpriced',
          'C104A7BC0182',
          null,
          null,
          'المبلغ غير محدد',
        ),
        ('Enrollment', 'zero', 'D104A7BC0182', 0, 'EGP', '0 ج.م'),
        ('Tournament', 'fee', 'E104A7BC0182', 120, 'EGP', '120 ج.م'),
        (
          'Renewal',
          'first',
          'F8A12C09-1182-4300-AA00-000000000001',
          721.25,
          'EGP',
          '721.25 ج.م',
        ),
        (
          'Renewal',
          'second',
          'F8A12C09-1182-4300-AA00-000000000002',
          721.25,
          'EGP',
          '721.25 ج.م',
        ),
      ];
      final auth = await owner((method, path, body, key) async {
        expect(method, 'GET');
        if (!path.startsWith('/api/v1/manage/payment-requests?')) return {};
        return {
          'pending': examples.length,
          'items': [
            for (final (type, id, reference, amount, currency, _) in examples)
              {
                ...common,
                'type': type,
                'id': id,
                'reference': reference,
                'amount': amount,
                'currency': currency,
              },
          ],
        };
      });
      await t.pumpWidget(
        frame(app(NativePaymentRequests(auth: auth), scale: 2)),
      );
      await t.pumpAndSettle();
      expect(
        find.text('اعتماد الاشتراكات الجديدة ورسوم البطولات من لوحة التحكم.'),
        findsOneWidget,
      );
      expect(find.text('طلبات معلّقة (5)'), findsOneWidget);
      await capture(t, 'payment-queue-shared-web-guidance-320-large-text');
      final scroll = find.byType(Scrollable).first;
      for (final (type, id, reference, _, _, expected) in examples) {
        final card = find.byKey(ValueKey('payment-request:$type:$id'));
        await t.scrollUntilVisible(card, 160, scrollable: scroll);
        await t.pumpAndSettle();
        expect(
          find.descendant(of: card, matching: find.textContaining(reference)),
          findsOneWidget,
        );
        expect(
          find.descendant(of: card, matching: find.text(expected)),
          findsOneWidget,
        );
      }
      expect(
        find.text('مراجعة هذا النوع من الطلبات متاحة في لوحة التحكم.'),
        findsNothing,
      );
      await capture(t, 'payment-queue-references-320-large-text');
      final target = find.descendant(
        of: find.byKey(const ValueKey('payment-request:Renewal:second')),
        matching: find.text('مراجعة إثبات التجديد'),
      );
      await t.ensureVisible(target);
      expect(target.hitTestable(), findsOneWidget);
      await t.tap(target);
      await t.pumpAndSettle();
      expect(
        t
            .widget<NativeRenewalProofReview>(
              find.byType(NativeRenewalProofReview),
            )
            .id,
        'second',
      );
      expect(t.takeException(), isNull);
      await t.pumpWidget(const SizedBox());
      auth.dispose();
    },
  );

  testWidgets(
    'merge re-previews a conflict and retries an uncertain commit with the same key',
    (t) async {
      final writes = <(String?, Json)>[];
      var previews = 0;
      final auth = await owner((method, path, body, key) async {
        expect(method, 'POST');
        if (path.endsWith('/preview')) {
          previews++;
          return {
            'fingerprint': 'preview-$previews',
            'count': 2,
            'target': 'المجموعة الثانية',
            'conflicts': [],
          };
        }
        writes.add((key, obj(body)));
        if (writes.length == 1) {
          throw const ApiFailure(409, 'تغيرت القائمة؛ أعد المعاينة.');
        }
        if (writes.length == 2) throw const ApiFailure(0, 'انقطع الاتصال.');
        return {'id': 'confirmed-merge', 'replayed': true};
      });
      const groups = [
        {
          'groupId': 'g1',
          'name': 'المجموعة الأولى',
          'branch': 'فرع العرض',
          'branchId': 'b',
          'sportId': 's',
          'occupied': 2,
          'available': 0,
        },
        {
          'groupId': 'g2',
          'name': 'المجموعة الثانية',
          'branch': 'فرع العرض',
          'branchId': 'b',
          'sportId': 's',
          'occupied': 0,
          'available': 3,
        },
      ];
      await t.pumpWidget(
        shell(
          Builder(
            builder: (context) => Scaffold(
              body: TextButton(
                onPressed: () => openPage(
                  context,
                  OwnerGroupMerge(
                    auth: auth,
                    groups: groups,
                    today: '2026-11-06',
                  ),
                ),
                child: const Text('فتح الدمج'),
              ),
            ),
          ),
        ),
      );
      Future<void> tap(String text) async {
        final target = find.text(text).last;
        await t.ensureVisible(target);
        await t.tap(target);
        await t.pumpAndSettle();
      }

      await tap('فتح الدمج');
      await t.tap(find.byType(DropdownButtonFormField<String>).first);
      await t.pumpAndSettle();
      await tap('المجموعة الأولى · فرع العرض');
      await t.tap(find.byType(DropdownButtonFormField<String>).last);
      await t.pumpAndSettle();
      await tap('المجموعة الثانية · فرع العرض');
      await tap('معاينة الدمج');
      await tap('تأكيد دمج اللاعبين');
      expect(find.text('تغيرت القائمة؛ أعد المعاينة.'), findsOneWidget);
      await tap('معاينة الدمج');
      await tap('تأكيد دمج اللاعبين');
      expect(
        find.text('لم يتأكد حفظ الدمج. أعد محاولة الطلب نفسه.'),
        findsOneWidget,
      );
      await tap('إعادة محاولة طلب الدمج نفسه');
      expect(writes[1].$1, isNotEmpty);
      expect(writes[1].$1, writes[2].$1);
      expect(writes[1].$2, writes[2].$2);
      expect(writes[1].$2['fingerprint'], 'preview-2');
      expect(find.byType(OwnerGroupMerge), findsNothing);
      expect(t.takeException(), isNull);
      await t.pumpWidget(const SizedBox());
      auth.dispose();
    },
  );
}
