import 'dart:async';

import 'package:academy_mobile/core/api/api_client.dart';
import 'package:academy_mobile/core/auth/session.dart';
import 'package:academy_mobile/app/app.dart';
import 'package:academy_mobile/core/auth/auth_controller.dart';
import 'package:flutter/material.dart';
import 'package:flutter_localizations/flutter_localizations.dart';
import 'package:flutter_test/flutter_test.dart';

import 'auth_controller_test.dart';

void main() {
  test('Two admin contexts retain independent names, branches and sports', () {
    final strong = Membership.fromJson({
      'id': 'strong',
      'academyId': 'same',
      'academyName': 'أكاديمية العرض',
      'role': 'AcademyAdmin',
      'contextName': 'تشغيل كرة القدم',
      'allBranches': false,
      'allSports': false,
      'branchNames': ['مدينة نصر'],
      'sportNames': ['كرة القدم'],
    });
    final weak = Membership.fromJson({
      'id': 'weak',
      'academyId': 'same',
      'academyName': 'أكاديمية العرض',
      'role': 'AcademyAdmin',
      'contextName': 'متابعة السباحة',
      'contextRevision': 7,
      'allBranches': false,
      'allSports': false,
      'branchNames': ['التجمع'],
      'sportNames': ['السباحة'],
    });
    expect(strong.label, 'تشغيل كرة القدم');
    expect(weak.label, 'متابعة السباحة');
    expect(strong.scopeLabel, 'مدينة نصر · كرة القدم');
    expect(weak.scopeLabel, 'التجمع · السباحة');
    expect(weak.role, 'AcademyAdmin');
    expect(weak.contextRevision, 7);
    expect(weak.id, isNot(strong.id));
  });
  testWidgets(
    'Same-role selector shows names and paired scopes, not two indistinguishable admin roles',
    (tester) async {
      final json = credentials(multi: true);
      json['memberships'] = [
        {
          'id': 'strong',
          'academyId': 'same',
          'academyName': 'أكاديمية العرض',
          'role': 'AcademyAdmin',
          'contextName': 'تشغيل كرة القدم',
          'allBranches': false,
          'allSports': false,
          'branchNames': ['مدينة نصر'],
          'sportNames': ['كرة القدم'],
          'contextRevision': 2,
        },
        {
          'id': 'weak',
          'academyId': 'same',
          'academyName': 'أكاديمية العرض',
          'role': 'AcademyAdmin',
          'contextName': 'متابعة السباحة',
          'allBranches': false,
          'allSports': false,
          'branchNames': ['التجمع'],
          'sportNames': ['السباحة'],
          'contextRevision': 3,
        },
      ];
      final auth = AuthController(
        FakeApi((a, b, c, d) async => json),
        MemoryStore(),
      )..restoring = false;
      await auth.verify('phone', 'challenge', 'otp');
      await tester.pumpWidget(AcademyApp(auth: auth, environment: 'Demo'));
      expect(find.text('اختيار الدور'), findsOneWidget);
      expect(find.text('تشغيل كرة القدم'), findsOneWidget);
      expect(find.text('متابعة السباحة'), findsOneWidget);
      expect(find.text('الفروع: مدينة نصر'), findsOneWidget);
      expect(find.text('الرياضات: كرة القدم'), findsOneWidget);
      expect(find.text('الفروع: التجمع'), findsOneWidget);
      expect(find.text('الرياضات: السباحة'), findsOneWidget);
      expect(auth.session!.membershipId, isNull);
    },
  );
  test('Legacy membership without context metadata keeps its role label', () {
    final membership = Membership.fromJson({
      'id': 'coach',
      'academyId': 'same',
      'academyName': 'أكاديمية العرض',
      'role': 'Coach',
    });
    expect(membership.label, 'مدرب');
    expect(membership.contextName, isNull);
  });

  testWidgets('The current role is identified without another switch action', (
    tester,
  ) async {
    final source = {...credentials(multi: true), 'membershipId': 'guardian'};
    final api = FakeApi((a, b, c, d) async => source);
    final auth = AuthController(api, MemoryStore());
    addTearDown(auth.dispose);
    await auth.verify('phone', 'challenge', 'code');
    await tester.pumpWidget(
      MaterialApp(
        locale: const Locale('ar'),
        supportedLocales: const [Locale('ar')],
        localizationsDelegates: GlobalMaterialLocalizations.delegates,
        home: RoleFoundation(auth: auth),
      ),
    );
    final badge = find.text('الدور الحالي');
    await tester.ensureVisible(badge);
    final card = find.ancestor(of: badge, matching: find.byType(ListTile));
    expect(tester.widget<ListTile>(card).onTap, isNull);
    await tester.tap(badge);
    await tester.pumpAndSettle();
    expect(api.paths.where((path) => path.endsWith('/select-role')), isEmpty);
    expect(auth.session!.membershipId, 'guardian');
  });

  test(
    'A mismatched switch response cannot publish or persist the wrong role',
    () async {
      final store = MemoryStore();
      final auth = AuthController(
        FakeApi((method, path, body, token) async {
          if (path.endsWith('/select-role')) {
            return {
              ...credentials(multi: true, suffix: 'wrong'),
              'membershipId': 'guardian',
            };
          }
          return credentials(multi: true);
        }),
        store,
      );
      addTearDown(auth.dispose);
      await auth.verify('phone', 'challenge', 'code');
      await expectLater(auth.selectRole('owner'), throwsFormatException);
      expect(auth.roleSessionUnconfirmed, isTrue);
      expect(auth.session!.selected, isNull);
      expect(store.token, 'refresh-one');
    },
  );

  testWidgets(
    'Role choice is explicit and a rejected role is refreshed without replay',
    (tester) async {
      final reply = Completer<Object?>();
      var refreshes = 0;
      var writes = 0;
      final source = credentials(multi: true);
      final auth = AuthController(
        FakeApi((method, path, body, token) async {
          if (path.endsWith('/select-role')) {
            writes++;
            expect(body, {'membershipId': 'owner'});
            return reply.future;
          }
          if (path.endsWith('/refresh') && ++refreshes == 2) {
            return {
              ...source,
              'memberships': [(source['memberships'] as List).first],
            };
          }
          return source;
        }),
        MemoryStore(),
      )..restoring = false;
      addTearDown(auth.dispose);
      await auth.verify('phone', 'challenge', 'code');
      await tester.pumpWidget(AcademyApp(auth: auth, environment: 'Demo'));
      final open = find.widgetWithText(FilledButton, 'متابعة بالدور المختار');
      expect(tester.widget<FilledButton>(open).onPressed, isNull);
      await tester.tap(find.text('مالك'));
      await tester.pumpAndSettle();
      expect(writes, 0);
      await tester.tap(open);
      await tester.pumpAndSettle();
      expect(writes, 1);
      expect(
        tester
            .widgetList<RadioListTile<String>>(
              find.byType(RadioListTile<String>),
            )
            .every((tile) => tile.enabled == false),
        isTrue,
      );
      reply.completeError(const ApiFailure(403, 'رفض'));
      await tester.pumpAndSettle();
      expect(
        find.text('هذا الدور غير متاح حاليًا. حدّث الأدوار المتاحة.'),
        findsOneWidget,
      );
      await tester.tap(find.text('تحديث الأدوار المتاحة'));
      await tester.pumpAndSettle();
      expect(find.text('مالك'), findsNothing);
      expect(tester.widget<FilledButton>(open).onPressed, isNull);
      expect(writes, 1);
    },
  );

  for (final tokensRotated in [false, true]) {
    testWidgets(
      'Lost switch reply gates private routes (rotated: $tokensRotated)',
      (tester) async {
        final source = credentials(multi: true);
        var writes = 0;
        var refreshes = 0;
        final store = MemoryStore();
        final api = FakeApi((method, path, body, token) async {
          if (path.endsWith('/select-role')) {
            writes++;
            throw const ApiFailure(0, 'Connection interrupted');
          }
          if (path.endsWith('/refresh') && ++refreshes == 2) {
            if (tokensRotated) {
              throw const ApiFailure(401, 'Invalid refresh token');
            }
            throw const ApiFailure(0, 'Connection interrupted');
          }
          return source;
        });
        final auth = AuthController(api, store)..restoring = false;
        addTearDown(auth.dispose);
        await auth.verify('phone', 'challenge', 'code');
        await tester.pumpWidget(AcademyApp(auth: auth, environment: 'Demo'));
        await tester.tap(find.text('مالك'));
        await tester.pumpAndSettle();
        await tester.tap(find.text('متابعة بالدور المختار'));
        await tester.pumpAndSettle();
        expect(find.text('تأكيد الجلسة'), findsOneWidget);
        expect(find.byType(RoleFoundation), findsNothing);
        expect(find.textContaining('Connection interrupted'), findsNothing);
        await expectLater(
          auth.request('GET', '/api/v1/people/players'),
          throwsA(isA<ApiFailure>()),
        );
        expect(api.paths, isNot(contains('/api/v1/people/players')));
        await tester.tap(find.text('تحقّق من الجلسة'));
        await tester.pumpAndSettle();
        if (!tokensRotated) {
          expect(find.text('تأكيد الجلسة'), findsOneWidget);
          expect(
            find.text('تعذر التحقق من الجلسة. تحقّق من الاتصال وأعد المحاولة.'),
            findsOneWidget,
          );
          await tester.tap(find.text('تحقّق من الجلسة'));
          await tester.pumpAndSettle();
        }
        expect(writes, 1);
        expect(refreshes, tokensRotated ? 2 : 3);
        expect(auth.roleSessionUnconfirmed, isFalse);
        if (tokensRotated) {
          expect(find.text('رقم الهاتف'), findsOneWidget);
          expect(
            find.text('انتهت الجلسة. سجّل الدخول لاختيار الدور.'),
            findsOneWidget,
          );
          expect(store.token, isNull);
        } else {
          expect(find.text('اختيار الدور'), findsOneWidget);
          expect(find.text('متابعة بالدور المختار'), findsOneWidget);
          expect(auth.session!.selected, isNull);
        }
      },
    );
  }
}
