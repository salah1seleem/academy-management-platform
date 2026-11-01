import 'package:academy_mobile/core/theme/ui_text.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:flutter_localizations/flutter_localizations.dart';

void main() {
  testWidgets(
    'currency stays secondary in complete amounts and labelled financial sentences without changing values',
    (t) async {
      const sentence = 'السعر الأصلي: 1,000 ج.م · استرداد -40.25 ج.م';
      await t.pumpWidget(
        const MaterialApp(
          locale: Locale('ar'),
          supportedLocales: [Locale('ar')],
          localizationsDelegates: GlobalMaterialLocalizations.delegates,
          home: Scaffold(
            body: UiText(sentence, style: TextStyle(fontSize: 24)),
          ),
        ),
      );
      final text = t.widget<Text>(find.byType(Text));
      expect(text.textSpan!.toPlainText(), sentence);
      expect(text.semanticsLabel, sentence);
      final currencies = (text.textSpan! as TextSpan).children!
          .whereType<TextSpan>()
          .where((s) => s.text == ' ج.م')
          .toList();
      expect(currencies.length, 2);
      expect(currencies.every((s) => s.style!.fontSize! < 24), isTrue);
    },
  );
  testWidgets(
    'dates counts and telephone numbers retain their ordinary meaning',
    (t) async {
      const sentence = '2026-10-21 · 30 لاعب · 01000000000';
      await t.pumpWidget(
        const MaterialApp(home: Scaffold(body: UiText(sentence))),
      );
      expect(t.widget<Text>(find.byType(Text)).data, sentence);
    },
  );
}
