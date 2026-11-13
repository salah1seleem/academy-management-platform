import 'dart:async';

import 'package:academy_mobile/core/api/api_client.dart';
import 'package:academy_mobile/core/auth/auth_controller.dart';
import 'package:academy_mobile/core/theme/app_theme.dart';
import 'package:academy_mobile/features/guardian/guardian_payments.dart';
import 'package:academy_mobile/features/shared/transfer_payment.dart';
import 'package:flutter/material.dart';
import 'package:flutter_localizations/flutter_localizations.dart';
import 'package:flutter_test/flutter_test.dart';

import 'auth_controller_test.dart' show FakeApi;
import 'v2_role_workspaces_test.dart' show authenticate;

const paymentPath = '/api/v1/guardian/subscriptions/payments';
const succeed = 'محاكاة دفع ناجح';
const checkStatus = 'التحقق من حالة الدفع';

Map<String, Object?> payment(String id) => {
  'id': id,
  'player': 'لاعب العرض',
  'sport': 'كرة القدم',
  'plan': 'اشتراك شهري',
  'amount': 775,
  'finalAmount': 775,
  'originalAmount': 900,
  'discountAmount': 125,
  'currency': 'EGP',
  'status': 'Pending',
  'isCurrent': true,
  'provider': 'InternalTest',
  'simulationAvailable': true,
};

Widget checkout(
  AuthController auth, {
  String id = 'current',
  String environment = 'Demo',
}) => CheckoutPage(auth: auth, environment: environment, id: id);

Widget frame(Widget page, {bool dark = false, double scale = 1}) => MaterialApp(
  locale: const Locale('ar'),
  supportedLocales: const [Locale('ar')],
  localizationsDelegates: GlobalMaterialLocalizations.delegates,
  theme: dark ? AcademyTheme.dark() : AcademyTheme.light(),
  builder: (context, child) => MediaQuery(
    data: MediaQuery.of(
      context,
    ).copyWith(textScaler: TextScaler.linear(scale), disableAnimations: true),
    child: child!,
  ),
  home: page,
);

Future<void> press(WidgetTester tester, String label) async {
  final control = find.text(label);
  if (control.evaluate().isEmpty) {
    await tester.scrollUntilVisible(control, 180);
  } else {
    await tester.ensureVisible(control);
  }
  await tester.pumpAndSettle();
  if (control.hitTestable().evaluate().isEmpty) {
    await tester.drag(find.byType(Scrollable).first, const Offset(0, -140));
    await tester.pumpAndSettle();
  }
  expect(control.hitTestable(), findsOneWidget);
  await tester.tap(control);
  await tester.pumpAndSettle();
}

void main() {
  testWidgets(
    'An old payment shows its own amount and leaves no stale screen in Back',
    (tester) async {
      final paths = <String>[];
      final auth = await authenticate('Guardian', (_, path, _, _) async {
        paths.add(path);
        if (path.endsWith('/old')) {
          return {
            ...payment('old'),
            'amount': 720,
            'status': 'Cancelled',
            'isCurrent': false,
            'currentPaymentId': 'current',
            'discountReason': 'سبب البديل',
          };
        }
        return payment('current');
      });
      await tester.pumpWidget(
        frame(
          Builder(
            builder: (context) => Scaffold(
              body: TextButton(
                onPressed: () => Navigator.of(context).push(
                  MaterialPageRoute<void>(
                    builder: (_) => checkout(auth, id: 'old'),
                  ),
                ),
                child: const Text('فتح الإشعار'),
              ),
            ),
          ),
        ),
      );
      await press(tester, 'فتح الإشعار');
      expect(find.text('استُبدل طلب الدفع'), findsOneWidget);
      expect(find.text('720 ج.م', findRichText: true), findsOneWidget);
      expect(find.text('775 ج.م', findRichText: true), findsNothing);
      expect(find.text('سبب البديل'), findsNothing);
      expect(find.text(succeed), findsNothing);
      expect(find.text('إعادة محاولة الدفع'), findsNothing);
      await press(tester, 'فتح طلب الدفع الحالي');
      expect(find.text('775 ج.م', findRichText: true), findsOneWidget);
      expect(find.text(succeed), findsOneWidget);
      await tester.binding.handlePopRoute();
      await tester.pumpAndSettle();
      expect(find.text('فتح الإشعار'), findsOneWidget);
      expect(paths, ['$paymentPath/old', '$paymentPath/current']);
      await tester.pumpWidget(const SizedBox());
      auth.dispose();
    },
  );

  for (final (provider, demo, allowed, simulation, transfer) in [
    ('InternalTest', true, true, true, false),
    ('InternalTest', true, false, false, false),
    ('InternalTest', false, true, false, false),
    ('Manual', true, true, false, true),
    ('UnavailableProvider', true, true, false, false),
  ]) {
    testWidgets('Payment method $provider demo=$demo allowed=$allowed', (
      tester,
    ) async {
      var settingsReads = 0;
      final auth = await authenticate('Guardian', (_, path, _, _) async {
        if (path.endsWith('/payment-settings')) {
          settingsReads++;
          return {
            'configured': true,
            'recipientName': 'أكاديمية العرض',
            'instaPayAddress': 'qa@example.test',
          };
        }
        return {
          ...payment('current'),
          'provider': provider,
          'simulationAvailable': allowed,
        };
      });
      await tester.pumpWidget(
        frame(checkout(auth, environment: demo ? 'Demo' : 'Production')),
      );
      await tester.pumpAndSettle();
      expect(find.text(succeed), simulation ? findsOneWidget : findsNothing);
      expect(
        find.byType(TransferPayment),
        transfer ? findsOneWidget : findsNothing,
      );
      expect(settingsReads, transfer ? 1 : 0);
      if (transfer) expect(find.text('رفع إيصال الدفع'), findsOneWidget);
      if (!transfer && !simulation) {
        expect(
          find.text('طريقة الدفع غير متاحة لهذا الطلب. تواصل مع الأكاديمية.'),
          findsOneWidget,
        );
      }
      await tester.pumpWidget(const SizedBox());
      auth.dispose();
    });
  }

  for (final (proof, state, message) in [
    ('Pending', 'Pending', 'وصل إثبات الدفع.'),
    ('Rejected', 'Cancelled', 'لم تعتمد الأكاديمية إثبات الدفع.'),
  ]) {
    testWidgets('Manual proof $proof does not offer another transfer', (
      tester,
    ) async {
      final auth = await authenticate('Guardian', (_, path, _, _) async {
        expect(path, '$paymentPath/current');
        return {
          ...payment('current'),
          'provider': 'Manual',
          'status': state,
          'proofStatus': proof,
        };
      });
      await tester.pumpWidget(frame(checkout(auth)));
      await tester.pumpAndSettle();
      expect(find.textContaining(message), findsOneWidget);
      expect(find.byType(TransferPayment), findsNothing);
      expect(find.text('إعادة محاولة الدفع'), findsNothing);
      await tester.pumpWidget(const SizedBox());
      auth.dispose();
    });
  }

  testWidgets('A double tap writes once and opens the issued receipt', (
    tester,
  ) async {
    var writes = 0;
    var paid = false;
    final response = Completer<Object?>();
    final auth = await authenticate('Guardian', (method, path, body, _) async {
      if (method == 'POST') {
        expect(path, '$paymentPath/current/simulate');
        expect(body, {'outcome': 'Success'});
        writes++;
        return response.future;
      }
      if (path.endsWith('/receipt-775')) {
        return {
          'receiptNumber': 'RC-QA-775',
          'paidAtUtc': '2026-11-12T02:00:00Z',
          'amount': 775,
          'originalAmount': 900,
          'discountAmount': 125,
          'currency': 'EGP',
        };
      }
      return {
        ...payment('current'),
        if (paid) 'status': 'Confirmed',
        if (paid) 'receiptId': 'receipt-775',
      };
    });
    await tester.pumpWidget(frame(checkout(auth)));
    await tester.pumpAndSettle();
    await tester.tap(find.text(succeed));
    await tester.tap(find.text(succeed));
    await tester.pump();
    final button = tester.widget<FilledButton>(
      find.widgetWithText(FilledButton, succeed),
    );
    expect(button.onPressed, isNull);
    expect(writes, 1);
    paid = true;
    response.complete(null);
    await tester.pumpAndSettle();
    expect(find.text('المبلغ المدفوع'), findsOneWidget);
    expect(find.text('تم الدفع'), findsOneWidget);
    expect(find.textContaining('السعر الأصلي'), findsNothing);
    await press(tester, 'عرض الإيصال');
    expect(find.text('RC-QA-775'), findsOneWidget);
    expect(find.text('775 ج.م', findRichText: true), findsOneWidget);
    expect(find.textContaining('125 ج.م'), findsOneWidget);
    await tester.binding.handlePopRoute();
    await tester.pumpAndSettle();
    expect(find.text('عرض الإيصال'), findsOneWidget);
    await tester.pumpWidget(const SizedBox());
    auth.dispose();
  });

  for (final (outcome, label) in [
    ('Failed', 'محاكاة فشل الدفع'),
    ('Cancelled', 'إلغاء الدفع التجريبي'),
  ]) {
    testWidgets('$outcome rereads a terminal payment without a receipt', (
      tester,
    ) async {
      var status = 'Pending';
      final auth = await authenticate('Guardian', (
        method,
        path,
        body,
        _,
      ) async {
        if (method == 'POST') {
          expect(body, {'outcome': outcome});
          status = outcome;
          return null;
        }
        return {...payment('current'), 'status': status};
      });
      await tester.pumpWidget(frame(checkout(auth)));
      await tester.pumpAndSettle();
      await press(tester, label);
      expect(find.text('إعادة محاولة الدفع'), findsOneWidget);
      expect(find.text('عرض الإيصال'), findsNothing);
      expect(find.text(succeed), findsNothing);
      await tester.pumpWidget(const SizedBox());
      auth.dispose();
    });
  }

  for (final saved in [false, true]) {
    testWidgets(
      'An ambiguous response requires a successful read (saved=$saved)',
      (tester) async {
        var writes = 0, reads = 0;
        final auth = await authenticate('Guardian', (method, path, _, _) async {
          if (method == 'POST') {
            writes++;
            throw const ApiFailure(0, 'انقطع الاتصال.');
          }
          if (++reads == 2) throw const ApiFailure(503, 'تعذر الاتصال.');
          return {
            ...payment('current'),
            if (reads > 2 && saved) 'status': 'Confirmed',
            if (reads > 2 && saved) 'receiptId': 'receipt-775',
          };
        });
        await tester.pumpWidget(frame(checkout(auth)));
        await tester.pumpAndSettle();
        await press(tester, succeed);
        expect(find.text(succeed), findsNothing);
        await press(tester, checkStatus);
        expect(find.text(succeed), findsNothing);
        expect(find.text('تعذر تحميل البيانات'), findsOneWidget);
        await press(tester, 'إعادة المحاولة');
        expect(find.text(succeed), saved ? findsNothing : findsOneWidget);
        expect(find.text('عرض الإيصال'), saved ? findsOneWidget : findsNothing);
        expect(writes, 1);
        expect(reads, 3);
        await tester.pumpWidget(const SizedBox());
        auth.dispose();
      },
    );
  }

  testWidgets(
    'Retry after loss and after remount uses one payment request key',
    (tester) async {
      var sends = 0;
      final auth = await authenticate('Guardian', (method, path, _, _) async {
        if (method == 'POST') {
          expect(path, '$paymentPath/current/retry');
          if (++sends <= 2) throw const ApiFailure(0, 'انقطع الاتصال.');
          return {'paymentId': 'next'};
        }
        return path.endsWith('/next')
            ? payment('next')
            : {...payment('current'), 'status': 'Failed'};
      });
      await tester.pumpWidget(frame(checkout(auth)));
      await tester.pumpAndSettle();
      await press(tester, 'إعادة محاولة الدفع');
      await press(tester, 'إعادة إرسال طلب الدفع نفسه');
      await tester.pumpWidget(const SizedBox());
      await tester.pumpWidget(frame(checkout(auth)));
      await tester.pumpAndSettle();
      await press(tester, 'إعادة محاولة الدفع');
      expect(find.text(succeed), findsOneWidget);
      final transport = auth.transport as FakeApi;
      final keys = [
        for (var index = 0; index < transport.paths.length; index++)
          if (transport.paths[index].endsWith('/retry')) transport.keys[index],
      ];
      expect(keys, hasLength(3));
      expect(keys.toSet(), {'guardian-checkout-retry:current'});
      await tester.pumpWidget(const SizedBox());
      auth.dispose();
    },
  );

  for (final malformed in [
    {'id': 'different'},
    {'amount': 'incomplete'},
    {'isCurrent': null},
  ]) {
    testWidgets('Invalid payment $malformed has no money or action', (
      tester,
    ) async {
      final auth = await authenticate(
        'Guardian',
        (_, _, _, _) async => {...payment('current'), ...malformed},
      );
      await tester.pumpWidget(frame(checkout(auth)));
      await tester.pumpAndSettle();
      expect(find.text('تعذر قراءة طلب الدفع.'), findsOneWidget);
      expect(find.text('المبلغ المطلوب'), findsNothing);
      expect(find.text(succeed), findsNothing);
      await tester.pumpWidget(const SizedBox());
      auth.dispose();
    });
  }

  testWidgets('404 hides all payment information', (tester) async {
    final auth = await authenticate(
      'Guardian',
      (_, _, _, _) async => throw const ApiFailure(404, 'غير متاح.'),
    );
    await tester.pumpWidget(frame(checkout(auth)));
    await tester.pumpAndSettle();
    expect(find.text('طلب الدفع غير متاح لهذا الحساب.'), findsOneWidget);
    expect(find.text('المبلغ المطلوب'), findsNothing);
    expect(find.text(succeed), findsNothing);
    await tester.pumpWidget(const SizedBox());
    auth.dispose();
  });

  for (final width in [320.0, 390.0, 440.0]) {
    for (final dark in [false, true]) {
      testWidgets(
        'Checkout at $width dark=$dark keeps actions reachable at 200%',
        (tester) async {
          tester.view.physicalSize = Size(width, 844);
          tester.view.devicePixelRatio = 1;
          addTearDown(tester.view.reset);
          final auth = await authenticate(
            'Guardian',
            (_, path, _, _) async => path.endsWith('/old')
                ? {
                    ...payment('old'),
                    'amount': 720,
                    'status': 'Cancelled',
                    'isCurrent': false,
                    'currentPaymentId': 'current',
                  }
                : payment('current'),
          );
          await tester.pumpWidget(
            frame(checkout(auth, id: 'old'), dark: dark, scale: 2),
          );
          await tester.pumpAndSettle();
          expect(find.text('720 ج.م', findRichText: true), findsOneWidget);
          await press(tester, 'فتح طلب الدفع الحالي');
          expect(find.text('775 ج.م', findRichText: true), findsOneWidget);
          final refresh = find.text('تحديث حالة الدفع');
          if (refresh.evaluate().isEmpty) {
            await tester.scrollUntilVisible(refresh, 180);
          }
          expect(refresh, findsOneWidget);
          expect(tester.takeException(), isNull);
          await tester.pumpWidget(const SizedBox());
          auth.dispose();
        },
      );
    }
  }
}
