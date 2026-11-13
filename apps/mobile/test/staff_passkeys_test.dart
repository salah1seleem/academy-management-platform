import 'dart:async';

import 'package:academy_mobile/core/api/api_client.dart';
import 'package:academy_mobile/core/auth/staff_passkey_provider.dart';
import 'package:academy_mobile/features/shared/staff_passkeys.dart';
import 'package:academy_mobile/core/theme/app_theme.dart';
import 'package:academy_mobile/features/guardian/guardian_ui.dart';
import 'package:flutter/material.dart';
import 'package:flutter_localizations/flutter_localizations.dart';
import 'package:flutter_test/flutter_test.dart';

import 'coach_test.dart' show coach, shell;
import 'native_staff_attendance_test.dart' show FakeSigner, state;

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
        find.textContaining('إضافة وسيلة تحقق غير متاحة على هذا الجهاز'),
        findsOneWidget,
      );
      expect(find.text('إضافة وسيلة تحقق'), findsNothing);
      await reach(t, find.text('إيقاف الوسيلة'));
      final date = find.byWidgetPredicate(
        (widget) =>
            widget is Text &&
            widget.textDirection == TextDirection.ltr &&
            RegExp(r'^\d{4}-\d{2}-\d{2} \d{2}:\d{2}$')
                .hasMatch(widget.data ?? ''),
      );
      expect(date, findsOneWidget);
      await t.tap(find.text('إيقاف الوسيلة'));
      await t.pumpAndSettle();
      await t.tap(find.text('إلغاء'));
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
      await reach(t, find.textContaining('إضافة وسيلة تحقق غير متاحة حاليًا'));
      expect(find.text('إيقاف الوسيلة'), findsNothing);
      await t.pumpWidget(const SizedBox());
      auth.dispose();
    },
  );

  testWidgets(
    'Unconfirmed stop reads the same key without replaying the stop',
    (t) async {
      var writes = 0;
      var reads = 0;
      final auth = await coach((m, p, b, token) async {
        if (m == 'POST') {
          expect(p, '/api/v1/staff/passkeys/credentials/own-key/revoke');
          writes++;
          throw const ApiFailure(0, 'Connection interrupted');
        }
        reads++;
        if (reads == 2) throw const ApiFailure(503, 'Service unavailable');
        return {
          'configured': false,
          'items': [
            {...key, 'isActive': writes == 0},
            {...key, 'id': 'other-key', 'name': 'هاتف احتياطي'},
          ],
        };
      });
      await t.pumpWidget(shell(StaffPasskeyPage(auth: auth)));
      await t.pumpAndSettle();
      await reach(t, find.text('إيقاف الوسيلة').first);
      await t.tap(find.text('إيقاف الوسيلة').first);
      await t.pumpAndSettle();
      expect(find.text('إيقاف وسيلة «وسيلة عرض اصطناعية»؟'), findsOneWidget);
      await t.tap(find.text('تأكيد الإيقاف'));
      await t.pumpAndSettle();
      final check = find.text('تحقّق من حالة «وسيلة عرض اصطناعية»');
      expect(check, findsOneWidget);
      expect(writes, 1);
      expect(reads, 1);
      await t.tap(check);
      await t.pumpAndSettle();
      expect(
        find.text('تعذر التحقق من الحالة. أعد قراءة القائمة.'),
        findsOneWidget,
      );
      await t.tap(check);
      await t.pumpAndSettle();
      expect(find.text('تم إيقاف وسيلة التحقق.'), findsOneWidget);
      await reach(t, find.text('متوقفة'));
      expect(writes, 1);
      expect(reads, 3);
      await t.pumpWidget(const SizedBox());
      auth.dispose();
    },
  );

  for (final leaving in [false, true]) {
    testWidgets(
      'Cancelled or late device verification sends no proof (leaving: $leaving)',
      (t) async {
        final proof = Completer<void>();
        final signer = FakeSigner()
          ..wait = leaving ? proof.future : null
          ..error = leaving
              ? null
              : const StaffPasskeyProblem('تم إلغاء التحقق على الجهاز.');
        final writes = <String>[];
        final auth = await coach((m, p, b, token) async {
          if (m == 'GET') {
            return {
              ...state(),
              'items': [key],
            };
          }
          writes.add(p);
          return {
            'purpose': 'Verify',
            'challengeId': 'fixture',
            'options': {'challenge': 'AQ'},
          };
        });
        await t.pumpWidget(
          shell(StaffPasskeyPage(auth: auth, provider: signer)),
        );
        await t.pumpAndSettle();
        await reach(t, find.text('تجربة وسيلة التحقق'));
        await t.tap(find.text('تجربة وسيلة التحقق'));
        await t.pumpAndSettle();
        expect(signer.assertions, 1);
        if (leaving) {
          await t.pumpWidget(const SizedBox());
          expect(signer.cancellations, 1);
          proof.complete();
          await t.pumpAndSettle();
        } else {
          expect(find.text('تم إلغاء التحقق على الجهاز.'), findsOneWidget);
          final retry = find.widgetWithText(TextButton, 'تجربة وسيلة التحقق');
          expect(t.widget<TextButton>(retry).onPressed, isNotNull);
        }
        expect(writes, ['/api/v1/staff/passkeys/verify/options']);
        await t.pumpWidget(const SizedBox());
        auth.dispose();
      },
    );
  }

  testWidgets(
    'An unconfirmed addition rereads the list without attributing another key',
    (t) async {
      final signer = FakeSigner();
      var attempts = 0;
      final auth = await coach((m, p, b, token) async {
        if (m == 'GET') {
          return {
            ...state(),
            'items': attempts == 0 ? [] : [key],
          };
        }
        if (p.endsWith('/options')) {
          return {
            'purpose': 'Register',
            'challengeId': 'fixture',
            'options': {'challenge': 'AQ'},
          };
        }
        attempts++;
        throw const ApiFailure(0, 'Connection interrupted');
      });
      await t.pumpWidget(shell(StaffPasskeyPage(auth: auth, provider: signer)));
      await t.pumpAndSettle();
      await t.enterText(find.byType(TextField), 'هاتف العمل');
      await reach(t, find.text('إضافة وسيلة تحقق'));
      await t.tap(find.text('إضافة وسيلة تحقق'));
      await t.pumpAndSettle();
      final check = find.text('تحقّق من الوسائل المحفوظة');
      expect(check, findsOneWidget);
      await t.tap(check);
      await t.pumpAndSettle();
      expect(
        find.text(
          'تم تحديث القائمة. راجع الوسائل المحفوظة قبل المحاولة مرة أخرى.',
        ),
        findsOneWidget,
      );
      expect(find.text('تمت إضافة وسيلة التحقق.'), findsNothing);
      expect(attempts, 1);
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
            await reach(
              t,
              find.text(
                locale.languageCode == 'ar' ? 'إيقاف الوسيلة' : 'Stop method',
              ),
            );
            expect(t.takeException(), isNull);
          }
        }
        await t.pumpWidget(const SizedBox());
        auth.dispose();
      },
    );
  }
}
