import 'package:flutter/material.dart';

import '../../core/theme/ui_text.dart';
import '../guardian/guardian_ui.dart';
import '../../core/theme/academy_home_skin.dart';

String? ownerDemoBranchPhoto(String academy, String branch) {
  if (!academy.toLowerCase().contains('professional')) return null;
  if (branch.contains('مدينتي')) return 'assets/demo/branch-madinaty.webp';
  if (branch.contains('الرحاب')) return 'assets/demo/branch-rehab.webp';
  if (branch.contains('الشروق')) return 'assets/demo/branch-shorouk.webp';
  return null;
}

class OwnerBranchRail extends StatelessWidget {
  const OwnerBranchRail({
    super.key,
    required this.branches,
    required this.branchId,
    required this.academy,
    required this.onChanged,
  });

  final List<Json> branches;
  final String branchId, academy;
  final ValueChanged<String> onChanged;

  @override
  Widget build(BuildContext context) {
    final colors = Theme.of(context).colorScheme;
    final accent = academyAccent(context);
    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        Row(
          children: [
            const Expanded(
              child: UiText(
                'الفروع',
                style: TextStyle(fontSize: 18, fontWeight: FontWeight.w800),
              ),
            ),
            if (branchId.isNotEmpty)
              TextButton.icon(
                onPressed: () => onChanged(''),
                icon: const Icon(Icons.filter_alt_off_outlined, size: 16),
                label: const UiText('إلغاء التصفية'),
              )
            else
              Padding(
                padding: const EdgeInsets.symmetric(vertical: 10),
                child: UiText(
                  'كل الفروع',
                  style: TextStyle(
                    fontSize: 12,
                    color: colors.onSurfaceVariant,
                  ),
                ),
              ),
          ],
        ),
        if (branches.isNotEmpty)
          SizedBox(
            height: 94,
            child: ListView.separated(
              key: const ValueKey('owner-branch-rail'),
              scrollDirection: Axis.horizontal,
              itemCount: branches.length,
              separatorBuilder: (_, _) => const SizedBox(width: 8),
              itemBuilder: (context, index) {
                final branch = branches[index];
                final id = textOf(branch['id']);
                final name = textOf(branch['arabicName'] ?? branch['name']);
                final image = ownerDemoBranchPhoto(academy, name);
                final selected = id == branchId;
                final border = BorderRadius.circular(16);
                return Semantics(
                  selected: selected,
                  button: true,
                  child: SizedBox(
                    width: 140,
                    child: Material(
                      borderRadius: border,
                      clipBehavior: Clip.antiAlias,
                      color: colors.surfaceContainerHighest,
                      child: InkWell(
                        onTap: () => onChanged(selected ? '' : id),
                        child: Stack(
                          fit: StackFit.expand,
                          children: [
                            if (image != null)
                              Image.asset(
                                image,
                                fit: BoxFit.cover,
                                errorBuilder: (_, _, _) =>
                                    const _BranchPlaceholder(),
                              )
                            else
                              const _BranchPlaceholder(),
                            const DecoratedBox(
                              decoration: BoxDecoration(
                                gradient: LinearGradient(
                                  begin: Alignment.bottomCenter,
                                  end: Alignment.topCenter,
                                  colors: [
                                    Color(0xED09202B),
                                    Color(0x1509202B),
                                  ],
                                ),
                              ),
                            ),
                            if (selected)
                              PositionedDirectional(
                                top: 7,
                                end: 7,
                                child: Icon(
                                  Icons.check_circle_rounded,
                                  color: accent,
                                  size: 20,
                                ),
                              ),
                            Align(
                              alignment: AlignmentDirectional.bottomStart,
                              child: Padding(
                                padding: const EdgeInsets.all(10),
                                child: UiText(
                                  name,
                                  maxLines: 2,
                                  style: const TextStyle(
                                    color: Colors.white,
                                    fontWeight: FontWeight.w800,
                                    height: 1.2,
                                  ),
                                ),
                              ),
                            ),
                            if (selected)
                              IgnorePointer(
                                child: DecoratedBox(
                                  decoration: BoxDecoration(
                                    borderRadius: border,
                                    border: Border.all(color: accent, width: 2),
                                  ),
                                ),
                              ),
                          ],
                        ),
                      ),
                    ),
                  ),
                );
              },
            ),
          ),
        const SizedBox(height: 14),
      ],
    );
  }
}

class _BranchPlaceholder extends StatelessWidget {
  const _BranchPlaceholder();

  @override
  Widget build(BuildContext context) => const Align(
    alignment: Alignment.topCenter,
    child: Padding(
      padding: EdgeInsets.only(top: 10),
      child: Icon(Icons.location_on_outlined, size: 32),
    ),
  );
}
