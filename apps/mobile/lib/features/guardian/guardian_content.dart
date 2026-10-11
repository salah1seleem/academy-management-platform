import 'package:academy_mobile/core/theme/ui_text.dart';
import 'package:flutter/material.dart';

import '../../core/auth/auth_controller.dart';
import 'guardian_ui.dart';
import 'daily_nutrition.dart';
import '../shared/private_media.dart';

class NutritionLibrary extends StatefulWidget {
  final AuthController auth;
  final String playerId, today;
  const NutritionLibrary({
    super.key,
    required this.auth,
    required this.playerId,
    required this.today,
  });
  @override
  State<NutritionLibrary> createState() => _NutritionLibraryState();
}

class _NutritionLibraryState extends State<NutritionLibrary> {
  String category = 'Breakfast';
  @override
  Widget build(BuildContext context) => GuardianPage(
    title: 'التغذية والصحة',
    child: Column(
      children: [
        Padding(
          padding: const EdgeInsets.fromLTRB(16, 4, 16, 0),
          child: DailyNutritionCard(
            auth: widget.auth,
            playerId: widget.playerId,
            playerName: 'اقتراحات اليوم',
            today: widget.today,
            compact: false,
          ),
        ),
        Padding(
          padding: const EdgeInsets.symmetric(horizontal: 16),
          child: SegmentedButton<String>(
            segments: const [
              ButtonSegment(value: 'Breakfast', label: UiText('الإفطار')),
              ButtonSegment(value: 'Lunch', label: UiText('الغداء')),
              ButtonSegment(value: 'Dinner', label: UiText('العشاء')),
            ],
            selected: {category},
            onSelectionChanged: (s) => setState(() => category = s.single),
          ),
        ),
        Expanded(
          child: RemoteBody(
            key: ValueKey(category),
            auth: widget.auth,
            path: '/api/v1/guardian/nutrition?category=$category',
            builder: (context, value) {
              final items = rows(value);
              return gList([
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
                                  style: const TextStyle(
                                    fontWeight: FontWeight.bold,
                                  ),
                                ),
                                if (item['dataStatus'] == 'DemoUnreviewed')
                                  const UiText(
                                    'بيانات تجريبية غير مراجعة',
                                    style: TextStyle(
                                      fontSize: 11,
                                      color: Colors.white60,
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
        ),
      ],
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
            const UiText(
              'المصادر: USDA FoodData Central · HealthyChildren/AAP · Sports Dietitians Australia',
              style: TextStyle(fontSize: 11, color: Colors.white60),
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

class GalleryPage extends StatelessWidget {
  final AuthController auth;
  final String playerId;
  const GalleryPage({super.key, required this.auth, required this.playerId});
  @override
  Widget build(BuildContext context) => GuardianPage(
    title: 'الصور والفيديوهات',
    child: RemoteBody(
      auth: auth,
      path: '/api/v1/guardian/children/$playerId/media',
      builder: (context, value) => gList([
        RemoteBody(
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
                if (d['arabicCaption'] != null) UiText(d['arabicCaption']),
                if (d['type'] == 'Video')
                  const UiText(
                    'صورة مصغرة للفيديو · تشغيل وسائط الإنتاج غير متاح في الديمو',
                  ),
              ],
            ),
          ),
      ]),
    ),
  );
}
