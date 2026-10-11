// Daily nutrition preview and full detail share one authenticated source.
import 'package:flutter/material.dart';

import '../../core/api/api_client.dart';
import '../../core/auth/auth_controller.dart';
import '../../core/theme/appearance.dart';
import '../../core/theme/ui_text.dart';
import 'guardian_ui.dart';
import 'guardian_content.dart';

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
    '/api/v1/guardian/children/${widget.playerId}/nutrition/recommendations${DateTime.tryParse(widget.today) == null ? '' : '?date=${day(widget.today)}'}',
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
                    : () => setState(() => future = load()),
                icon: Icon(unavailable ? Icons.chevron_left : Icons.refresh),
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
                onPressed: () => setState(() => future = load()),
                icon: const Icon(Icons.refresh),
              ),
            ],
          ),
        );
      }
      final d = obj(result.data),
          meals = [
            ('الإفطار', 'Breakfast', d['breakfast']),
            ('الغداء', 'Lunch', d['lunch']),
            ('العشاء', 'Dinner', d['dinner']),
            if (d['preTraining'] != null)
              ('قبل التدريب', 'Before training', d['preTraining']),
            if (d['postTraining'] != null)
              ('بعد التدريب', 'After training', d['postTraining']),
          ];
      if (widget.compact) {
        return GCard(
          onTap: library,
          child: Row(
            children: [
              SizedBox(
                width: 76,
                height: 56,
                child: Stack(
                  children: [
                    for (var i = 0; i < meals.take(3).length; i++)
                      if (meals[i].$3 is Map)
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
                                obj(meals[i].$3)['imageReference'],
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
                        'متابعة الغذاء اليومي لطفلك',
                        'Your child’s daily nutrition',
                      ),
                      style: const TextStyle(
                        fontSize: 12,
                        fontWeight: FontWeight.w800,
                      ),
                    ),
                    const SizedBox(height: 4),
                    UiText(
                      '${widget.playerName} · ${localized(context, d['isTrainingDay'] == true ? 'يوم تدريب' : 'يوم راحة', d['isTrainingDay'] == true ? 'Training day' : 'Rest day')}',
                      style: Theme.of(context).textTheme.labelSmall,
                      maxLines: 1,
                      overflow: TextOverflow.ellipsis,
                    ),
                  ],
                ),
              ),
              Icon(
                Icons.chevron_left,
                size: 20,
                color: Theme.of(context).colorScheme.primary,
              ),
            ],
          ),
        );
      }
      return GCard(
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
                    localized(context, 'اقتراحات اليوم', 'Today’s suggestions'),
                    style: const TextStyle(fontWeight: FontWeight.w800),
                  ),
                ),
              ],
            ),
            const SizedBox(height: 8),
            Wrap(
              spacing: 6,
              runSpacing: 4,
              children: [
                for (final m in meals)
                  if (m.$3 is Map)
                    ActionChip(
                      avatar: const Icon(Icons.restaurant_outlined, size: 14),
                      label: UiText(
                        '${localized(context, m.$1, m.$2)} · ${textOf(obj(m.$3)['arabicName'])}',
                        style: const TextStyle(fontSize: 11),
                      ),
                      onPressed: () => openPage(
                        context,
                        MealDetails(
                          auth: widget.auth,
                          id: textOf(obj(m.$3)['id']),
                        ),
                      ),
                    ),
              ],
            ),
            const SizedBox(height: 6),
            UiText(
              localized(
                context,
                d['isTrainingDay'] == true
                    ? 'يوم تدريب · اقتراحات تتغير يوميًا'
                    : 'يوم راحة · اقتراحات تتغير يوميًا',
                d['isTrainingDay'] == true
                    ? 'Training day · Daily rotation'
                    : 'Rest day · Daily rotation',
              ),
              style: Theme.of(context).textTheme.labelSmall,
            ),
            UiText(
              textOf(d['disclaimer']),
              style: const TextStyle(fontSize: 10),
            ),
          ],
        ),
      );
    },
  );
}
