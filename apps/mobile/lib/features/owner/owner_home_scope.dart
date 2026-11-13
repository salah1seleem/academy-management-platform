import 'package:flutter/material.dart';

import '../../core/theme/academy_home_skin.dart';
import '../../core/theme/ui_text.dart';
import '../guardian/guardian_ui.dart';

const _months = [
  'يناير',
  'فبراير',
  'مارس',
  'أبريل',
  'مايو',
  'يونيو',
  'يوليو',
  'أغسطس',
  'سبتمبر',
  'أكتوبر',
  'نوفمبر',
  'ديسمبر',
];
String _date(DateTime value) =>
    '${value.day} ${_months[value.month - 1]} ${value.year}';

enum OwnerPeriodPreset { range, today, month }

class OwnerHomePeriod {
  const OwnerHomePeriod({this.preset = OwnerPeriodPreset.range, this.custom});

  final OwnerPeriodPreset preset;
  final DateTimeRange? custom;

  DateTimeRange dates(DateTime today, DateTime trailingStart) =>
      switch (preset) {
        OwnerPeriodPreset.range =>
          custom ?? DateTimeRange(start: trailingStart, end: today),
        OwnerPeriodPreset.today => DateTimeRange(start: today, end: today),
        OwnerPeriodPreset.month => DateTimeRange(
          start: DateTime(today.year, today.month),
          end: today,
        ),
      };
}

class OwnerPeriodControls extends StatelessWidget {
  const OwnerPeriodControls({
    super.key,
    required this.period,
    required this.today,
    required this.trailingStart,
    required this.onChanged,
    this.sectionLabel,
  });

  final OwnerHomePeriod period;
  final String? sectionLabel;
  final DateTime today, trailingStart;
  final ValueChanged<OwnerHomePeriod> onChanged;

  @override
  Widget build(BuildContext context) {
    final colors = Theme.of(context).colorScheme;
    final accent = academyAccent(context);
    final range =
        period.custom ?? DateTimeRange(start: trailingStart, end: today);
    final applied = period.dates(today, trailingStart);
    final label = applied.start == applied.end
        ? _date(applied.end)
        : 'من ${_date(applied.start)} إلى ${_date(applied.end)}';
    Widget button(
      OwnerPeriodPreset preset,
      Widget content,
      VoidCallback action,
    ) {
      final active = period.preset == preset;
      return Semantics(
        selected: active,
        child: Material(
          color: active
              ? Color.alphaBlend(accent.withValues(alpha: .14), colors.surface)
              : colors.surface,
          borderRadius: BorderRadius.circular(12),
          clipBehavior: Clip.antiAlias,
          child: InkWell(
            onTap: action,
            child: Container(
              constraints: const BoxConstraints(minHeight: 60),
              alignment: Alignment.center,
              padding: const EdgeInsets.all(5),
              decoration: BoxDecoration(
                borderRadius: BorderRadius.circular(12),
                border: Border.all(
                  color: active ? accent : colors.outlineVariant,
                ),
              ),
              child: content,
            ),
          ),
        ),
      );
    }

    Future<void> pick() async {
      final firstDate = DateTime(2000);
      final lastDate = DateTime(today.year, today.month, today.day + 365);
      final initial =
          range.start.isBefore(firstDate) || range.end.isAfter(lastDate)
          ? DateTimeRange(start: trailingStart, end: today)
          : range;
      final picked = await showDateRangePicker(
        context: context,
        initialDateRange: initial,
        currentDate: today,
        firstDate: firstDate,
        lastDate: lastDate,
        helpText: 'اختيار الفترة',
        saveText: 'تطبيق',
        locale: const Locale('ar', 'EG'),
        textDirection: TextDirection.rtl,
      );
      if (!context.mounted || picked == null) return;
      DateTime utcDay(DateTime day) =>
          DateTime.utc(day.year, day.month, day.day);
      if (utcDay(picked.end).difference(utcDay(picked.start)).inDays > 366) {
        ScaffoldMessenger.of(context).showSnackBar(
          const SnackBar(content: UiText('اختر فترة لا تتجاوز 366 يومًا.')),
        );
        return;
      }
      if (period.preset != OwnerPeriodPreset.range || picked != range) {
        onChanged(OwnerHomePeriod(custom: picked));
      }
    }

    return Semantics(
      container: true,
      explicitChildNodes: true,
      label: sectionLabel == null ? null : 'فترة $sectionLabel',
      child: Padding(
        padding: const EdgeInsets.only(bottom: 10),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            UiText(
              label,
              style: TextStyle(fontSize: 13, color: colors.onSurfaceVariant),
            ),
            const SizedBox(height: 6),
            Row(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                for (final option in [
                  (OwnerPeriodPreset.range, 'الفترة المخصصة'),
                  (OwnerPeriodPreset.today, 'هذا اليوم'),
                  (OwnerPeriodPreset.month, 'من أول الشهر'),
                ]) ...[
                  if (option.$1 != OwnerPeriodPreset.range)
                    const SizedBox(width: 6),
                  Expanded(
                    child: button(
                      option.$1,
                      UiText(
                        option.$2,
                        textAlign: TextAlign.center,
                        style: const TextStyle(
                          fontSize: 12,
                          fontWeight: FontWeight.w700,
                        ),
                      ),
                      option.$1 == OwnerPeriodPreset.range
                          ? pick
                          : () {
                              if (option.$1 != period.preset) {
                                onChanged(
                                  OwnerHomePeriod(
                                    preset: option.$1,
                                    custom: period.custom,
                                  ),
                                );
                              }
                            },
                    ),
                  ),
                ],
              ],
            ),
          ],
        ),
      ),
    );
  }
}

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
