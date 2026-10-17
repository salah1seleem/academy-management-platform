import 'dart:async';

import 'package:academy_mobile/core/api/api_client.dart';
import 'package:academy_mobile/features/shared/community_ads.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

import 'guardian_test.dart' as fixture;

Map<String, dynamic> ad() => {
  'id': 'ad',
  'title': 'ورشة برمجة اصطناعية',
  'body': 'تفاصيل مناسبة للأسرة بلا شراء أو بيانات أطفال.',
  'advertiser': 'مركز افتراضي',
  'buttonText': 'التفاصيل',
  'imageUrl': null,
  'endsAtUtc': '2100-01-01T00:00:00Z',
};
void main() {
  testWidgets(
    'Detailed creative reauthorizes its lease and cannot survive logout',
    (t) async {
      var revoked = false;
      final auth = await fixture.logged((m, p, b, token) async {
        if (revoked) throw const ApiFailure(404, 'غير متاح');
        return ad();
      });
      await t.pumpWidget(
        fixture.shell(GuardianSponsorDetail(auth: auth, id: 'ad')),
      );
      await t.pumpAndSettle();
      expect(find.text('ورشة برمجة اصطناعية'), findsOneWidget);
      revoked = true;
      await t.pump(const Duration(seconds: 31));
      await t.pumpAndSettle();
      expect(find.text('ورشة برمجة اصطناعية'), findsNothing);
      expect(
        find.text('المحتوى غير متاح أو سُحبت الموافقة عليه.'),
        findsOneWidget,
      );
      await auth.logout();
      await t.pumpAndSettle();
      expect(
        find.text('المحتوى غير متاح أو سُحبت الموافقة عليه.'),
        findsNothing,
      );
      await t.pumpWidget(const SizedBox());
    },
  );
  testWidgets(
    'Off/withdrawn/unavailable sponsor feed is silent and never records a view',
    (t) async {
      final writes = <Object?>[];
      final auth = await fixture.logged((m, p, b, token) async {
        if (m == 'POST') {
          writes.add(b);
        }
        return {'item': null};
      });
      await t.pumpWidget(fixture.shell(GuardianSponsoredCard(auth: auth)));
      await t.pumpAndSettle();
      await t.pump(const Duration(seconds: 2));
      expect(find.text('محتوى برعاية'), findsNothing);
      expect(writes, isEmpty);
      await t.pumpWidget(const SizedBox());
    },
  );
  testWidgets(
    'A below-viewport GET is not a view; half-visible for one second records once',
    (t) async {
      t.binding.handleAppLifecycleStateChanged(AppLifecycleState.resumed);
      final events = <Object?>[];
      var reads = 0;
      final controller = ScrollController();
      addTearDown(controller.dispose);
      final auth = await fixture.logged((m, p, b, token) async {
        if (p.endsWith('/feed')) {
          reads++;
          return {'item': ad()};
        }
        events.add(b);
        return null;
      });
      await t.pumpWidget(
        fixture.shell(
          Scaffold(
            body: SingleChildScrollView(
              controller: controller,
              child: Column(
                children: [
                  const SizedBox(height: 1200),
                  GuardianSponsoredCard(auth: auth),
                  const SizedBox(height: 400),
                ],
              ),
            ),
          ),
        ),
      );
      await t.pumpAndSettle();
      await t.pump(const Duration(seconds: 2));
      expect(reads, 1);
      expect(events, isEmpty);
      controller.jumpTo(1100);
      await t.pump();
      await t.pump(const Duration(milliseconds: 999));
      expect(events, isEmpty);
      await t.pump(const Duration(milliseconds: 10));
      await t.pump();
      expect(events, [
        {'kind': 'Impression'},
      ]);
      await t.pump(const Duration(seconds: 2));
      expect(events.length, 1);
      await t.pumpWidget(const SizedBox());
    },
  );
  testWidgets(
    'Leaving visibility or backgrounding cancels the pending exposure',
    (t) async {
      t.binding.handleAppLifecycleStateChanged(AppLifecycleState.resumed);
      final events = <Object?>[];
      final auth = await fixture.logged((m, p, b, token) async {
        if (p.endsWith('/feed')) {
          return {'item': ad()};
        }
        events.add(b);
        return null;
      });
      await t.pumpWidget(fixture.shell(GuardianSponsoredCard(auth: auth)));
      await t.pumpAndSettle();
      await t.pump(const Duration(milliseconds: 300));
      t.binding.handleAppLifecycleStateChanged(AppLifecycleState.paused);
      await t.pump(const Duration(seconds: 2));
      expect(events, isEmpty);
      await t.pumpWidget(const SizedBox());
      t.binding.handleAppLifecycleStateChanged(AppLifecycleState.resumed);
    },
  );
  testWidgets('Hide is immediate and cannot be mistaken for an impression', (
    t,
  ) async {
    final events = <Object?>[];
    final auth = await fixture.logged((m, p, b, token) async {
      if (p.endsWith('/feed')) {
        return {'item': ad()};
      }
      events.add(b);
      return null;
    });
    await t.pumpWidget(fixture.shell(GuardianSponsoredCard(auth: auth)));
    await t.pumpAndSettle();
    await t.tap(find.byTooltip('إخفاء الإعلان اليوم'));
    await t.pumpAndSettle();
    expect(find.text('ورشة برمجة اصطناعية'), findsNothing);
    expect(events, [
      {'kind': 'Hide'},
    ]);
    await t.pumpWidget(const SizedBox());
  });
  testWidgets('Late sponsor response after logout stays hidden', (t) async {
    final pending = Completer<Object?>();
    final auth = await fixture.logged((m, p, b, token) async => pending.future);
    await t.pumpWidget(fixture.shell(GuardianSponsoredCard(auth: auth)));
    await t.pump();
    await auth.logout();
    pending.complete({'item': ad()});
    await t.pumpAndSettle();
    expect(find.text('ورشة برمجة اصطناعية'), findsNothing);
    expect(t.takeException(), isNull);
    await t.pumpWidget(const SizedBox());
  });
  for (final width in [360.0, 390.0, 430.0]) {
    testWidgets(
      'Urgent notice precedes a non-blocking sponsored card at $width and 200%',
      (t) async {
        t.view.physicalSize = Size(width, 1200);
        t.view.devicePixelRatio = 1;
        addTearDown(t.view.resetPhysicalSize);
        addTearDown(t.view.resetDevicePixelRatio);
        final auth = await fixture.logged(
          (m, p, b, token) async => {'item': ad()},
        );
        await t.pumpWidget(
          fixture.shell(
            MediaQuery(
              data: MediaQueryData(
                size: Size(width, 1200),
                textScaler: const TextScaler.linear(2),
              ),
              child: Scaffold(
                body: SingleChildScrollView(
                  child: Column(
                    children: [
                      GuardianUrgentMatchAlerts(
                        auth: auth,
                        alerts: [
                          {
                            'id': 'notice',
                            'message': 'تم تغيير موعد مباراة ابنك',
                          },
                        ],
                      ),
                      GuardianSponsoredCard(auth: auth),
                    ],
                  ),
                ),
              ),
            ),
          ),
        );
        await t.pumpAndSettle();
        expect(
          t.getTopLeft(find.text('تم تغيير موعد مباراة ابنك')).dy,
          lessThan(t.getTopLeft(find.text('محتوى برعاية')).dy),
        );
        expect(t.takeException(), isNull);
        await t.pumpWidget(const SizedBox());
      },
    );
  }
  testWidgets(
    'Consent can be withdrawn after the academy disables the feature',
    (t) async {
      Object? body;
      final auth = await fixture.logged((m, p, b, token) async {
        if (m == 'PUT') {
          body = b;
          return null;
        }
        return {
          'available': false,
          'inAppOptIn': true,
          'adultDeclared': true,
          'notice': 'موافقة داخل التطبيق فقط',
        };
      });
      await t.pumpWidget(fixture.shell(GuardianSponsorPreferences(auth: auth)));
      await t.pumpAndSettle();
      await t.tap(find.byType(Switch));
      await t.pump();
      await t.tap(find.text('حفظ الاختيار'));
      await t.pumpAndSettle();
      expect(body, {'inAppOptIn': false, 'adultDeclared': true});
      await t.pumpWidget(const SizedBox());
    },
  );
  testWidgets('A revoked creative cannot open a cached details page', (
    t,
  ) async {
    final auth = await fixture.logged((m, p, b, token) async {
      if (p.endsWith('/feed')) {
        return {'item': ad()};
      }
      throw const ApiFailure(404, 'المحتوى لم يعد متاحًا');
    });
    await t.pumpWidget(fixture.shell(GuardianSponsoredCard(auth: auth)));
    await t.pumpAndSettle();
    await t.tap(find.text('التفاصيل'));
    await t.pumpAndSettle();
    expect(find.text('ورشة برمجة اصطناعية'), findsNothing);
    await t.pumpWidget(const SizedBox());
  });
}
