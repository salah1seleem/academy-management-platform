import '../shared/profile_avatar.dart';
import '../shared/person_summary_card.dart';

import 'package:flutter_svg/flutter_svg.dart';
import 'package:flutter/material.dart';

import '../../core/auth/auth_controller.dart';
import '../../core/theme/ui_text.dart';
import '../guardian/guardian_ui.dart';
import '../shared/private_media.dart';

const honorFallbackAssets = <String, List<String>>{
  'coach': [
    'adult-neutral',
    'adult-male',
    'adult-female',
    'coach-01',
    'coach-02',
  ],
  'member': [
    'adult-neutral',
    'adult-male',
    'adult-female',
    'guardian-01',
    'guardian-02',
  ],
  'player': [
    'child-neutral',
    'child-male',
    'child-female',
    'child-01',
    'child-02',
  ],
};
String honorFallback(String role, Object? value) {
  final allowed = honorFallbackAssets[role]!;
  final selected = allowed.contains(value) ? value.toString() : allowed.first;
  return selected.endsWith('01') || selected.endsWith('02')
      ? 'assets/demo/portraits/$selected.jpg'
      : 'assets/avatars/$selected.svg';
}

class HonorPortraitCard extends StatelessWidget {
  const HonorPortraitCard({
    super.key,
    required this.auth,
    required this.name,
    required this.reference,
    required this.details,
    required this.onTap,
    this.category,
    this.compact = false,
    this.gender,
    this.identity,
    this.avatarRole,
    this.fallbackAsset,
    this.height = 360,
    this.photoHeight,
    this.compactPortraitSize = 64,
    this.categoryIcon = Icons.workspace_premium_outlined,
  });
  final bool compact;
  final double compactPortraitSize;
  final AuthController auth;
  final String name;
  final Object? reference;
  final List<(String, Color)> details;
  final VoidCallback onTap;
  final String? category, fallbackAsset, identity, avatarRole;
  final Object? gender;
  final double height;
  final double? photoHeight;
  final IconData categoryIcon;
  @override
  Widget build(BuildContext context) {
    if (compact) {
      return PersonSummaryCard(
        auth: auth,
        name: name,
        reference: reference,
        details: details,
        onTap: onTap,
        gender: gender,
        identity: identity,
        role: avatarRole == 'member' ? 'guardian' : avatarRole ?? 'player',
        portraitSize: compactPortraitSize,
      );
    }
    final theme = Theme.of(context);
    final fallback =
        fallbackAsset == null || fallbackAsset!.endsWith('-neutral.svg')
        ? IllustratedAvatar(
            adult: avatarRole != 'player',
            gender: gender,
            identity: identity ?? name,
            role: avatarRole == 'member' ? 'guardian' : avatarRole,
          )
        : Stack(
            fit: StackFit.expand,
            children: [
              if (fallbackAsset!.endsWith('.svg'))
                SvgPicture.asset(fallbackAsset!, fit: BoxFit.cover)
              else
                Image.asset(
                  fallbackAsset!,
                  fit: BoxFit.cover,
                  alignment: Alignment.topCenter,
                ),
              const PositionedDirectional(
                top: 10,
                end: 10,
                child: DecoratedBox(
                  decoration: BoxDecoration(
                    color: Color(0xDDFFFFFF),
                    borderRadius: BorderRadius.all(Radius.circular(8)),
                  ),
                  child: Padding(
                    padding: EdgeInsets.all(5),
                    child: UiText(
                      'صورة رمزية',
                      style: TextStyle(fontSize: 8, color: Color(0xFF343434)),
                    ),
                  ),
                ),
              ),
            ],
          );
    if (photoHeight != null) {
      return SizedBox(
        height: height,
        child: Material(
          color: theme.colorScheme.surface,
          shape: RoundedRectangleBorder(
            borderRadius: BorderRadius.circular(20),
            side: BorderSide(color: theme.colorScheme.outline),
          ),
          clipBehavior: Clip.antiAlias,
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              if (category != null)
                Padding(
                  padding: const EdgeInsetsDirectional.fromSTEB(12, 6, 4, 6),
                  child: Row(
                    children: [
                      Icon(
                        categoryIcon,
                        color: theme.colorScheme.primary,
                        size: 22,
                      ),
                      const SizedBox(width: 8),
                      Expanded(
                        child: UiText(
                          category!,
                          maxLines: 3,
                          overflow: TextOverflow.ellipsis,
                          style: const TextStyle(
                            fontSize: 16,
                            fontWeight: FontWeight.w700,
                            height: 1.3,
                          ),
                        ),
                      ),
                      SizedBox(
                        width: 80,
                        child: TextButton(
                          style: TextButton.styleFrom(
                            padding: const EdgeInsets.symmetric(horizontal: 4),
                          ),
                          onPressed: onTap,
                          child: const UiText(
                            'عرض الكل',
                            style: TextStyle(
                              fontSize: 11,
                              fontWeight: FontWeight.w700,
                            ),
                          ),
                        ),
                      ),
                    ],
                  ),
                ),
              InkWell(
                onTap: onTap,
                child: SizedBox(
                  key: const ValueKey('honor-photo-viewport'),
                  height: photoHeight,
                  child: Stack(
                    fit: StackFit.expand,
                    children: [
                      if (isPrivateAsset(reference))
                        PrivateImage(
                          auth: auth,
                          path: '$reference',
                          height: height,
                          alignment: Alignment.topCenter,
                          fallback: fallback,
                        )
                      else if (RegExp(
                        r'^/demo-assets/portraits/(coach|guardian|child)-0[12]\.jpg$',
                      ).hasMatch('$reference'))
                        Image.asset(
                          'assets/demo/portraits/${reference.toString().split('/').last}',
                          fit: BoxFit.cover,
                          alignment: Alignment.topCenter,
                          errorBuilder: (_, _, _) => fallback,
                        )
                      else
                        fallback,
                    ],
                  ),
                ),
              ),
              Expanded(
                child: InkWell(
                  onTap: onTap,
                  child: SingleChildScrollView(
                    child: Container(
                      padding: const EdgeInsets.all(12),
                      color: theme.colorScheme.surface.withValues(alpha: .96),
                      child: Column(
                        crossAxisAlignment: CrossAxisAlignment.start,
                        mainAxisSize: MainAxisSize.min,
                        children: [
                          UiText(
                            name,
                            maxLines: 2,
                            overflow: TextOverflow.ellipsis,
                            style: const TextStyle(
                              fontSize: 15,
                              fontWeight: FontWeight.w700,
                            ),
                          ),
                          const SizedBox(height: 7),
                          Wrap(
                            spacing: 5,
                            runSpacing: 5,
                            children: [
                              for (final detail in details)
                                Container(
                                  padding: const EdgeInsets.symmetric(
                                    horizontal: 7,
                                    vertical: 4,
                                  ),
                                  decoration: BoxDecoration(
                                    color: detail.$2.withValues(
                                      alpha: theme.brightness == Brightness.dark
                                          ? .2
                                          : .09,
                                    ),
                                    borderRadius: BorderRadius.circular(7),
                                  ),
                                  child: UiText(
                                    detail.$1,
                                    style: TextStyle(
                                      fontSize: 10,
                                      color: theme.brightness == Brightness.dark
                                          ? Color.lerp(
                                              detail.$2,
                                              Colors.white,
                                              .6,
                                            )
                                          : detail.$2,
                                      fontWeight: FontWeight.w600,
                                    ),
                                  ),
                                ),
                            ],
                          ),
                        ],
                      ),
                    ),
                  ),
                ),
              ),
            ],
          ),
        ),
      );
    }
    return SizedBox(
      height: height,
      child: Material(
        color: theme.colorScheme.surface,
        shape: RoundedRectangleBorder(
          borderRadius: BorderRadius.circular(20),
          side: BorderSide(color: theme.colorScheme.outline),
        ),
        clipBehavior: Clip.antiAlias,
        child: InkWell(
          onTap: onTap,
          child: Stack(
            fit: StackFit.expand,
            children: [
              if (isPrivateAsset(reference))
                PrivateImage(
                  auth: auth,
                  path: '$reference',
                  height: height,
                  alignment: Alignment.topCenter,
                  fallback: fallback,
                )
              else if (RegExp(
                r'^/demo-assets/portraits/(coach|guardian|child)-0[12]\.jpg$',
              ).hasMatch('$reference'))
                Image.asset(
                  'assets/demo/portraits/${reference.toString().split('/').last}',
                  fit: BoxFit.cover,
                  alignment: Alignment.topCenter,
                  errorBuilder: (_, _, _) => fallback,
                )
              else
                fallback,
              PositionedDirectional(
                bottom: 0,
                start: 0,
                end: 0,
                child: Container(
                  padding: const EdgeInsets.all(12),
                  color: theme.colorScheme.surface.withValues(alpha: .96),
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    mainAxisSize: MainAxisSize.min,
                    children: [
                      UiText(
                        name,
                        maxLines: 2,
                        overflow: TextOverflow.ellipsis,
                        style: const TextStyle(
                          fontSize: 15,
                          fontWeight: FontWeight.w700,
                        ),
                      ),
                      const SizedBox(height: 7),
                      Wrap(
                        spacing: 5,
                        runSpacing: 5,
                        children: [
                          for (final detail in details)
                            Container(
                              padding: const EdgeInsets.symmetric(
                                horizontal: 7,
                                vertical: 4,
                              ),
                              decoration: BoxDecoration(
                                color: detail.$2.withValues(
                                  alpha: theme.brightness == Brightness.dark
                                      ? .2
                                      : .09,
                                ),
                                borderRadius: BorderRadius.circular(7),
                              ),
                              child: UiText(
                                detail.$1,
                                style: TextStyle(
                                  fontSize: 10,
                                  color: theme.brightness == Brightness.dark
                                      ? Color.lerp(detail.$2, Colors.white, .6)
                                      : detail.$2,
                                  fontWeight: FontWeight.w600,
                                ),
                              ),
                            ),
                        ],
                      ),
                    ],
                  ),
                ),
              ),
            ],
          ),
        ),
      ),
    );
  }
}

List<(String, Color)> coachDetails(Json coach, List<Json> branches) {
  const palette = [
    Color(0xFF6941A5),
    Color(0xFF126C81),
    Color(0xFF3155A4),
    Color(0xFF236D46),
  ];
  final branchNames = (coach['branches'] as List? ?? [])
      .map(
        (id) => branches.where((b) => b['id'] == id).firstOrNull?['arabicName'],
      )
      .whereType<String>();
  return [
    if (coach['parentRating'] != null)
      (
        '★ ${(coach['parentRating'] as num).toStringAsFixed(1)}',
        const Color(0xFF9A5719),
      ),
    for (final name in branchNames) (name, palette[0]),
    for (final sport in (coach['sports'] as List? ?? []))
      ('$sport', palette[1]),
    ('${coach['groupCount']} مجموعات', palette[2]),
    ('${coach['sessionCount']} حصص', palette[3]),
    ('${coach['parentRatingCount'] ?? 0} تقييمات', const Color(0xFF8A3654)),
  ];
}
