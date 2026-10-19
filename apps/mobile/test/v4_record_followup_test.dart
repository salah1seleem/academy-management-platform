import 'package:academy_mobile/features/owner/record_followup.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

import 'owner_test.dart' show owner, shell;

void main() {
  testWidgets(
    'A record task carries only the selected source and its returned scope',
    (tester) async {
      Map<String, dynamic>? saved;
      final auth = await owner((method, path, body, token) async {
        if (method == 'POST') {
          saved = Map<String, dynamic>.from(body as Map);
          return {'id': 'new-task'};
        }
        return {
          'items': [
            {
              'id': 'p',
              'name': 'لاعب اصطناعي',
              'branchId': 'b',
              'branch': 'فرع اصطناعي',
              'sportId': 's',
              'sport': 'كرة القدم',
            },
          ],
          'owners': [],
          'notice': 'لا تحصيل من هذه المتابعة',
        };
      });
      await tester.pumpWidget(
        shell(
          Builder(
            builder: (context) => FilledButton(
              onPressed: () => Navigator.of(context).push(
                MaterialPageRoute(
                  builder: (_) => OwnerRecordFollowUpCreate(auth: auth),
                ),
              ),
              child: const Text('ابدأ'),
            ),
          ),
        ),
      );
      await tester.tap(find.text('ابدأ'));
      await tester.pumpAndSettle();
      await tester.tap(find.byType(DropdownButtonFormField<String>).at(1));
      await tester.pumpAndSettle();
      await tester.tap(find.textContaining('لاعب اصطناعي').last);
      await tester.pumpAndSettle();
      final save = find.text('حفظ المتابعة');
      await tester.scrollUntilVisible(save, 200, scrollable: find.byType(Scrollable).first);
      await tester.ensureVisible(save);
      await tester.tap(save);
      await tester.pumpAndSettle();
      expect(saved, containsPair('sourceId', 'p'));
      expect(saved, containsPair('branchId', 'b'));
      expect(saved, containsPair('sportId', 's'));
      expect(saved!.containsKey('trainingGroupId'), isFalse);
      expect(saved!['requestId'], isA<String>());
      expect(tester.takeException(), isNull);
    },
  );
}
