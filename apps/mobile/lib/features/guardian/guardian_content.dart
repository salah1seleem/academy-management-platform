import 'package:academy_mobile/core/theme/ui_text.dart';
import 'package:flutter/material.dart';

import '../../core/auth/auth_controller.dart';
import '../../core/theme/appearance.dart';
import '../../core/widgets/app_widgets.dart' show ErrorNotice;
import 'guardian_ui.dart';
import 'daily_nutrition.dart';
import '../shared/private_media.dart';
import '../shared/group_media_consent.dart';
import '../shared/media_storage_notice.dart';
import '../shared/media_memories.dart';

class NutritionLibrary extends StatefulWidget {
  final AuthController auth;
  final String playerId, today;
  final String? playerName;
  final bool showToday;
  const NutritionLibrary({
    super.key,
    required this.auth,
    required this.playerId,
    required this.today,
    this.playerName,
    this.showToday = false,
  });
  @override
  State<NutritionLibrary> createState() => _NutritionLibraryState();
}

class _NutritionLibraryState extends State<NutritionLibrary> {
  String category = 'Breakfast';
  @override
  Widget build(BuildContext context) => GuardianPage(
    title: localized(context, 'مكتبة التغذية', 'Nutrition library'),
    child: RemoteBody(
      key: ValueKey(category),
      auth: widget.auth,
      path: '/api/v1/guardian/nutrition?category=$category',
      builder: (context, value) {
        final items = rows(value);
        return gList([
          Padding(
            padding: const EdgeInsets.fromLTRB(16, 4, 16, 0),
            child: Card(
              clipBehavior: Clip.antiAlias,
              child: ExpansionTile(
                initiallyExpanded: widget.showToday,
                leading: Icon(
                  Icons.today_rounded,
                  color: Theme.of(context).colorScheme.primary,
                ),
                title: const UiText(
                  'اقتراحات اليوم',
                  style: TextStyle(fontWeight: FontWeight.w700),
                ),
                subtitle: widget.playerName == null
                    ? null
                    : UiText(widget.playerName!),
                children: [
                  DailyNutritionCard(
                    auth: widget.auth,
                    playerId: widget.playerId,
                    playerName: widget.playerName ?? 'اقتراحات اليوم',
                    today: widget.today,
                    compact: false,
                  ),
                ],
              ),
            ),
          ),
          Padding(
            padding: const EdgeInsets.symmetric(horizontal: 16),
            child: MediaQuery.textScalerOf(context).scale(1) > 1.3
                ? Wrap(
                    spacing: 8,
                    runSpacing: 8,
                    children: [
                      for (final entry in const {
                        'Breakfast': 'الإفطار',
                        'Lunch': 'الغداء',
                        'Dinner': 'العشاء',
                      }.entries)
                        ChoiceChip(
                          label: UiText(entry.value),
                          selected: category == entry.key,
                          onSelected: (_) =>
                              setState(() => category = entry.key),
                        ),
                    ],
                  )
                : SegmentedButton<String>(
                    segments: const [
                      ButtonSegment(
                        value: 'Breakfast',
                        label: UiText('الإفطار'),
                      ),
                      ButtonSegment(value: 'Lunch', label: UiText('الغداء')),
                      ButtonSegment(value: 'Dinner', label: UiText('العشاء')),
                    ],
                    selected: {category},
                    onSelectionChanged: (s) =>
                        setState(() => category = s.single),
                  ),
          ),
          if (items.isEmpty)
            const EmptyMessage('لا توجد وجبات في هذا القسم حاليًا.'),
          LayoutBuilder(
            builder: (context, c) => Wrap(
              spacing: 12,
              runSpacing: 12,
              children: [
                for (final item in items)
                  SizedBox(
                    width: (c.maxWidth - 12) / 2,
                    child: GCard(
                      onTap: () => openPage(
                        context,
                        MealDetails(auth: widget.auth, id: item['id']),
                      ),
                      child: Column(
                        crossAxisAlignment: CrossAxisAlignment.start,
                        children: [
                          DemoImage(item['imageReference'], height: 125),
                          const SizedBox(height: 10),
                          UiText(
                            textOf(item['arabicName']),
                            style: const TextStyle(fontWeight: FontWeight.bold),
                          ),
                          if (item['dataStatus'] == 'DemoUnreviewed')
                            UiText(
                              'بيانات تجريبية غير مراجعة',
                              style: TextStyle(
                                fontSize: 11,
                                color: Theme.of(context)
                                    .colorScheme
                                    .onSurfaceVariant,
                              ),
                            ),
                        ],
                      ),
                    ),
                  ),
              ],
            ),
          ),
        ]);
      },
    ),
  );
}

class MealDetails extends StatelessWidget {
  final AuthController auth;
  final String id;
  const MealDetails({super.key, required this.auth, required this.id});
  @override
  Widget build(BuildContext context) => GuardianPage(
    title: 'تفاصيل الوجبة',
    child: RemoteBody(
      auth: auth,
      path: '/api/v1/guardian/nutrition/$id',
      builder: (context, value) {
        final d = obj(value);
        return gList([
          DemoImage(d['imageReference'], height: 235),
          Heading(textOf(d['arabicName'])),
          UiText(textOf(d['arabicDescription'])),
          const SizedBox(height: 14),
          UiText('الحصة: ${d['servingDescription']}'),
          if (d['servingWeightGrams'] != null)
            UiText('وزن الحصة التقريبي: ${d['servingWeightGrams']} جم'),
          const Heading('القيم الغذائية'),
          Facts([
            ('سعرات حرارية', textOf(d['calories'])),
            ('بروتين · جرام', textOf(d['proteinGrams'])),
            ('كربوهيدرات · جرام', textOf(d['carbohydratesGrams'])),
            ('دهون · جرام', textOf(d['fatGrams'])),
          ]),
          const UiText('القيم الغذائية تقديرية حسب الحصة الموضحة'),
          if (d['minimumAge'] != null)
            UiText(
              'الفئة العمرية: ${d['minimumAge']}–${d['maximumAge']} سنة · ${label(d['servingProfile'])}',
            ),
          UiText(switch (d['dataStatus']) {
            'DemoUnreviewed' => 'بيانات تجريبية غير مراجعة',
            'Reviewed' => 'بيانات مراجعة وفق المصدر المسجل',
            _ => 'حالة مراجعة البيانات غير محددة',
          }),
          if (d['sourceDescription'] != null) UiText(d['sourceDescription']),
          if (d['sourceReference'] != null)
            UiText(
              'المصادر: USDA FoodData Central · HealthyChildren/AAP · Sports Dietitians Australia',
              style: TextStyle(
                fontSize: 11,
                color: Theme.of(context).colorScheme.onSurfaceVariant,
              ),
            ),
        ]);
      },
    ),
  );
}

class MedicalPage extends StatelessWidget {
  final AuthController auth;
  final String playerId;
  const MedicalPage({super.key, required this.auth, required this.playerId});
  @override
  Widget build(BuildContext context) => GuardianPage(
    title: 'الإصابات والاستشارات الطبية',
    child: RemoteBody(
      auth: auth,
      path: '/api/v1/guardian/children/$playerId/medical',
      builder: (context, value) => gList([
        const UiText('السجلات المنشورة لهذا الطفل فقط'),
        if (rows(value).isEmpty)
          const EmptyMessage('لا توجد إصابات أو استشارات منشورة.'),
        for (final d in rows(value))
          GCard(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                StatusPill(d['status']),
                Heading(textOf(d['arabicTitle'])),
                UiText('${label(d['type'])} · ${day(d['recordDate'])}'),
                UiText(textOf(d['arabicDescription'])),
                if (d['guardianVisibleNotes'] != null)
                  UiText(d['guardianVisibleNotes']),
              ],
            ),
          ),
      ]),
    ),
  );
}

class GalleryPage extends StatefulWidget {
  final AuthController auth;
  final String playerId;
  final bool embedded;
  const GalleryPage({
    super.key,
    required this.auth,
    required this.playerId,
    this.embedded = false,
  });
  @override
  State<GalleryPage> createState() => _GalleryState();
}

class _GalleryState extends State<GalleryPage> with WidgetsBindingObserver {
  bool foreground = true;
  String? get identity => auth.session?.selected == null
      ? null
      : '${auth.session!.selected!.id}:${auth.session!.selected!.contextRevision}';
  late final original = identity;
  late Future<List<Json>?> publishedMedia, legacyMedia;
  AuthController get auth => widget.auth;
  String get playerId => widget.playerId;

  Future<List<Json>?> read(String path) async {
    try {
      return rows(await auth.request('GET', path));
    } catch (_) {
      return null;
    }
  }

  void load() {
    publishedMedia = read(
      '/api/v1/assets/gallery?playerId=${Uri.encodeQueryComponent(playerId)}',
    );
    legacyMedia = read(
      '/api/v1/guardian/children/${Uri.encodeComponent(playerId)}/media',
    );
  }

  void refresh() {
    if (mounted && foreground && identity == original) {
      setState(load);
    }
  }

  @override
  void initState() {
    super.initState();
    original;
    load();
    WidgetsBinding.instance.addObserver(this);
  }

  @override
  void didUpdateWidget(covariant GalleryPage oldWidget) {
    super.didUpdateWidget(oldWidget);
    if (playerId != oldWidget.playerId || auth != oldWidget.auth) {
      if (foreground && identity == original) load();
    }
  }

  @override
  void dispose() {
    WidgetsBinding.instance.removeObserver(this);
    super.dispose();
  }

  @override
  void didChangeAppLifecycleState(AppLifecycleState state) {
    setState(() {
      foreground = state == AppLifecycleState.resumed;
      if (foreground && identity == original) load();
    });
  }

  Widget galleryContent(
    AsyncSnapshot<List<Json>?> published,
    AsyncSnapshot<List<Json>?> legacy,
  ) {
    final currentReady = published.connectionState == ConnectionState.done;
    final legacyReady = legacy.connectionState == ConnectionState.done;
    final currentItems = currentReady ? published.data : null;
    final oldItems = legacyReady ? legacy.data : null;
    final currentFailed = currentReady && currentItems == null;
    final legacyFailed = legacyReady && oldItems == null;
    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        if (currentFailed || legacyFailed) ...[
          ErrorNotice(
            currentFailed && legacyFailed
                ? 'تعذر تحميل الصور والفيديوهات.'
                : 'تعذر تحميل بعض الصور والفيديوهات.',
          ),
          TextButton(onPressed: refresh, child: const UiText('إعادة المحاولة')),
        ],
        if (currentItems?.isEmpty == true && oldItems?.isEmpty == true)
          const EmptyMessage('لا توجد صور أو فيديوهات منشورة لهذا الطفل.'),
        for (final item in currentItems ?? const <Json>[])
          MediaTile(auth: auth, media: item),
        for (final item in oldItems ?? const <Json>[])
          GCard(
            child: Column(
              children: [
                DemoImage(
                  item['thumbnailReference'] ?? item['mediaReference'],
                  height: 210,
                ),
                if (item['arabicCaption'] != null)
                  UiText(item['arabicCaption']),
                if (item['type'] == 'Video')
                  const UiText(
                    'صورة مصغرة للفيديو · تشغيل وسائط الإنتاج غير متاح في الديمو',
                  ),
              ],
            ),
          ),
        if (!currentReady || !legacyReady)
          const Center(child: CircularProgressIndicator()),
      ],
    );
  }

  @override
  Widget build(BuildContext context) => ListenableBuilder(
    listenable: auth,
    builder: (context, _) {
      if (!foreground || identity == null || identity != original) {
        return const SizedBox.shrink();
      }
      final children = <Widget>[
        GLink(
          'ذكريات الشهر',
          Icons.collections_outlined,
          () => openPage(
            context,
            MediaMemoriesPage(auth: auth, playerId: playerId),
          ),
        ),
        FutureBuilder(
          future: publishedMedia,
          builder: (context, published) => FutureBuilder(
            future: legacyMedia,
            builder: (context, legacy) => galleryContent(published, legacy),
          ),
        ),
        GroupMediaConsentCard(
          key: ValueKey(playerId),
          auth: auth,
          playerId: playerId,
          compact: widget.embedded,
          onSaved: refresh,
        ),
        MediaStorageNotice(auth: auth),
      ];
      return widget.embedded
          ? Column(
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: children,
            )
          : GuardianPage(title: 'الصور والفيديوهات', child: gList(children));
    },
  );
}
