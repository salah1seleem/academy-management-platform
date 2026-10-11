import 'package:academy_mobile/features/guardian/player_card.dart';
import 'package:flutter/material.dart';
import 'package:flutter_localizations/flutter_localizations.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  testWidgets(
    'English card preserves the player identity and explains manual fallback',
    (tester) async {
      await tester.pumpWidget(
        const MaterialApp(
          locale: Locale('en'),
          home: Scaffold(
            body: PlayerCardView(name: 'لاعب اصطناعي', data: {'active': false}),
          ),
        ),
      );
      expect(find.text('لاعب اصطناعي'), findsOneWidget);
      expect(find.textContaining('No active card'), findsOneWidget);
      expect(find.textContaining('manual lookup'), findsOneWidget);
      expect(find.byType(ElevatedButton), findsNothing);
    },
  );
  testWidgets(
    'Guardian card has no attendance or issuance action, including at 200% text',
    (tester) async {
      await tester.pumpWidget(
        MaterialApp(
          locale: const Locale('ar'),
          supportedLocales: const [Locale('ar')],
          localizationsDelegates: GlobalMaterialLocalizations.delegates,
          home: MediaQuery(
            data: const MediaQueryData(textScaler: TextScaler.linear(2)),
            child: const Scaffold(
              body: SingleChildScrollView(
                child: PlayerCardView(
                  name: 'لاعب اصطناعي',
                  data: {'active': false},
                ),
              ),
            ),
          ),
        ),
      );
      expect(find.text('لاعب اصطناعي'), findsOneWidget);
      expect(find.textContaining('لا توجد بطاقة فعالة'), findsOneWidget);
      expect(find.byType(ElevatedButton), findsNothing);
      expect(find.byType(ChoiceChip), findsNothing);
      expect(tester.takeException(), isNull);
    },
  );
  testWidgets('Malformed QR image offers manual lookup instead of a crash', (
    tester,
  ) async {
    await tester.pumpWidget(
      const MaterialApp(
        locale: Locale('ar'),
        supportedLocales: [Locale('ar')],
        localizationsDelegates: GlobalMaterialLocalizations.delegates,
        home: Scaffold(
          body: PlayerCardView(
            name: 'لاعب اصطناعي',
            data: {'active': true, 'pngBase64': '%invalid%'},
          ),
        ),
      ),
    );
    await tester.pumpAndSettle();
    expect(find.textContaining('استخدم كود اللاعب يدويًا'), findsOneWidget);
    expect(tester.takeException(), isNull);
  });
}
