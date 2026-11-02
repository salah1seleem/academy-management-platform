import 'package:flutter/material.dart';

import '../../core/auth/auth_controller.dart';
import '../../core/theme/ui_text.dart';
import '../guardian/guardian_ui.dart';

class PersonSummaryCard extends StatelessWidget {
  const PersonSummaryCard({
    super.key,
    required this.auth,
    required this.name,
    required this.reference,
    required this.details,
    required this.onTap,
    this.gender,
    this.identity,
    this.role = 'player',
    this.portraitSize = 64,
  });
  final AuthController auth;
  final String name, role;
  final Object? reference, gender;
  final String? identity;
  final List<(String, Color)> details;
  final double portraitSize;
  final VoidCallback onTap;
  @override
  Widget build(BuildContext context) => GCard(
    onTap: onTap,
    child: Row(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        PlayerAvatar(
          name,
          auth: auth,
          reference: reference,
          gender: gender,
          identity: identity,
          avatarRole: role,
          adult: role != 'player',
          size: portraitSize,
        ),
        const SizedBox(width: 14),
        Expanded(
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              UiText(
                name,
                style: const TextStyle(
                  fontSize: 16,
                  fontWeight: FontWeight.w800,
                ),
              ),
              const SizedBox(height: 8),
              Wrap(
                spacing: 6,
                runSpacing: 6,
                children: [
                  for (final d in details)
                    Container(
                      padding: const EdgeInsets.symmetric(
                        horizontal: 8,
                        vertical: 5,
                      ),
                      decoration: BoxDecoration(
                        color: Theme.of(context)
                            .colorScheme
                            .surfaceContainerHighest,
                        borderRadius: BorderRadius.circular(8),
                      ),
                      child: UiText(
                        d.$1,
                        style: TextStyle(
                          fontSize: 11,
                          color: Theme.of(context).colorScheme.onSurfaceVariant,
                        ),
                      ),
                    ),
                ],
              ),
            ],
          ),
        ),
        const SizedBox(width: 4),
        const Icon(Icons.chevron_left, size: 20),
      ],
    ),
  );
}
