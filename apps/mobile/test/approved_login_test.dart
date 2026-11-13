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
  testWidgets('Short phone screen keeps the entire first action visible', (
    t,
  ) async {
    t.view.physicalSize = const Size(320, 568);
    t.view.devicePixelRatio = 1;
    t.view.padding = const FakeViewPadding(top: 38, bottom: 29);
    addTearDown(t.view.reset);
    final auth = AuthController(
      FakeApi((_, _, _, _) async => null),
      MemoryStore(),
    )..restoring = false;
    await t.pumpWidget(login(auth));
    await t.pumpAndSettle();
    final viewport = t.getRect(find.byType(ListView));
    final action = t.getRect(find.byKey(const ValueKey('login-submit')));
    expect(action.bottom, lessThanOrEqualTo(viewport.bottom));
    expect(find.text('مصر (\u2066+20\u2069)'), findsOneWidget);
    await t.pumpWidget(const SizedBox());
    auth.dispose();
  });

  testWidgets('Verification keeps one named editable field', (t) async {
    final semantics = t.ensureSemantics();
    final auth = AuthController(
      FakeApi((_, _, _, _) async => {'challengeId': 'synthetic-challenge'}),
      MemoryStore(),
    )..restoring = false;
    try {
      await t.pumpWidget(login(auth));
      await t.enterText(find.byType(TextFormField), '01099900100');
      await submit(t);
      final input = find.byType(EditableText);
      expect(input, findsOneWidget);
      expect(t.getSemantics(input).label, 'رمز التحقق');
      await t.enterText(find.byType(TextFormField), '٢٤٦٨١٠');
      await t.pump();
      expect(t.getSemantics(input).label, 'رمز التحقق');
      expect(t.getSemantics(input).value, '246810');
    } finally {
      await t.pumpWidget(const SizedBox());
      auth.dispose();
      semantics.dispose();
    }
  });

  testWidgets(
    'Egypt login rejects a missing local zero and accepts +20 with the zero',
    (t) async {
      final sent = <String>[];
      final api = FakeApi((method, path, body, token) async {
        sent.add((body as Map)['phoneNumber'] as String);
        return {'challengeId': 'synthetic-challenge'};
      });
      await t.pumpWidget(
        login(AuthController(api, MemoryStore())..restoring = false),
      );
      await t.pumpAndSettle();
      expect(find.text('مصر (\u2066+20\u2069)'), findsOneWidget);
      for (final invalid in [
        '',
        '1099900100',
        '010999001000',
        '+9711099900100',
        '01099900abc',
      ]) {
        await t.enterText(find.byType(TextFormField), invalid);
        await submit(t);
        expect(find.text('أدخل رقم الهاتف المصري.'), findsOneWidget);
        expect(sent, isEmpty);
      }
      await t.enterText(find.byType(TextFormField), '+٢٠ (٠١٠) ٩٩٩٠-٠١٠٠');
      await submit(t);
      expect(sent, ['+20 (010) 9990-0100']);
      expect(find.text('+20 ••••• 100'), findsOneWidget);
      expect(
        find.byKey(const ValueKey('login-six-code-boxes')),
        findsOneWidget,
      );
    },
  );

  for (final width in [320.0, 375.0, 440.0]) {
    for (final dark in [false, true]) {
      for (final scale in [1.0, 2.0]) {
        for (final language in ['ar', 'en']) {
          testWidgets(
            'approved login $width dark=$dark scale=$scale $language keeps six-digit server contract',
            (t) async {
              t.view.physicalSize = Size(width, switch (width) {
                320 => 568,
                375 => 667,
                _ => 956,
              });
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
                width <= 375 ? 120 : 226,
              );
              expect(
                find.byKey(const ValueKey('login-six-code-boxes')),
                findsNothing,
              );
              expect(
                find.text(language == 'ar' ? 'متابعة' : 'Continue'),
                findsOneWidget,
              );
              expect(auth.session, isNull);
              final phoneError = find.text(
                language == 'ar'
                    ? 'أدخل رقم الهاتف المصري.'
                    : 'Enter an Egyptian phone number.',
              );
              await t.enterText(find.byType(TextFormField), '010');
              await t.pump();
              expect(phoneError, findsNothing);
              await submit(t);
              expect(phoneError, findsOneWidget);
              expect(bodies, isEmpty);
              await t.enterText(find.byType(TextFormField), '01099900100');
              await t.pumpAndSettle();
              expect(phoneError, findsNothing);
              expect(bodies, isEmpty);
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
              final otpError = find.text(
                language == 'ar'
                    ? 'أدخل رمز التحقق من ٦ أرقام.'
                    : 'Enter the six-digit verification code.',
              );
              await submit(t);
              expect(otpError, findsOneWidget);
              expect(bodies, hasLength(1));
              await t.enterText(find.byType(TextFormField), '2468');
              await t.pump();
              await submit(t);
              expect(
                bodies.length,
                1,
              ); // Prototype OTP4 must never reach verify.
              expect(otpError, findsWidgets);
              await t.enterText(find.byType(TextFormField), 'abcdef');
              await submit(t);
              expect(otpError, findsWidgets);
              expect(bodies.length, 1);
              await t.enterText(
                find.byType(TextFormField),
                language == 'ar' ? '٢٤٦٨١٠' : '246810',
              );
              await t.pump();
              expect(otpError, findsNothing);
              expect(bodies.length, 1);
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
      final numbers = <String>[];
      final api = FakeApi((method, path, body, token) async {
        numbers.add((body as Map)['phoneNumber'] as String);
        if (numbers.length == 1) return await pending.future;
        return {'challengeId': 'second-phone'};
      });
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
      await t.ensureVisible(find.text('تعديل الرقم'));
      await t.pumpAndSettle();
      await t.tap(find.text('تعديل الرقم'));
      await t.pumpAndSettle();
      expect(find.byKey(const ValueKey('login-six-code-boxes')), findsNothing);
      expect(
        t.widget<TextFormField>(find.byType(TextFormField)).controller!.text,
        '01099900100',
      );
      expect(
        t.widget<EditableText>(find.byType(EditableText)).focusNode.hasFocus,
        isTrue,
      );
      expect(auth.session, isNull);
      await t.enterText(find.byType(TextFormField), '01099900101');
      await submit(t);
      expect(numbers, ['01099900100', '01099900101']);
      expect(
        t.widget<EditableText>(find.byType(EditableText)).controller.text,
        isEmpty,
      );
      await t.pumpWidget(const SizedBox());
      auth.dispose();
    },
  );

  testWidgets('Corrected code uses the same challenge and submits once', (
    t,
  ) async {
    final verification = Completer<Object?>();
    final codes = <String>[];
    final api = FakeApi((_, path, body, _) async {
      if (path.endsWith('/request')) return {'challengeId': 'current'};
      final request = body as Map;
      expect(request['challengeId'], 'current');
      codes.add(request['code'] as String);
      if (codes.length == 1) throw const ApiFailure(401, 'رمز غير صحيح');
      return await verification.future;
    });
    final auth = AuthController(api, MemoryStore())..restoring = false;
    await t.pumpWidget(login(auth));
    final input = find.byType(TextFormField);
    final confirm = find.byKey(const ValueKey('login-submit'));
    await t.enterText(input, '01099900100');
    await submit(t);
    await t.enterText(input, '246811');
    await submit(t);
    expect(
      find.textContaining('الرمز غير صحيح أو انتهت صلاحيته'),
      findsOneWidget,
    );
    expect(auth.session, isNull);

    await t.enterText(input, '246810');
    await t.ensureVisible(confirm);
    await t.pumpAndSettle();
    expect(
      find.textContaining('الرمز غير صحيح أو انتهت صلاحيته'),
      findsNothing,
    );
    await t.tap(confirm);
    await t.pump();
    expect(t.widget<FilledButton>(confirm).onPressed, isNull);
    await t.tap(confirm);
    expect(codes, ['246811', '246810']);
    expect(auth.session, isNull);

    verification.complete(credentials());
    await t.pumpAndSettle();
    expect(auth.session?.selected?.role, 'Guardian');
    await t.pumpWidget(const SizedBox());
    auth.dispose();
  });

  testWidgets('Resend stays on verification and uses only the new challenge', (
    t,
  ) async {
    final replacement = Completer<Object?>();
    var requests = 0;
    final verified = <String>[];
    final auth = AuthController(
      FakeApi((_, path, body, _) async {
        if (path.endsWith('/request')) {
          expect((body as Map)['phoneNumber'], '01099900100');
          if (++requests == 1) return {'challengeId': 'first-challenge'};
          return await replacement.future;
        }
        verified.add((body as Map)['challengeId'] as String);
        if (verified.last == 'first-challenge') {
          throw const ApiFailure(401, 'الرمز غير صحيح.');
        }
        return credentials();
      }),
      MemoryStore(),
    )..restoring = false;
    await t.pumpWidget(login(auth));
    final input = find.byType(TextFormField);
    await t.enterText(input, '01099900100');
    await submit(t);
    await t.enterText(input, '246811');
    await submit(t);
    expect(verified, ['first-challenge']);

    final resend = find.widgetWithText(TextButton, 'طلب رمز جديد');
    await t.ensureVisible(resend);
    await t.pumpAndSettle();
    await t.tap(resend);
    await t.pump();
    final waiting = find.widgetWithText(TextButton, 'جارٍ طلب الرمز');
    expect(t.widget<TextButton>(waiting).onPressed, isNull);
    expect(
      t
          .widget<TextButton>(find.widgetWithText(TextButton, 'تعديل الرقم'))
          .onPressed,
      isNull,
    );
    expect(
      t
          .widget<FilledButton>(find.byKey(const ValueKey('login-submit')))
          .onPressed,
      isNull,
    );
    await t.tap(waiting);
    expect(requests, 2);

    replacement.complete({'challengeId': 'replacement-challenge'});
    await t.pumpAndSettle();
    expect(find.text('يمكنك إدخال الرمز الجديد.'), findsOneWidget);
    expect(find.text('تأكيد الدخول'), findsOneWidget);
    expect(
      t.widget<EditableText>(find.byType(EditableText)).controller.text,
      isEmpty,
    );
    expect(
      t.widget<EditableText>(find.byType(EditableText)).focusNode.hasFocus,
      isTrue,
    );
    await t.enterText(input, '246810');
    await submit(t);
    expect(verified, ['first-challenge', 'replacement-challenge']);
    expect(auth.session?.selected?.role, 'Guardian');
    await t.pumpWidget(const SizedBox());
    auth.dispose();
  });

  for (final status in [429, 503]) {
    testWidgets('Resend failure $status leaves the current entry in place', (
      t,
    ) async {
      var requests = 0;
      final message = status == 429
          ? 'محاولات كثيرة. انتظر قليلًا قبل المحاولة.'
          : 'تعذر تنفيذ الطلب الآن. حاول لاحقًا.';
      final auth = AuthController(
        FakeApi((_, path, body, _) async {
          if (++requests == 1) return {'challengeId': 'first-challenge'};
          throw ApiFailure(status, message);
        }),
        MemoryStore(),
      )..restoring = false;
      await t.pumpWidget(login(auth));
      final input = find.byType(TextFormField);
      await t.enterText(input, '01099900100');
      await submit(t);
      await t.enterText(input, '246811');
      final resend = find.widgetWithText(TextButton, 'طلب رمز جديد');
      await t.ensureVisible(resend);
      await t.pumpAndSettle();
      await t.tap(resend);
      await t.pumpAndSettle();
      expect(requests, 2);
      expect(find.text(message), findsOneWidget);
      expect(find.text('يمكنك إدخال الرمز الجديد.'), findsNothing);
      expect(t.widget<TextFormField>(input).controller?.text, '246811');
      expect(find.text('تأكيد الدخول'), findsOneWidget);
      expect(t.widget<TextButton>(resend).onPressed, isNotNull);
      expect(auth.session, isNull);
      await t.pumpWidget(const SizedBox());
      auth.dispose();
    });
  }

  testWidgets(
    'Compact verification keeps its actions reachable with a keyboard',
    (t) async {
      t.view.physicalSize = const Size(320, 568);
      t.view.devicePixelRatio = 1;
      addTearDown(t.view.reset);
      final auth = AuthController(
        FakeApi((_, _, _, _) async => {'challengeId': 'small-screen'}),
        MemoryStore(),
      )..restoring = false;
      await t.pumpWidget(login(auth, dark: true, scale: 2));
      await t.enterText(find.byType(TextFormField), '01099900100');
      await submit(t);
      t.view.viewInsets = const FakeViewPadding(bottom: 270);
      await t.pumpAndSettle();
      final boxes = find.byKey(const ValueKey('login-six-code-boxes'));
      expect(t.getRect(boxes).left, greaterThanOrEqualTo(0));
      expect(t.getRect(boxes).right, lessThanOrEqualTo(320));
      for (final label in ['تأكيد الدخول', 'طلب رمز جديد', 'تعديل الرقم']) {
        final action = find.text(label);
        await t.ensureVisible(action);
        await t.pumpAndSettle();
        expect(action.hitTestable(), findsOneWidget);
      }
      expect(t.takeException(), isNull);
      await t.pumpWidget(const SizedBox());
      auth.dispose();
    },
  );

  testWidgets(
    'unknown account keeps the phone field available for correction',
    (t) async {
      final auth = AuthController(
        FakeApi(
          (_, _, _, _) async => throw const ApiFailure(401, 'رفض الطلب.'),
        ),
        MemoryStore(),
      )..restoring = false;
      await t.pumpWidget(login(auth));
      final field = find.byType(TextFormField);
      await t.enterText(field, '01099900100');
      await submit(t);
      expect(
        find.text('تعذر إرسال الرمز. راجع رقم الهاتف المسجل لدى الأكاديمية.'),
        findsOneWidget,
      );
      expect(t.widget<TextFormField>(field).controller?.text, '01099900100');
      expect(find.byKey(const ValueKey('login-six-code-boxes')), findsNothing);
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
                : 'الرمز غير صحيح أو انتهت صلاحيته. يمكنك طلب رمز جديد.',
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
