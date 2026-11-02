import 'dart:async';

import 'package:academy_mobile/core/auth/auth_controller.dart';
import 'package:academy_mobile/core/auth/session.dart';
import 'package:academy_mobile/features/shared/guardian_contact.dart';
import 'package:flutter/material.dart';
import 'package:flutter_localizations/flutter_localizations.dart';
import 'package:flutter_test/flutter_test.dart';

import 'auth_controller_test.dart' show FakeApi, MemoryStore, credentials;

const label = 'التواصل مع ولي الأمر عبر واتساب';
Map<String, dynamic> contact(String name, String phone) => {
  'name': name,
  'phone': '+$phone',
  'relationship': 'ولي أمر',
  'url':
      'https://wa.me/$phone?text=${Uri.encodeComponent('مرحبًا من أكاديمية خيالية.')}',
};
AuthController auth(
  Future<Object?> Function(String) read, {
  String role = 'AcademyOwner',
}) {
  final a = AuthController(FakeApi((m, p, b, t) => read(p)), MemoryStore());
  final c = credentials();
  (c['memberships'] as List).first['role'] = role;
  a.session = Session.fromJson(c);
  return a;
}

Widget frame(
  AuthController a,
  Future<bool> Function(Uri) open, {
  bool dark = false,
}) => MaterialApp(
  locale: const Locale('ar'),
  supportedLocales: const [Locale('ar')],
  localizationsDelegates: GlobalMaterialLocalizations.delegates,
  theme: ThemeData(brightness: dark ? Brightness.dark : Brightness.light),
  home: Scaffold(
    body: GuardianContactAction(auth: a, playerId: 'child-a', openUrl: open),
  ),
);
void main() {
  test('only bounded official contact URLs are accepted', () {
    expect(guardianChatUri('https://wa.me/201000000001?text=hello'), isNotNull);
    for (final u in [
      'https://wa.me.evil.test/201000000001',
      'javascript:alert(1)',
      'https://evil@wa.me/201000000001',
      'https://wa.me:4443/201000000001',
      'https://wa.me/01000000001',
    ]) {
      expect(guardianChatUri(u), isNull);
    }
  });
  testWidgets(
    'one linked guardian opens exactly the server recipient without sending',
    (t) async {
      final calls = <Uri>[];
      String? read;
      final a = auth((p) async {
        read = p;
        return {
          'contacts': [contact('ولي خيالي', '201000000001')],
        };
      });
      await t.pumpWidget(
        frame(a, (u) async {
          calls.add(u);
          return true;
        }),
      );
      await t.tap(find.text(label));
      await t.pumpAndSettle();
      expect(read, '/api/v1/people/players/child-a/guardian-contacts');
      expect(calls.single.path, '/201000000001');
      expect(
        calls.single.queryParameters['text'],
        'مرحبًا من أكاديمية خيالية.',
      );
    },
  );
  testWidgets(
    'multiple guardians require a choice and preserve the selected Arabic name and phone',
    (t) async {
      final calls = <Uri>[];
      final a = auth(
        (p) async => {
          'contacts': [
            contact('الأم الخيالية', '201000000001'),
            contact('الأب الخيالي', '201000000002'),
          ],
        },
      );
      await t.pumpWidget(
        frame(a, (u) async {
          calls.add(u);
          return true;
        }),
      );
      await t.tap(find.text(label));
      await t.pumpAndSettle();
      expect(calls, isEmpty);
      expect(find.text('اختر ولي الأمر للتواصل'), findsOneWidget);
      await t.tap(find.text('الأب الخيالي'));
      await t.pumpAndSettle();
      expect(calls.single.path, '/201000000002');
    },
  );
  testWidgets('missing phone provides a fix and never opens a guessed number', (
    t,
  ) async {
    final calls = <Uri>[];
    final a = auth(
      (p) async => {
        'contacts': [
          {'name': 'ولي خيالي', 'phone': null, 'url': null},
        ],
      },
    );
    await t.pumpWidget(
      frame(a, (u) async {
        calls.add(u);
        return true;
      }),
    );
    await t.tap(find.text(label));
    await t.pumpAndSettle();
    expect(calls, isEmpty);
    expect(find.textContaining('راجع بيانات التواصل'), findsOneWidget);
  });
  testWidgets('unavailable external application reports Arabic retry state', (
    t,
  ) async {
    final a = auth(
      (p) async => {
        'contacts': [contact('ولي خيالي', '201000000001')],
      },
    );
    await t.pumpWidget(frame(a, (u) async => false, dark: true));
    await t.tap(find.text(label));
    await t.pumpAndSettle();
    expect(find.textContaining('تعذر فتح واتساب'), findsOneWidget);
  });
  testWidgets(
    'a late contact response after logout cannot open or show another context',
    (t) async {
      final pending = Completer<Object?>();
      final calls = <Uri>[];
      final a = auth(
        (p) =>
            p.endsWith('guardian-contacts') ? pending.future : Future.value({}),
      );
      await t.pumpWidget(
        frame(a, (u) async {
          calls.add(u);
          return true;
        }),
      );
      await t.tap(find.text(label));
      await t.pump();
      await a.logout();
      pending.complete({
        'contacts': [contact('ولي قديم', '201000000001')],
      });
      await t.pumpAndSettle();
      expect(calls, isEmpty);
      expect(find.text('ولي قديم'), findsNothing);
    },
  );
  testWidgets('guardian has no redundant self-contact action', (t) async {
    final a = auth((p) async => {}, role: 'Guardian');
    await t.pumpWidget(frame(a, (u) async => true));
    expect(find.text(label), findsNothing);
  });
  testWidgets(
    '320px RTL with 200 percent text keeps recipient selection scrollable',
    (t) async {
      t.view.physicalSize = const Size(320, 667);
      t.view.devicePixelRatio = 1;
      addTearDown(t.view.reset);
      final calls = <Uri>[];
      final a = auth(
        (p) async => {
          'contacts': [
            contact('الأم الخيالية', '201000000001'),
            contact('الأب الخيالي', '201000000002'),
          ],
        },
      );
      await t.pumpWidget(
        MaterialApp(
          locale: const Locale('ar'),
          supportedLocales: const [Locale('ar')],
          localizationsDelegates: GlobalMaterialLocalizations.delegates,
          builder: (context, child) => MediaQuery(
            data: MediaQuery.of(context)
                .copyWith(textScaler: TextScaler.linear(2)),
            child: child!,
          ),
          home: Scaffold(
            body: GuardianContactAction(
              auth: a,
              playerId: 'child-a',
              openUrl: (u) async {
                calls.add(u);
                return true;
              },
            ),
          ),
        ),
      );
      await t.tap(find.text(label));
      await t.pumpAndSettle();
      await t.scrollUntilVisible(
        find.text('الأب الخيالي'),
        150,
        scrollable: find
            .descendant(
              of: find.byType(BottomSheet),
              matching: find.byType(Scrollable),
            )
            .first,
      );
      await t.pumpAndSettle();
      await t.tap(find.text('الأب الخيالي'));
      await t.pumpAndSettle();
      expect(calls.single.path, '/201000000002');
      expect(t.takeException(), isNull);
    },
  );
}
