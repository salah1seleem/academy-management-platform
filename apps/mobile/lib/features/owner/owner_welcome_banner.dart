import 'package:flutter/material.dart';
import 'package:flutter_svg/flutter_svg.dart';

import '../../core/theme/ui_text.dart';

/// Decorative academy artwork; operational figures remain in their live cards.
class OwnerWelcomeBanner extends StatelessWidget {
  const OwnerWelcomeBanner({super.key});
  @override
  Widget build(BuildContext context) {
    final colors = Theme.of(context).colorScheme;
    return ClipRRect(
      borderRadius: BorderRadius.circular(24),
      child: DecoratedBox(
        decoration: BoxDecoration(
          gradient: LinearGradient(
            begin: AlignmentDirectional.centerStart,
            end: AlignmentDirectional.centerEnd,
            colors: [colors.primaryContainer, colors.surfaceContainerHighest],
          ),
        ),
        child: Stack(
          children: [
            PositionedDirectional(
              end: -30,
              top: -20,
              bottom: -35,
              width: 185,
              child: ExcludeSemantics(
                child: Opacity(
                  opacity: .5,
                  child: SvgPicture.asset(
                    'assets/home/subscriptions-general.svg',
                    fit: BoxFit.cover,
                  ),
                ),
              ),
            ),
            Padding(
              padding: const EdgeInsets.all(22),
              child: Row(
                children: [
                  Expanded(
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        UiText(
                          'كل يوم… فرصة لبطل جديد',
                          style: TextStyle(
                            color: colors.onPrimaryContainer,
                            fontSize: 23,
                            fontWeight: FontWeight.w800,
                            height: 1.5,
                          ),
                        ),
                        const SizedBox(height: 4),
                        UiText(
                          'أكاديميتك تتحرك معك',
                          style: TextStyle(
                            color: colors.onPrimaryContainer,
                            fontSize: 13,
                          ),
                        ),
                      ],
                    ),
                  ),
                  const SizedBox(width: 24),
                  Icon(
                    Icons.auto_awesome_rounded,
                    color: colors.primary,
                    size: 42,
                  ),
                ],
              ),
            ),
          ],
        ),
      ),
    );
  }
}
