import 'package:flutter/material.dart';

import '../../core/auth/auth_controller.dart';
import '../../core/theme/ui_text.dart';
import '../guardian/guardian_ui.dart';
import '../shared/experience_ui.dart';
import 'owner_operations.dart';

class HomeCoaches extends StatelessWidget {
  const HomeCoaches({
    super.key,
    required this.auth,
    required this.items,
    required this.branches,
    required this.filters,
    this.all = false,
  });
  final AuthController auth;
  final List<Json> items, branches;
  final Map<String, String> filters;
  final bool all;
  Widget card(BuildContext context, Json c) => GCard(
    onTap: () => openPage(
      context,
      OwnerCoachOperations(auth: auth, id: textOf(c['id']), filters: filters),
    ),
    child: Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        PlayerAvatar(
          textOf(c['name']),
          auth: auth,
          adult: true,
          reference: c['photoReference'],
          size: 64,
        ),
        const SizedBox(height: 12),
        UiText(
          textOf(c['name']),
          maxLines: 2,
          style: const TextStyle(fontSize: 12, fontWeight: FontWeight.w800),
        ),
        UiText(
          (c['branches'] as List? ?? [])
              .map(
                (id) => branches
                    .where((b) => b['id'] == id)
                    .map((b) => b['arabicName'])
                    .join(),
              )
              .join(' · '),
          maxLines: 2,
          style: const TextStyle(fontSize: 9),
        ),
        if ((c['sports'] as List? ?? []).isNotEmpty)
          UiText(
            (c['sports'] as List).join(' · '),
            maxLines: 1,
            style: const TextStyle(fontSize: 9),
          ),
        const SizedBox(height: 8),
        UiText(
          '${c['groupCount']} مجموعات · ${c['sessionCount']} حصص',
          style: const TextStyle(fontSize: 9),
        ),
        const SizedBox(height: 8),
        UiText(
          c['parentRating'] == null
              ? 'لم يُقيّم بعد'
              : '${(c['parentRating'] as num).toStringAsFixed(1)} ★ · ${c['parentRatingCount']} تقييمات',
          style: const TextStyle(
            fontSize: 11,
            color: ExperienceColors.gold,
            fontWeight: FontWeight.w700,
          ),
        ),
      ],
    ),
  );
  @override
  Widget build(BuildContext context) {
    final list = [...items];
    if (all) {
      return GuardianPage(
        title: 'كل المدربين',
        child: gList([for (final c in list) card(context, c)]),
      );
    }
    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        ExperienceSection(
          'المدربون · ${items.length}',
          Icons.sports_rounded,
          actionLabel: 'كل المدربين',
          action: () => openPage(
            context,
            HomeCoaches(
              auth: auth,
              items: items,
              branches: branches,
              filters: filters,
              all: true,
            ),
          ),
        ),
        if (items.isEmpty) const EmptyMessage('لا يوجد مدربون في هذا النطاق.'),
        if (items.isNotEmpty)
          SizedBox(
            height: 260,
            child: ListView.separated(
              scrollDirection: Axis.horizontal,
              itemCount: list.length,
              separatorBuilder: (_, _) => const SizedBox(width: 10),
              itemBuilder: (context, i) =>
                  SizedBox(width: 162, child: card(context, list[i])),
            ),
          ),
      ],
    );
  }
}

class HomeSubscriptionPlayers extends StatefulWidget {
  const HomeSubscriptionPlayers({
    super.key,
    required this.auth,
    required this.filters,
    this.state = '',
  });
  final AuthController auth;
  final Map<String, String> filters;
  final String state;
  @override
  State<HomeSubscriptionPlayers> createState() =>
      _HomeSubscriptionPlayersState();
}

class _HomeSubscriptionPlayersState extends State<HomeSubscriptionPlayers> {
  int page = 1;
  @override
  Widget build(BuildContext context) => GuardianPage(
    title: 'الاشتراكات',
    child: RemoteBody(
      auth: widget.auth,
      path:
          '/api/v1/reports/subscription-snapshot?${Uri(queryParameters: {'page': '$page', if (widget.filters['branchId'] != null) 'branchId': widget.filters['branchId']!, if (widget.filters['to'] != null) 'asOf': widget.filters['to']!, 'state': widget.state.isEmpty ? 'all' : widget.state}).query}',
      builder: (context, value) {
        final d = obj(value);
        final items = rows(d['items']);
        return gList([
          Heading('${d['total']} اشتراك'),
          if (items.isEmpty) const EmptyMessage('لا توجد اشتراكات مطابقة.'),
          for (final p in items)
            GCard(
              child: ListTile(
                contentPadding: EdgeInsets.zero,
                leading: PlayerAvatar(
                  textOf(p['name']),
                  auth: widget.auth,
                  reference: p['photoReference'],
                  size: 48,
                ),
                title: UiText(textOf(p['name'])),
                subtitle: UiText(
                  "${p['sport']} · ${p['branch']}\n${p['active'] == true ? 'فعال' : 'غير فعال'} · ${day(p['endDate'])}",
                  style: const TextStyle(fontSize: 11),
                ),
                trailing: const Icon(Icons.chevron_left),
                onTap: () => openPage(
                  context,
                  StaffPlayerRead(auth: widget.auth, id: textOf(p['playerId'])),
                ),
              ),
            ),
          Row(
            mainAxisAlignment: MainAxisAlignment.spaceBetween,
            children: [
              TextButton(
                onPressed: page > 1 ? () => setState(() => page--) : null,
                child: const UiText('السابق'),
              ),
              UiText('$page'),
              TextButton(
                onPressed: page * 50 < (d['total'] as int)
                    ? () => setState(() => page++)
                    : null,
                child: const UiText('التالي'),
              ),
            ],
          ),
        ]);
      },
    ),
  );
}

class StaffPlayerRead extends StatelessWidget {
  const StaffPlayerRead({super.key, required this.auth, required this.id});
  final AuthController auth;
  final String id;
  @override
  Widget build(BuildContext context) => GuardianPage(
    title: 'ملف اللاعب',
    child: RemoteBody(
      auth: auth,
      path: '/api/v1/people/players/$id',
      builder: (context, value) {
        final p = obj(value);
        return gList([
          Center(
            child: PlayerAvatar(
              textOf(p['arabicName']),
              auth: auth,
              reference: p['photoReference'],
              size: 96,
            ),
          ),
          Heading(textOf(p['arabicName'])),
          for (final e in rows(p['enrollments']))
            GCard(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  UiText('${e['sportName']} · ${e['branchName']}'),
                  UiText(textOf(e['groupName'])),
                  if (e['subscription'] is Map) ...[
                    StatusPill(e['subscription']['status']),
                    UiText(
                      '${day(e['subscription']['startDate'])} — ${day(e['subscription']['endDate'])}',
                    ),
                  ],
                ],
              ),
            ),
        ]);
      },
    ),
  );
}
