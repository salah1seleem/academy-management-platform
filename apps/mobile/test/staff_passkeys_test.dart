import 'package:academy_mobile/features/shared/staff_passkeys.dart';
import 'package:academy_mobile/core/theme/app_theme.dart';
import 'package:academy_mobile/features/guardian/guardian_ui.dart';
import 'package:flutter/material.dart';
import 'package:flutter_localizations/flutter_localizations.dart';
import 'package:flutter_test/flutter_test.dart';

import 'coach_test.dart' show coach, shell;

final key = <String, dynamic>{
  'id': 'own-key',
  'name': 'وسيلة عرض اصطناعية',
  'isActive': true,
  'mayBeSynced': true,
  'createdAtUtc': '2026-10-16T10:00:00Z',
};
Future<void> reach(WidgetTester t, Finder f) async {
  await t.scrollUntilVisible(f, 180, scrollable: find.byType(Scrollable).first);
  await t.ensureVisible(f);
  await t.pumpAndSettle();
}

void main() {
  testWidgets(
    'Branch policy reflects saved bounds, required category and disabled site without a write',
    (t) async {
      final calls = <String>[];
      final auth = await coach((m, p, b, token) async {
        expect(m, 'GET');
        calls.add(p);
        return {
          'branches': <Map<String, dynamic>>[
            {
              'id': 'one',
              'name': 'فرع سياسة اصطناعي',
              'enabled': true,
              'configured': true,
              'radiusMeters': 80,
              'maxAccuracyMeters': 10,
              'maxAgeSeconds': 15,
              'challengeSeconds': 60,
              'attendanceRequired': true,
            },
            {
              'id': 'two',
              'name': 'فرع موقوف',
              'enabled': false,
              'configured': false,
            },
          ],
        };
      });
      await t.pumpWidget(shell(StaffAttendanceBranchPolicies(auth: auth)));
      await t.pumpAndSettle();
      expect(find.text('النطاق 80 مترًا · الدقة حتى 10 مترًا'), findsOneWidget);
      expect(
        find.text('تسجيل الحضور مطلوب لفئتك في هذا الفرع.'),
        findsOneWidget,
      );
      expect(
        find.text('التسجيل الذاتي موقوف — المراجعة الإدارية متاحة.'),
        findsOneWidget,
      );
      expect(calls, ['/api/v1/staff/passkeys/attendance']);
      expect(t.takeException(), isNull);
      await t.pumpWidget(const SizedBox());
      auth.dispose();
    },
  );
  testWidgets(
    'Own methods are read only until explicit revocation; cancelling does not write; server stop remains in history',
    (t) async {
      var stopped = false;
      final writes = <String>[];
      final auth = await coach((m, p, b, token) async {
        if (m == 'POST') {
          writes.add(p);
          stopped = true;
          return {...key, 'isActive': false};
        }
        return {
          'configured': true,
          'items': [
            {...key, 'isActive': !stopped},
          ],
        };
      });
      await t.pumpWidget(shell(StaffPasskeyPage(auth: auth)));
      await t.pumpAndSettle();
      expect(writes, isEmpty);
      expect(
        find.textContaining('تكامل الجهاز الأصلي لم يكتمل'),
        findsOneWidget,
      );
      expect(find.text('إضافة وسيلة تحقق'), findsNothing);
      await reach(t, find.text('إيقاف الوسيلة'));
      await t.tap(find.text('إيقاف الوسيلة'));
      await t.pumpAndSettle();
      await t.tap(find.text('رجوع'));
      await t.pumpAndSettle();
      expect(writes, isEmpty);
      await t.tap(find.text('إيقاف الوسيلة'));
      await t.pumpAndSettle();
      await t.tap(find.text('تأكيد الإيقاف'));
      await t.pumpAndSettle();
      expect(writes, ['/api/v1/staff/passkeys/credentials/own-key/revoke']);
      await reach(t, find.text('متوقفة'));
      expect(find.text(key['name'] as String), findsOneWidget);
      expect(t.takeException(), isNull);
      await auth.logout();
      await t.pumpAndSettle();
      expect(find.text(key['name'] as String), findsNothing);
      await t.pumpWidget(const SizedBox());
      auth.dispose();
    },
  );
  testWidgets(
    'Disabled or failed server configuration is not a verified biometric or an empty success',
    (t) async {
      var fail = true;
      final auth = await coach((m, p, b, token) async {
        expect(m, 'GET');
        if (fail) throw StateError('Technical detail');
        return {'configured': false, 'items': []};
      });
      await t.pumpWidget(shell(StaffPasskeyPage(auth: auth)));
      await t.pumpAndSettle();
      expect(find.text('تعذر عرض وسائل التحقق الخاصة بحسابك.'), findsOneWidget);
      expect(find.text('Technical detail'), findsNothing);
      fail = false;
      await t.tap(find.text('إعادة المحاولة'));
      await t.pumpAndSettle();
      await reach(t, find.textContaining('يلزم إعداد التحقق الآمن'));
      expect(find.text('إيقاف الوسيلة'), findsNothing);
      await t.pumpWidget(const SizedBox());
      auth.dispose();
    },
  );
  for (final width in [360.0, 390.0, 430.0]) {
    testWidgets(
      'Own verification methods fit $width RTL/English dark/light 200% text',
      (t) async {
        t.view.physicalSize = Size(width, 1100);
        t.view.devicePixelRatio = 1;
        addTearDown(t.view.resetPhysicalSize);
        addTearDown(t.view.resetDevicePixelRatio);
        final auth = await coach(
          (m, p, b, token) async => {
            'configured': true,
            'items': [key],
          },
        );
        for (final locale in [const Locale('ar'), const Locale('en')]) {
          for (final dark in [true, false]) {
            await t.pumpWidget(
              MaterialApp(
                locale: locale,
                supportedLocales: const [Locale('ar'), Locale('en')],
                localizationsDelegates: GlobalMaterialLocalizations.delegates,
                theme: dark ? guardianTheme() : AcademyTheme.light(),
                home: MediaQuery(
                  data: MediaQueryData(
                    size: Size(width, 1100),
                    textScaler: const TextScaler.linear(2),
                  ),
                  child: StaffPasskeyPage(auth: auth),
                ),
              ),
            );
            await t.pumpAndSettle();
            await reach(t, find.text('إيقاف الوسيلة'));
            expect(t.takeException(), isNull);
          }
        }
        await t.pumpWidget(const SizedBox());
        auth.dispose();
      },
    );
  }
}
