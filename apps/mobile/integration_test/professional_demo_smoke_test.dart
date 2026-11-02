import 'package:academy_mobile/features/owner/owner_action_rail.dart';
import 'package:academy_mobile/features/shared/subscription_home.dart';

import 'dart:ui' as ui;

import 'package:academy_mobile/app/app.dart';
import 'package:academy_mobile/core/api/api_client.dart';
import 'package:academy_mobile/core/auth/auth_controller.dart';
import 'package:academy_mobile/core/storage/refresh_store.dart';
import 'package:academy_mobile/core/theme/appearance.dart';
import 'package:academy_mobile/core/theme/app_theme.dart';
import 'package:academy_mobile/features/owner/owner_welcome_banner.dart';
import 'package:academy_mobile/features/owner/inventory_preview.dart';
import 'package:academy_mobile/features/coach/coach_home.dart';
import 'package:academy_mobile/features/guardian/sport_store.dart';
import 'package:academy_mobile/features/guardian/guardian_ui.dart';
import 'package:academy_mobile/features/admin/native_product_orders.dart';
import 'package:academy_mobile/features/shared/transfer_payment.dart';
import 'package:flutter/material.dart';
import 'package:flutter_secure_storage/flutter_secure_storage.dart';
import 'package:flutter_localizations/flutter_localizations.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:integration_test/integration_test.dart';

class MemoryStore implements RefreshStore {
  String? token;
  @override
  Future<String?> read() async => token;
  @override
  Future<void> write(String value) async {
    token = value;
  }

  @override
  Future<void> clear() async {
    token = null;
  }
}

void main() {
  final binding = IntegrationTestWidgetsFlutterBinding.ensureInitialized();
  testWidgets(
    'Professional mobile: real owner, assigned coach, purchase, review and remaining stock',
    (t) async {
      const base = String.fromEnvironment('API_BASE_URL');
      expect(const bool.fromEnvironment('ISOLATED_PROFESSIONAL_DEMO'), true);
      expect(base, 'http://127.0.0.1:5396');
      t.platformDispatcher.accessibilityFeaturesTestValue =
          const FakeAccessibilityFeatures(disableAnimations: true);
      final config = AppConfig('Demo', base);
      Future<AuthController> login(String phone, {bool persist = false}) async {
        final auth = AuthController(
          HttpApiTransport(config),
          persist ? SecureRefreshStore(config.storageKey) : MemoryStore(),
        );
        await auth.restore();
        if (auth.session == null) {
          final challenge = await auth.requestOtp(phone);
          await auth.verify(phone, challenge, '246810');
        }
        expect(
          auth.session!.selected!.academyId,
          '30000000-0000-0000-0000-000000000003',
        );
        return auth;
      }

      Future<void> wait(Finder target) async {
        for (var i = 0; i < 300 && target.evaluate().isEmpty; i++) {
          await t.pump(const Duration(milliseconds: 100));
        }
        expect(target, findsWidgets);
        await t.pump(const Duration(milliseconds: 400));
      }

      Future<void> enterHome() async {
        for (var i = 0; i < 200; i++) {
          final next = find.byKey(const ValueKey('role-intro-next'));
          if (next.evaluate().isNotEmpty) {
            await t.tap(next);
            await t.pumpAndSettle();
          } else if (find.byType(OwnerWelcomeBanner).evaluate().isNotEmpty ||
              find.byType(CoachHome).evaluate().isNotEmpty) {
            return;
          } else {
            await t.pump(const Duration(milliseconds: 100));
          }
        }
      }

      Future<void> reach(Finder target) async {
        await wait(find.byType(Scrollable));
        await t.scrollUntilVisible(
          target,
          190,
          scrollable: find.byType(Scrollable).first,
          maxScrolls: 60,
        );
        await t.ensureVisible(target);
        await t.pumpAndSettle();
      }

      Future<void> capture(String suffix) async {
        for (var i = 0; i < 100; i++) {
          if (t
              .widgetList<RawImage>(find.byType(RawImage))
              .every((image) => image.image != null)) {
            break;
          }
          await t.pump(const Duration(milliseconds: 100));
        }
        expect(t.takeException(), isNull);
        await binding.takeScreenshot('phase-professional-$suffix');
      }

      final appearance = Appearance(storage: const FlutterSecureStorage());
      await appearance.update(
        visualModel: VisualModel.premium,
        completed: true,
      );
      for (final role in ['AcademyOwner', 'Coach', 'Guardian']) {
        for (final sport in ['football', 'swimming', 'general']) {
          await appearance.completeRoleIntro(role, sport);
        }
      }
      final owner = await login('01099900010', persist: true);
      const foreignPlayer = '7b7fad84-21ca-53a8-81ca-d183aa97d60e';
      await expectLater(
        owner.request('GET', '/api/v1/people/players/$foreignPlayer'),
        throwsA(isA<ApiFailure>().having((e) => e.status, 'status', 404)),
      );
      final structure = obj(
        await owner.request('GET', '/api/v1/manage/structure/options'),
      );
      expect(rows(structure['branches']).map((b) => b['arabicName']).toSet(), {
        'فرع الرحاب',
        'فرع مدينتي',
        'فرع الشروق',
      });
      expect(rows(structure['sports']), hasLength(3));
      await t.pumpWidget(
        AcademyApp(auth: owner, environment: 'Demo', appearance: appearance),
      );
      await enterHome();
      await wait(find.byType(OwnerWelcomeBanner));
      await wait(find.text('اشتراكات الأكاديمية'));
      await wait(find.byType(OwnerActionRail));
      expect(
        t.getTopLeft(find.byType(OwnerActionRail)).dy,
        lessThan(t.getTopLeft(find.byType(SubscriptionHomePanel)).dy),
      );
      await t.pump(const Duration(seconds: 2));
      await capture('owner-home-390');

      final coach = await login('01099900020');
      final groups = rows(await coach.request('GET', '/api/v1/coach/groups'));
      final options = obj(
        await coach.request('GET', '/api/v1/evaluations/options'),
      );
      expect(groups, isNotEmpty);
      expect(options['canManageAttendance'], false);
      expect(options['canEvaluate'], true);
      await t.pumpWidget(const SizedBox());
      await appearance.update(visualModel: VisualModel.pitch);
      await t.pumpWidget(
        AcademyApp(auth: coach, environment: 'Demo', appearance: appearance),
      );
      await enterHome();
      await wait(find.byType(CoachSessionCard));
      expect(find.text('فتح الحصة'), findsNothing);
      await capture('coach-home-390');
      await reach(find.text('عرض المجموعة ولاعبيها'));
      await t.tap(find.text('عرض المجموعة ولاعبيها'));
      await t.pumpAndSettle();
      await wait(find.byType(CoachGroup));
      expect(find.text('تسجيل الحضور'), findsNothing);
      await capture('coach-group-390');

      final guardian = await login('01099900100');
      await expectLater(
        guardian.request(
          'GET',
          '/api/v1/guardian/children/$foreignPlayer/card',
        ),
        throwsA(isA<ApiFailure>().having((e) => e.status, 'status', 404)),
      );
      final sections = rows(
        await guardian.request('GET', '/api/v1/guardian/catalog'),
      );
      final products = sections
          .expand(
            (section) =>
                rows(section['items'])
                    .map((item) => {...item, 'sportId': section['sportId']}),
          )
          .toList();
      final product = products.singleWhere(
        (p) => p['arabicName'] == 'طقم Professional — الأحمر',
      );
      final stock = product['stockQuantity'] as int;
      final oldOrders = rows(
        await guardian.request('GET', '/api/v1/guardian/product-sales'),
      ).map((r) => r['id']).toSet();
      Widget frame(Widget child) => MaterialApp(
        locale: const Locale('ar'),
        supportedLocales: const [Locale('ar')],
        localizationsDelegates: GlobalMaterialLocalizations.delegates,
        theme: AcademyTheme.light(),
        home: child,
      );
      await t.pumpWidget(const SizedBox());
      await t.pumpWidget(
        frame(ProductPurchase(auth: guardian, product: product)),
      );
      await wait(find.byType(DropdownButtonFormField<String>));
      final contexts = rows(
        await guardian.request(
          'GET',
          '/api/v1/guardian/subscriptions/enrollments',
        ),
      ).where((e) => e['sportId'] == product['sportId']).toList();
      if (contexts.length > 1) {
        await reach(find.byType(DropdownButtonFormField<String>).first);
        await t.tap(find.byType(DropdownButtonFormField<String>).first);
        await t.pumpAndSettle();
        await t.tap(
          find
              .text('${contexts.first['player']} · ${contexts.first['branch']}')
              .last,
        );
        await t.pumpAndSettle();
      }
      await reach(find.text('متابعة إلى الدفع'));
      await capture('family-product-review-390');
      await t.tap(find.text('متابعة إلى الدفع'));
      await t.pumpAndSettle();
      await wait(find.byType(TransferPayment));
      final order = rows(
        await guardian.request('GET', '/api/v1/guardian/product-sales'),
      ).singleWhere((r) => !oldOrders.contains(r['id']));
      expect(order['quantity'], 1);
      expect(order['status'], 'Pending');
      // Explicitly synthetic proof, drawn as a test fixture; no bank/customer data.
      final recorder = ui.PictureRecorder();
      final canvas = Canvas(recorder);
      canvas.drawColor(Colors.white, BlendMode.src);
      final painter = TextPainter(
        text: TextSpan(
          text:
              'إثبات تجريبي — ليس تحويلًا بنكيًا\nطلب منتج: ${order['amount']} جنيه\nلا توجد أموال حقيقية',
          style: const TextStyle(
            color: Colors.black,
            fontSize: 34,
            fontFamily: 'Cairo',
          ),
        ),
        textDirection: TextDirection.rtl,
      )..layout(maxWidth: 640);
      painter.paint(canvas, const Offset(35, 60));
      final image = await recorder.endRecording().toImage(710, 360);
      final proof = await image.toByteData(format: ui.ImageByteFormat.png);
      final asset = obj(
        await guardian.upload(proof!.buffer.asUint8List(), 'image/png', {
          'purpose': 'Proof',
        }),
      );
      await guardian.request(
        'POST',
        '/api/v1/guardian/product-sales/${order['id']}/proof',
        body: {'assetId': asset['id']},
      );
      image.dispose();
      painter.dispose();
      final before = obj(
        await owner.request(
          'GET',
          '/api/v1/reports/financial?allDates=true&collectionType=Product',
        ),
      );
      await t.pumpWidget(const SizedBox());
      await t.pumpWidget(
        frame(NativeProductOrderReview(auth: owner, id: textOf(order['id']))),
      );
      await wait(find.text('قبول وخصم المخزون'));
      await reach(find.text('قبول وخصم المخزون'));
      await capture('owner-product-proof-390');
      await t.tap(find.text('قبول وخصم المخزون'));
      await t.pumpAndSettle();
      await t.tap(find.text('تأكيد المراجعة'));
      await t.pumpAndSettle();
      for (var i = 0; i < 80; i++) {
        final saved = obj(
          await owner.request(
            'GET',
            '/api/v1/manage/product-sales/${order['id']}',
          ),
        );
        if (saved['status'] == 'Confirmed') break;
        await t.pump(const Duration(milliseconds: 150));
      }
      final stored = obj(
        await owner.request('GET', '/api/v1/content/catalog/${product['id']}'),
      );
      expect(stored['stockQuantity'], stock - 1);
      final key = 'professional-review-${order['id']}';
      await owner.request(
        'POST',
        '/api/v1/manage/product-sales/${order['id']}/review',
        body: {'accept': true, 'reason': null},
        idempotencyKey: key,
      );
      expect(
        obj(
          await owner.request(
            'GET',
            '/api/v1/content/catalog/${product['id']}',
          ),
        )['stockQuantity'],
        stock - 1,
      );
      final after = obj(
        await owner.request(
          'GET',
          '/api/v1/reports/financial?allDates=true&collectionType=Product',
        ),
      );
      double amount(Object? value) => double.parse('$value');
      expect(
        amount(after['totalAmount']) - amount(before['totalAmount']),
        amount(order['amount']),
      );
      expect(
        obj(
          await guardian.request(
            'GET',
            '/api/v1/guardian/product-sales/${order['id']}',
          ),
        )['status'],
        'Confirmed',
      );
      await t.pumpWidget(const SizedBox());
      await t.pumpWidget(
        frame(
          GuardianPage(
            title: 'مخزون Professional Academy',
            child: OwnerInventoryPreview(auth: owner),
          ),
        ),
      );
      await wait(find.text('المتبقي ${stock - 1}'));
      await capture('owner-stock-after-sale-390');
      await t.pumpWidget(const SizedBox());
      await appearance.update(visualModel: VisualModel.premium);
      await t.pumpWidget(
        AcademyApp(auth: owner, environment: 'Demo', appearance: appearance),
      );
      await enterHome();
      await wait(find.byType(OwnerWelcomeBanner));
      await capture('owner-ready-for-video-390');
      binding.reportData = {
        ...?binding.reportData,
        'source': 'real-isolated-api',
        'players': 300,
        'sports': 3,
        'stockBefore': stock,
        'stockAfter': stock - 1,
        'productSaleId': order['id'],
      };
    },
  );
}
