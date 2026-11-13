import 'package:flutter/material.dart';

import '../../core/auth/auth_controller.dart';
import '../../core/theme/academy_brand.dart';
import '../../core/theme/academy_home_skin.dart';
import '../../core/theme/appearance.dart';
import '../../core/theme/ocean_palette.dart';
import '../../core/theme/ui_text.dart';
import '../guardian/guardian_ui.dart';
import 'notification_inbox.dart';
import 'self_profile.dart';
import 'branded_header_surface.dart';

abstract final class ExperienceColors {
  static LinearGradient hero(BuildContext context) => LinearGradient(
    begin: const Alignment(-.342, -.94),
    end: const Alignment(.342, .94),
    colors: OceanPalette.active(Theme.of(context))
        ? const [OceanPalette.surface, OceanPalette.raised]
        : Theme.of(context).brightness == Brightness.light
        ? const [Color(0xFFFFFFFF), Color(0xFFF1ECE2)]
        : const [Color(0xFF2340A0), Color(0xFF0F1736)],
  );

  static Color semantic(BuildContext context, Color value) => value == green
      ? positive(context)
      : value == red
      ? negative(context)
      : value == amber
      ? warning(context)
      : value == blue
      ? information(context)
      : value;

  static Color award(BuildContext context) =>
      OceanPalette.active(Theme.of(context))
      ? OceanPalette.warning
      : Theme.of(context).brightness == Brightness.light
      ? const Color(0xFF956413)
      : gold;

  static Color positive(BuildContext context) =>
      OceanPalette.active(Theme.of(context))
      ? OceanPalette.positive
      : Theme.of(context).brightness == Brightness.light
      ? const Color(0xFF16803C)
      : green;
  static Color negative(BuildContext context) =>
      OceanPalette.active(Theme.of(context))
      ? OceanPalette.negative
      : Theme.of(context).brightness == Brightness.light
      ? const Color(0xFFC8102E)
      : red;
  static Color warning(BuildContext context) =>
      OceanPalette.active(Theme.of(context))
      ? OceanPalette.warning
      : Theme.of(context).brightness == Brightness.light
      ? const Color(0xFFB45309)
      : amber;
  static Color information(BuildContext context) =>
      OceanPalette.active(Theme.of(context))
      ? OceanPalette.accent
      : Theme.of(context).brightness == Brightness.light
      ? const Color(0xFF2165B7)
      : blue;
  static const green = Color(0xFF34D399),
      amber = Color(0xFFFBBF24),
      blue = Color(0xFF8FB0FF),
      red = Color(0xFFF87171),
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
    this.home = false,
  });
  final AuthController auth;
  final String subtitle, lesson;
  final Widget? account;
  final bool home;
  static double heightFor(
    BuildContext context,
    AuthController auth,
    String role,
  ) {
    final brand = BrandScope.maybeOf(context);
    final width = (MediaQuery.sizeOf(context).width - 242).clamp(40.0, 650.0);
    double measure(String value, double size, FontWeight weight) {
      final painter = TextPainter(
        text: TextSpan(
          text: value,
          style: Theme.of(context).textTheme.bodyMedium!
              .copyWith(fontSize: size, fontWeight: weight),
        ),
        textDirection: Directionality.of(context),
        textScaler: MediaQuery.textScalerOf(context),
        maxLines: 2,
        ellipsis: '…',
      )..layout(maxWidth: width);
      final h = painter.height;
      painter.dispose();
      return h;
    }

    final name =
        brand?.name ?? auth.session?.selected?.academyName ?? 'الأكاديمية';
    final person =
        '${brand?.profileName ?? auth.session?.displayName ?? ''} · $role';
    final text =
        measure(name, 14, FontWeight.w800) +
        measure(person, 11, FontWeight.normal);
    return (text > 48 ? text : 48).ceilToDouble() + 22;
  }

  @override
  Widget build(BuildContext context) {
    if (home) return _homeHeader(context);
    return BrandedHeaderSurface(
      premium: false,
      child: Padding(
        padding: const EdgeInsets.symmetric(horizontal: 20, vertical: 10),
        child: Row(
          children: [
            const SizedBox(
              key: ValueKey('home-primary-crest'),
              width: 48,
              height: 48,
              child: CanonicalAcademyLogo(),
            ),
            const SizedBox(width: 10),
            Expanded(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  UiText(
                    BrandScope.maybeOf(context)?.name ??
                        auth.session?.selected?.academyName ??
                        'الأكاديمية',
                    key: const ValueKey('home-academy-name'),
                    maxLines: 2,
                    style: const TextStyle(
                      fontSize: 14,
                      fontWeight: FontWeight.w800,
                    ),
                  ),
                  UiText(
                    '${BrandScope.maybeOf(context)?.profileName ?? auth.session?.displayName ?? ''} · ${subtitle == 'لوحة المالك' ? 'المالك' : subtitle}',
                    maxLines: 2,
                    overflow: TextOverflow.ellipsis,
                    style: TextStyle(
                      color: Theme.of(context).colorScheme.onSurfaceVariant,
                      fontSize: 11,
                    ),
                  ),
                ],
              ),
            ),
            const HeaderAppearanceButton(),
            const SizedBox(width: 6),
            NotificationBell(auth: auth),
            const SizedBox(width: 6),
            if (account != null)
              IconButton(
                tooltip: 'حسابي',
                onPressed: () => openPage(context, account!),
                icon: ClipOval(child: SelfProfileAvatar(auth: auth, size: 30)),
              ),
          ],
        ),
      ),
    );
  }

  Widget _homeHeader(BuildContext context) => ExperienceHeader(
    auth: auth,
    subtitle: subtitle,
    lesson: lesson,
    account: account,
  ).build(context);
}

class ScopeDateTiles extends StatelessWidget {
  const ScopeDateTiles({
    super.key,
    required this.from,
    required this.to,
    required this.onChanged,
    this.onClear,
    this.clearLabel = 'مسح الفلاتر',
    this.compact = false,
  });
  final bool compact;
  final String from, to;
  final void Function(String, String) onChanged;
  final VoidCallback? onClear;
  final String clearLabel;
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
  Widget build(BuildContext context) => compact
      ? Wrap(
          spacing: 5,
          crossAxisAlignment: WrapCrossAlignment.center,
          children: [
            for (final item in [('من', from, true), ('إلى', to, false)])
              Semantics(
                button: true,
                label: '${item.$1} ${item.$2}',
                child: Material(
                  color: Theme.of(context).colorScheme.surface,
                  shape: RoundedRectangleBorder(
                    borderRadius: BorderRadius.circular(10),
                    side: BorderSide(
                      color: Theme.of(context).colorScheme.outline,
                    ),
                  ),
                  clipBehavior: Clip.antiAlias,
                  child: InkWell(
                    onTap: () => choose(context, item.$3),
                    child: SizedBox(
                      width: MediaQuery.textScalerOf(context).scale(10) > 15
                          ? 72
                          : 54,
                      child: Padding(
                        padding: const EdgeInsets.symmetric(vertical: 4),
                        child: Column(
                          mainAxisSize: MainAxisSize.min,
                          children: [
                            UiText(
                              item.$1,
                              style: const TextStyle(fontSize: 8),
                            ),
                            UiText(
                              DateTime.tryParse(item.$2)?.day.toString() ?? '—',
                              style: const TextStyle(
                                fontSize: 21,
                                fontWeight: FontWeight.w700,
                                height: 1.15,
                              ),
                            ),
                            UiText(
                              _months[DateTime.tryParse(item.$2)?.month ?? 0],
                              style: const TextStyle(fontSize: 8, height: 1.2),
                            ),
                            UiText(
                              DateTime.tryParse(item.$2)?.year.toString() ??
                                  'الكل',
                              style: const TextStyle(fontSize: 8, height: 1.2),
                            ),
                          ],
                        ),
                      ),
                    ),
                  ),
                ),
              ),
            if (onClear != null)
              SizedBox(
                width: 30,
                child: IconButton(
                  tooltip: clearLabel,
                  padding: EdgeInsets.zero,
                  onPressed: onClear,
                  icon: const Icon(Icons.filter_alt_off_outlined, size: 17),
                ),
              ),
          ],
        )
      : Row(
          children: [
            for (final item in [('من', from, true), ('إلى', to, false)]) ...[
              Expanded(
                child: Material(
                  color: Theme.of(context).colorScheme.surface,
                  shape: RoundedRectangleBorder(
                    borderRadius: BorderRadius.circular(16),
                    side: BorderSide(
                      color: Theme.of(context).colorScheme.outlineVariant,
                    ),
                  ),
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
                              UiText(
                                item.$1,
                                style: const TextStyle(fontSize: 11),
                              ),
                              UiText(
                                DateTime.tryParse(item.$2)?.day.toString() ??
                                    '—',
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
                                _months[(DateTime.tryParse(item.$2)?.month ??
                                    0)],
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
                tooltip: clearLabel,
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
    this.compact = false,
  });
  final List<Json> items;
  final bool compact;
  final String selected, allLabel;
  final ValueChanged<String> onSelected;
  @override
  Widget build(BuildContext context) => SingleChildScrollView(
    scrollDirection: Axis.horizontal,
    padding: EdgeInsets.symmetric(vertical: compact ? 3 : 12),
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
              selectedColor: academyAccent(context),
              label: UiText(
                textOf(b['arabicName'] ?? b['name']),
                style: TextStyle(
                  fontWeight: FontWeight.w700,
                  color: selected == b['id']
                      ? Theme.of(context).colorScheme.onPrimary
                      : Theme.of(context).colorScheme.onSurface,
                ),
              ),
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
        Icon(
          icon,
          color: ExperienceColors.semantic(
            context,
            color ?? ExperienceColors.blue,
          ),
          size: 20,
        ),
        const SizedBox(width: 8),
        Expanded(
          child: UiText(
            title,
            style: const TextStyle(fontSize: 16, fontWeight: FontWeight.w600),
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
