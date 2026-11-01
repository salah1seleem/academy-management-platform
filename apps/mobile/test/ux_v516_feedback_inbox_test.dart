import 'dart:async';

import 'package:academy_mobile/features/owner/owner_feedback.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

import 'v2_role_workspaces_test.dart' as fixture;

void main() {
  testWidgets(
    'inbox shows notes before secondary filters and preserves safety',
    (t) async {
      final reads = <Uri>[];
      final auth = await fixture.authenticate('AcademyOwner', (
        m,
        p,
        b,
        token,
      ) async {
        reads.add(Uri.parse(p));
        return [
          {
            'id': 'f1',
            'kind': 'Complaint',
            'message': 'ملاحظة اصطناعية للمتابعة',
            'status': 'Open',
            'playerName': 'طفل اصطناعي',
            'branchName': 'فرع العرض',
            'safety': true,
          },
        ];
      });
      await t.pumpWidget(
        fixture.app(
          OwnerFeedbackInbox(
            auth: auth,
            initialFilters: const {
              'safetyOnly': 'true',
              'status': 'Unresolved',
            },
          ),
        ),
      );
      await t.pumpAndSettle();
      expect(reads.single.queryParameters['safetyOnly'], 'true');
      expect(find.text('ملاحظة اصطناعية للمتابعة'), findsOneWidget);
      expect(find.byType(DropdownButtonFormField<String>), findsNothing);
      expect(find.text('المعروض: بلاغات السلامة فقط'), findsOneWidget);
      expect(find.text('بلاغ سلامة — يحتاج فرزًا بشريًا'), findsOneWidget);
      await t.tap(find.text('تصفية الملاحظات · 2'));
      await t.pumpAndSettle();
      expect(find.byType(DropdownButtonFormField<String>), findsNWidgets(3));
      await t.ensureVisible(find.text('مسح الفلاتر'));
      await t.tap(find.text('مسح الفلاتر'));
      await t.pumpAndSettle();
      expect(reads.last.queryParameters, isEmpty);
      expect(find.text('الملاحظات المفتوحة فقط'), findsNothing);
      expect(find.text('بلاغ سلامة — يحتاج فرزًا بشريًا'), findsOneWidget);
      expect(t.takeException(), isNull);
      await t.pumpWidget(const SizedBox());
      auth.dispose();
    },
  );

  testWidgets('review cannot be submitted twice while the command is pending', (
    t,
  ) async {
    final pending = Completer<Object?>();
    var writes = 0;
    final auth = await fixture.authenticate('AcademyOwner', (m, p, b, token) {
      if (m == 'POST') {
        writes++;
        return pending.future;
      }
      return Future.value([
        {
          'id': 'f1',
          'kind': 'Suggestion',
          'message': 'اقتراح اصطناعي',
          'status': 'Open',
          'playerName': 'طفل اصطناعي',
          'branchName': 'فرع العرض',
        },
      ]);
    });
    await t.pumpWidget(fixture.app(OwnerFeedbackInbox(auth: auth)));
    await t.pumpAndSettle();
    await t.ensureVisible(find.text('تسجيل المراجعة'));
    await t.tap(find.text('تسجيل المراجعة'));
    await t.pump();
    await t.tap(find.text('تسجيل المراجعة'));
    await t.pump();
    expect(writes, 1);
    pending.complete({});
    await t.pumpAndSettle();
    expect(find.text('تم تسجيل مراجعة الملاحظة.'), findsOneWidget);
    expect(t.takeException(), isNull);
    await t.pumpWidget(const SizedBox());
    auth.dispose();
  });
}
