import 'package:academy_mobile/core/theme/ui_text.dart';
import 'package:flutter/material.dart';

import '../../core/auth/auth_controller.dart';
import '../../core/theme/appearance.dart';
import '../guardian/guardian_ui.dart';
import 'private_media.dart';

class NewsStrip extends StatelessWidget {
  final AuthController auth;
  const NewsStrip({super.key, required this.auth});
  @override
  Widget build(BuildContext context) => RemoteBody(
    auth: auth,
    path: '/api/v1/news',
    builder: (context, value) {
      final news = rows(value);
      if (news.isEmpty) return const SizedBox.shrink();
      final cardWidth = MediaQuery.sizeOf(context).width.clamp(300, 520) * .78;
      final scaler = MediaQuery.textScalerOf(context);
      final titleStyle = Theme.of(context).textTheme.bodyMedium!
          .copyWith(fontSize: 14, fontWeight: FontWeight.w700);
      double measure(String text, TextStyle style, int lines) {
        final painter = TextPainter(
          text: TextSpan(text: text, style: style),
          textDirection: Directionality.of(context),
          textScaler: scaler,
          maxLines: lines,
          ellipsis: '…',
        )..layout(maxWidth: cardWidth - 36);
        final height = painter.height;
        painter.dispose();
        return height;
      }

      final cardHeight = news
          .map(
            (n) =>
                (isPrivateAsset(n['imageUrl']) ? 82.0 : 68.0) +
                8 +
                measure(textOf(n['title']), titleStyle, 2) +
                6 +
                measure(
                  day(n['publishedAtUtc']),
                  titleStyle.copyWith(
                    fontSize: 10,
                    fontWeight: FontWeight.w400,
                  ),
                  1,
                ) +
                48,
          )
          .fold<double>(0, (a, b) => a > b ? a : b);
      return Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Heading(localized(context, 'من الأكاديمية', 'Academy news')),
          SizedBox(
            height: cardHeight,
            child: ListView.separated(
              scrollDirection: Axis.horizontal,
              itemCount: news.length,
              separatorBuilder: (_, _) => const SizedBox(width: 12),
              itemBuilder: (context, i) {
                final n = news[i];
                return SizedBox(
                  width: cardWidth,
                  child: GCard(
                    onTap: () => openPage(
                      context,
                      GuardianPage(
                        title: textOf(n['title']),
                        child: gList([
                          if (isPrivateAsset(n['imageUrl']))
                            PrivateImage(
                              auth: auth,
                              path: n['imageUrl'],
                              height: 210,
                            ),
                          Heading(textOf(n['title'])),
                          UiText(textOf(n['body'])),
                          UiText(
                            day(n['publishedAtUtc']),
                            style: Theme.of(context).textTheme.bodySmall,
                          ),
                        ]),
                      ),
                    ),
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.stretch,
                      children: [
                        if (isPrivateAsset(n['imageUrl']))
                          PrivateImage(
                            auth: auth,
                            path: n['imageUrl'],
                            height: 82,
                          )
                        else
                          Container(
                            height: 68,
                            color: Theme.of(context).colorScheme.primary
                                .withValues(alpha: .12),
                            alignment: Alignment.center,
                            child: const Icon(
                              Icons.newspaper_outlined,
                              size: 30,
                            ),
                          ),
                        const SizedBox(height: 8),
                        UiText(
                          textOf(n['title']),
                          maxLines: 2,
                          overflow: TextOverflow.ellipsis,
                          style: const TextStyle(
                            fontSize: 14,
                            fontWeight: FontWeight.w700,
                          ),
                        ),
                        const SizedBox(height: 6),
                        UiText(
                          day(n['publishedAtUtc']),
                          style: const TextStyle(fontSize: 10),
                        ),
                      ],
                    ),
                  ),
                );
              },
            ),
          ),
        ],
      );
    },
  );
}

class AcademyNewsPage extends StatelessWidget {
  final AuthController auth;
  const AcademyNewsPage({super.key, required this.auth});
  @override
  Widget build(BuildContext context) => GuardianPage(
    title: localized(context, 'أخبار الأكاديمية', 'Academy news'),
    child: gList([
      NewsStrip(auth: auth),
      RemoteBody(
        auth: auth,
        path: '/api/v1/notices',
        builder: (context, v) => Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            Heading(localized(context, 'إشعاراتي', 'My notices')),
            if (rows(v).isEmpty) const EmptyMessage(),
            for (final n in rows(v))
              GCard(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    UiText(textOf(n['message'])),
                    UiText(
                      day(n['createdAtUtc']),
                      style: Theme.of(context).textTheme.labelSmall,
                    ),
                  ],
                ),
              ),
          ],
        ),
      ),
    ]),
  );
}
