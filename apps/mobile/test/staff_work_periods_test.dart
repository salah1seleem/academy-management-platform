import 'package:academy_mobile/features/shared/staff_work_periods.dart';
import 'package:flutter/material.dart';
import 'package:flutter_localizations/flutter_localizations.dart';
import 'package:flutter_test/flutter_test.dart';

import 'coach_test.dart' show coach, shell;

final sample = {
  'configured': true,
  'today': '2026-10-16',
  'workDate': '2026-10-15',
  'totalWorkedSeconds': null,
  'knownWorkedSeconds': 7200,
  'items': [
    {
      'id': 'p1',
      'startDate': '2026-10-15',
      'startTime': '08:00:00',
      'endDate': '2026-10-15',
      'endTime': '10:00:00',
      'trustSource': 'LegacyLocal',
      'workedSeconds': null,
    },
    {
      'id': 'p2',
      'startDate': '2026-10-15',
      'startTime': '14:00:00',
      'endDate': null,
      'endTime': null,
      'trustSource': 'AdminDeclared',
      'workedSeconds': null,
    },
  ],
};

void main() {
  testWidgets(
    'Verified start and mixed checkout keep different trust labels without offering writes',
    (t) async {
      final auth = await coach(
        (m, p, b, token) async => {
          ...sample,
          'items': [
            <String, dynamic>{
              ...Map<String, dynamic>.from((sample['items'] as List)[0] as Map),
              'trustSource': 'ServerVerified',
            },
            <String, dynamic>{
              ...Map<String, dynamic>.from((sample['items'] as List)[1] as Map),
              'trustSource': 'MixedVerifiedEnd',
            },
          ],
        },
      );
      await t.pumpWidget(shell(StaffWorkPeriodsPage(auth: auth)));
      await t.pumpAndSettle();
      await t.scrollUntilVisible(
        find.text('بداية بتوقيع متحقق خادميًا — لا يضمن سلامة الموقع'),
        160,
        scrollable: find.byType(Scrollable).first,
      );
      expect(
        find.text('بداية بتوقيع متحقق خادميًا — لا يضمن سلامة الموقع'),
        findsOneWidget,
      );
      await t.scrollUntilVisible(
        find.text('بداية إدارية وانصراف بتوقيع متحقق خادميًا'),
        160,
        scrollable: find.byType(Scrollable).first,
      );
      expect(
        find.text('بداية إدارية وانصراف بتوقيع متحقق خادميًا'),
        findsOneWidget,
      );
      expect(find.text('حفظ'), findsNothing);
      await t.pumpWidget(const SizedBox());
      auth.dispose();
    },
  );
  testWidgets(
    'Own preserved periods show legacy/open uncertainty and never offer employee or salary writes',
    (t) async {
      final calls = <String>[];
      final auth = await coach((method, path, body, token) async {
        calls.add('$method $path');
        return sample;
      });
      await t.pumpWidget(shell(StaffWorkPeriodsPage(auth: auth)));
      await t.pumpAndSettle();
      expect(find.text('يوم العمل: 2026-10-15'), findsOneWidget);
      expect(find.text('الإجمالي غير مكتمل أو غير مؤكد'), findsOneWidget);
      expect(find.text('سجل محلي قديم — مدة غير مؤكدة'), findsOneWidget);
      await t.scrollUntilVisible(
        find.text('فترة مفتوحة — لم يسجل انصراف'),
        160,
        scrollable: find.byType(Scrollable).first,
      );
      expect(find.text('فترة مفتوحة — لم يسجل انصراف'), findsOneWidget);
      expect(find.byType(TextFormField), findsNothing);
      expect(find.text('حفظ'), findsNothing);
      expect(calls, ['GET /api/v1/staff/work-periods']);
    },
  );
  testWidgets(
    'Refresh retries the same own day and clears private data on membership removal',
    (t) async {
      var reads = 0;
      final auth = await coach((m, p, b, token) async {
        reads++;
        return {...sample, 'totalWorkedSeconds': 14400};
      });
      await t.pumpWidget(shell(StaffWorkPeriodsPage(auth: auth)));
      await t.pumpAndSettle();
      expect(find.text('إجمالي الفترات: 240 دقيقة'), findsOneWidget);
      await t.tap(find.text('تحديث'));
      await t.pumpAndSettle();
      expect(reads, 2);
      await auth.logout();
      await t.pumpAndSettle();
      expect(find.text('الفترة 1'), findsNothing);
      expect(find.text('إجمالي الفترات: 240 دقيقة'), findsNothing);
    },
  );
  for (final width in [360.0, 390.0, 430.0]) {
    testWidgets(
      'Own period cards remain usable in RTL/English, light/dark and 200 percent at $width',
      (t) async {
        t.view.physicalSize = Size(width * 2, 1000 * 2);
        t.view.devicePixelRatio = 2;
        addTearDown(t.view.resetPhysicalSize);
        addTearDown(t.view.resetDevicePixelRatio);
        final auth = await coach((m, p, b, token) async => sample);
        for (final locale in [const Locale('ar'), const Locale('en')]) {
          for (final mode in [Brightness.light, Brightness.dark]) {
            await t.pumpWidget(
              MaterialApp(
                key: UniqueKey(),
                locale: locale,
                supportedLocales: const [Locale('ar'), Locale('en')],
                localizationsDelegates: GlobalMaterialLocalizations.delegates,
                theme: ThemeData(brightness: mode),
                builder: (c, w) => MediaQuery(
                  data: MediaQuery.of(c)
                      .copyWith(textScaler: const TextScaler.linear(2)),
                  child: w!,
                ),
                home: StaffWorkPeriodsPage(auth: auth),
              ),
            );
            await t.pumpAndSettle();
            await t.drag(find.byType(Scrollable).first, const Offset(0, -700));
            await t.pumpAndSettle();
            expect(t.takeException(), isNull);
          }
        }
      },
    );
  }
}
