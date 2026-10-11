import 'package:academy_mobile/features/guardian/group_transfer_notice.dart';
import 'package:flutter/material.dart';
import 'package:flutter_localizations/flutter_localizations.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  testWidgets(
    'Guardian sees the effective date and target without an editable assignment',
    (tester) async {
      await tester.pumpWidget(
        const MaterialApp(
          locale: Locale('ar'),
          supportedLocales: [Locale('ar'), Locale('en')],
          localizationsDelegates: GlobalMaterialLocalizations.delegates,
          home: Scaffold(
            body: GroupTransferNotice(group: 'مجموعة ب', date: '2026-12-01'),
          ),
        ),
      );
      expect(find.text('نقل مجموعة مجدول'), findsOneWidget);
      expect(find.text('مجموعة ب · 2026-12-01'), findsOneWidget);
      expect(find.byType(ElevatedButton), findsNothing);
    },
  );
  testWidgets(
    'Group transfer notice supports English without changing the group identity',
    (tester) async {
      await tester.pumpWidget(
        const MaterialApp(
          locale: Locale('en'),
          home: Scaffold(
            body: GroupTransferNotice(group: 'مجموعة ب', date: '2026-12-01'),
          ),
        ),
      );
      expect(find.text('Scheduled group transfer'), findsOneWidget);
      expect(find.text('مجموعة ب · 2026-12-01'), findsOneWidget);
    },
  );
}
