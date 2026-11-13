import 'dart:async';
import 'dart:io';

import 'package:academy_mobile/app/app.dart';
import 'package:academy_mobile/core/api/api_client.dart';
import 'package:academy_mobile/core/auth/auth_controller.dart';
import 'package:academy_mobile/core/theme/appearance.dart';
import 'package:academy_mobile/features/guardian/guardian_ui.dart';
import 'package:academy_mobile/features/owner/inventory_preview.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:integration_test/integration_test.dart';

import '../test/auth_controller_test.dart' show MemoryStore;

const _name = 'طقم اختبار رصيد المقاسات';
const _catalog = '/api/v1/content/catalog/';
const _sales = '/api/v1/manage/product-sales';
const _report =
    '/api/v1/reports/financial?allDates=true&collectionType=Product';
const _subscriptions =
    '/api/v1/reports/financial?allDates=true&collectionType=Subscription';

class _ReturningOwner extends Appearance {
  @override
  Future<bool> hasRoleIntro(String role, String sportTheme) async => true;
}

class _LiveTransport extends HttpApiTransport {
  _LiveTransport(this.io) : super(AppConfig('Demo', 'http://127.0.0.1:5500'));
  final Zone io;

  @override
  Future<Object?> send(
    String method,
    String path, {
    Object? body,
    String? token,
    String? idempotencyKey,
  }) {
    final result = Completer<Object?>();
    io.run(() async {
      try {
        result.complete(
          await super.send(
            method,
            path,
            body: body,
            token: token,
            idempotencyKey: idempotencyKey,
          ),
        );
      } catch (error, trace) {
        result.completeError(error, trace);
      }
    });
    return result.future;
  }
}

void main() {
  final binding = IntegrationTestWidgetsFlutterBinding.ensureInitialized();
  testWidgets('owner inventory updates after an external sale and remains scoped', (
    t,
  ) async {
    expect(const bool.fromEnvironment('QA_ISOLATED_OWNER_INVENTORY'), isTrue);
    final oldOverrides = HttpOverrides.current;
    addTearDown(() => HttpOverrides.global = oldOverrides);
    final setup = (await t.runAsync(() async {
      HttpOverrides.global = null;
      final api = _LiveTransport(Zone.current);
      addTearDown(api.close);
      Future<AuthController> login(String phone, String role) async {
        final auth = AuthController(api, MemoryStore())..restoring = false;
        addTearDown(auth.dispose);
        await auth.verify(phone, await auth.requestOtp(phone), '246810');
        expect(auth.session?.selected?.role, role);
        return auth;
      }

      final owner = await login('01000000010', 'AcademyOwner');
      final guardian = await login('01000000001', 'Guardian');
      expect(
        owner.session?.selected?.academyId,
        '10000000-0000-0000-0000-000000000001',
      );
      final catalog = rows(await owner.request('GET', '$_catalog?active=true'));
      final football = catalog.firstWhere(
        (item) => item['sport'] == 'كرة القدم',
      );
      final existing = catalog.where((item) => item['arabicName'] == _name);
      final product = existing.isEmpty
          ? obj(
              await owner.request(
                'POST',
                _catalog,
                body: {
                  'sportId': football['sportId'],
                  'arabicName': _name,
                  'imageReference': '/demo-assets/catalog-iacademy-kit.png',
                  'displayPrice': 100,
                  'currency': 'EGP',
                  'discountPercentage': 10,
                  'displayOrder': -50,
                  'isActive': true,
                  'stockQuantity': 7,
                },
              ),
            )
          : existing.single;
      final id = product['id'] as String;
      final inventoryPath = '$_catalog$id/inventory';
      final initial = obj(await owner.request('GET', inventoryPath));
      final prepared = obj(
        await owner.request(
          'PUT',
          inventoryPath,
          body: {
            'expectedRevision': initial['stockRevision'],
            'numberedUniform': true,
            'reconcileExistingStock': initial['usesVariants'] == false,
            'variants': [
              {'size': 'M', 'color': 'كحلي', 'shirtNumber': 7, 'quantity': 3},
              {'size': 'M', 'color': 'أبيض', 'shirtNumber': 7, 'quantity': 4},
            ],
          },
        ),
      );
      final blue = rows(prepared['variants'])
          .firstWhere((row) => row['color'] == 'كحلي');
      final branches = rows(
        obj(await owner.request('GET', '$_sales/options'))['branches'],
      );
      final branch = branches.first['id'];
      final quote = obj(
        await owner.request(
          'GET',
          '$_sales/quote?productId=$id&branchId=$branch&quantity=1&variantId=${blue['id']}',
        ),
      );
      expect(quote['amount'], '90.00');
      final before = obj(await owner.request('GET', _report));
      final subscriptions = obj(await owner.request('GET', _subscriptions));
      await expectLater(
        guardian.request('GET', inventoryPath),
        throwsA(isA<ApiFailure>().having((e) => e.status, 'status', 403)),
      );
      return (
        owner,
        id,
        branch,
        blue['id'],
        quote['amount'],
        before,
        subscriptions['totalAmount'],
      );
    }))!;
    final (owner, id, branch, variant, amount, before, subscriptionBefore) =
        setup;

    Future<void> waitFor(Finder finder) async {
      for (
        var attempt = 0;
        attempt < 240 && finder.evaluate().isEmpty;
        attempt++
      ) {
        await t.runAsync(
          () => Future<void>.delayed(const Duration(milliseconds: 35)),
        );
        await t.pump(const Duration(milliseconds: 45));
      }
      expect(finder, findsWidgets);
    }

    await t.pumpWidget(
      AcademyApp(
        auth: owner,
        environment: 'Demo',
        appearance: _ReturningOwner(),
        entryFrameReady: Future<void>.value(),
      ),
    );
    await t.pump(const Duration(seconds: 2));
    final vertical = find.byWidgetPredicate(
      (widget) =>
          widget is Scrollable && widget.axisDirection == AxisDirection.down,
    );
    await waitFor(vertical);
    final preview = find.byType(OwnerInventoryPreview);
    for (
      var attempt = 0;
      attempt < 30 && preview.evaluate().isEmpty;
      attempt++
    ) {
      await t.drag(vertical.first, const Offset(0, -420));
      await t.pump(const Duration(milliseconds: 320));
    }
    await waitFor(preview);
    final card = find.descendant(of: preview, matching: find.text(_name));
    await waitFor(card);
    await t.ensureVisible(card);
    await t.pump(const Duration(milliseconds: 400));
    await t.tap(card.hitTestable());
    await t.pump(const Duration(milliseconds: 700));
    await waitFor(find.text('إجمالي المتبقي 7'));
    await binding.takeScreenshot('phase-ownerinventory-before-sale');

    final sale = (await t.runAsync(() async {
      final result = obj(
        await owner.request(
          'POST',
          _sales,
          body: {
            'productId': id,
            'branchId': branch,
            'variantId': variant,
            'quantity': 1,
            'paymentMethod': 'Online',
            'expectedAmount': amount,
          },
          idempotencyKey: commandKey(),
        ),
      );
      expect(result['status'], 'Confirmed');
      final updated = obj(await owner.request('GET', '$_catalog$id/inventory'));
      expect(updated['stockQuantity'], 6);
      final quantities = {
        for (final row in rows(updated['variants']))
          row['color']: row['quantity'],
      };
      expect(quantities, {'كحلي': 2, 'أبيض': 4});
      final report = obj(await owner.request('GET', _report));
      final subscriptions = obj(await owner.request('GET', _subscriptions));
      num number(Object? value) => num.parse('$value');
      expect(number(report['totalAmount']) - number(before['totalAmount']), 90);
      expect(subscriptions['totalAmount'], subscriptionBefore);
      debugPrint(
        'QA inventory product=$id sale=${result['id']} stock=7->6 productIncome=+90',
      );
      return result;
    }))!;
    expect(find.text('إجمالي المتبقي 7'), findsOneWidget);
    await t.tap(find.byTooltip('تحديث المخزون'));
    await t.pump();
    await waitFor(find.text('إجمالي المتبقي 6'));
    expect(find.text('كحلي · مقاس M · رقم 7'), findsOneWidget);
    expect(find.text('أبيض · مقاس M · رقم 7'), findsOneWidget);
    await binding.takeScreenshot('phase-ownerinventory-after-sale');

    await t.binding.handlePopRoute();
    await t.pump(const Duration(milliseconds: 750));
    await waitFor(
      find.descendant(of: preview, matching: find.text('المتبقي 6')),
    );
    await t.ensureVisible(card);
    await t.pump(const Duration(milliseconds: 350));
    await binding.takeScreenshot('phase-ownerinventory-preview-after');
    binding.reportData = {
      ...?binding.reportData,
      'source': 'real-isolated-api',
      'stockBefore': 7,
      'stockAfter': 6,
      'productSaleId': sale['id'],
    };
  });
}
