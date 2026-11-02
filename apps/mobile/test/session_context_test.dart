import 'package:academy_mobile/core/theme/app_theme.dart';
import 'package:academy_mobile/features/coach/coach_attendance.dart';
import 'package:academy_mobile/features/coach/coach_home.dart';
import 'package:academy_mobile/features/coach/session_context.dart';
import 'package:academy_mobile/features/guardian/guardian_ui.dart';
import 'package:flutter/material.dart';
import 'package:flutter_localizations/flutter_localizations.dart';
import 'package:flutter_test/flutter_test.dart';

import 'coach_test.dart' show coach, shell;
import 'coach_session_workspace_test.dart' show roster;

void main() {
  testWidgets(
    'Badges require explicit known source; names and missing data never imply private or substitute',
    (tester) async {
      for (final entry in {
        'Group': 'جماعية',
        'Private': 'برايفت',
        'SemiPrivate': 'شبه خاصة',
      }.entries) {
        await tester.pumpWidget(
          shell(
            Scaffold(
              body: SessionContextBadges(
                session: {
                  'group': 'اسم جماعي برايفت مضلل',
                  'trainingFormat': entry.key,
                  'trainingFormatSource': 'CurrentGroup',
                  'isSubstitute': true,
                },
                showSource: true,
              ),
            ),
          ),
        );
        expect(find.text(entry.value), findsOneWidget);
        expect(find.text('مدرب بديل'), findsOneWidget);
        expect(
          find.text('نوع التدريب حسب إعداد المجموعة الحالي'),
          findsOneWidget,
        );
      }
      for (final value in [
        <String, dynamic>{'group': 'برايفت بديل'},
        {'trainingFormat': 'Private'},
        {
          'trainingFormat': 'Unspecified',
          'trainingFormatSource': 'CurrentGroup',
        },
      ]) {
        await tester.pumpWidget(
          shell(Scaffold(body: SessionContextBadges(session: value))),
        );
        expect(find.byType(Chip), findsNothing);
      }
    },
  );

  testWidgets(
    'Roster repeats actual session metadata without granting attendance or altering the evaluation context',
    (tester) async {
      final calls = <String>[];
      final auth = await coach((m, p, b, t) async {
        expect(m, 'GET');
        calls.add(p);
        final data = roster();
        data['session'] = {
          ...obj(data['session']),
          'trainingFormat': 'SemiPrivate',
          'trainingFormatSource': 'CurrentGroup',
          'isSubstitute': true,
        };
        return data;
      });
      await tester.pumpWidget(
        shell(CoachAttendance(auth: auth, sessionId: 'actual')),
      );
      await tester.pumpAndSettle();
      expect(find.text('شبه خاصة'), findsOneWidget);
      expect(find.text('مدرب بديل'), findsOneWidget);
      expect(find.text('حفظ الحضور'), findsNothing);
      expect(calls, ['/api/v1/attendance/sessions/actual/players']);
      expect(tester.takeException(), isNull);
      await tester.pumpWidget(const SizedBox());
      auth.dispose();
    },
  );

  for (final width in [360.0, 390.0, 430.0]) {
    testWidgets(
      'Session badges and real CTA at $width, RTL/English, dark/light and 200% text',
      (tester) async {
        tester.view.physicalSize = Size(width, 1400);
        tester.view.devicePixelRatio = 1;
        addTearDown(tester.view.resetPhysicalSize);
        addTearDown(tester.view.resetDevicePixelRatio);
        var opened = 0;
        for (final locale in [const Locale('ar'), const Locale('en')]) {
          for (final dark in [true, false]) {
            await tester.pumpWidget(
              MaterialApp(
                locale: locale,
                supportedLocales: const [Locale('ar'), Locale('en')],
                localizationsDelegates: GlobalMaterialLocalizations.delegates,
                theme: dark ? guardianTheme() : AcademyTheme.light(),
                home: MediaQuery(
                  data: MediaQueryData(
                    size: Size(width, 1400),
                    textScaler: const TextScaler.linear(2),
                  ),
                  child: Scaffold(
                    body: ListView(
                      children: [
                        CoachSessionCard(
                          featured: true,
                          session: const {
                            'id': 'actual',
                            'group': 'مجموعة الناشئين للتطوير والتدريب بفرع التجمع الخامس',
                            'sessionDate': '2026-10-16',
                            'startTime': '18:00',
                            'endTime': '19:00',
                            'branch': 'التجمع الخامس',
                            'sport': 'السباحة',
                            'resource': 'المسبح · الحارة أ',
                            'trainingFormat': 'SemiPrivate',
                            'trainingFormatSource': 'CurrentGroup',
                            'isSubstitute': true,
                          },
                          onOpen: () => opened++,
                        ),
                      ],
                    ),
                  ),
                ),
              ),
            );
            await tester.pumpAndSettle();
            final button = find.byType(FilledButton);
            await tester.scrollUntilVisible(
              button,
              200,
              scrollable: find.byType(Scrollable).first,
            );
            await Scrollable.ensureVisible(
              tester.element(button),
              alignment: .5,
            );
            await tester.pumpAndSettle();
            await tester.tap(button);
            expect(
              find.text(
                locale.languageCode == 'ar' ? 'شبه خاصة' : 'Semi-private',
              ),
              findsOneWidget,
            );
            expect(
              find.text(
                locale.languageCode == 'ar' ? 'مدرب بديل' : 'Substitute coach',
              ),
              findsOneWidget,
            );
            expect(tester.takeException(), isNull);
          }
        }
        expect(opened, 4);
      },
    );
  }
}
