import 'package:flutter/material.dart';

import '../../core/auth/auth_controller.dart';
import '../../core/theme/app_theme.dart';
import 'guardian_ui.dart';

class TodayNutritionCard extends StatelessWidget {
  final AuthController auth;
  final String playerId, playerName, today;
  final VoidCallback onTap;
  const TodayNutritionCard({
    super.key,
    required this.auth,
    required this.playerId,
    required this.playerName,
    required this.today,
    required this.onTap,
  });

  @override
  Widget build(BuildContext context) => FutureBuilder<Object?>(
    future: auth.request(
      'GET',
      '/api/v1/guardian/children/$playerId/nutrition/recommendations?date=$today',
    ),
    builder: (context, snapshot) {
      final data = snapshot.data is Map<String, dynamic>
          ? obj(snapshot.data)
          : null;
      final breakfast = data?['breakfast'] is Map
          ? textOf(obj(data!['breakfast'])['arabicName'])
          : 'جارٍ تجهيز اقتراح اليوم';
      return GCard(
        onTap: onTap,
        child: Row(
          children: [
            Container(
              width: 48,
              height: 48,
              decoration: BoxDecoration(
                color: AcademyTheme.gold.withValues(alpha: .14),
                borderRadius: BorderRadius.circular(15),
              ),
              child: const Icon(
                Icons.restaurant_menu,
                color: AcademyTheme.gold,
              ),
            ),
            const SizedBox(width: 12),
            Expanded(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Text(
                    'اقتراحات غذاء اليوم · $playerName',
                    maxLines: 1,
                    overflow: TextOverflow.ellipsis,
                    style: const TextStyle(fontWeight: FontWeight.bold),
                  ),
                  Text(
                    breakfast,
                    maxLines: 1,
                    overflow: TextOverflow.ellipsis,
                    style: const TextStyle(fontSize: 12, color: Colors.white60),
                  ),
                ],
              ),
            ),
            const Icon(Icons.chevron_left, color: AcademyTheme.gold),
          ],
        ),
      );
    },
  );
}

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
          padding: const EdgeInsets.fromLTRB(16, 8, 16, 10),
          child: SegmentedButton<String>(
            segments: const [
              ButtonSegment(value: 'Breakfast', label: Text('الإفطار')),
              ButtonSegment(value: 'Lunch', label: Text('الغداء')),
              ButtonSegment(value: 'Dinner', label: Text('العشاء')),
            ],
            selected: {category},
            onSelectionChanged: (s) => setState(() => category = s.single),
          ),
        ),
        Padding(
          padding: const EdgeInsets.symmetric(horizontal: 16),
          child: RemoteBody(
            auth: widget.auth,
            path:
                '/api/v1/guardian/children/${widget.playerId}/nutrition/recommendations?date=${widget.today}',
            builder: (context, value) =>
                _TodaySuggestions(auth: widget.auth, data: obj(value)),
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
                                Text(
                                  textOf(item['arabicName']),
                                  style: const TextStyle(
                                    fontWeight: FontWeight.bold,
                                  ),
                                ),
                                if (item['dataStatus'] == 'DemoUnreviewed')
                                  const Text(
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

class _TodaySuggestions extends StatelessWidget {
  final AuthController auth;
  final Json data;
  const _TodaySuggestions({required this.auth, required this.data});
  @override
  Widget build(BuildContext context) {
    final suggestions = [
      ('الإفطار', data['breakfast']),
      ('الغداء', data['lunch']),
      ('العشاء', data['dinner']),
      if (data['preTraining'] != null) ('قبل التدريب', data['preTraining']),
      if (data['postTraining'] != null) ('بعد التدريب', data['postTraining']),
    ];
    return Container(
      margin: const EdgeInsets.only(bottom: 8),
      padding: const EdgeInsets.all(16),
      decoration: BoxDecoration(
        gradient: LinearGradient(
          colors: [AcademyTheme.red.withValues(alpha: .28), AcademyTheme.card],
        ),
        borderRadius: BorderRadius.circular(20),
        border: Border.all(color: Colors.white10),
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            children: [
              const Expanded(
                child: Text(
                  'اقتراحات اليوم',
                  style: TextStyle(fontSize: 18, fontWeight: FontWeight.bold),
                ),
              ),
              StatusPill(data['isTrainingDay'] == true ? 'Training' : 'Rest'),
            ],
          ),
          const SizedBox(height: 10),
          SizedBox(
            height: 82,
            child: ListView.separated(
              scrollDirection: Axis.horizontal,
              itemCount: suggestions.length,
              separatorBuilder: (_, _) => const SizedBox(width: 8),
              itemBuilder: (context, index) {
                final suggestion = suggestions[index];
                if (suggestion.$2 is! Map) return const SizedBox.shrink();
                final meal = obj(suggestion.$2);
                return SizedBox(
                  width: 190,
                  child: Material(
                    color: Colors.white.withValues(alpha: .07),
                    borderRadius: BorderRadius.circular(14),
                    child: InkWell(
                      borderRadius: BorderRadius.circular(14),
                      onTap: () => openPage(
                        context,
                        MealDetails(auth: auth, id: textOf(meal['id'])),
                      ),
                      child: Padding(
                        padding: const EdgeInsets.all(12),
                        child: Column(
                          crossAxisAlignment: CrossAxisAlignment.start,
                          mainAxisAlignment: MainAxisAlignment.center,
                          children: [
                            Text(
                              suggestion.$1,
                              style: const TextStyle(
                                color: AcademyTheme.gold,
                                fontSize: 11,
                              ),
                            ),
                            Text(
                              textOf(meal['arabicName']),
                              maxLines: 2,
                              overflow: TextOverflow.ellipsis,
                              style: const TextStyle(
                                fontWeight: FontWeight.bold,
                              ),
                            ),
                          ],
                        ),
                      ),
                    ),
                  ),
                );
              },
            ),
          ),
        ],
      ),
    );
  }
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
          Text(textOf(d['arabicDescription'])),
          const SizedBox(height: 14),
          Text('الحصة: ${d['servingDescription']}'),
          if (d['servingWeightGrams'] != null)
            Text('وزن الحصة التقريبي: ${d['servingWeightGrams']} جم'),
          const Heading('القيم الغذائية'),
          Facts([
            ('سعرات حرارية', textOf(d['calories'])),
            ('بروتين · جرام', textOf(d['proteinGrams'])),
            ('كربوهيدرات · جرام', textOf(d['carbohydratesGrams'])),
            ('دهون · جرام', textOf(d['fatGrams'])),
          ]),
          const Text('القيم الغذائية تقديرية حسب الحصة الموضحة'),
          if (d['minimumAge'] != null)
            Text(
              'الفئة العمرية: ${d['minimumAge']}–${d['maximumAge']} سنة · ${label(d['servingProfile'])}',
            ),
          Text(
            d['dataStatus'] == 'DemoUnreviewed'
                ? 'بيانات تجريبية غير مراجعة'
                : 'بيانات مراجعة وفق المصدر المسجل',
          ),
          if (d['sourceDescription'] != null) Text(d['sourceDescription']),
          if (d['sourceReference'] != null)
            const Text(
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
        const Text('السجلات المنشورة لهذا الطفل فقط'),
        if (rows(value).isEmpty)
          const EmptyMessage('لا توجد إصابات أو استشارات منشورة.'),
        for (final d in rows(value))
          GCard(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                StatusPill(d['status']),
                Heading(textOf(d['arabicTitle'])),
                Text('${label(d['type'])} · ${day(d['recordDate'])}'),
                Text(textOf(d['arabicDescription'])),
                if (d['guardianVisibleNotes'] != null)
                  Text(d['guardianVisibleNotes']),
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
        if (rows(value).isEmpty) const EmptyMessage('لا توجد وسائط منشورة.'),
        for (final d in rows(value))
          GCard(
            child: Column(
              children: [
                DemoImage(
                  d['thumbnailReference'] ?? d['mediaReference'],
                  height: 210,
                ),
                if (d['arabicCaption'] != null) Text(d['arabicCaption']),
                if (d['type'] == 'Video')
                  const Text(
                    'صورة مصغرة للفيديو · تشغيل وسائط الإنتاج غير متاح في الديمو',
                  ),
              ],
            ),
          ),
      ]),
    ),
  );
}
