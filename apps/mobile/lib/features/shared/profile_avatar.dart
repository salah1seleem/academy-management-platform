import 'package:flutter/material.dart';
import 'package:flutter_svg/flutter_svg.dart';

/// Only recorded enum values are used. Names, roles and photos never infer sex.
String profileAvatarAsset({
  bool adult = false,
  Object? gender,
  String? identity,
  String? role,
}) {
  final sex = gender == 1 || gender == 'Male'
      ? 'male'
      : gender == 2 || gender == 'Female'
      ? 'female'
      : 'neutral';
  if (identity == null || identity.isEmpty) {
    return 'assets/avatars/${adult ? 'adult' : 'child'}-$sex.svg';
  }
  var hash = 2166136261;
  for (final c in identity.codeUnits) {
    hash = ((hash ^ c) * 16777619) & 0x7fffffff;
  }
  final kind = adult
      ? (['coach', 'guardian', 'owner'].contains(role) ? role! : 'guardian')
      : 'child';
  final variant = kind == 'owner' ? 1 : hash % 3 + 1;
  return 'assets/avatars/$kind-$sex-$variant.svg';
}

class IllustratedAvatar extends StatelessWidget {
  const IllustratedAvatar({
    super.key,
    this.adult = false,
    this.gender,
    this.identity,
    this.role,
  });
  final bool adult;
  final Object? gender;
  final String? identity, role;
  @override
  Widget build(BuildContext context) => SvgPicture.asset(
    profileAvatarAsset(
      adult: adult,
      gender: gender,
      identity: identity,
      role: role,
    ),
    fit: BoxFit.cover,
    alignment: const Alignment(0, -.4),
    excludeFromSemantics: true,
  );
}
