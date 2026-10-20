import 'package:flutter/material.dart';

import '../../core/auth/auth_controller.dart';
import '../../core/theme/academy_brand.dart';
import '../../core/theme/appearance.dart';
import '../../core/theme/ui_text.dart';
import '../guardian/guardian_ui.dart';
import 'notification_inbox.dart';
import 'page_video.dart';

abstract final class ExperienceColors {
  static const green = Color(0xFF49D6A0),
      amber = Color(0xFFF5BE61),
      blue = Color(0xFF83B9FF),
      red = Color(0xFFFF7989),
      gold = Color(0xFFF3D083);
}

/// Calendar arithmetic, not a fixed 30-day duration. Jan31 -> Dec31; Mar31 -> Feb28.
DateTime trailingMonth(DateTime end) {
  final last = DateTime(end.year, end.month, 0).day;
  return DateTime(end.year, end.month - 1, end.day > last ? last : end.day);
}

String isoDay(DateTime d) =>
    '${d.year.toString().padLeft(4, '0')}-${d.month.toString().padLeft(2, '0')}-${d.day.toString().padLeft(2, '0')}';

class ExperienceHeader extends StatelessWidget {
  const ExperienceHeader({
    super.key,
    required this.auth,
    required this.subtitle,
    required this.lesson,
    this.account,
  });
  final AuthController auth;
  final String subtitle, lesson;
  final Widget? account;
  @override
  Widget build(BuildContext context) => Padding(
    padding: const EdgeInsets.only(bottom: 20),
    child: Row(
      children: [
        const AcademyCrest(size: 48),
        const SizedBox(width: 12),
        Expanded(
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              UiText(
                BrandScope.maybeOf(context)?.name ??
                    auth.session?.selected?.academyName ??
                    'الأكاديمية',
                maxLines: 2,
                style: const TextStyle(
                  fontSize: 17,
                  fontWeight: FontWeight.w800,
                ),
              ),
              UiText(
                subtitle,
                style: TextStyle(
                  color: Theme.of(context).colorScheme.onSurfaceVariant,
                  fontSize: 12,
                ),
              ),
            ],
          ),
        ),
        NotificationBell(auth: auth),
        PageVideoButton(auth: auth, screen: 'home'),
        if (account != null)
          IconButton(
            tooltip: 'حسابي',
            onPressed: () => openPage(context, account!),
            icon: const Icon(Icons.person_outline_rounded),
          ),
      ],
    ),
  );
}

class ScopeDateTiles extends StatelessWidget {
  const ScopeDateTiles({
    super.key,
    required this.from,
    required this.to,
    required this.onChanged,
    this.onClear,
  });
  final String from, to;
  final void Function(String, String) onChanged;
  final VoidCallback? onClear;
  Future<void> choose(BuildContext context, bool start) async {
    final date = DateTime.tryParse(start ? from : to) ?? DateTime.now();
    final chosen = await showDatePicker(
      context: context,
      initialDate: date,
      firstDate: DateTime(2000),
      lastDate: DateTime(2100),
      helpText: start ? 'من تاريخ' : 'إلى تاريخ',
    );
    if (chosen == null || !context.mounted) return;
    final value = isoDay(chosen);
    if (start) {
      onChanged(value, to.isNotEmpty && value.compareTo(to) > 0 ? value : to);
    } else {
      onChanged(
        from.isNotEmpty && value.compareTo(from) < 0 ? value : from,
        value,
      );
    }
  }

  @override
  Widget build(BuildContext context) => Row(
    children: [
      for (final item in [('من', from, true), ('إلى', to, false)]) ...[
        Expanded(
          child: Material(
            color: Theme.of(context).colorScheme.surface,
            borderRadius: BorderRadius.circular(18),
            clipBehavior: Clip.antiAlias,
            child: InkWell(
              onTap: () => choose(context, item.$3),
              child: Padding(
                padding: const EdgeInsets.symmetric(
                  vertical: 12,
                  horizontal: 14,
                ),
                child: Wrap(
                  alignment: WrapAlignment.spaceBetween,
                  crossAxisAlignment: WrapCrossAlignment.center,
                  spacing: 6,
                  runSpacing: 8,
                  children: [
                    Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        UiText(item.$1, style: const TextStyle(fontSize: 11)),
                        UiText(
                          DateTime.tryParse(item.$2)?.day.toString() ?? '—',
                          style: const TextStyle(
                            fontSize: 27,
                            fontWeight: FontWeight.w800,
                            height: 1.35,
                          ),
                        ),
                      ],
                    ),
                    Column(
                      children: [
                        UiText(
                          _months[(DateTime.tryParse(item.$2)?.month ?? 0)],
                          style: const TextStyle(fontSize: 12),
                        ),
                        UiText(
                          DateTime.tryParse(item.$2)?.year.toString() ??
                              'كل التواريخ',
                          style: const TextStyle(fontSize: 10),
                        ),
                      ],
                    ),
                    const SizedBox(width: 6),
                    const Icon(Icons.expand_more, size: 16),
                  ],
                ),
              ),
            ),
          ),
        ),
        if (item.$3) const SizedBox(width: 10),
      ],
      if (onClear != null)
        IconButton(
          tooltip: 'مسح الفلاتر',
          onPressed: onClear,
          icon: const Icon(Icons.filter_alt_off_outlined, size: 20),
        ),
    ],
  );
  static const _months = [
    '',
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
}

class ScopeBranchTabs extends StatelessWidget {
  const ScopeBranchTabs({
    super.key,
    required this.items,
    required this.selected,
    required this.onSelected,
    this.allLabel = 'كل الفروع',
  });
  final List<Json> items;
  final String selected, allLabel;
  final ValueChanged<String> onSelected;
  @override
  Widget build(BuildContext context) => SingleChildScrollView(
    scrollDirection: Axis.horizontal,
    padding: const EdgeInsets.symmetric(vertical: 12),
    child: Row(
      children: [
        for (final b in [
          {'id': '', 'arabicName': allLabel},
          ...items,
        ])
          Padding(
            padding: const EdgeInsetsDirectional.only(end: 8),
            child: ChoiceChip(
              showCheckmark: false,
              label: UiText(textOf(b['arabicName'] ?? b['name'])),
              selected: selected == b['id'],
              onSelected: (_) => onSelected(textOf(b['id'])),
              shape: RoundedRectangleBorder(
                borderRadius: BorderRadius.circular(12),
              ),
            ),
          ),
      ],
    ),
  );
}

class ExperienceSection extends StatelessWidget {
  const ExperienceSection(
    this.title,
    this.icon, {
    super.key,
    this.action,
    this.actionLabel = 'عرض الكل',
    this.color,
  });
  final String title, actionLabel;
  final IconData icon;
  final Color? color;
  final VoidCallback? action;
  @override
  Widget build(BuildContext context) => Padding(
    padding: const EdgeInsets.only(top: 12, bottom: 10),
    child: Row(
      children: [
        Icon(icon, color: color ?? ExperienceColors.blue, size: 20),
        const SizedBox(width: 8),
        Expanded(
          child: UiText(
            title,
            style: const TextStyle(fontSize: 17, fontWeight: FontWeight.w800),
          ),
        ),
        if (action != null)
          TextButton(
            onPressed: action,
            child: UiText(actionLabel, style: const TextStyle(fontSize: 11)),
          ),
      ],
    ),
  );
}
