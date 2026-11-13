import 'package:flutter/material.dart';

import '../../core/theme/academy_brand.dart';
import '../../core/theme/academy_home_skin.dart';
import '../../core/theme/ui_text.dart';
import '../../core/theme/ocean_palette.dart';

class SubscriptionIdentityCard extends StatelessWidget {
  const SubscriptionIdentityCard({
    super.key,
    required this.data,
    required this.owner,
    required this.onTotal,
    required this.onStatus,
    this.onPlayers,
    this.newSubscriptions,
  });
  final Map<String, dynamic> data;
  final bool owner;
  final num? newSubscriptions;
  final VoidCallback onTotal;
  final VoidCallback? onPlayers;
  final ValueChanged<String> onStatus;
  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context), dark = theme.brightness == Brightness.dark;
    final ink = theme.colorScheme.onSurface,
        muted = theme.colorScheme.onSurfaceVariant;
    final ocean = OceanPalette.active(theme);
    final logo = BrandScope.maybeOf(context)?.logo != null;
    final asOf = DateTime.tryParse('${data['asOf']}');
    final statuses = [
      (
        'current',
        'لا تنتهي قريبًا',
        ocean
            ? OceanPalette.positive
            : dark
            ? const Color(0xFF5BE0B5)
            : const Color(0xFF13764E),
      ),
      (
        'expiring',
        'تنتهي قريبًا',
        ocean
            ? OceanPalette.warning
            : dark
            ? const Color(0xFFFFD477)
            : const Color(0xFF8C570B),
      ),
      (
        'expired',
        'منتهية',
        ocean
            ? OceanPalette.negative
            : dark
            ? const Color(0xFFFF969D)
            : const Color(0xFFB42B40),
      ),
    ];
    return AnimatedContainer(
      key: const ValueKey('subscription-home-hero'),
      duration: MediaQuery.disableAnimationsOf(context)
          ? Duration.zero
          : const Duration(milliseconds: 240),
      curve: Curves.easeOutCubic,
      decoration: BoxDecoration(
        gradient: LinearGradient(
          begin: Alignment.topRight,
          end: Alignment.bottomLeft,
          colors: [
            Color.alphaBlend(
              academyAccent(context).withValues(alpha: dark ? .12 : .045),
              theme.colorScheme.surface,
            ),
            theme.colorScheme.surface,
          ],
        ),
        borderRadius: BorderRadius.circular(20),
        border: Border.all(
          color: academyAccent(context).withValues(alpha: .13),
        ),
        boxShadow: [
          BoxShadow(
            color: academyAccent(context).withValues(alpha: dark ? .03 : .055),
            blurRadius: 20,
            offset: const Offset(0, 6),
          ),
        ],
      ),
      clipBehavior: Clip.antiAlias,
      child: Padding(
        padding: const EdgeInsets.all(14),
        child: Column(
          mainAxisSize: MainAxisSize.min,
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            Row(
              children: [
                Expanded(
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      UiText(
                        owner ? 'اشتراكات الأكاديمية' : 'اشتراكات نطاقك',
                        style: TextStyle(
                          fontSize: 18,
                          fontWeight: FontWeight.w800,
                          color: ink,
                        ),
                      ),
                      if (asOf != null)
                        UiText(
                          'الحالة في ${MaterialLocalizations.of(context).formatShortDate(asOf)}',
                          style: TextStyle(fontSize: 11, color: muted),
                        ),
                      TextButton(
                        onPressed: onTotal,
                        style: TextButton.styleFrom(
                          padding: EdgeInsets.zero,
                          alignment: AlignmentDirectional.centerStart,
                          minimumSize: const Size(44, 48),
                          foregroundColor: ink,
                        ),
                        child: Wrap(
                          spacing: 8,
                          crossAxisAlignment: WrapCrossAlignment.center,
                          children: [
                            UiText(
                              '${data['active']}',
                              style: TextStyle(
                                fontSize: 38,
                                height: 1.15,
                                fontWeight: FontWeight.w800,
                                color: ink,
                              ),
                            ),
                            UiText(
                              'إجمالي الاشتراكات السارية',
                              style: TextStyle(fontSize: 11, color: muted),
                            ),
                          ],
                        ),
                      ),
                      if (newSubscriptions != null)
                        UiText(
                          '$newSubscriptions اشتراكات جديدة خلال الفترة',
                          style: TextStyle(
                            fontSize: 12,
                            fontWeight: FontWeight.w700,
                            color: academyAccent(context),
                          ),
                        ),
                    ],
                  ),
                ),
                const SizedBox(width: 8),
                SizedBox(
                  key: const ValueKey('subscription-identity-art'),
                  width: 86,
                  height: 78,
                  child: logo
                      ? const AcademyLogoMark(opacity: 1)
                      : Icon(
                          Icons.school_outlined,
                          size: 46,
                          color: theme.colorScheme.outlineVariant,
                        ),
                ),
              ],
            ),
            const SizedBox(height: 6),
            LayoutBuilder(
              builder: (context, b) {
                final full = MediaQuery.textScalerOf(context).scale(1) > 1.4;
                return Wrap(
                  spacing: 6,
                  runSpacing: 6,
                  children: [
                    for (final s in statuses)
                      SizedBox(
                        width: full ? b.maxWidth : (b.maxWidth - 12) / 3,
                        child: Material(
                          color: Color.alphaBlend(
                            s.$3.withValues(alpha: dark ? .10 : .055),
                            academyIdentityBase(context),
                          ),
                          borderRadius: BorderRadius.circular(12),
                          clipBehavior: Clip.antiAlias,
                          child: InkWell(
                            onTap: () => onStatus(s.$1),
                            child: Padding(
                              padding: const EdgeInsets.symmetric(
                                horizontal: 6,
                                vertical: 7,
                              ),
                              child: Column(
                                children: [
                                  UiText(
                                    '${s.$1 == 'current' ? ((data['active'] as num) - (data['expiring'] as num)).clamp(0, double.infinity).toInt() : data[s.$1]}',
                                    style: TextStyle(
                                      color: s.$3,
                                      fontSize: 25,
                                      fontWeight: FontWeight.w800,
                                      height: 1.2,
                                    ),
                                  ),
                                  const SizedBox(height: 3),
                                  UiText(
                                    s.$2,
                                    style: TextStyle(color: ink, fontSize: 11),
                                  ),
                                ],
                              ),
                            ),
                          ),
                        ),
                      ),
                  ],
                );
              },
            ),
            const SizedBox(height: 8),
            TextButton(
              onPressed: onPlayers,
              style: TextButton.styleFrom(
                alignment: AlignmentDirectional.centerStart,
                padding: EdgeInsets.zero,
                minimumSize: const Size(44, 40),
                tapTargetSize: MaterialTapTargetSize.shrinkWrap,
              ),
              child: UiText(
                'قائمة اللاعبين · ${data['players']} لاعب',
                style: TextStyle(
                  fontSize: 12,
                  fontWeight: FontWeight.w700,
                  color: ink,
                ),
              ),
            ),
          ],
        ),
      ),
    );
  }
}
