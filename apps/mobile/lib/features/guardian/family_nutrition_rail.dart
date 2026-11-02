import 'package:flutter/material.dart';

import '../../core/api/api_client.dart';
import '../../core/auth/auth_controller.dart';
import '../../core/theme/ui_text.dart';
import 'daily_nutrition.dart';
import 'guardian_content.dart';
import 'guardian_ui.dart';

typedef _Meal = ({
  Json child,
  Json? meal,
  String slot,
  String note,
  bool failed,
});

class FamilyNutritionRail extends StatefulWidget {
  const FamilyNutritionRail({
    super.key,
    required this.auth,
    required this.children,
    required this.today,
  });
  final AuthController auth;
  final List<Json> children;
  final String today;
  @override
  State<FamilyNutritionRail> createState() => _FamilyNutritionRailState();
}

class _FamilyNutritionRailState extends State<FamilyNutritionRail> {
  String? identity() => widget.auth.session?.selected == null
      ? null
      : '${widget.auth.session!.selected!.academyId}:${widget.auth.session!.selected!.id}:${widget.auth.session!.selected!.contextRevision}';
  late final openedIdentity = identity();
  late Future<List<_Meal>> future;
  String childIds(List<Json> children) =>
      children.map((c) => textOf(c['id'])).join('|');
  Future<List<_Meal>> load() async {
    final groups = await Future.wait(
      widget.children.map((child) async {
        try {
          final data = await widget.auth.request(
            'GET',
            dailyNutritionPath(textOf(child['id']), widget.today),
          );
          if (data is! Map) throw const FormatException();
          final d = obj(data);
          final meals = <_Meal>[
            for (final item in dailyNutritionMeals(d))
              if (item.$3 is Map &&
                  obj(item.$3)['id'] is String &&
                  (obj(item.$3)['id'] as String).trim().isNotEmpty)
                (
                  child: child,
                  meal: obj(item.$3),
                  slot: item.$1,
                  note: d['isTrainingDay'] == true ? 'يوم تدريب' : 'يوم راحة',
                  failed: false,
                ),
          ];
          if (meals.isNotEmpty) {
            return [
              meals[widget.children.indexWhere((c) => c['id'] == child['id']) %
                  meals.length],
            ];
          }
          return <_Meal>[
            (
              child: child,
              meal: null,
              slot: '',
              note: 'لا توجد اقتراحات مناسبة حاليًا',
              failed: false,
            ),
          ];
        } catch (error) {
          final unavailable =
              error is ApiFailure && [400, 404, 422].contains(error.status);
          return <_Meal>[
            (
              child: child,
              meal: null,
              slot: '',
              note: unavailable
                  ? 'لا توجد اقتراحات مناسبة حاليًا'
                  : 'تعذر تحميل وجبات اليوم',
              failed: !unavailable,
            ),
          ];
        }
      }),
    );
    return groups.expand((g) => g).toList();
  }

  @override
  void initState() {
    super.initState();
    future = load();
  }

  @override
  void didUpdateWidget(FamilyNutritionRail old) {
    super.didUpdateWidget(old);
    if (openedIdentity == identity() &&
        (old.today != widget.today ||
            childIds(old.children) != childIds(widget.children))) {
      future = load();
    }
  }

  void open(_Meal item) {
    if (identity() != openedIdentity) return;
    if (item.failed) {
      setState(() {
        future = load();
      });
      return;
    }
    openPage(
      context,
      NutritionLibrary(
        auth: widget.auth,
        playerId: textOf(item.child['id']),
        playerName: textOf(item.child['arabicName']),
        today: widget.today,
      ),
    );
  }

  @override
  Widget build(BuildContext context) => ListenableBuilder(
    listenable: widget.auth,
    builder: (context, _) {
      if (openedIdentity == null || openedIdentity != identity()) {
        return const SizedBox.shrink();
      }
      if (widget.children.isEmpty) return const SizedBox.shrink();
      return FutureBuilder<List<_Meal>>(
        future: future,
        builder: (context, result) {
          if (result.connectionState != ConnectionState.done) {
            return const Padding(
              padding: EdgeInsets.all(24),
              child: Center(child: LinearProgressIndicator()),
            );
          }
          final items = result.data ?? <_Meal>[];
          return LayoutBuilder(
            builder: (context, constraints) {
              final width = constraints.maxWidth * .58;
              final colors = Theme.of(context).colorScheme;
              return SingleChildScrollView(
                key: const ValueKey('family-meals-rail'),
                scrollDirection: Axis.horizontal,
                child: IntrinsicHeight(
                  child: Row(
                    crossAxisAlignment: CrossAxisAlignment.stretch,
                    children: [
                      for (var i = 0; i < items.length; i++) ...[
                        if (i > 0) const SizedBox(width: 10),
                        SizedBox(
                          width: width,
                          child: Material(
                            key: ValueKey('family-meal-$i'),
                            color: colors.surface,
                            shape: Border.all(color: colors.outlineVariant),
                            clipBehavior: Clip.antiAlias,
                            child: InkWell(
                              onTap: () => open(items[i]),
                              child: Column(
                                mainAxisSize: MainAxisSize.min,
                                crossAxisAlignment: CrossAxisAlignment.start,
                                children: [
                                  Stack(
                                    children: [
                                      if (items[i].meal != null)
                                        DemoImage(
                                          items[i].meal!['imageReference'],
                                          height: width * .75,
                                          borderRadius: BorderRadius.zero,
                                        )
                                      else
                                        SizedBox(
                                          height: width * .75,
                                          width: width,
                                          child: Icon(
                                            items[i].failed
                                                ? Icons.refresh
                                                : Icons.restaurant_outlined,
                                            color: colors.primary,
                                            size: 36,
                                          ),
                                        ),
                                      PositionedDirectional(
                                        top: 8,
                                        start: 8,
                                        end: 8,
                                        child: Align(
                                          alignment:
                                              AlignmentDirectional.topStart,
                                          child: Container(
                                            color: colors.surface,
                                            padding: const EdgeInsets.symmetric(
                                              horizontal: 8,
                                              vertical: 4,
                                            ),
                                            child: Row(
                                              mainAxisSize: MainAxisSize.min,
                                              children: [
                                                SizedBox(
                                                  width: 8,
                                                  height: 8,
                                                  child: ColoredBox(
                                                    color: colors.primary,
                                                  ),
                                                ),
                                                const SizedBox(width: 6),
                                                Flexible(
                                                  child: UiText(
                                                    textOf(
                                                      items[i]
                                                          .child['arabicName'],
                                                    ),
                                                    maxLines: 2,
                                                    overflow:
                                                        TextOverflow.ellipsis,
                                                    style: const TextStyle(
                                                      fontSize: 12,
                                                      fontWeight:
                                                          FontWeight.w600,
                                                    ),
                                                  ),
                                                ),
                                              ],
                                            ),
                                          ),
                                        ),
                                      ),
                                    ],
                                  ),
                                  Padding(
                                    padding: const EdgeInsets.all(10),
                                    child: Column(
                                      crossAxisAlignment:
                                          CrossAxisAlignment.start,
                                      children: [
                                        UiText(
                                          items[i].meal == null
                                              ? items[i].note
                                              : 'النظام الغذائي ليوم ${day(widget.today)}',
                                          maxLines: 3,
                                          overflow: TextOverflow.ellipsis,
                                          style: const TextStyle(
                                            fontSize: 14,
                                            fontWeight: FontWeight.w600,
                                          ),
                                        ),
                                        const SizedBox(height: 2),
                                        UiText(
                                          items[i].meal == null
                                              ? (items[i].failed
                                                    ? 'إعادة المحاولة'
                                                    : 'عرض مكتبة التغذية')
                                              : '${items[i].slot} · ${items[i].note}',
                                          style: TextStyle(
                                            fontSize: 12,
                                            color: colors.onSurfaceVariant,
                                          ),
                                        ),
                                      ],
                                    ),
                                  ),
                                ],
                              ),
                            ),
                          ),
                        ),
                      ],
                    ],
                  ),
                ),
              );
            },
          );
        },
      );
    },
  );
}
