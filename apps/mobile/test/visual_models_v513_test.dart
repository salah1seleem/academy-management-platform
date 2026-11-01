import 'package:academy_mobile/app/app.dart';
import 'package:academy_mobile/core/auth/auth_controller.dart';
import 'package:academy_mobile/core/theme/appearance.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

import 'auth_controller_test.dart';

void main() {
  for (final mode in [
    VisualModel.dark,
    VisualModel.pitch,
    VisualModel.premium,
  ]) {
    testWidgets(
      '${mode.name} persists across internal routes and returning to light keeps session',
      (t) async {
        final prefs = Appearance();
        final auth = AuthController(
          FakeApi((a, b, c, d) async => null),
          MemoryStore(),
        )..restoring = false;
        await prefs.update(visualModel: mode);
        await t.pumpWidget(
          AcademyApp(auth: auth, appearance: prefs, environment: 'Demo'),
        );
        await t.pumpAndSettle();
        final context = t.element(find.text('رقم الهاتف'));
        final expected = Color(switch (mode) {
          VisualModel.dark => 0xFF10479D,
          VisualModel.pitch => 0xFF09271F,
          _ => 0xFF111416,
        });
        expect(Theme.of(context).scaffoldBackgroundColor, expected);
        Navigator.of(context).push(
          MaterialPageRoute<void>(
            builder: (_) => const Scaffold(body: Text('المسار الداخلي')),
          ),
        );
        await t.pumpAndSettle();
        expect(
          Theme.of(t.element(find.text('المسار الداخلي')))
              .scaffoldBackgroundColor,
          expected,
        );
        await prefs.update(night: false);
        await t.pumpAndSettle();
        expect(prefs.model, VisualModel.light);
        expect(
          Theme.of(t.element(find.text('المسار الداخلي'))).brightness,
          Brightness.light,
        );
        expect(auth.session, isNull);
        expect(t.takeException(), isNull);
        await t.pumpWidget(const SizedBox());
        auth.dispose();
        prefs.dispose();
      },
    );
  }
}
