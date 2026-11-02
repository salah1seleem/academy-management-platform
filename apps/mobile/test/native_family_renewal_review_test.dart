import 'dart:convert';

import 'package:academy_mobile/core/api/api_client.dart';
import 'package:academy_mobile/features/guardian/family_renewal_review.dart';
import 'package:academy_mobile/features/guardian/family_renewal_request.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

import 'owner_test.dart' as fixture;

Map<String, dynamic> family({
  String status = 'Pending',
  bool canReview = true,
  bool proof = true,
}) => {
  'id': 'family',
  'revision': 2,
  'status': status,
  'canReview': canReview,
  'currency': 'EGP',
  'totalAmount': '1600.00',
  if (proof) 'proofUrl': '/api/v1/assets/proof',
  'items': [
    for (var i = 1; i <= 2; i++)
      {
        'id': 'l$i',
        'sportEnrollmentId': 'e$i',
        'player': 'ابن اصطناعي $i',
        'currency': 'EGP',
        'originalAmount': '1000.00',
        'discountAmount': '200.00',
        'finalAmount': '800.00',
        'groupId': i == 1 ? 'g1' : null,
        'groups': [
          {'id': 'g$i', 'name': 'مجموعة $i'},
        ],
        if (status == 'Approved') 'receiptId': 'r$i',
      },
  ],
};
Map<String, dynamic> receipt(String id) => {
  'id': id,
  'isVoided': false,
  'playerNameSnapshot': 'ابن اصطناعي ${id.substring(1)}',
  'currency': 'EGP',
  'amount': '800.00',
  'originalAmount': '1000.00',
  'discountAmount': '200.00',
  'finalAmount': '800.00',
};
Future<void> chooseAndConfirm(WidgetTester t, {bool accept = true}) async {
  if (accept) {
    final group = find.widgetWithText(
      DropdownButtonFormField<String>,
      'مجموعة ابن اصطناعي 2',
    );
    await t.ensureVisible(group);
    await t.pumpAndSettle();
    await t.tap(group);
    await t.pumpAndSettle();
    await t.tap(find.text('مجموعة 2').last);
    await t.pumpAndSettle();
  }
  final button = find.text(
    accept ? 'قبول وتجديد كل التسجيلات' : 'رفض الطلب كاملًا',
  );
  await t.ensureVisible(button);
  await t.pumpAndSettle();
  await t.tap(button);
  await t.pumpAndSettle();
  await t.tap(find.text(accept ? 'تأكيد قبول الأسرة' : 'تأكيد رفض الأسرة'));
  await t.pumpAndSettle();
}

Widget page(Widget child) =>
    fixture.shell(Scaffold(body: ListView(children: [child])));
void main() {
  testWidgets(
    'unknown review intent stays locked and identical after inspecting earlier details',
    (t) async {
      final writes = <String>[];
      var confirmed = 0;
      final auth = await fixture.owner((m, p, b, k) async {
        if (m == 'POST') {
          writes.add(jsonEncode(b));
          if (writes.length == 1) {
            throw const ApiFailure(503, 'تعذر تأكيد الرد.');
          }
        }
        return p.contains('/receipts/')
            ? receipt(p.split('/').last)
            : family(status: 'Approved');
      });
      final review = NativeFamilyRenewalReview(
        auth: auth,
        id: 'family',
        data: family(),
        onConfirmed: () => confirmed++,
      );
      await t.pumpWidget(
        fixture.shell(
          Scaffold(
            body: ListView(
              children: [
                const SizedBox(height: 2000),
                review,
                const SizedBox(height: 2000),
              ],
            ),
          ),
        ),
      );
      final scroll = find.byType(Scrollable).first;
      await t.scrollUntilVisible(
        find.byType(NativeFamilyRenewalReview),
        200,
        scrollable: scroll,
        maxScrolls: 50,
      );
      await chooseAndConfirm(t);
      expect(confirmed, 0);
      t.state<ScrollableState>(scroll).position.jumpTo(0);
      await t.pumpAndSettle();
      await t.scrollUntilVisible(
        find.byType(NativeFamilyRenewalReview),
        200,
        scrollable: scroll,
        maxScrolls: 50,
      );
      final retry = find.text('إعادة تأكيد نفس القرار');
      await t.ensureVisible(retry);
      await t.pumpAndSettle();
      expect(find.text('قبول وتجديد كل التسجيلات'), findsNothing);
      await t.tap(retry);
      await t.pumpAndSettle();
      expect(confirmed, 1);
      expect(writes.length, 2);
      expect(writes[0], writes[1]);
      await t.pumpWidget(const SizedBox());
      auth.dispose();
    },
  );
  testWidgets(
    'review draft survives scrolling up to inspect the family proof and back',
    (t) async {
      final auth = await fixture.owner((m, p, b, k) async => family());
      final review = NativeFamilyRenewalReview(
        auth: auth,
        id: 'family',
        data: family(),
        onConfirmed: () {},
      );
      await t.pumpWidget(
        fixture.shell(
          Scaffold(
            body: ListView(
              children: [
                const SizedBox(height: 2000),
                review,
                const SizedBox(height: 2000),
              ],
            ),
          ),
        ),
      );
      final scroll = find.byType(Scrollable).first;
      await t.scrollUntilVisible(
        find.byType(NativeFamilyRenewalReview),
        200,
        scrollable: scroll,
        maxScrolls: 50,
      );
      final note = find.widgetWithText(
        TextField,
        'ملاحظة أو سبب الرفض (اختياري)',
      );
      await t.ensureVisible(note);
      await t.enterText(note, 'سبب مراجعة اصطناعي محفوظ');
      FocusManager.instance.primaryFocus?.unfocus();
      await t.pumpAndSettle();
      t.state<ScrollableState>(scroll).position.jumpTo(0);
      await t.pumpAndSettle();
      await t.scrollUntilVisible(
        find.byType(NativeFamilyRenewalReview),
        200,
        scrollable: scroll,
        maxScrolls: 50,
      );
      await t.ensureVisible(note);
      await t.pumpAndSettle();
      expect(
        t.widget<TextField>(note).controller!.text,
        'سبب مراجعة اصطناعي محفوظ',
      );
      await t.pumpWidget(const SizedBox());
      auth.dispose();
    },
  );
  testWidgets(
    'family detail clears child names and financial details after logout',
    (t) async {
      final auth = await fixture.owner(
        (m, p, b, k) async => family(proof: false),
      );
      await t.pumpWidget(
        fixture.shell(
          NativeFamilyRenewalRequest(auth: auth, id: 'family', staff: true),
        ),
      );
      await t.pumpAndSettle();
      expect(find.text('ابن اصطناعي 1'), findsOneWidget);
      await auth.logout();
      await t.pumpAndSettle();
      expect(find.text('ابن اصطناعي 1'), findsNothing);
      expect(find.textContaining('1,600'), findsNothing);
      await t.pumpWidget(const SizedBox());
      auth.dispose();
    },
  );
  for (final accept in [true, false]) {
    testWidgets(
      'whole family ${accept ? 'approval verifies each receipt' : 'rejection creates no receipt reads'} before refreshing saved status',
      (t) async {
        final calls = <String>[];
        Map? body;
        var confirmed = 0;
        final auth = await fixture.owner((m, p, b, k) async {
          calls.add('$m $p');
          if (m == 'POST') body = b as Map;
          if (p.contains('/receipts/')) return receipt(p.split('/').last);
          return family(status: accept ? 'Approved' : 'Rejected');
        });
        await t.pumpWidget(
          page(
            NativeFamilyRenewalReview(
              auth: auth,
              id: 'family',
              data: family(),
              onConfirmed: () => confirmed++,
            ),
          ),
        );
        await t.pumpAndSettle();
        expect(
          t
              .widget<DropdownButtonFormField<String>>(
                find.widgetWithText(
                  DropdownButtonFormField<String>,
                  'مجموعة ابن اصطناعي 1',
                ),
              )
              .onChanged,
          isNull,
        );
        await chooseAndConfirm(t, accept: accept);
        expect(confirmed, 1);
        expect(body!['accept'], accept);
        expect(body!['revision'], 2);
        expect(
          body!['placements'],
          accept
              ? [
                  {'lineId': 'l1', 'groupId': 'g1'},
                  {'lineId': 'l2', 'groupId': 'g2'},
                ]
              : null,
        );
        expect(
          calls.where((p) => p.contains('/receipts/')).length,
          accept ? 2 : 0,
        );
        expect(t.takeException(), isNull);
        await t.pumpWidget(const SizedBox());
        auth.dispose();
      },
    );
  }
  for (final lostPost in [true, false]) {
    testWidgets(
      '${lostPost ? 'unknown POST result retries identical intent' : 'acknowledged POST retries only reads'} and locks selections while confirmation is missing',
      (t) async {
        final writes = <String>[];
        var reads = 0, confirmed = 0;
        final auth = await fixture.owner((m, p, b, k) async {
          if (m == 'POST') {
            writes.add(jsonEncode(b));
            if (lostPost && writes.length == 1) {
              throw const ApiFailure(503, 'تعذر تأكيد الرد.');
            }
            return family(status: 'Approved');
          }
          if (p.contains('/receipts/')) return receipt(p.split('/').last);
          if (!lostPost && reads++ == 0) {
            throw const ApiFailure(503, 'تعذرت القراءة.');
          }
          return family(status: 'Approved');
        });
        await t.pumpWidget(
          page(
            NativeFamilyRenewalReview(
              auth: auth,
              id: 'family',
              data: family(),
              onConfirmed: () => confirmed++,
            ),
          ),
        );
        await t.pumpAndSettle();
        await chooseAndConfirm(t);
        expect(confirmed, 0);
        expect(t.widget<TextField>(find.byType(TextField)).enabled, isFalse);
        final retry = find.text(
          lostPost ? 'إعادة تأكيد نفس القرار' : 'إعادة قراءة القرار والإيصالات',
        );
        await t.ensureVisible(retry);
        await t.tap(retry);
        await t.pumpAndSettle();
        expect(confirmed, 1);
        expect(writes.length, lostPost ? 2 : 1);
        if (lostPost) expect(writes[0], writes[1]);
        await t.pumpWidget(const SizedBox());
        auth.dispose();
      },
    );
  }
  testWidgets('a mismatched receipt cannot announce completed family renewal', (
    t,
  ) async {
    var confirmed = 0;
    final auth = await fixture.owner(
      (m, p, b, k) async => p.contains('/receipts/')
          ? {...receipt(p.split('/').last), 'amount': '999.00'}
          : family(status: 'Approved'),
    );
    await t.pumpWidget(
      page(
        NativeFamilyRenewalReview(
          auth: auth,
          id: 'family',
          data: family(),
          onConfirmed: () => confirmed++,
        ),
      ),
    );
    await t.pumpAndSettle();
    await chooseAndConfirm(t);
    expect(confirmed, 0);
    expect(find.textContaining('لم تتأكد مطابقة إيصال'), findsOneWidget);
    await t.pumpWidget(const SizedBox());
    auth.dispose();
  });
  testWidgets(
    'read-only permission and missing proof cannot offer an approvable action',
    (t) async {
      final auth = await fixture.owner(
        (m, p, b, k) async => throw StateError('No financial request expected'),
      );
      await t.pumpWidget(
        page(
          NativeFamilyRenewalReview(
            auth: auth,
            id: 'family',
            data: family(canReview: false),
            onConfirmed: () {},
          ),
        ),
      );
      await t.pumpAndSettle();
      expect(find.text('قبول وتجديد كل التسجيلات'), findsNothing);
      await t.pumpWidget(
        page(
          NativeFamilyRenewalReview(
            key: const ValueKey('proof'),
            auth: auth,
            id: 'family',
            data: family(proof: false),
            onConfirmed: () {},
          ),
        ),
      );
      await t.pumpAndSettle();
      expect(
        t
            .widget<FilledButton>(
              find.widgetWithText(FilledButton, 'قبول وتجديد كل التسجيلات'),
            )
            .onPressed,
        isNull,
      );
      await auth.logout();
      await t.pumpAndSettle();
      expect(find.text('قبول وتجديد كل التسجيلات'), findsNothing);
      await t.pumpWidget(const SizedBox());
      auth.dispose();
    },
  );
  testWidgets(
    'authoritative rejection requires refreshed request and does not retry the write',
    (t) async {
      var writes = 0, confirmed = 0;
      final auth = await fixture.owner((m, p, b, k) async {
        if (m == 'POST') {
          writes++;
          throw const ApiFailure(403, 'لا تملك الإذن.');
        }
        return family();
      });
      await t.pumpWidget(
        page(
          NativeFamilyRenewalReview(
            auth: auth,
            id: 'family',
            data: family(),
            onConfirmed: () => confirmed++,
          ),
        ),
      );
      await t.pumpAndSettle();
      await chooseAndConfirm(t);
      expect(writes, 1);
      expect(confirmed, 0);
      expect(find.text('تحديث الطلب'), findsOneWidget);
      expect(find.text('إعادة تأكيد نفس القرار'), findsNothing);
      expect(find.text('قبول وتجديد كل التسجيلات'), findsNothing);
      await t.pumpWidget(const SizedBox());
      auth.dispose();
    },
  );
}
