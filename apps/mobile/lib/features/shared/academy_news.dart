import 'package:flutter/material.dart';

import '../../core/auth/auth_controller.dart';
import '../../core/theme/academy_brand.dart';
import '../../core/theme/ui_text.dart';
import '../guardian/guardian_ui.dart';
import 'private_media.dart';

String _kindLabel(Json item) => switch (item['kind']) {
  'Event' => 'فعالية',
  'Announcement' => 'إعلان',
  _ => 'خبر',
};

class _Publication extends StatelessWidget {
  const _Publication(this.item);
  final Json item;

  @override
  Widget build(BuildContext context) {
    final published = DateTime.tryParse(textOf(item['publishedAtUtc']));
    final style = Theme.of(context).textTheme.labelMedium;
    return Wrap(
      spacing: 8,
      runSpacing: 4,
      children: [
        UiText(_kindLabel(item), style: style),
        if (published != null)
          UiText(
            '${ui(context, 'نُشر في')} ${MaterialLocalizations.of(context).formatShortDate(published.toLocal())}',
            style: style,
          ),
      ],
    );
  }
}

class NewsStrip extends StatelessWidget {
  const NewsStrip({super.key, required this.auth});
  final AuthController auth;

  @override
  Widget build(BuildContext context) {
    final brand = BrandScope.maybeOf(context);
    if (brand != null && !brand.features.contains('news')) {
      return const SizedBox.shrink();
    }
    return RemoteBody(
      auth: auth,
      path: '/api/v1/news',
      builder: (context, value) {
        final items = rows(value);
        if (items.isEmpty) return const SizedBox.shrink();
        final width = MediaQuery.sizeOf(context).width.clamp(280, 460) * .74;
        return Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            Wrap(
              alignment: WrapAlignment.spaceBetween,
              crossAxisAlignment: WrapCrossAlignment.center,
              children: [
                const Heading('الأخبار والفعاليات'),
                TextButton(
                  onPressed: () =>
                      openPage(context, AcademyNewsPage(auth: auth)),
                  child: const UiText('كل الأنشطة'),
                ),
              ],
            ),
            SingleChildScrollView(
              scrollDirection: Axis.horizontal,
              child: IntrinsicHeight(
                child: Row(
                  crossAxisAlignment: CrossAxisAlignment.stretch,
                  children: [
                    for (final item in items)
                      Padding(
                        padding: const EdgeInsetsDirectional.only(end: 10),
                        child: SizedBox(
                          width: width,
                          child: _ActivityCard(auth: auth, item: item),
                        ),
                      ),
                  ],
                ),
              ),
            ),
          ],
        );
      },
    );
  }
}

class AcademyNewsPage extends StatefulWidget {
  const AcademyNewsPage({super.key, required this.auth});
  final AuthController auth;

  @override
  State<AcademyNewsPage> createState() => _AcademyNewsPageState();
}

class _AcademyNewsPageState extends State<AcademyNewsPage> {
  String kind = 'News';

  @override
  Widget build(BuildContext context) => GuardianPage(
    title: 'الأخبار والفعاليات',
    child: Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        Padding(
          padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 6),
          child: Wrap(
            spacing: 8,
            children: [
              for (final option in const {
                'News': 'الأخبار',
                'Event': 'الفعاليات',
              }.entries)
                ChoiceChip(
                  label: UiText(option.value),
                  selected: kind == option.key,
                  onSelected: (_) => setState(() => kind = option.key),
                ),
            ],
          ),
        ),
        Expanded(
          child: RemoteBody(
            key: ValueKey(kind),
            auth: widget.auth,
            path: '/api/v1/news?kind=$kind&page=1',
            builder: (context, data) {
              final items = data is Map ? rows(obj(data)['items']) : rows(data);
              return gList([
                if (items.isEmpty)
                  EmptyMessage(
                    kind == 'News'
                        ? 'لا توجد أخبار منشورة.'
                        : 'لا توجد فعاليات منشورة.',
                  ),
                for (final item in items)
                  _ActivityCard(auth: widget.auth, item: item),
              ]);
            },
          ),
        ),
      ],
    ),
  );
}

class _ActivityCard extends StatelessWidget {
  const _ActivityCard({required this.auth, required this.item});
  final AuthController auth;
  final Json item;

  static Widget cover(
    BuildContext context,
    AuthController auth,
    Json item,
    double height,
  ) {
    final colors = Theme.of(context).colorScheme;
    final fallback = Container(
      height: height,
      color: colors.primaryContainer,
      alignment: Alignment.center,
      child: Icon(
        item['kind'] == 'Event'
            ? Icons.event_outlined
            : Icons.newspaper_rounded,
        color: colors.onPrimaryContainer,
        size: 32,
      ),
    );
    return isPrivateAsset(item['imageUrl'])
        ? PrivateImage(
            auth: auth,
            path: textOf(item['imageUrl']),
            height: height,
            fallback: fallback,
          )
        : fallback;
  }

  @override
  Widget build(BuildContext context) {
    final colors = Theme.of(context).colorScheme;
    final title = textOf(item['title']);
    return GCard(
      padding: EdgeInsets.zero,
      onTap: () => openPage(
        context,
        _ActivityDetail(
          auth: auth,
          id: textOf(item['id']),
          filter: item['kind'] == 'Event' ? 'Event' : 'News',
        ),
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          if (isPrivateAsset(item['imageUrl'])) cover(context, auth, item, 130),
          Padding(
            padding: const EdgeInsets.fromLTRB(12, 8, 12, 12),
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                _Publication(item),
                const SizedBox(height: 6),
                Row(
                  children: [
                    Expanded(
                      child: UiText(
                        title,
                        maxLines: 2,
                        overflow: TextOverflow.ellipsis,
                        style: const TextStyle(
                          fontSize: 15,
                          fontWeight: FontWeight.w800,
                        ),
                      ),
                    ),
                    const SizedBox(width: 8),
                    ExcludeSemantics(
                      child: Icon(
                        Icons.arrow_forward_ios_rounded,
                        color: colors.primary,
                        size: 16,
                      ),
                    ),
                  ],
                ),
              ],
            ),
          ),
        ],
      ),
    );
  }
}

class _ActivityDetail extends StatelessWidget {
  const _ActivityDetail({
    required this.auth,
    required this.id,
    required this.filter,
  });
  final AuthController auth;
  final String id, filter;

  @override
  Widget build(BuildContext context) => GuardianPage(
    title: 'تفاصيل النشاط',
    child: RemoteBody(
      auth: auth,
      path:
          '/api/v1/news?kind=$filter&page=1&item=${Uri.encodeQueryComponent(id)}',
      autoPaginate: false,
      builder: (context, data) {
        final item = rows(obj(data)['items'])
            .where((row) => row['id'] == id)
            .firstOrNull;
        if (item == null) {
          return gList([const EmptyMessage('هذا النشاط غير متاح.')]);
        }
        return gList([
          Heading(textOf(item['title'])),
          _Publication(item),
          if (isPrivateAsset(item['imageUrl'])) ...[
            const SizedBox(height: 12),
            ClipRRect(
              borderRadius: BorderRadius.circular(16),
              child: _ActivityCard.cover(context, auth, item, 210),
            ),
          ],
          const SizedBox(height: 12),
          UiText(textOf(item['body'])),
        ]);
      },
    ),
  );
}
