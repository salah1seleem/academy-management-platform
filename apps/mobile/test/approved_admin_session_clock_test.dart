import 'package:academy_mobile/features/guardian/guardian_ui.dart';
import 'package:academy_mobile/core/theme/app_theme.dart';
import 'package:academy_mobile/core/theme/ui_text.dart';
import 'package:academy_mobile/features/admin/admin_home.dart';
import 'package:flutter/material.dart';
import 'package:flutter_localizations/flutter_localizations.dart';
import 'package:flutter_test/flutter_test.dart';

import 'v2_role_workspaces_test.dart' as f;

void main() {
  for (final light in [true, false]) {
    for (final width in [375.0, 440.0]) {
      testWidgets(
        'admin clock remains start to end in RTL at 200 percent, light=$light width=$width',
        (t) async {
          t.view.physicalSize = Size(width, 956);
          t.view.devicePixelRatio = 1;
          addTearDown(t.view.resetPhysicalSize);
          addTearDown(t.view.resetDevicePixelRatio);
          final date = DateTime.now().toIso8601String().substring(0, 10);
          final auth = await f.authenticate(
            'AcademyAdmin',
            (m, p, b, token) async => [
              {
                'id': 'session',
                'group': 'مجموعة الوقت الاصطناعية',
                'sessionDate': date,
                'startTime': '12:00:00',
                'endTime': '13:00:00',
                'branch': 'فرع تجريبي',
                'sport': 'كرة القدم',
                'status': 'Scheduled',
              },
            ],
          );
          await t.pumpWidget(
            MaterialApp(
              locale: const Locale('ar'),
              supportedLocales: const [Locale('ar')],
              localizationsDelegates: GlobalMaterialLocalizations.delegates,
              theme: light ? AcademyTheme.light() : AcademyTheme.dark(),
              builder: (context, child) => MediaQuery(
                data: MediaQuery.of(context)
                    .copyWith(textScaler: const TextScaler.linear(2)),
                child: child!,
              ),
              home: NativeSessions(auth: auth),
            ),
          );
          await t.pumpAndSettle();
          final clock = find.widgetWithText(
            UiText,
            '${sessionDateLabel(date)} · من 12:00 مساءً إلى 1:00 مساءً',
          );
          expect(clock, findsOneWidget);
          expect(t.widget<UiText>(clock).textDirection, TextDirection.rtl);
          expect(find.text('فرع تجريبي · كرة القدم'), findsOneWidget);
          expect(
            t
                .widget<ListTile>(
                  find.widgetWithText(ListTile, 'مجموعة الوقت الاصطناعية'),
                )
                .onTap,
            isNotNull,
          );
          expect(t.takeException(), isNull);
          await t.pumpWidget(const SizedBox());
          auth.dispose();
        },
      );
    }
  }
}
