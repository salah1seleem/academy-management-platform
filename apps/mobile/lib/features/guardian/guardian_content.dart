import 'package:flutter/material.dart';

import '../../core/auth/auth_controller.dart';
import 'guardian_ui.dart';

class NutritionLibrary extends StatefulWidget {
  final AuthController auth;
  const NutritionLibrary({super.key, required this.auth});
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
        const Padding(
          padding: EdgeInsets.all(12),
          child: Text(
            'مكتبة معلومات عامة، وليست وصفة أو خطة علاجية.',
            textAlign: TextAlign.center,
          ),
        ),
        Padding(
          padding: const EdgeInsets.symmetric(horizontal: 16),
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
          const Heading('القيم الغذائية'),
          Facts([
            ('سعرات حرارية', textOf(d['calories'])),
            ('بروتين · جرام', textOf(d['proteinGrams'])),
            ('كربوهيدرات · جرام', textOf(d['carbohydratesGrams'])),
            ('دهون · جرام', textOf(d['fatGrams'])),
          ]),
          const Text('القيم الغذائية تقديرية حسب الحصة الموضحة'),
          Text(
            d['dataStatus'] == 'DemoUnreviewed'
                ? 'بيانات تجريبية غير مراجعة'
                : 'بيانات مراجعة وفق المصدر المسجل',
          ),
          if (d['sourceDescription'] != null) Text(d['sourceDescription']),
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
