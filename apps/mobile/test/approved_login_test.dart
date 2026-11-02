import 'dart:async';

import 'package:academy_mobile/core/api/api_client.dart';
import 'package:academy_mobile/core/auth/auth_controller.dart';
import 'package:academy_mobile/core/theme/app_theme.dart';
import 'package:academy_mobile/features/auth/login_page.dart';
import 'package:flutter/material.dart';
import 'package:flutter_localizations/flutter_localizations.dart';
import 'package:flutter_test/flutter_test.dart';

import 'auth_controller_test.dart' show FakeApi, MemoryStore, credentials;

Widget login(
  AuthController auth, {
  bool dark = false,
  double scale = 1,
  String language = 'ar',
  String environment = 'Demo',
}) => MaterialApp(
  locale: Locale(language),
  supportedLocales: const [Locale('ar'), Locale('en')],
  localizationsDelegates: GlobalMaterialLocalizations.delegates,
  theme: dark ? AcademyTheme.dark() : AcademyTheme.light(),
  builder: (context, child) => MediaQuery(
    data: MediaQuery.of(
      context,
    ).copyWith(textScaler: TextScaler.linear(scale), disableAnimations: true),
    child: child!,
  ),
  home: LoginPage(auth: auth, environment: environment),
);

Future<void> submit(WidgetTester t) async {
  await t.pumpAndSettle();
  await t.ensureVisible(find.byKey(const ValueKey('login-submit')));
  await t.pumpAndSettle();
  await t.tap(find.byKey(const ValueKey('login-submit')));
  await t.pumpAndSettle();
}

void main() {
  for (final width in [375.0, 440.0]) {
    for (final dark in [false, true]) {
      for (final scale in [1.0, 2.0]) {
        for (final language in ['ar', 'en']) {
          testWidgets(
            'approved login $width dark=$dark scale=$scale $language keeps six-digit server contract',
            (t) async {
              t.view.physicalSize = Size(width, width == 375 ? 667 : 956);
              t.view.devicePixelRatio = 1;
              addTearDown(t.view.reset);
              final bodies = <Map<String, dynamic>>[];
              final api = FakeApi((method, path, body, token) async {
                expect(method, 'POST');
                bodies.add(Map<String, dynamic>.from(body as Map));
                return path.endsWith('/request')
                    ? {'challengeId': 'synthetic-challenge'}
                    : credentials();
              });
              final store = MemoryStore();
              final auth = AuthController(api, store)..restoring = false;
              await t.pumpWidget(
                login(auth, dark: dark, scale: scale, language: language),
              );
              await t.pumpAndSettle();
              expect(
                t
                    .getSize(find.byKey(const ValueKey('approved-login-hero')))
                    .height,
                226,
              );
              expect(
                find.byKey(const ValueKey('login-six-code-boxes')),
                findsNothing,
              );
              expect(auth.session, isNull);
              await t.enterText(find.byType(TextFormField), '01099900100');
              await submit(t);
              expect(bodies, [
                {'phoneNumber': '01099900100'},
              ]);
              final boxes = find.byKey(const ValueKey('login-six-code-boxes'));
              expect(
                find.descendant(of: boxes, matching: find.byType(Expanded)),
                findsNWidgets(6),
              );
              expect(
                t
                    .widget<Text>(
                      find.byKey(const ValueKey('login-masked-phone')),
                    )
                    .data,
                '010 ••••• 100',
              );
              await t.enterText(find.byType(TextFormField), '2468');
              await submit(t);
              expect(
                bodies.length,
                1,
              ); // Prototype OTP4 must never reach verify.
              expect(
                find.text(
                  language == 'ar'
                      ? 'أدخل رمز التحقق من ٦ أرقام.'
                      : 'Enter the six-digit verification code.',
                ),
                findsWidgets,
              );
              await t.enterText(find.byType(TextFormField), '246810');
              await t.pump();
              for (final digit in ['2', '4', '6', '8', '1', '0']) {
                expect(
                  find.descendant(of: boxes, matching: find.text(digit)),
                  findsOneWidget,
                );
              }
              await submit(t);
              expect(bodies.last, {
                'phoneNumber': '01099900100',
                'challengeId': 'synthetic-challenge',
                'code': '246810',
                'deviceName': 'Academy Native',
              });
              expect(auth.session!.selected!.role, 'Guardian');
              expect(store.token, 'refresh-one');
              expect(t.takeException(), isNull);
              await t.pumpWidget(const SizedBox());
              auth.dispose();
            },
          );
        }
      }
    }
  }

  testWidgets(
    'busy OTP request cannot submit twice and changing number clears the code',
    (t) async {
      final pending = Completer<Object?>();
      final api = FakeApi((method, path, body, token) => pending.future);
      final auth = AuthController(api, MemoryStore())..restoring = false;
      await t.pumpWidget(login(auth));
      await t.enterText(find.byType(TextFormField), '01099900100');
      await t.ensureVisible(find.byKey(const ValueKey('login-submit')));
      await t.tap(find.byKey(const ValueKey('login-submit')));
      await t.pump();
      expect(
        t
            .widget<FilledButton>(find.byKey(const ValueKey('login-submit')))
            .onPressed,
        isNull,
      );
      await t.tap(find.byKey(const ValueKey('login-submit')));
      expect(api.paths.length, 1);
      pending.complete({'challengeId': 'synthetic-challenge'});
      await t.pumpAndSettle();
      await t.enterText(find.byType(TextFormField), '246810');
      await t.pumpAndSettle();
      await t.ensureVisible(find.text('تغيير الرقم أو طلب رمز جديد'));
      await t.pumpAndSettle();
      await t.tap(find.text('تغيير الرقم أو طلب رمز جديد'));
      await t.pumpAndSettle();
      expect(find.byKey(const ValueKey('login-six-code-boxes')), findsNothing);
      expect(
        t.widget<TextFormField>(find.byType(TextFormField)).controller!.text,
        '01099900100',
      );
      expect(auth.session, isNull);
      await t.pumpWidget(const SizedBox());
      auth.dispose();
    },
  );

  for (final storageFailure in [false, true]) {
    testWidgets(
      'rejected login does not claim success storageFailure=$storageFailure',
      (t) async {
        final store = MemoryStore()..failWrite = storageFailure;
        final auth = AuthController(
          FakeApi((method, path, body, token) async {
            if (path.endsWith('/request')) {
              return {'challengeId': 'synthetic-challenge'};
            }
            if (!storageFailure) {
              throw const ApiFailure(401, 'رمز التحقق غير صحيح.');
            }
            return credentials();
          }),
          store,
        )..restoring = false;
        await t.pumpWidget(login(auth));
        await t.enterText(find.byType(TextFormField), '01099900100');
        await submit(t);
        await t.enterText(find.byType(TextFormField), '246810');
        await submit(t);
        expect(auth.session, isNull);
        expect(store.token, isNull);
        expect(
          find.text(
            storageFailure
                ? 'تعذر إكمال الدخول أو حفظ الجلسة بأمان. أعد المحاولة.'
                : 'رمز التحقق غير صحيح.',
          ),
          findsOneWidget,
        );
        expect(
          find.byKey(const ValueKey('login-six-code-boxes')),
          findsOneWidget,
        );
        await t.pumpWidget(const SizedBox());
        auth.dispose();
      },
    );
  }
}
