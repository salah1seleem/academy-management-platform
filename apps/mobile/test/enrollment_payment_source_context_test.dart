import 'dart:async';
import 'dart:convert';
import 'dart:typed_data';

import 'package:academy_mobile/core/api/api_client.dart';
import 'package:academy_mobile/core/auth/auth_controller.dart';
import 'package:academy_mobile/core/theme/app_theme.dart';
import 'package:academy_mobile/core/theme/ui_text.dart';
import 'package:academy_mobile/features/admin/native_enrollment_payment_read.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

import 'v2_role_workspaces_test.dart' show authenticate, app;
import 'auth_controller_test.dart' show FakeApi, MemoryStore, credentials;

const proof = '/api/v1/assets/1209828f-a878-4739-a522-a4c25b5a9b2c';
final pixel = base64Decode(
  'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+ip1sAAAAASUVORK5CYII=',
);

class ProofTransport extends FakeApi implements AssetTransport {
  ProofTransport(this.response, {this.hasProof = true})
    : super((_, _, _, _) async => null);
  final Future<Uint8List> Function(int read) response;
  final bool hasProof;
  int reads = 0, detailReads = 0;

  @override
  Future<Object?> send(
    String method,
    String path, {
    Object? body,
    String? token,
    String? idempotencyKey,
  }) async {
    if (path.contains('/auth/')) {
      return {
        ...credentials(),
        'membershipId': 'staff',
        'memberships': [
          {
            'id': 'staff',
            'academyId': 'academy',
            'academyName': 'أكاديمية اختبار',
            'role': 'AcademyOwner',
          },
        ],
      };
    }
    expect(method, 'GET');
    expect(path, '/api/v1/manage/enrollment-requests/source/payment');
    detailReads++;
    return {
      'id': 'source',
      'child': 'لاعب اختبار',
      'status': 'Pending',
      'amount': 720,
      'currency': 'EGP',
      'proofUrl': hasProof ? proof : null,
    };
  }

  @override
  Future<Uint8List> asset(String path, String token) {
    expect(path, proof);
    return response(++reads);
  }

  @override
  Future<Object?> upload(
    Uint8List bytes,
    String contentType,
    Map<String, String> fields,
    String token,
  ) => throw StateError('Read-only payment screen');
}

Future<AuthController> openProof(
  WidgetTester t,
  ProofTransport transport,
) async {
  t.view.physicalSize = const Size(320, 844);
  t.view.devicePixelRatio = 1;
  addTearDown(t.view.reset);
  final auth = AuthController(transport, MemoryStore())..restoring = false;
  await auth.verify('phone', 'challenge', 'code');
  await t.pumpWidget(
    app(NativeEnrollmentPaymentRead(auth: auth, id: 'source'), scale: 2),
  );
  await until(t, find.byType(ListView));
  return auth;
}

Future<void> until(WidgetTester t, Finder finder) async {
  for (var i = 0; i < 100 && finder.evaluate().isEmpty; i++) {
    await t.runAsync(
      () => Future<void>.delayed(const Duration(milliseconds: 10)),
    );
    await t.pump(const Duration(milliseconds: 30));
  }
  expect(finder, findsOneWidget);
}

void main() {
  for (final corrupt in [false, true]) {
    testWidgets(
      'enrollment proof recovers and zooms after an image failure, corrupt=$corrupt',
      (t) async {
        final first = Completer<Uint8List>();
        final api = ProofTransport(
          (read) async => read == 1 ? first.future : pixel,
        );
        final auth = await openProof(t, api);
        await t.scrollUntilVisible(find.text('إثبات الدفع'), 160);
        expect(find.text('تكبير'), findsNothing);
        if (corrupt) {
          first.complete(Uint8List.fromList([1, 2, 3]));
        } else {
          first.completeError(const ApiFailure(503, 'تعذر الاتصال'));
        }
        final retry = find.text('إعادة تحميل الصورة');
        await until(t, retry);
        await t.scrollUntilVisible(retry.hitTestable(), 140);
        await t.pumpAndSettle();
        expect(find.text('تعذر فتح صورة الإثبات.'), findsOneWidget);
        expect(find.text('تكبير'), findsNothing);
        await t.tap(retry.hitTestable());
        final zoom = find.text('تكبير');
        await until(t, zoom);
        await Scrollable.ensureVisible(t.element(zoom), alignment: .5);
        await t.pump();
        await t.tap(zoom);
        await t.pump();
        final viewer = t.widget<InteractiveViewer>(
          find.byType(InteractiveViewer),
        );
        expect(viewer.transformationController!.value.getMaxScaleOnAxis(), 2);
        await t.tap(find.text('الحجم الأصلي'));
        await t.pump();
        expect(viewer.transformationController!.value.getMaxScaleOnAxis(), 1);
        expect(api.reads, 2);
        expect(api.detailReads, 1);
        expect(t.takeException(), isNull);
        await t.pumpWidget(const SizedBox());
        auth.dispose();
      },
    );
  }
  testWidgets('enrollment proof never renders bytes that arrive after logout', (
    t,
  ) async {
    final response = Completer<Uint8List>();
    final api = ProofTransport((_) => response.future);
    final auth = await openProof(t, api);
    await t.scrollUntilVisible(find.text('إثبات الدفع'), 160);
    expect(api.reads, 1);
    await auth.logout();
    await t.pump();
    response.complete(pixel);
    await t.pumpAndSettle();
    expect(find.byType(InteractiveViewer), findsNothing);
    expect(find.text('تكبير'), findsNothing);
    expect(find.text('لاعب اختبار'), findsNothing);
    await t.pumpWidget(const SizedBox());
    auth.dispose();
  });
  testWidgets('a pending enrollment without proof offers no image action', (
    t,
  ) async {
    final api = ProofTransport((_) async => pixel, hasProof: false);
    final auth = await openProof(t, api);
    await t.scrollUntilVisible(find.text('لم يصل إثبات دفع.'), 140);
    expect(find.text('إعادة تحميل الصورة'), findsNothing);
    expect(api.reads, 0);
    await t.pumpWidget(const SizedBox());
    auth.dispose();
  });
  for (final (status, paid, permitted, failsOnce) in [
    ('Approved', true, true, false),
    ('Approved', true, true, true),
    ('Approved', true, false, false),
    ('Pending', false, true, false),
  ]) {
    testWidgets(
      'staff receipt follows the confirmed source $status $paid $permitted $failsOnce',
      (t) async {
        final reads = <String>[];
        var receiptReads = 0;
        final auth = await authenticate('AcademyAdmin', (
          method,
          path,
          _,
          _,
        ) async {
          expect(method, 'GET');
          reads.add(path);
          if (path.endsWith('/receipt')) {
            if (++receiptReads == 1 && failsOnce) {
              throw const ApiFailure(503, 'تعذر الاتصال');
            }
            return {
              'receiptNumber': 'QA-ENROLLMENT-720',
              'amount': 720,
              'currency': 'EGP',
              'paidAtUtc': '2026-11-13T10:00:00Z',
              'paidAtAcademy': '2026-11-13T12:00:00+02:00',
            };
          }
          return {
            'id': 'source',
            'status': status,
            'child': 'لاعب اختبار',
            'amount': 720,
            'currency': 'EGP',
            if (paid) 'paymentRequestId': 'payment',
            if (permitted) 'receiptId': 'receipt',
          };
        });
        t.view.physicalSize = const Size(320, 844);
        t.view.devicePixelRatio = 1;
        addTearDown(t.view.reset);
        await t.pumpWidget(
          app(NativeEnrollmentPaymentRead(auth: auth, id: 'source'), scale: 2),
        );
        await t.pumpAndSettle();
        expect(
          find.text(paid ? 'المبلغ المحصّل' : 'المبلغ المطلوب'),
          findsOneWidget,
        );
        final open = find.text('عرض الإيصال');
        if (paid && permitted) {
          await t.ensureVisible(open);
          await t.tap(open);
          await t.pumpAndSettle();
          if (failsOnce) {
            expect(find.textContaining('QA-ENROLLMENT-720'), findsNothing);
            await t.tap(find.text('إعادة المحاولة'));
            await t.pumpAndSettle();
          }
          expect(find.textContaining('QA-ENROLLMENT-720'), findsOneWidget);
          expect(reads.last, '/api/v1/subscriptions/receipts/receipt');
          expect(receiptReads, failsOnce ? 2 : 1);
          await t.tap(find.byType(BackButton));
          await t.pumpAndSettle();
          expect(open, findsOneWidget);
          expect(find.text('المبلغ المحصّل'), findsOneWidget);
        } else {
          expect(open, findsNothing);
          expect(reads, hasLength(1));
        }
        expect(t.takeException(), isNull);
        await t.pumpWidget(const SizedBox());
        auth.dispose();
      },
    );
  }
  for (final width in [375.0, 440.0]) {
    for (final scale in [1.0, 2.0]) {
      for (final dark in [true, false]) {
        testWidgets(
          'enrollment source context is readable at $width scale $scale dark=$dark',
          (t) async {
            t.view.physicalSize = Size(width, 900);
            t.view.devicePixelRatio = 1;
            addTearDown(t.view.resetPhysicalSize);
            addTearDown(t.view.resetDevicePixelRatio);
            final calls = <String>[];
            final auth = await authenticate('AcademyOwner', (
              m,
              p,
              b,
              token,
            ) async {
              calls.add('$m $p');
              return {
                'id': 'source',
                'child': 'طفل مصدر اصطناعي باسم طويل لاختبار التفاف السطر',
                'guardian': 'ولي أمر اصطناعي',
                'sport': 'كرة القدم',
                'branch': 'فرع العرض الاصطناعي',
                'status': 'Pending',
                'originalAmount': 1000,
                'discountAmount': 100,
                'amount': 900,
                'currency': 'EGP',
              };
            });
            await t.pumpWidget(
              app(
                Theme(
                  data: dark ? AcademyTheme.dark() : AcademyTheme.light(),
                  child: NativeEnrollmentPaymentRead(auth: auth, id: 'source'),
                ),
                scale: scale,
              ),
            );
            await t.pumpAndSettle();
            expect(
              find.text('طفل مصدر اصطناعي باسم طويل لاختبار التفاف السطر'),
              findsOneWidget,
            );
            expect(find.text('ولي الأمر: ولي أمر اصطناعي'), findsOneWidget);
            expect(
              find.text('كرة القدم · فرع العرض الاصطناعي'),
              findsOneWidget,
            );
            await t.scrollUntilVisible(
              find.text('المبلغ المطلوب'),
              140,
              scrollable: find.byType(Scrollable).first,
            );
            expect(find.text('900 ج.م'), findsOneWidget);
            for (final label in [
              'الحالة',
              'السعر الأصلي',
              'الخصم',
              'المبلغ المطلوب',
            ]) {
              expect(
                t
                    .widget<UiText>(
                      find.byWidgetPredicate(
                        (w) => w is UiText && w.data == label,
                      ),
                    )
                    .style
                    ?.color,
                isNull,
              );
            }
            expect(calls, [
              'GET /api/v1/manage/enrollment-requests/source/payment',
            ]);
            expect(t.takeException(), isNull);
            await auth.logout();
            await t.pumpAndSettle();
            expect(find.text('ولي الأمر: ولي أمر اصطناعي'), findsNothing);
            await t.pumpWidget(const SizedBox());
            auth.dispose();
          },
        );
      }
    }
  }
}
