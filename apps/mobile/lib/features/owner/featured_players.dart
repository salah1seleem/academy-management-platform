import 'package:flutter/material.dart';

import '../../core/auth/auth_controller.dart';
import '../guardian/guardian_ui.dart';
import '../shared/experience_ui.dart';
import 'home_destinations.dart';
import 'honor_portrait_card.dart';

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
        Widget card(Json p) => HonorPortraitCard(
          compact: true,
          compactPortraitSize: 84,
          auth: auth,
          name: textOf(p['name']),
          reference: p['photoReference'],
          gender: p['gender'],
          identity: textOf(p['playerId'] ?? p['id']),
          avatarRole: 'player',
          height:
              350 +
              (MediaQuery.textScalerOf(context).scale(12) - 12)
                      .clamp(0, 24)
                      .toDouble() *
                  10,
          details: [
            ('${p['stars']} ★', const Color(0xFF9A5719)),
            ('التقييم ${p['score']}', const Color(0xFF236D46)),
            (textOf(p['branch']), const Color(0xFF6941A5)),
          ],
          onTap: () => openPage(
            context,
            StaffPlayerRead(auth: auth, id: textOf(p['id'])),
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
            const SizedBox.shrink(),
          ]);
        }
        return SingleChildScrollView(
          scrollDirection: Axis.horizontal,
          child: IntrinsicHeight(
            child: Row(
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: [
                for (var i = 0; i < items.length; i++) ...[
                  if (i > 0) const SizedBox(width: 10),
                  SizedBox(width: 300, child: card(items[i])),
                ],
              ],
            ),
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
