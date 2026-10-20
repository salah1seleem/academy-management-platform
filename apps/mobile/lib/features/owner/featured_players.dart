import 'package:flutter/material.dart';

import '../../core/auth/auth_controller.dart';
import '../../core/theme/ui_text.dart';
import '../guardian/guardian_ui.dart';
import '../shared/experience_ui.dart';
import 'home_destinations.dart';

class FeaturedPlayers extends StatefulWidget {
  const FeaturedPlayers({
    super.key,
    required this.auth,
    this.branch = '',
    this.asOf = '',
    this.all = false,
  });
  final AuthController auth;
  final String branch, asOf;
  final bool all;
  @override
  State<FeaturedPlayers> createState() => _FeaturedPlayersState();
}

class _FeaturedPlayersState extends State<FeaturedPlayers> {
  int page = 1;
  AuthController get auth => widget.auth;
  String get branch => widget.branch;
  String get asOf => widget.asOf;
  bool get all => widget.all;
  @override
  Widget build(BuildContext context) {
    final body = RemoteBody(
      auth: auth,
      path:
          '/api/v1/reports/featured-players?${Uri(queryParameters: {'page': '$page', if (branch.isNotEmpty) 'branchId': branch, if (asOf.isNotEmpty) 'asOf': asOf}).query}',
      builder: (context, value) {
        final list = rows(obj(value)['items']);
        final items = all ? list : list.take(5).toList();
        Widget card(Json p) => GCard(
          onTap: () => openPage(
            context,
            StaffPlayerRead(auth: auth, id: textOf(p['id'])),
          ),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              UiText(
                '${p['stars']} ★',
                style: const TextStyle(
                  color: ExperienceColors.gold,
                  fontWeight: FontWeight.w800,
                ),
              ),
              const SizedBox(height: 8),
              PlayerAvatar(
                textOf(p['name']),
                auth: auth,
                reference: p['photoReference'],
                size: 70,
              ),
              const SizedBox(height: 12),
              UiText(
                textOf(p['name']),
                maxLines: 2,
                style: const TextStyle(
                  fontSize: 12,
                  fontWeight: FontWeight.w800,
                ),
              ),
              UiText(
                textOf(p['branch']),
                maxLines: 1,
                style: const TextStyle(fontSize: 9),
              ),
              const SizedBox(height: 8),
              UiText(
                '${p['score']}',
                style: const TextStyle(
                  fontSize: 28,
                  fontWeight: FontWeight.w900,
                  color: ExperienceColors.green,
                ),
              ),
            ],
          ),
        );
        if (items.isEmpty) {
          return const EmptyMessage(
            'لا توجد تقييمات منشورة مكتملة وقابلة للمقارنة في هذا النطاق.',
          );
        }
        if (all) {
          return gList([
            for (final p in items) card(p),
            Row(
              mainAxisAlignment: MainAxisAlignment.spaceBetween,
              children: [
                TextButton(
                  onPressed: page > 1 ? () => setState(() => page--) : null,
                  child: const UiText('السابق'),
                ),
                UiText('$page'),
                TextButton(
                  onPressed: page * 25 < (obj(value)['total'] as int)
                      ? () => setState(() => page++)
                      : null,
                  child: const UiText('التالي'),
                ),
              ],
            ),
          ]);
        }
        return SizedBox(
          height: 285,
          child: ListView.separated(
            scrollDirection: Axis.horizontal,
            itemCount: items.length,
            separatorBuilder: (_, _) => const SizedBox(width: 10),
            itemBuilder: (context, i) =>
                SizedBox(width: 158, child: card(items[i])),
          ),
        );
      },
    );
    if (all) return GuardianPage(title: 'أبرز اللاعبين', child: body);
    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        ExperienceSection(
          'أبرز اللاعبين',
          Icons.emoji_events_outlined,
          color: ExperienceColors.gold,
          action: () => openPage(
            context,
            FeaturedPlayers(auth: auth, branch: branch, asOf: asOf, all: true),
          ),
        ),
        body,
      ],
    );
  }
}
