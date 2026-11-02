import 'package:flutter/material.dart';

import '../../core/auth/auth_controller.dart';
import '../../core/theme/ui_text.dart';
import '../shared/vip_rewards.dart';
import 'guardian_ui.dart';

/// A reward occupies space only when the trusted guardian read returns an
/// available reward. Never infer a percentage from academy branding or a mock.
class GuardianRewardBanner extends StatefulWidget {
  const GuardianRewardBanner({super.key, required this.auth});
  final AuthController auth;
  @override
  State<GuardianRewardBanner> createState() => _GuardianRewardBannerState();
}

class _GuardianRewardBannerState extends State<GuardianRewardBanner> {
  String? get identity => widget.auth.session?.selected == null
      ? null
      : '${widget.auth.session!.selected!.id}:${widget.auth.session!.selected!.contextRevision}';
  late final opened = identity;
  late final future = widget.auth.request(
    'GET',
    '/api/v1/guardian/vip-rewards',
  );
  @override
  Widget build(BuildContext context) => ListenableBuilder(
    listenable: widget.auth,
    builder: (context, _) {
      if (identity == null || identity != opened) {
        return const SizedBox.shrink();
      }
      return FutureBuilder(
        future: future,
        builder: (context, snapshot) {
          if (snapshot.hasError || snapshot.data is! Map) {
            return const SizedBox.shrink();
          }
          final rewards = rows(obj(snapshot.data)['items'] ?? [])
              .where((r) => r['state'] == 'Available')
              .toList();
          if (snapshot.hasError || rewards.isEmpty) {
            return const SizedBox.shrink();
          }
          final r = rewards.first;
          final colors = Theme.of(context).colorScheme;
          return Padding(
            padding: const EdgeInsets.only(bottom: 12),
            child: Material(
              color: colors.primaryContainer,
              borderRadius: BorderRadius.circular(16),
              clipBehavior: Clip.antiAlias,
              child: InkWell(
                onTap: () => openPage(
                  context,
                  NativeVipRewards(
                    auth: widget.auth,
                    rewardId: textOf(r['id']),
                  ),
                ),
                child: Padding(
                  padding: const EdgeInsets.all(14),
                  child: Row(
                    children: [
                      UiText(
                        '${r['percentage']}%',
                        style: TextStyle(
                          fontSize: 26,
                          fontWeight: FontWeight.w900,
                          color: colors.onPrimaryContainer,
                        ),
                      ),
                      const SizedBox(width: 12),
                      Expanded(
                        child: Column(
                          crossAxisAlignment: CrossAxisAlignment.start,
                          children: [
                            UiText(
                              'مكافأة الأعضاء المميزين',
                              style: TextStyle(
                                fontWeight: FontWeight.w800,
                                color: colors.onPrimaryContainer,
                              ),
                            ),
                            UiText(
                              'خصم متاح حتى ${day(r['expiresOn'])}',
                              style: TextStyle(
                                fontSize: 12,
                                color: colors.onPrimaryContainer,
                              ),
                            ),
                          ],
                        ),
                      ),
                      Icon(
                        Icons.chevron_left,
                        color: colors.onPrimaryContainer,
                      ),
                    ],
                  ),
                ),
              ),
            ),
          );
        },
      );
    },
  );
}
