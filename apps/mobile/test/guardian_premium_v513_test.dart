import 'dart:async';

import 'package:academy_mobile/app/app.dart';
import 'package:academy_mobile/core/auth/auth_controller.dart';
import 'package:academy_mobile/core/widgets/platform_splash.dart';
import 'package:academy_mobile/core/widgets/gentle_carousel.dart';
import 'package:academy_mobile/features/guardian/guardian_home.dart';
import 'package:academy_mobile/features/guardian/reward_banner.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

import 'auth_controller_test.dart' show FakeApi, MemoryStore;
import 'v2_role_workspaces_test.dart' as f;

void main() {
  testWidgets(
    'startup completes the identity motion without accepting an unauthenticated session',
    (t) async {
      final auth = AuthController(
        FakeApi((m, p, b, k) async => null),
        MemoryStore(),
      );
      final frameReady = Completer<void>();
      await t.pumpWidget(
        AcademyApp(
          auth: auth,
          environment: 'Demo',
          entryFrameReady: frameReady.future,
        ),
      );
      expect(find.byType(PlatformSplash), findsOneWidget);
      await auth.restore();
      await t.pump(const Duration(seconds: 2));
      expect(find.byType(PlatformSplash), findsOneWidget);
      frameReady.complete();
      await t.pump();
      await t.pump(const Duration(milliseconds: 800));
      expect(find.byType(PlatformSplash), findsOneWidget);
      await t.pump(const Duration(milliseconds: 800));
      expect(find.byType(PlatformSplash), findsNothing);
      expect(find.text('رقم الهاتف'), findsOneWidget);
      expect(auth.session, isNull);
      await t.pumpWidget(const SizedBox());
      auth.dispose();
    },
  );
  testWidgets('unequal child data keeps profile buttons on the same baseline', (
    t,
  ) async {
    final auth = await f.authenticate('Guardian', (m, p, b, k) async => []);
    final kids = [
      {
        'id': 'a',
        'arabicName': 'يوسف خالد',
        'dateOfBirth': '2013-03-12',
        'sports': [
          {
            'id': 'e1',
            'sport': 'كرة القدم',
            'group': 'منتخب 2013',
            'period': {
              'plan': 'باقة 12 حصة',
              'status': 'Active',
              'remainingSessions': 8,
            },
          },
        ],
      },
      {
        'id': 'b',
        'arabicName': 'ليلى خالد',
        'dateOfBirth': '2017-03-12',
        'sports': [
          {
            'id': 'e2',
            'sport': 'السباحة',
            'group': 'مستوى 2',
            'period': {
              'plan': 'باقة 8 حصص',
              'status': 'Active',
              'remainingSessions': 2,
            },
          },
          {'id': 'e3', 'sport': 'الجمباز'},
        ],
      },
    ];
    await t.pumpWidget(
      f.app(
        Scaffold(
          body: SingleChildScrollView(
            child: GuardianChildCarousel(
              auth: auth,
              children: kids,
              today: '2026-10-28',
              onSelected: (_) {},
              onOpen: (_) {},
            ),
          ),
        ),
      ),
    );
    await t.pumpAndSettle();
    Finder footer(String id) => find.descendant(
      of: find.byKey(ValueKey('large-child-$id')),
      matching: find.byType(FilledButton),
    );
    expect(
      t.getRect(footer('a')).bottom,
      closeTo(t.getRect(footer('b')).bottom, .5),
    );
    expect(t.takeException(), isNull);
    await t.pumpWidget(const SizedBox());
    auth.dispose();
  });
  for (final state in ['Available', 'Used', 'Expired']) {
    testWidgets(
      'reward banner only occupies space for an available reward: $state',
      (t) async {
        final auth = await f.authenticate(
          'Guardian',
          (m, p, b, k) async => {
            'items': [
              {
                'id': 'reward',
                'state': state,
                'percentage': 15,
                'expiresOn': '2026-11-30',
              },
            ],
          },
        );
        await t.pumpWidget(
          f.app(Scaffold(body: GuardianRewardBanner(auth: auth))),
        );
        await t.pumpAndSettle();
        expect(
          find.text('15%'),
          state == 'Available' ? findsOneWidget : findsNothing,
        );
        expect(t.takeException(), isNull);
        await t.pumpWidget(const SizedBox());
        auth.dispose();
      },
    );
  }
  for (final reduced in [false, true]) {
    testWidgets(
      'product carousel does not duplicate cards and respects reduced motion: $reduced',
      (t) async {
        await t.pumpWidget(
          MaterialApp(
            home: MediaQuery(
              data: MediaQueryData(disableAnimations: reduced),
              child: Scaffold(
                body: GentleCarousel(
                  count: 4,
                  width: 240,
                  height: 100,
                  itemBuilder: (c, i) => Text('منتج $i'),
                ),
              ),
            ),
          ),
        );
        await t.pump();
        final state = t.state<ScrollableState>(find.byType(Scrollable));
        await t.pump(const Duration(seconds: 4));
        await t.pump(const Duration(seconds: 1));
        expect(state.position.pixels, reduced ? 0 : greaterThan(0));
        expect(find.text('منتج 0'), findsOneWidget);
        await t.pumpWidget(const SizedBox());
      },
    );
  }
}
