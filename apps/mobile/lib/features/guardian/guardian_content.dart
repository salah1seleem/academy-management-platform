import 'package:academy_mobile/core/theme/ui_text.dart';
import 'package:flutter/material.dart';

import '../../core/auth/auth_controller.dart';
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
    title: 'النظام الغذائي · ${widget.playerName ?? 'اليوم'}',
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
                  'خطة اليوم',
                  style: TextStyle(fontWeight: FontWeight.w700),
                ),
                subtitle: UiText(day(widget.today)),
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
          if (items.isEmpty) const EmptyMessage(),
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
          UiText(
            d['dataStatus'] == 'DemoUnreviewed'
                ? 'بيانات تجريبية غير مراجعة'
                : 'بيانات مراجعة وفق المصدر المسجل',
          ),
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
  int revision = 0;
  bool foreground = true;
  String? get identity => auth.session?.selected == null
      ? null
      : '${auth.session!.selected!.id}:${auth.session!.selected!.contextRevision}';
  late final original = identity;
  AuthController get auth => widget.auth;
  String get playerId => widget.playerId;
  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addObserver(this);
  }

  @override
  void dispose() {
    WidgetsBinding.instance.removeObserver(this);
    super.dispose();
  }

  @override
  void didChangeAppLifecycleState(AppLifecycleState s) {
    setState(() {
      foreground = s == AppLifecycleState.resumed;
      revision++;
    });
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
        MediaStorageNotice(auth: auth),
        GroupMediaConsentCard(
          auth: auth,
          playerId: playerId,
          compact: widget.embedded,
          onSaved: () => setState(() => revision++),
        ),
        RemoteBody(
          key: ValueKey('legacy:$revision'),
          auth: auth,
          path: '/api/v1/guardian/children/$playerId/media',
          builder: (context, value) => Column(
            children: [
              RemoteBody(
                key: ValueKey('private:$revision'),
                auth: auth,
                path: '/api/v1/assets/gallery?playerId=$playerId',
                builder: (context, v) => Column(
                  children: [
                    for (final m in rows(v)) MediaTile(auth: auth, media: m),
                  ],
                ),
              ),
              if (rows(value).isEmpty) const SizedBox.shrink(),
              for (final d in rows(value))
                GCard(
                  child: Column(
                    children: [
                      DemoImage(
                        d['thumbnailReference'] ?? d['mediaReference'],
                        height: 210,
                      ),
                      if (d['arabicCaption'] != null)
                        UiText(d['arabicCaption']),
                      if (d['type'] == 'Video')
                        const UiText(
                          'صورة مصغرة للفيديو · تشغيل وسائط الإنتاج غير متاح في الديمو',
                        ),
                    ],
                  ),
                ),
            ],
          ),
        ),
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
