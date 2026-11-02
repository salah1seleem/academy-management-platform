import 'package:academy_mobile/features/shared/profile_avatar.dart';
import 'package:academy_mobile/features/guardian/guardian_ui.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:flutter_svg/flutter_svg.dart';

void main() {
  test(
    'recorded values alone select six shared variants; unknown is neutral',
    () {
      expect(profileAvatarAsset(gender: 1), endsWith('child-male.svg'));
      expect(
        profileAvatarAsset(gender: 'Female'),
        endsWith('child-female.svg'),
      );
      expect(
        profileAvatarAsset(gender: 'اسم بنت'),
        endsWith('child-neutral.svg'),
      );
      expect(
        profileAvatarAsset(adult: true, gender: 'Male'),
        endsWith('adult-male.svg'),
      );
      expect(
        profileAvatarAsset(adult: true, gender: 2),
        endsWith('adult-female.svg'),
      );
      expect(profileAvatarAsset(adult: true), endsWith('adult-neutral.svg'));
    },
  );
  testWidgets('shared missing portrait is cached SVG at list and card sizes', (
    t,
  ) async {
    await t.pumpWidget(
      const MaterialApp(
        home: Scaffold(
          body: Row(
            children: [
              PlayerAvatar('اسم لا يحدد النوع', size: 32),
              PlayerAvatar('عضو', adult: true, size: 120),
            ],
          ),
        ),
      ),
    );
    await t.pumpAndSettle();
    expect(find.byType(SvgPicture), findsNWidgets(2));
    expect(t.takeException(), isNull);
  });
  testWidgets('packaged photo wins; missing packaged file falls back', (
    t,
  ) async {
    await t.pumpWidget(
      const MaterialApp(
        home: PlayerAvatar(
          'خيالي',
          reference: '/demo-assets/portraits/child-01.jpg',
        ),
      ),
    );
    await t.pumpAndSettle();
    expect(find.byType(IllustratedAvatar), findsNothing);
    expect(find.byType(Image), findsOneWidget);
    expect(t.takeException(), isNull);
  });
}
