// Daily nutrition preview and full detail share one authenticated source.
import 'package:flutter/material.dart';

import '../../core/api/api_client.dart';
import '../../core/auth/auth_controller.dart';
import '../../core/theme/appearance.dart';
import '../../core/theme/ui_text.dart';
import 'guardian_ui.dart';
import 'guardian_content.dart';

String dailyNutritionPath(String playerId, String today) =>
    '/api/v1/guardian/children/$playerId/nutrition/recommendations${DateTime.tryParse(today) == null ? '' : '?date=${day(today)}'}';

List<(String, String, Json)> dailyNutritionMeals(Json d) {
  final slots = [
    ('الإفطار', 'Breakfast', d['breakfast']),
    ('الغداء', 'Lunch', d['lunch']),
    ('العشاء', 'Dinner', d['dinner']),
    ('قبل التدريب', 'Before training', d['preTraining']),
    ('بعد التدريب', 'After training', d['postTraining']),
  ];
  return [
    for (final (ar, en, data) in slots)
      if (data is Json &&
          data['id'] is String &&
          (data['id'] as String).trim().isNotEmpty &&
          data['arabicName'] is String &&
          (data['arabicName'] as String).trim().isNotEmpty)
        (ar, en, data),
  ];
}

String? dailyNutritionActivity(Object? isTrainingDay) =>
    switch (isTrainingDay) {
      true => 'يوم تدريب',
      false => 'يوم راحة',
      _ => null,
    };

class DailyNutritionCard extends StatefulWidget {
  final AuthController auth;
  final String playerId, playerName, today;
  final bool compact;
  const DailyNutritionCard({
    super.key,
    required this.auth,
    required this.playerId,
    required this.playerName,
    required this.today,
    this.compact = true,
  });
  @override
  State<DailyNutritionCard> createState() => _DailyNutritionCardState();
}

class _DailyNutritionCardState extends State<DailyNutritionCard> {
  late Future<Object?> future;
  Future<Object?> load() => widget.auth.request(
    'GET',
    dailyNutritionPath(widget.playerId, widget.today),
  );
  @override
  void initState() {
    super.initState();
    future = load();
  }

  @override
  void didUpdateWidget(DailyNutritionCard old) {
    super.didUpdateWidget(old);
    if (old.playerId != widget.playerId ||
        old.today != widget.today ||
        old.auth != widget.auth) {
      future = load();
    }
  }

  void library() => openPage(
    context,
    NutritionLibrary(
      auth: widget.auth,
      playerId: widget.playerId,
      playerName: widget.playerName,
      today: widget.today,
    ),
  );
  @override
  Widget build(BuildContext context) => FutureBuilder<Object?>(
    future: future,
    builder: (context, result) {
      if (result.connectionState != ConnectionState.done) {
        return const SizedBox(
          height: 64,
          child: Center(child: LinearProgressIndicator()),
        );
      }
      if (result.hasError) {
        final unavailable =
            result.error is ApiFailure &&
            [404, 422, 400].contains((result.error as ApiFailure).status);
        if (unavailable && !widget.compact) {
          return const EmptyMessage('لا توجد اقتراحات لهذا اليوم.');
        }
        return GCard(
          child: Row(
            children: [
              const Icon(Icons.restaurant_outlined),
              const SizedBox(width: 10),
              Expanded(
                child: UiText(
                  localized(
                    context,
                    unavailable
                        ? 'مكتبة التغذية · لا توجد اقتراحات مناسبة حاليًا'
                        : 'تعذر تحميل وجبات اليوم',
                    unavailable
                        ? 'Nutrition library · No matching suggestions yet'
                        : 'Could not load today’s meals',
                  ),
                ),
              ),
              IconButton(
                tooltip: localized(
                  context,
                  unavailable ? 'عرض المكتبة' : 'إعادة المحاولة',
                  unavailable ? 'Open library' : 'Retry',
                ),
                onPressed: unavailable
                    ? library
                    : () => setState(() {
                        future = load();
                      }),
                icon: Icon(unavailable ? Icons.chevron_right : Icons.refresh),
              ),
            ],
          ),
        );
      }
      if (result.data is! Map) {
        return GCard(
          child: Row(
            children: [
              Expanded(
                child: UiText(
                  localized(
                    context,
                    'تعذر تحميل وجبات اليوم',
                    'Could not load today’s meals',
                  ),
                ),
              ),
              IconButton(
                tooltip: localized(context, 'حاول مجددًا', 'Retry'),
                onPressed: () => setState(() {
                  future = load();
                }),
                icon: const Icon(Icons.refresh),
              ),
            ],
          ),
        );
      }
      final d = obj(result.data), meals = dailyNutritionMeals(d);
      final dayActivity = dailyNutritionActivity(d['isTrainingDay']);
      final activity = dayActivity == null
          ? null
          : localized(
              context,
              dayActivity,
              d['isTrainingDay'] == true ? 'Training day' : 'Rest day',
            );
      final disclaimer = d['disclaimer'] is String
          ? (d['disclaimer'] as String).trim()
          : '';
      if (widget.compact) {
        return GCard(
          onTap: library,
          child: Row(
            children: [
              SizedBox(
                width: 76,
                height: 56,
                child: meals.isEmpty
                    ? const Icon(Icons.restaurant_outlined)
                    : Stack(
                        children: [
                          for (var i = 0; i < meals.take(3).length; i++)
                            PositionedDirectional(
                              start: i * 18.0,
                              top: i * 3.0,
                              child: Container(
                                width: 40,
                                height: 40,
                                padding: const EdgeInsets.all(2),
                                decoration: BoxDecoration(
                                  color: Theme.of(context).colorScheme.surface,
                                  borderRadius: BorderRadius.circular(12),
                                ),
                                child: ClipRRect(
                                  borderRadius: BorderRadius.circular(10),
                                  child: DemoImage(
                                    meals[i].$3['imageReference'],
                                    height: 36,
                                  ),
                                ),
                              ),
                            ),
                        ],
                      ),
              ),
              const SizedBox(width: 10),
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    UiText(
                      localized(
                        context,
                        'اقتراحات من مكتبة التغذية',
                        'Suggestions from the nutrition library',
                      ),
                      maxLines: 2,
                      overflow: TextOverflow.ellipsis,
                      style: const TextStyle(
                        fontSize: 14,
                        fontWeight: FontWeight.w800,
                      ),
                    ),
                    const SizedBox(height: 4),
                    UiText(
                      [
                        widget.playerName,
                        if (meals.isEmpty)
                          'لا توجد اقتراحات اليوم'
                        else ?activity,
                      ].join(' · '),
                      style: Theme.of(context).textTheme.labelSmall,
                      maxLines: 1,
                      overflow: TextOverflow.ellipsis,
                    ),
                  ],
                ),
              ),
              Icon(
                Icons.chevron_right,
                size: 20,
                color: Theme.of(context).colorScheme.primary,
              ),
            ],
          ),
        );
      }
      final selectedDate = d['date'] ?? widget.today;
      return Padding(
        padding: const EdgeInsets.fromLTRB(16, 4, 16, 16),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Row(
              children: [
                Icon(
                  Icons.restaurant_menu,
                  color: Theme.of(context).colorScheme.primary,
                ),
                const SizedBox(width: 8),
                Expanded(
                  child: UiText(
                    localized(
                      context,
                      sessionDateLabel(selectedDate),
                      day(selectedDate),
                    ),
                    style: const TextStyle(fontWeight: FontWeight.w800),
                  ),
                ),
              ],
            ),
            const SizedBox(height: 8),
            if (meals.isEmpty)
              const UiText('لا توجد اقتراحات لهذا اليوم.')
            else
              Wrap(
                spacing: 6,
                runSpacing: 4,
                children: [
                  for (final m in meals)
                    ActionChip(
                      avatar: const Icon(Icons.restaurant_outlined, size: 14),
                      label: UiText(
                        '${localized(context, m.$1, m.$2)} · ${textOf(m.$3['arabicName'])}',
                        style: Theme.of(context).textTheme.bodySmall,
                      ),
                      onPressed: () => openPage(
                        context,
                        MealDetails(
                          auth: widget.auth,
                          id: m.$3['id'] as String,
                        ),
                      ),
                    ),
                ],
              ),
            const SizedBox(height: 6),
            if (activity != null)
              UiText(activity, style: Theme.of(context).textTheme.labelSmall),
            if (disclaimer.isNotEmpty)
              UiText(disclaimer, style: Theme.of(context).textTheme.bodySmall),
          ],
        ),
      );
    },
  );
}
