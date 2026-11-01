import 'package:academy_mobile/features/guardian/guardian_ui.dart';
import 'package:flutter/material.dart';
import 'package:flutter_localizations/flutter_localizations.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  for (final direction in [TextDirection.rtl, TextDirection.ltr]) {
    for (final width in [375.0, 440.0]) {
      for (final scale in [1.0, 2.0]) {
        for (final compact in [false, true]) {
          testWidgets(
            'shared link opens one destination and back restores its source: '
            '$direction $width scale$scale compact$compact',
            (t) async {
              t.view.physicalSize = Size(width, 956);
              t.view.devicePixelRatio = 1;
              addTearDown(t.view.resetPhysicalSize);
              addTearDown(t.view.resetDevicePixelRatio);
              var opened = 0;
              const title = 'ملف اللاعب وسجل حضوره وتقييماته';
              await t.pumpWidget(
                MaterialApp(
                  locale: const Locale('ar'),
                  supportedLocales: const [Locale('ar')],
                  localizationsDelegates: GlobalMaterialLocalizations.delegates,
                  theme: ThemeData(
                    brightness: width == 375
                        ? Brightness.light
                        : Brightness.dark,
                  ),
                  builder: (context, child) => Directionality(
                    textDirection: direction,
                    child: MediaQuery(
                      data: MediaQuery.of(context)
                          .copyWith(textScaler: TextScaler.linear(scale)),
                      child: child!,
                    ),
                  ),
                  home: Builder(
                    builder: (context) => Scaffold(
                      body: SingleChildScrollView(
                        child: GLink(
                          title,
                          Icons.person_outline,
                          () {
                            opened++;
                            Navigator.of(context).push(
                              MaterialPageRoute<void>(
                                builder: (_) => Scaffold(
                                  appBar: AppBar(
                                    title: const Text('ملف اللاعب'),
                                  ),
                                  body: const Text('سجل اللاعب المحفوظ'),
                                ),
                              ),
                            );
                          },
                          subtitle: 'عرض التفاصيل دون تعديل البيانات',
                          compact: compact,
                        ),
                      ),
                    ),
                  ),
                ),
              );
              await t.pumpAndSettle();
              final link = find.byType(GLink);
              final tile = find.descendant(
                of: link,
                matching: find.byType(ListTile),
              );
              final label = find.text(title);
              final trailing = find
                  .descendant(of: tile, matching: find.byType(Icon))
                  .last;
              final labelRect = t.getRect(label);
              final iconRect = t.getRect(trailing);
              expect(iconRect.width, greaterThanOrEqualTo(24));
              if (direction == TextDirection.rtl) {
                expect(iconRect.right, lessThanOrEqualTo(labelRect.left));
              } else {
                expect(iconRect.left, greaterThanOrEqualTo(labelRect.right));
              }
              expect(t.getSize(tile).height, greaterThanOrEqualTo(48));
              expect(t.takeException(), isNull);
              // The complete tile remains an action, including its arrow area.
              await t.tapAt(iconRect.center);
              await t.pumpAndSettle();
              expect(opened, 1);
              expect(find.text('سجل اللاعب المحفوظ'), findsOneWidget);
              expect(find.byType(BackButton), findsOneWidget);
              await t.tap(find.byType(BackButton));
              await t.pumpAndSettle();
              expect(find.text(title), findsOneWidget);
              expect(opened, 1);
              expect(t.takeException(), isNull);
            },
          );
        }
      }
    }
  }
}
