import 'package:flutter/material.dart';

import '../../core/auth/auth_controller.dart';
import '../../core/theme/ui_text.dart';
import '../guardian/guardian_ui.dart';
import 'operation_design.dart';

int? ageInYears(Object? date, {DateTime? today}) {
  final d = DateTime.tryParse('$date');
  if (d == null) return null;
  final now = today ?? DateTime.now();
  return now.year -
      d.year -
      ((now.month < d.month || now.month == d.month && now.day < d.day)
          ? 1
          : 0);
}

int? remainingDays(Object? date, {DateTime? asOf}) {
  final d = DateTime.tryParse('$date');
  if (d == null) return null;
  final now = asOf ?? DateTime.now();
  return DateTime.utc(
    d.year,
    d.month,
    d.day,
  ).difference(DateTime.utc(now.year, now.month, now.day)).inDays;
}

class DirectoryTag extends StatelessWidget {
  const DirectoryTag(this.label, {super.key, this.icon, this.positive});
  final String label;
  final IconData? icon;
  final bool? positive;
  @override
  Widget build(BuildContext context) {
    final c = Theme.of(context).colorScheme;
    final color = positive == true
        ? (Theme.of(context).brightness == Brightness.dark
              ? const Color(0xFF5BE0B5)
              : const Color(0xFF13764E))
        : positive == false
        ? c.error
        : c.onSurfaceVariant;
    return Container(
      padding: const EdgeInsets.symmetric(horizontal: 9, vertical: 6),
      decoration: BoxDecoration(
        color: color.withValues(alpha: .07),
        borderRadius: BorderRadius.circular(9),
      ),
      child: Row(
        mainAxisSize: MainAxisSize.min,
        children: [
          if (icon != null) ...[
            Icon(icon, size: 13, color: color),
            const SizedBox(width: 4),
          ],
          Flexible(
            child: UiText(
              label,
              maxLines: 2,
              style: TextStyle(
                fontSize: 11,
                color: color,
                fontWeight: FontWeight.w600,
              ),
            ),
          ),
        ],
      ),
    );
  }
}

class PlayerDirectoryCard extends StatelessWidget {
  const PlayerDirectoryCard({
    super.key,
    required this.auth,
    required this.player,
    this.enrollments = const [],
    this.onOpen,
    this.onEvaluate,
    this.openLabel = 'ملف اللاعب',
    this.selected,
    this.onSelection,
    this.subscriptionAsOf,
    this.showSubscriptionStatus = true,
    this.subscriptionStatusLabel,
    this.showSubscriptionBalance = false,
  });
  final AuthController auth;
  final Json player;
  final List<Json> enrollments;
  final VoidCallback? onOpen, onEvaluate;
  final String openLabel;
  final bool? selected;
  final ValueChanged<bool?>? onSelection;
  final DateTime? subscriptionAsOf;
  final bool showSubscriptionStatus;
  final String? subscriptionStatusLabel;
  final bool showSubscriptionBalance;
  @override
  Widget build(BuildContext context) {
    final name = textOf(
          player['arabicName'] ?? player['player'] ?? player['name'],
        ),
        code = textOf(player['playerCode'] ?? player['code'] ?? '');
    final age =
        (player['age'] as num?)?.toInt() ?? ageInYears(player['dateOfBirth']);
    final known = enrollments
        .where(
          (e) =>
              e['subscription'] is Map && (e['subscription'] as Map).isNotEmpty,
        )
        .toList();
    final subscribed = known.any((e) {
      final s = obj(e['subscription']);
      final status = s['status'];
      final days = remainingDays(s['endDate'], asOf: subscriptionAsOf);
      return ![
            'Expired',
            'Cancelled',
            'Frozen',
            'Scheduled',
          ].contains(status) &&
          s['expired'] != true &&
          s['frozen'] != true &&
          (s['sessionBased'] == false ||
              s['remainingSessions'] == null ||
              (s['remainingSessions'] as num) > 0) &&
          (days == null || days >= 0);
    });
    final active =
        player['isActive'] != false &&
        ((player['hasActiveSubscription'] as bool?) ??
            (known.isEmpty ? true : subscribed));
    return Theme(
      data: operationTheme(context),
      child: Builder(
        builder: (context) => GCard(
          onTap: onSelection != null
              ? () => onSelection!(!(selected ?? false))
              : onOpen,
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              Row(
                children: [
                  if (selected != null)
                    Checkbox(value: selected, onChanged: onSelection),
                  PlayerAvatar(
                    name,
                    auth: auth,
                    reference: player['photoReference'],
                    gender: player['gender'],
                    size: 54,
                  ),
                  const SizedBox(width: 12),
                  Expanded(
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        UiText(
                          name,
                          style: const TextStyle(
                            fontSize: 16,
                            fontWeight: FontWeight.w700,
                          ),
                        ),
                        if (code.isNotEmpty) ...[
                          const SizedBox(height: 4),
                          UiText(
                            code,
                            style: TextStyle(
                              fontSize: 11,
                              color: Theme.of(context)
                                  .colorScheme
                                  .onSurfaceVariant,
                            ),
                          ),
                        ],
                      ],
                    ),
                  ),
                  if (showSubscriptionStatus) ...[
                    const SizedBox(width: 8),
                    DirectoryTag(
                      subscriptionStatusLabel ?? (active ? 'نشط' : 'غير نشط'),
                      positive: active,
                    ),
                  ],
                ],
              ),
              const SizedBox(height: 12),
              if (age != null)
                Padding(
                  padding: const EdgeInsets.only(bottom: 8),
                  child: Wrap(
                    children: [
                      DirectoryTag('$age سنة', icon: Icons.cake_outlined),
                    ],
                  ),
                ),
              for (final e in enrollments)
                Padding(
                  padding: const EdgeInsets.only(bottom: 8),
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      Wrap(
                        spacing: 6,
                        runSpacing: 6,
                        children: [
                          if (e['sportName'] != null || e['sport'] != null)
                            DirectoryTag(
                              textOf(e['sportName'] ?? e['sport']),
                              icon: Icons.sports_outlined,
                            ),
                          if (e['branchName'] != null || e['branch'] != null)
                            DirectoryTag(
                              textOf(e['branchName'] ?? e['branch']),
                              icon: Icons.location_on_outlined,
                            ),
                          if (e['groupName'] != null || e['group'] != null)
                            DirectoryTag(
                              textOf(e['groupName'] ?? e['group']),
                              icon: Icons.groups_outlined,
                            ),
                          ..._subscriptionTags(e),
                        ],
                      ),
                    ],
                  ),
                ),
              if ((player['reasons'] as List? ?? []).isNotEmpty)
                Padding(
                  padding: const EdgeInsets.only(top: 6),
                  child: UiText(
                    (player['reasons'] as List).join(' · '),
                    style: TextStyle(
                      fontSize: 11,
                      color: Theme.of(context).colorScheme.error,
                    ),
                  ),
                ),
              if (onOpen != null || onEvaluate != null) ...[
                const SizedBox(height: 4),
                const Divider(height: 20),
                Row(
                  children: [
                    if (onOpen != null)
                      Expanded(
                        child: TextButton.icon(
                          onPressed: onOpen,
                          icon: const Icon(Icons.badge_outlined, size: 18),
                          label: UiText(openLabel),
                        ),
                      ),
                    if (onEvaluate != null)
                      Expanded(
                        child: FilledButton.tonalIcon(
                          onPressed: onEvaluate,
                          icon: const Icon(Icons.assessment_outlined, size: 18),
                          label: const UiText('تقييم'),
                        ),
                      ),
                  ],
                ),
              ],
            ],
          ),
        ),
      ),
    );
  }

  List<Widget> _subscriptionTags(Json e) {
    final s = e['subscription'] is Map
        ? obj(e['subscription'])
        : <String, dynamic>{};
    final status = s['status'];
    final expired =
        s['expired'] == true || status == 'Expired' || status == 'Cancelled';
    final frozen = s['frozen'] == true || status == 'Frozen';
    final left = s['remainingSessions'] as num?;
    final days = remainingDays(s['endDate'], asOf: subscriptionAsOf);
    final showPerRegistration =
        showSubscriptionStatus &&
        (subscriptionStatusLabel == null || showSubscriptionBalance);
    final showRegistrationStatus =
        subscriptionStatusLabel == null || enrollments.length > 1;
    return [
      if (e['price'] != null)
        DirectoryTag(
          money(e['price'], e['currency']),
          icon: Icons.payments_outlined,
        ),
      if (showPerRegistration) ...[
        if (expired && showRegistrationStatus)
          DirectoryTag(
            showSubscriptionBalance ? 'منتهٍ' : 'غير نشط',
            positive: false,
          )
        else if (frozen && showRegistrationStatus)
          const DirectoryTag('مجمّد')
        else if (status == 'Scheduled' && showRegistrationStatus)
          const DirectoryTag('لم يبدأ بعد')
        else if (!expired && !frozen && status != 'Scheduled') ...[
          if (left != null && s['sessionBased'] != false)
            DirectoryTag(
              'متبقي $left حصة',
              icon: Icons.event_available_outlined,
              positive: left > 2,
            ),
          if (days != null && days >= 0)
            DirectoryTag(
              'متبقي $days يوم',
              icon: Icons.timelapse_outlined,
              positive: days > 7,
            ),
        ],
      ],
    ];
  }
}

class GroupDirectoryCard extends StatelessWidget {
  const GroupDirectoryCard({
    super.key,
    required this.group,
    required this.onTap,
  });
  final Json group;
  final VoidCallback onTap;
  @override
  Widget build(BuildContext context) {
    final coaches = (group['coaches'] as List? ?? [])
        .map((x) => x is Map ? textOf(x['name'] ?? x['arabicName']) : textOf(x))
        .toList();
    return Theme(
      data: operationTheme(context),
      child: Builder(
        builder: (context) => GCard(
          onTap: onTap,
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              Row(
                children: [
                  Container(
                    width: 44,
                    height: 44,
                    decoration: BoxDecoration(
                      borderRadius: BorderRadius.circular(13),
                      color: Theme.of(context).colorScheme.primary
                          .withValues(alpha: .08),
                    ),
                    child: Icon(
                      Icons.groups_outlined,
                      color: Theme.of(context).colorScheme.primary,
                    ),
                  ),
                  const SizedBox(width: 10),
                  Expanded(
                    child: UiText(
                      textOf(group['arabicName'] ?? group['name']),
                      style: const TextStyle(
                        fontWeight: FontWeight.w700,
                        fontSize: 16,
                      ),
                    ),
                  ),
                  const Icon(
                    Icons.chevron_left,
                    size: 19,
                    textDirection: TextDirection.ltr,
                  ),
                ],
              ),
              const SizedBox(height: 12),
              Wrap(
                spacing: 6,
                runSpacing: 6,
                children: [
                  if (group['sportName'] != null || group['sport'] != null)
                    DirectoryTag(
                      textOf(group['sportName'] ?? group['sport']),
                      icon: Icons.sports_outlined,
                    ),
                  if (group['branchName'] != null || group['branch'] != null)
                    DirectoryTag(
                      textOf(group['branchName'] ?? group['branch']),
                      icon: Icons.location_on_outlined,
                    ),
                  DirectoryTag(
                    '${group['playerCount'] ?? group['playersCount'] ?? 0} لاعب',
                    icon: Icons.people_outline,
                  ),
                ],
              ),
              const SizedBox(height: 10),
              UiText(
                coaches.isEmpty
                    ? 'لم يُسند مدرب بعد'
                    : 'المدرب: ${coaches.join('، ')}',
                style: TextStyle(
                  fontSize: 12,
                  color: Theme.of(context).colorScheme.onSurfaceVariant,
                ),
              ),
            ],
          ),
        ),
      ),
    );
  }
}

class SearchableDirectory extends StatefulWidget {
  const SearchableDirectory({
    super.key,
    required this.items,
    required this.itemBuilder,
    this.label = 'بحث بالاسم أو الفرع أو الرياضة',
  });
  final List<Json> items;
  final Widget Function(BuildContext, Json) itemBuilder;
  final String label;
  @override
  State<SearchableDirectory> createState() => _SearchableDirectoryState();
}

String directorySearchKey(String value) {
  var text = value
      .toLowerCase()
      .replaceAll(RegExp(r'[ً-ٰٟـ]'), '')
      .replaceAll(RegExp('[أإآٱ]'), 'ا')
      .replaceAll('ى', 'ي');
  for (var i = 0; i < 10; i++) {
    text = text
        .replaceAll('٠١٢٣٤٥٦٧٨٩'[i], '$i')
        .replaceAll('۰۱۲۳۴۵۶۷۸۹'[i], '$i');
  }
  return text.trim().replaceAll(RegExp(r'\s+'), ' ');
}

class _SearchableDirectoryState extends State<SearchableDirectory> {
  final search = TextEditingController();
  @override
  void dispose() {
    search.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final q = directorySearchKey(search.text);
    final filtered = widget.items
        .where(
          (p) =>
              q.isEmpty ||
              directorySearchKey(p.values.whereType<String>().join(' '))
                  .contains(q),
        )
        .toList();
    return Theme(
      data: operationTheme(context),
      child: gList([
        TextField(
          controller: search,
          onChanged: (_) => setState(() {}),
          textInputAction: TextInputAction.search,
          decoration: InputDecoration(
            labelText: widget.label,
            prefixIcon: const Icon(Icons.search),
            suffixIcon: search.text.isEmpty
                ? null
                : IconButton(
                    tooltip: 'مسح البحث',
                    icon: const Icon(Icons.close_rounded),
                    onPressed: () => setState(search.clear),
                  ),
          ),
        ),
        const SizedBox(height: 12),
        Semantics(
          liveRegion: true,
          child: UiText(
            '${filtered.length} نتيجة',
            style: TextStyle(
              fontSize: 12,
              color: Theme.of(context).colorScheme.onSurfaceVariant,
            ),
          ),
        ),
        const SizedBox(height: 12),
        for (final p in filtered) widget.itemBuilder(context, p),
        if (filtered.isEmpty) const EmptyMessage('لا توجد نتائج لهذا البحث.'),
      ]),
    );
  }
}
