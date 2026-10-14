import 'package:academy_mobile/features/guardian/coach_feedback.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

import 'coach_test.dart' show coach, shell;

void main() {
  testWidgets('Guardian rates an assigned coach without exposing other children', (tester) async {
    final sent = <Object?>[];
    final auth = await coach((method, path, body, token) async {
      if (method == 'POST') { sent.add(body); return {'id': 'feedback-1'}; }
      return { 'periodMonth': '2026-10-01', 'groups': [
        {'id': 'group-1', 'name': 'مجموعة أ', 'sport': 'كرة القدم', 'coaches': [{'id': 'coach-1', 'name': 'مدرب تجريبي'}]},
      ], 'mine': [] };
    });
    await tester.pumpWidget(shell(CoachFeedbackPage(auth: auth, playerId: 'own-child')));
    await tester.pumpAndSettle();
    await tester.ensureVisible(find.text('إرسال للإدارة'));
    await tester.tap(find.text('إرسال للإدارة')); await tester.pumpAndSettle();
    expect(sent, isEmpty);
    await tester.ensureVisible(find.byType(DropdownButtonFormField<String>).first);
    await tester.tap(find.byType(DropdownButtonFormField<String>).first); await tester.pumpAndSettle();
    await tester.tap(find.text('مجموعة أ · كرة القدم').last); await tester.pumpAndSettle();
    await tester.ensureVisible(find.byType(DropdownButtonFormField<String>).at(2));
    await tester.tap(find.byType(DropdownButtonFormField<String>).at(2)); await tester.pumpAndSettle();
    await tester.tap(find.text('مدرب تجريبي').last); await tester.pumpAndSettle();
    await tester.ensureVisible(find.text('إرسال للإدارة'));
    await tester.tap(find.text('إرسال للإدارة')); await tester.pumpAndSettle();
    expect(sent.single, {'trainingGroupId': 'group-1', 'coachMembershipId': 'coach-1', 'kind': 'Rating', 'score': 5, 'message': ''});
    expect(find.text('وصلت ملاحظتك لإدارة الأكاديمية.'), findsOneWidget);
    expect(tester.takeException(), isNull);
  });
}
