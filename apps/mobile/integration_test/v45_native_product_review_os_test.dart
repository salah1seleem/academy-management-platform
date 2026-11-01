import 'package:academy_mobile/app/app.dart';
import 'package:academy_mobile/core/api/api_client.dart';
import 'package:academy_mobile/core/auth/auth_controller.dart';
import 'package:academy_mobile/core/storage/refresh_store.dart';
import 'package:academy_mobile/core/theme/appearance.dart';
import 'package:academy_mobile/features/admin/native_product_orders.dart';
import 'package:academy_mobile/features/guardian/guardian_home.dart';
import 'package:academy_mobile/features/guardian/guardian_ui.dart';
import 'package:academy_mobile/features/guardian/sport_store.dart';
import 'package:academy_mobile/features/shared/notification_inbox.dart';
import 'package:academy_mobile/features/shared/private_media.dart';
import 'package:academy_mobile/features/shared/role_navigation.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:integration_test/integration_test.dart';

class ProductReviewAcceptanceTransport extends HttpApiTransport {
  ProductReviewAcceptanceTransport(super.config);
  Json? proofBody, inventory;
  final reviews = <Json>[];
  var purchases = 0;
  @override
  Future<Object?> send(
    String method,
    String path, {
    Object? body,
    String? token,
    String? idempotencyKey,
  }) async {
    final result = await super.send(
      method,
      path,
      body: body,
      token: token,
      idempotencyKey: idempotencyKey,
    );
    if (method == 'POST' &&
        path.endsWith('/proof') &&
        path.contains('/product-sales/')) {
      proofBody = obj(body);
    }
    if (method == 'POST' &&
        path.endsWith('/review') &&
        path.contains('/product-sales/')) {
      reviews.add(Map<String, dynamic>.from(obj(body)));
    }
    if (method == 'POST' && path == '/api/v1/guardian/product-sales') {
      purchases++;
    }
    if (method == 'GET' &&
        path.startsWith('/api/v1/guardian/catalog/') &&
        path.contains('/variants')) {
      inventory = obj(result);
    }
    return result;
  }
}

void main() {
  final binding = IntegrationTestWidgetsFlutterBinding.ensureInitialized();
  testWidgets(
    'actual OS proof, exact owner notice, scoped admin approval and guardian result preserve one numbered sale without a second purchase',
    (t) async {
      expect(const bool.fromEnvironment('ISOLATED_DEMO'), true);
      const base = String.fromEnvironment('API_BASE_URL');
      expect(base, 'http://127.0.0.1:5278');
      expect((t.view.physicalSize / t.view.devicePixelRatio).width, 375);
      const saleId = '53edec50-6ba6-4d60-8c67-af755bb52b4a';
      const productId = 'bf770426-3388-837b-77fd-a14f723e887a';
      const note = 'قبول منتجM7اصطناعي للعرض — لا تحويل حقيقي';
      final transport = ProductReviewAcceptanceTransport(
        AppConfig('Demo', base),
      );
      Future<AuthController> login(String phone, String key) async {
        final auth = AuthController(transport, SecureRefreshStore(key));
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

      final owner = await login(
        '01099900010',
        'v45.5278.os-media-entry.01099900010',
      );
      final guardian = await login(
        '01099900100',
        'v45.5278.guardian-workspace.01099900100',
      );
      expect(owner.session!.selected!.role, 'AcademyOwner');
      expect(guardian.session!.selected!.role, 'Guardian');
      final path = '/api/v1/manage/product-sales/$saleId';
      final original = obj(await owner.request('GET', path));
      expect(original['id'], saleId);
      expect(original['productId'], productId);
      expect(
        original['status'],
        'Pending',
        reason: 'Do not approve a saved confirmed sale again; resume read only by its ID.',
      );
      expect(original['amount'], 450);
      expect(original['sizeSnapshot'], 'M');
      expect(original['shirtNumberSnapshot'], 7);
      expect(original['quantity'], 1);
      // Prove the current API exposes the new bounded native list before creating any fixture account.
      final list = obj(
        await owner.request(
          'GET',
          '/api/v1/manage/product-sales?status=Pending&pageSize=100',
        ),
      );
      expect(rows(list['items']).any((r) => r['id'] == saleId), true);
      final beforeMoney = obj(
        await owner.request(
          'GET',
          '/api/v1/reports/financial?allDates=true&pageSize=1',
        ),
      );
      expect(beforeMoney['totalCount'], 71);
      expect(beforeMoney['totalAmount'], 60285);
      expect(beforeMoney['totalRefunds'], 1);
      final beforeStock = obj(
        await owner.request(
          'GET',
          '/api/v1/content/catalog/$productId/inventory',
        ),
      );
      final m7 = rows(beforeStock['variants'])
          .singleWhere((v) => v['size'] == 'M' && v['shirtNumber'] == 7);
      final m8 = rows(beforeStock['variants'])
          .singleWhere((v) => v['size'] == 'M' && v['shirtNumber'] == 8);
      expect(m7['quantity'], 1);
      expect(m8['quantity'], 2);
      expect(beforeStock['stockQuantity'], 5);
      final beforeSubscriptions = await guardian.request(
        'GET',
        '/api/v1/guardian/subscriptions/enrollments',
      );
      final catalogue = rows(
        await guardian.request('GET', '/api/v1/guardian/catalog'),
      );
      final sectionIndex = catalogue.indexWhere(
        (s) => rows(s['items']).any((p) => p['id'] == productId),
      );
      expect(sectionIndex, greaterThanOrEqualTo(0));
      final product = rows(catalogue[sectionIndex]['items'])
          .singleWhere((p) => p['id'] == productId);
      const phone = '01099900997', name = 'إداري طلبات اصطناعي997';
      var account =
          rows(
                obj(
                  await owner.request('GET', '/api/v1/manage/staff'),
                )['members'],
              )
              .where(
                (m) =>
                    m['phoneNumber'] == '+201099900997' ||
                    m['phoneNumber'] == phone,
              )
              .firstOrNull;
      if (account == null) {
        account = obj(
          await owner.request(
            'POST',
            '/api/v1/manage/staff',
            body: {
              'name': name,
              'phone': phone,
              'email': 'product.admin.997@example.test',
              'temporaryPassword': 'Synthetic-Product-Account-2026!',
              'role': 'AcademyAdmin',
              'jobTitle': 'طلبات منتجات اصطناعية',
            },
          ),
        );
        final accessPath = '/api/v1/manage/staff/${account['id']}/access';
        final choices = obj(await owner.request('GET', accessPath));
        await owner.request(
          'PUT',
          accessPath,
          body: {
            'allBranches': false,
            'branches': [original['branchId']],
            'allSports': false,
            'sports': [catalogue[sectionIndex]['sportId']],
            'actions': [
              for (final module in (choices['modules'] as List).cast<String>())
                {
                  'module': module,
                  'read': ['catalog', 'finance', 'payments'].contains(module),
                  'add': module == 'finance',
                  'edit': module == 'catalog',
                  'delete': false,
                },
            ],
            'sensitivePermissions': <String>[],
            'workflowActions': ['workflow.payments.read'],
          },
        );
      }
      final access = obj(
        await owner.request(
          'GET',
          '/api/v1/manage/staff/${account['id']}/access',
        ),
      );
      expect(access['selectedBranches'], [original['branchId']]);
      expect(access['selectedSports'], [catalogue[sectionIndex]['sportId']]);
      expect(access['allBranches'], false);
      expect(access['allSports'], false);
      final admin = await login(phone, 'v45.5278.product-review.admin997');
      expect(admin.session!.selected!.role, 'AcademyAdmin');
      expect(admin.session!.selected!.id, account['id']);
      final denied = await login(
        '01099900993',
        'v45.5278.enrollment-no-discount.01099900993',
      );
      await expectLater(
        denied.request('GET', path),
        throwsA(isA<ApiFailure>().having((e) => e.status, 'status', 403)),
      );
      final settings = obj(
        await owner.request('GET', '/api/v1/payment-settings'),
      );
      expect(settings['configured'], false);
      const address = 'synthetic-product-acceptance@example.test';
      await owner.request(
        'PUT',
        '/api/v1/payment-settings',
        body: {
          'instaPayAddress': address,
          'walletNumber': null,
          'recipientName': 'اختبار منتج اصطناعي — لا تحول أموالًا',
          'isActive': true,
        },
      );
      Future<void> wait(Finder f, {int attempts = 240}) async {
        for (var i = 0; i < attempts && f.evaluate().isEmpty; i++) {
          await t.pump(const Duration(milliseconds: 150));
        }
        expect(f, findsWidgets);
        await t.pumpAndSettle();
      }

      Future<void> reach(Finder f) async {
        final scroll = find.byType(Scrollable).first;
        t.state<ScrollableState>(scroll).position.jumpTo(0);
        await t.pumpAndSettle();
        await t.scrollUntilVisible(f, 150, scrollable: scroll, maxScrolls: 120);
        await t.ensureVisible(f.first);
        await t.pumpAndSettle();
      }

      Future<void> tap(Finder f) async {
        await reach(f);
        await t.tap(f.first);
        await t.pump(const Duration(milliseconds: 400));
      }

      Future<void> capture(String name) async {
        await t.pumpAndSettle();
        await t.runAsync(
          () => Future<void>.delayed(const Duration(milliseconds: 500)),
        );
        await binding.takeScreenshot(name);
      }

      Future<void> show(AuthController auth) async {
        await t.pumpWidget(const SizedBox.shrink());
        await t.pumpAndSettle();
        final appearance = Appearance();
        for (final theme in ['football', 'general']) {
          await appearance.completeRoleIntro(
            auth.session!.selected!.role,
            theme,
          );
        }
        await t.pumpWidget(
          AcademyApp(auth: auth, environment: 'Demo', appearance: appearance),
        );
        await wait(find.byType(NativeRoleTabs));
      }

      Future<void> store() async {
        await show(guardian);
        await wait(find.byType(GuardianHome));
        await wait(find.text('أبنائي'));
        await tap(find.text('المتجر'));
        await wait(find.byType(CatalogSections));
        await wait(find.text('طلباتي من المنتجات الرياضية'));
      }

      Future<void> openNotice(
        AuthController auth,
        Json notice,
        int page,
        String action,
      ) async {
        await show(auth);
        await t.tap(find.byTooltip('التنبيهات'));
        await t.pump(const Duration(milliseconds: 400));
        await wait(find.byType(NotificationInbox));
        await wait(find.byType(Scrollable));
        for (var current = 1; current < page; current++) {
          await tap(find.text('التالي'));
          await wait(find.byType(Scrollable));
        }
        final text = find.text(textOf(notice['message']));
        final card = find.ancestor(of: text, matching: find.byType(GCard));
        await reach(card);
        if (notice['isRead'] != true) {
          await t.tap(
            find.descendant(of: card, matching: find.text('تمت القراءة')).first,
          );
          await t.pump(const Duration(milliseconds: 500));
          await wait(
            find.descendant(of: card, matching: find.text('تحديد كغير مقروء')),
          );
        }
        await capture(
          'phase-approved-product-${auth.session!.selected!.role == 'Guardian' ? 'guardian' : 'owner'}-exact-notice-w375',
        );
        await t.tap(
          find.descendant(of: card, matching: find.text(action)).first,
        );
        await t.pump(const Duration(milliseconds: 400));
      }

      Future<({Json notice, int page})> notice(
        AuthController auth,
        String kind,
      ) async {
        for (var page = 1; page <= 20; page++) {
          final feed = obj(
            await auth.request('GET', '/api/v1/notifications?page=$page'),
          );
          final found = rows(feed['items'])
              .where((n) => n['targetKind'] == kind && n['targetId'] == saleId)
              .toList();
          if (found.isNotEmpty) {
            expect(found, hasLength(1));
            expect(textOf(found.single['message']), isNot(contains('{')));
            return (notice: found.single, page: page);
          }
          if (page * 25 >= (feed['total'] as num)) break;
        }
        fail('Saved product notice must reach its intended recipient.');
      }

      try {
        await store();
        await tap(find.text('طلباتي من المنتجات الرياضية'));
        await wait(find.byType(GuardianProductOrders));
        final card = find.ancestor(
          of: find.text(textOf(original['productName'])),
          matching: find.byType(GCard),
        );
        await reach(card);
        await t.tap(
          find.descendant(of: card, matching: find.text('عرض الطلب والدفع')),
        );
        await t.pump(const Duration(milliseconds: 400));
        await wait(find.byType(GuardianProductOrder));
        await wait(find.text('رقم القميص'));
        expect(
          t.widget<GuardianProductOrder>(find.byType(GuardianProductOrder)).id,
          saleId,
        );
        if (original['proofUrl'] == null) {
          await reach(find.text('رفع إيصال الدفع'));
          await t.tap(find.text('رفع إيصال الدفع'));
          await t.pump(const Duration(milliseconds: 500));
          debugPrint(
            'Choose I-Academy-Photo-Test.png in the real iOS picker for this synthetic product proof. No child or real bank data.',
          );
          for (var i = 0; i < 1600 && transport.proofBody == null; i++) {
            await t.pump(const Duration(milliseconds: 150));
          }
          expect(transport.proofBody, isNotNull);
          await wait(
            find.text(
              'وصل إثبات الدفع. تنتظر العملية مراجعة الإدارة؛ لا تحتاج إلى الدفع مرة أخرى.',
            ),
          );
        }
        final withProof = obj(await owner.request('GET', path));
        expect(withProof['status'], 'Pending');
        expect(isPrivateAsset(withProof['proofUrl']), true);
        expect(
          (await guardian.asset(textOf(withProof['proofUrl']))).isNotEmpty,
          true,
        );
        await capture('phase-approved-product-os-proof-submitted-w375');
        final ownerNotice = await notice(owner, 'ProductReview');
        await openNotice(
          owner,
          ownerNotice.notice,
          ownerNotice.page,
          'مراجعة طلب المنتج',
        );
        await wait(find.byType(NativeProductOrderReview));
        expect(
          t
              .widget<NativeProductOrderReview>(
                find.byType(NativeProductOrderReview),
              )
              .id,
          saleId,
        );
        await wait(find.byType(PrivateImage));
        await wait(
          find.descendant(
            of: find.byType(PrivateImage),
            matching: find.byType(Image),
          ),
        );
        await reach(find.byType(PrivateImage));
        await capture('phase-approved-owner-product-proof-review-w375');
        await show(admin);
        await t.tap(
          find.descendant(
            of: find.byType(NavigationBar),
            matching: find.text('المزيد'),
          ),
        );
        await t.pump(const Duration(milliseconds: 400));
        await wait(find.byType(NativeMore));
        await tap(find.text('طلبات المنتجات'));
        await wait(find.byType(NativeProductOrders));
        await wait(find.text(textOf(original['productName'])));
        await tap(find.text('عرض الطلب ومراجعته'));
        await wait(find.byType(NativeProductOrderReview));
        expect(
          t
              .widget<NativeProductOrderReview>(
                find.byType(NativeProductOrderReview),
              )
              .id,
          saleId,
        );
        await wait(find.byType(PrivateImage));
        await wait(
          find.descendant(
            of: find.byType(PrivateImage),
            matching: find.byType(Image),
          ),
        );
        await reach(find.widgetWithText(TextFormField, 'ملاحظة لولي الأمر'));
        await t.enterText(
          find.widgetWithText(TextFormField, 'ملاحظة لولي الأمر'),
          note,
        );
        await reach(find.text('قبول وخصم المخزون'));
        await capture('phase-approved-admin-product-approval-review-w375');
        await t.tap(find.text('قبول وخصم المخزون'));
        await t.pumpAndSettle();
        await t.tap(find.text('تأكيد المراجعة'));
        await t.pump(const Duration(milliseconds: 600));
        for (var i = 0; i < 240 && transport.reviews.isEmpty; i++) {
          await t.pump(const Duration(milliseconds: 150));
        }
        expect(transport.reviews, hasLength(1));
        await t.pumpAndSettle();
        await reach(find.text('حُفظت المراجعة وأُعيدت قراءة الطلب نفسه.'));
        await capture('phase-approved-admin-product-approval-saved-w375');
        final saved = obj(await owner.request('GET', path));
        expect(saved['status'], 'Confirmed');
        expect(saved['reviewReason'], note);
        for (final key in [
          'productId',
          'playerId',
          'sportEnrollmentId',
          'variantId',
          'sizeSnapshot',
          'shirtNumberSnapshot',
          'quantity',
          'unitPrice',
          'amount',
          'currency',
        ]) {
          expect(saved[key], original[key]);
        }
        final replay = obj(
          await admin.request(
            'POST',
            '$path/review',
            body: transport.reviews.single,
          ),
        );
        expect(replay['replay'], true);
        final afterStock = obj(
          await owner.request(
            'GET',
            '/api/v1/content/catalog/$productId/inventory',
          ),
        );
        expect(afterStock['stockQuantity'], 4);
        expect(
          afterStock['stockRevision'],
          (beforeStock['stockRevision'] as num) + 1,
        );
        for (final v in rows(afterStock['variants'])) {
          expect(
            v['quantity'],
            v['id'] == m7['id']
                ? 0
                : rows(beforeStock['variants'])
                      .singleWhere((b) => b['id'] == v['id'])['quantity'],
          );
        }
        final familyNotice = await notice(guardian, 'Product');
        expect(familyNotice.notice['isRead'], false);
        await openNotice(
          guardian,
          familyNotice.notice,
          familyNotice.page,
          'عرض طلب المنتج',
        );
        await wait(find.byType(GuardianProductOrder));
        expect(
          t.widget<GuardianProductOrder>(find.byType(GuardianProductOrder)).id,
          saleId,
        );
        await wait(find.text('رقم القميص'));
        expect(find.text('M'), findsOneWidget);
        expect(find.text('7'), findsOneWidget);
        expect(find.text('رفع إيصال الدفع'), findsNothing);
        await capture(
          'phase-approved-guardian-product-confirmed-same-order-w375',
        );
        // Read-only: unavailable assigned number 7 must not silently become available number 8.
        await store();
        final productName = find.text(textOf(product['arabicName']));
        final horizontal = find
            .descendant(
              of: find.byType(CatalogSections),
              matching: find.byType(Scrollable),
            )
            .at(sectionIndex);
        await t.scrollUntilVisible(
          productName,
          140,
          scrollable: horizontal,
          maxScrolls: 30,
        );
        await t.ensureVisible(productName);
        await t.pumpAndSettle();
        final productCard = find.ancestor(
          of: productName,
          matching: find.byType(GCard),
        );
        await t.tap(
          find.descendant(of: productCard, matching: find.text('شراء')),
        );
        await t.pump(const Duration(milliseconds: 400));
        await wait(find.byType(ProductPurchase));
        for (var i = 0; i < 240 && transport.inventory == null; i++) {
          await t.pump(const Duration(milliseconds: 150));
        }
        expect(transport.inventory!['assignedShirtNumber'], 7);
        await reach(
          find.widgetWithText(DropdownButtonFormField<String>, 'المقاس'),
        );
        await t.tap(
          find.widgetWithText(DropdownButtonFormField<String>, 'المقاس'),
        );
        await t.pumpAndSettle();
        await t.tap(find.text('M').last);
        await t.pumpAndSettle();
        await reach(
          find.text(
            'رقم الطفل غير متاح بهذا المقاس. اختر رقمًا متاحًا وراجعه قبل المتابعة.',
          ),
        );
        final number = find.widgetWithText(
          DropdownButtonFormField<String>,
          'رقم القميص المتاح',
        );
        expect(
          t.widget<DropdownButtonFormField<String>>(number).initialValue,
          isNull,
        );
        await t.tap(number);
        await t.pumpAndSettle();
        await t.tap(find.text('8 · المتاح 2').last);
        await t.pumpAndSettle();
        await reach(find.text('أؤكد رقم القميص المختار: 8'));
        expect(
          t
              .widget<CheckboxListTile>(
                find.widgetWithText(
                  CheckboxListTile,
                  'أؤكد رقم القميص المختار: 8',
                ),
              )
              .value,
          false,
        );
        await t.tap(find.text('أؤكد رقم القميص المختار: 8'));
        await t.pumpAndSettle();
        await capture(
          'phase-approved-guardian-unavailable-number-explicit-eight-w375',
        );
        expect(transport.purchases, 0);
        expect(
          await guardian.request(
            'GET',
            '/api/v1/guardian/subscriptions/enrollments',
          ),
          beforeSubscriptions,
        );
        final afterMoney = obj(
          await owner.request(
            'GET',
            '/api/v1/reports/financial?allDates=true&pageSize=1',
          ),
        );
        expect(afterMoney['totalCount'], 72);
        expect(afterMoney['totalAmount'], 60735);
        expect(afterMoney['totalRefunds'], 1);
        expect(afterMoney['netAmount'], 60734);
        expect(
          rows(afterMoney['sourceSummary'])
              .singleWhere((s) => s['key'] == 'Product')['amount'],
          450,
        );
        expect(t.takeException(), isNull);
        debugPrint(
          'Saved synthetic product sale $saleId once: M7 stock 1 to 0; total stock 5 to 4; canonical revenue +450, no new subscription or second purchase.',
        );
      } finally {
        final current = obj(
          await owner.request('GET', '/api/v1/payment-settings'),
        );
        if (current['instaPayAddress'] == address) {
          await owner.request(
            'PUT',
            '/api/v1/payment-settings',
            body: {
              'instaPayAddress': settings['instaPayAddress'],
              'walletNumber': settings['walletNumber'],
              'recipientName': settings['recipientName'],
              'isActive': settings['isActive'],
            },
          );
          expect(
            obj(await owner.request('GET', '/api/v1/payment-settings')),
            settings,
          );
        }
        await t.pumpWidget(const SizedBox.shrink());
        for (final auth in [owner, guardian, admin, denied]) {
          auth.dispose();
        }
      }
    },
  );
}
