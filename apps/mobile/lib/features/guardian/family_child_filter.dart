import 'package:flutter/material.dart';

import '../../core/theme/appearance.dart';
import '../../core/theme/ui_text.dart';
import 'guardian_ui.dart';

class FamilyChildFilter extends StatelessWidget {
  const FamilyChildFilter({
    super.key,
    required this.children,
    required this.selected,
    required this.onSelected,
    this.enabled = true,
    this.allLabel,
    this.wrap = false,
  });
  final List<Json> children;
  final String selected;
  final ValueChanged<String> onSelected;
  final bool enabled;
  final String? allLabel;
  final bool wrap;
  @override
  Widget build(BuildContext context) {
    final colors = Theme.of(context).colorScheme;
    final buttons = <Widget>[
      for (final child in <Json>[
        {
          'id': '',
          'arabicName':
              allLabel ?? localized(context, 'كل الأبناء', 'All children'),
        },
        ...children,
      ])
        Padding(
          padding: const EdgeInsetsDirectional.only(end: 6),
          child: Semantics(
            key: ValueKey('family-filter-${child['id']}'),
            button: true,
            selected: selected == child['id'],
            enabled: enabled,
            child: Material(
              color: Colors.transparent,
              child: InkWell(
                onTap: enabled ? () => onSelected(textOf(child['id'])) : null,
                child: Padding(
                  padding: const EdgeInsets.symmetric(vertical: 4),
                  child: Container(
                    constraints: const BoxConstraints(minHeight: 36),
                    padding: const EdgeInsets.symmetric(
                      horizontal: 12,
                      vertical: 6,
                    ),
                    decoration: BoxDecoration(
                      color: selected == child['id']
                          ? colors.primary
                          : Colors.transparent,
                      border: Border.all(
                        color: selected == child['id']
                            ? colors.primary
                            : colors.outlineVariant,
                      ),
                    ),
                    child: Row(
                      mainAxisSize: MainAxisSize.min,
                      children: [
                        if (child['id'] != '') ...[
                          SizedBox(
                            width: 8,
                            height: 8,
                            child: ColoredBox(
                              color: selected == child['id']
                                  ? colors.onPrimary
                                  : colors.primary,
                            ),
                          ),
                          const SizedBox(width: 6),
                        ],
                        Flexible(
                          child: UiText(
                            textOf(child['arabicName']),
                            maxLines: wrap ? 2 : null,
                            overflow: wrap ? TextOverflow.ellipsis : null,
                            style: TextStyle(
                              fontSize: 13,
                              fontWeight: FontWeight.w600,
                              color: selected == child['id']
                                  ? colors.onPrimary
                                  : colors.onSurfaceVariant,
                            ),
                          ),
                        ),
                      ],
                    ),
                  ),
                ),
              ),
            ),
          ),
        ),
    ];
    const filterKey = ValueKey('family-child-filter');
    if (wrap) {
      return Wrap(key: filterKey, children: buttons);
    }
    return SingleChildScrollView(
      key: filterKey,
      scrollDirection: Axis.horizontal,
      child: Row(children: buttons),
    );
  }
}
