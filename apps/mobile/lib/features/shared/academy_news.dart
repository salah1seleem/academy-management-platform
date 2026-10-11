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
      return Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Heading(localized(context, 'من الأكاديمية', 'Academy news')),
          for (final n in news.take(3))
            GCard(
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
              child: Row(
                children: [
                  if (isPrivateAsset(n['imageUrl']))
                    SizedBox(
                      width: 68,
                      child: ClipRRect(
                        borderRadius: BorderRadius.circular(12),
                        child: PrivateImage(
                          auth: auth,
                          path: n['imageUrl'],
                          height: 68,
                        ),
                      ),
                    )
                  else
                    const AcademyCrest(size: 50),
                  const SizedBox(width: 14),
                  Expanded(
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        UiText(
                          textOf(n['title']),
                          maxLines: 2,
                          overflow: TextOverflow.ellipsis,
                          style: const TextStyle(fontWeight: FontWeight.bold),
                        ),
                        UiText(
                          day(n['publishedAtUtc']),
                          style: Theme.of(context).textTheme.labelSmall,
                        ),
                      ],
                    ),
                  ),
                  const Icon(Icons.chevron_left),
                ],
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
