import 'package:academy_mobile/core/theme/academy_home_skin.dart';
import 'package:academy_mobile/core/theme/app_theme.dart';
import 'package:academy_mobile/core/theme/appearance.dart';
import 'package:academy_mobile/core/theme/ocean_palette.dart';
import 'package:academy_mobile/features/guardian/guardian_ui.dart';
import 'package:academy_mobile/features/shared/experience_ui.dart';
import 'package:flutter/material.dart';
import 'package:flutter_secure_storage/flutter_secure_storage.dart';
import 'package:flutter_test/flutter_test.dart';

double contrast(Color a, Color b) {
  final values = [a.computeLuminance(), b.computeLuminance()]..sort();
  return (values.last + .05) / (values.first + .05);
}

void main() {
  TestWidgetsFlutterBinding.ensureInitialized();

  test(
    'Ocean is brand independent and legacy theme keys retain preferences',
    () async {
      FlutterSecureStorage.setMockInitialValues({
        'academy.appearance': 'dark',
        'academy.onboarded': 'yes',
        'academy.language': 'ar',
      });
      final prefs = Appearance(storage: const FlutterSecureStorage());
      await prefs.load();
      expect(prefs.model, VisualModel.dark);
      expect(prefs.model.label, 'أزرق');
      expect(prefs.model.fixedPalette, true);
      for (final brand in [
        Colors.red,
        Colors.green,
        Colors.black,
        Colors.white,
      ]) {
        final theme = AcademyTheme.dark(primary: brand);
        expect(theme.scaffoldBackgroundColor, OceanPalette.background);
        expect(theme.colorScheme.primary, OceanPalette.accent);
        expect(theme.colorScheme.onPrimary, OceanPalette.onAccent);
        expect(theme.colorScheme.surface, OceanPalette.surface);
        expect(theme.colorScheme.surfaceContainerHighest, OceanPalette.raised);
        expect(theme.dialogTheme.backgroundColor, OceanPalette.surface);
        expect(theme.bottomSheetTheme.backgroundColor, OceanPalette.surface);
        expect(theme.navigationBarTheme.backgroundColor, OceanPalette.surface);
      }
      await prefs.update(visualModel: VisualModel.premium);
      expect(prefs.model.label, 'داكن مميز');
      expect(await prefs.storage!.read(key: 'academy.appearance'), 'premium');
      await prefs.update(visualModel: VisualModel.dark);
      expect(await prefs.storage!.read(key: 'academy.appearance'), 'dark');
      expect(prefs.onboarded, true);
      expect(prefs.locale, const Locale('ar'));
      prefs.dispose();
    },
  );

  test('Ocean text, actions and semantic values meet text contrast on all surfaces', () {
    for (final surface in [
      OceanPalette.background,
      OceanPalette.surface,
      OceanPalette.raised,
    ]) {
      for (final ink in [
        OceanPalette.ink,
        OceanPalette.muted,
        OceanPalette.accent,
        OceanPalette.positive,
        OceanPalette.warning,
        OceanPalette.negative,
      ]) {
        expect(
          contrast(ink, surface),
          greaterThanOrEqualTo(4.5),
          reason: '$ink on $surface',
        );
      }
    }
    expect(
      contrast(OceanPalette.onAccent, OceanPalette.accent),
      greaterThanOrEqualTo(4.5),
    );
    expect(
      AcademyTheme.light().scaffoldBackgroundColor,
      const Color(0xFFF3F6FA),
    );
    expect(
      AcademyTheme.themed(VisualModel.pitch).scaffoldBackgroundColor,
      const Color(0xFF09271F),
    );
    expect(
      AcademyTheme.themed(VisualModel.premium).scaffoldBackgroundColor,
      const Color(0xFF111416),
    );
  });

  testWidgets(
    'Ocean carries through identity cards, form, status, dialog and sheet',
    (t) async {
      late BuildContext page;
      await t.pumpWidget(
        MaterialApp(
          theme: AcademyTheme.themed(VisualModel.dark),
          home: Directionality(
            textDirection: TextDirection.rtl,
            child: Builder(
              builder: (context) {
                page = context;
                return Scaffold(
                  body: Column(
                    children: [
                      const TextField(
                        decoration: InputDecoration(labelText: 'اسم الباقة'),
                      ),
                      const StatusPill('active'),
                      FilledButton(
                        onPressed: () {},
                        child: const Text('مراجعة الباقة'),
                      ),
                    ],
                  ),
                );
              },
            ),
          ),
        ),
      );
      expect(academyIdentityBase(page), OceanPalette.surface);
      expect(ExperienceColors.positive(page), OceanPalette.positive);
      expect(ExperienceColors.negative(page), OceanPalette.negative);
      expect(ExperienceColors.warning(page), OceanPalette.warning);
      final button = t.widget<FilledButton>(find.byType(FilledButton));
      final style = button.themeStyleOf(t.element(find.byType(FilledButton)))!;
      expect(style.backgroundColor!.resolve({}), OceanPalette.accent);
      expect(style.foregroundColor!.resolve({}), OceanPalette.onAccent);
      showDialog<void>(
        context: page,
        builder: (_) => const AlertDialog(title: Text('مراجعة الطلب')),
      );
      await t.pumpAndSettle();
      expect(
        Theme.of(t.element(find.text('مراجعة الطلب'))).colorScheme.surface,
        OceanPalette.surface,
      );
      Navigator.of(page).pop();
      await t.pumpAndSettle();
      showModalBottomSheet<void>(
        context: page,
        builder: (_) => const Text('اختيار الباقة'),
      );
      await t.pumpAndSettle();
      expect(
        Theme.of(t.element(find.text('اختيار الباقة'))).scaffoldBackgroundColor,
        OceanPalette.background,
      );
      expect(t.takeException(), isNull);
    },
  );
}
