import 'package:academy_mobile/features/shared/profile_avatar.dart';
import 'package:academy_mobile/core/theme/app_theme.dart';
import 'package:flutter/material.dart';
import 'package:flutter_svg/flutter_svg.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:integration_test/integration_test.dart';

void main() {
  final binding = IntegrationTestWidgetsFlutterBinding.ensureInitialized();
  testWidgets(
    'six original reusable vector fallbacks at phone and list sizes',
    (t) async {
      Widget portrait(bool adult, Object? gender) => Expanded(
        child: Padding(
          padding: const EdgeInsets.all(5),
          child: Column(
            children: [
              ClipRRect(
                borderRadius: BorderRadius.circular(18),
                child: AspectRatio(
                  aspectRatio: .8,
                  child: IllustratedAvatar(adult: adult, gender: gender),
                ),
              ),
              const SizedBox(height: 8),
              SizedBox(
                width: 32,
                height: 32,
                child: ClipOval(
                  child: IllustratedAvatar(adult: adult, gender: gender),
                ),
              ),
            ],
          ),
        ),
      );
      await t.runAsync(() async {
        for (final adult in [false, true]) {
          for (final gender in [1, 2, null]) {
            await SvgAssetLoader(
              profileAvatarAsset(adult: adult, gender: gender),
            ).loadBytes(null);
          }
        }
      });
      await t.pumpWidget(
        MaterialApp(
          theme: AcademyTheme.light(),
          home: Directionality(
            textDirection: TextDirection.rtl,
            child: Scaffold(
              appBar: AppBar(title: const Text('الصور الرمزية المشتركة')),
              body: ListView(
                padding: const EdgeInsets.all(20),
                children: [
                  const Text('رسومات بديلة · لا تمثل أشخاصًا حقيقيين'),
                  for (final adult in [false, true]) ...[
                    Padding(
                      padding: const EdgeInsets.symmetric(vertical: 20),
                      child: Text(
                        adult ? 'الكبار' : 'الأطفال',
                        style: const TextStyle(fontSize: 22),
                      ),
                    ),
                    Row(
                      children: [
                        for (final gender in [1, 2, null])
                          portrait(adult, gender),
                      ],
                    ),
                  ],
                  const Padding(
                    padding: EdgeInsets.only(top: 24),
                    child: Text(
                      'الصورة المرفوعة أولًا. النوع غير المسجل يستخدم الرسم المحايد؛ لا استنتاج من الاسم.',
                    ),
                  ),
                ],
              ),
            ),
          ),
        ),
      );
      await t.runAsync(() async {
        await Future<void>.delayed(const Duration(seconds: 3));
      });
      await t.pumpAndSettle();
      for (var i = 0; i < 40; i++) {
        await t.pump(const Duration(milliseconds: 100));
      }
      expect(t.takeException(), isNull);
      await binding.takeScreenshot('phase-avatar-six-family');
    },
  );
}
