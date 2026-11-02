import 'package:flutter/material.dart';

import '../../core/theme/ui_text.dart';

class RoleHomeIntro extends StatelessWidget {
  const RoleHomeIntro({
    super.key,
    required this.title,
    required this.subtitle,
    this.trailing,
  });
  final String title, subtitle;
  final Widget? trailing;
  @override
  Widget build(BuildContext context) => Padding(
    padding: const EdgeInsets.only(top: 10, bottom: 20),
    child: Row(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Expanded(
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              UiText(title, style: Theme.of(context).textTheme.headlineSmall),
              const SizedBox(height: 3),
              UiText(subtitle, style: Theme.of(context).textTheme.bodySmall),
            ],
          ),
        ),
        if (trailing != null) ...[const SizedBox(width: 12), trailing!],
      ],
    ),
  );
}
