import 'package:academy_mobile/features/shared/technical_promotion.dart';
import 'package:academy_mobile/core/api/api_client.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:flutter_localizations/flutter_localizations.dart';

import 'owner_test.dart' as f;

Map<String, dynamic> options() => {
  'enrollmentVersion': 9,
  'effectiveDate': '2026-10-18',
  'recommendedLevel': 'مستوى تالٍ',
  'canPreview': true,
  'applied': null,
  'groups': [
    {'id': 'g', 'name': 'مجموعة متقدمة'},
  ],
};
Map<String, dynamic> preview() => {
  'fingerprint': 'fp',
  'enrollmentVersion': 9,
  'level': 'مستوى تالٍ',
  'preview': {
    'sourceGroup': 'قديمة',
    'targetGroup': 'جديدة',
    'effectiveDate': '2026-10-18',
    'conflicts': <Object>[],
    'removedSessions': <Object>[],
    'addedSessions': [
      {'id': 's'},
    ],
  },
};
void main() {
  testWidgets(
    'Lost save response retains exact retry intent and does not invent success',
    (t) async {
      final writes = <Object?>[];
      final auth = await f.owner((m, p, b, k) async {
        if (m == 'POST') {
          if (p.endsWith('/preview')) return preview();
          writes.add(b);
          throw ApiFailure(503, 'تعذر الاتصال');
        }
        return options();
      });
      await t.pumpWidget(
        f.shell(TechnicalPromotionPage(auth: auth, requestId: 'r')),
      );
      await t.pumpAndSettle();
      await t.tap(find.byType(DropdownButtonFormField<String>));
      await t.pumpAndSettle();
      await t.tap(find.text('مجموعة متقدمة').last);
      await t.pumpAndSettle();
      await t.tap(find.text('معاينة المستوى والمواعيد'));
      await t.pumpAndSettle();
      for (var i = 0; i < 2; i++) {
        await t.scrollUntilVisible(
          find.text('اعتماد المستوى ونقل المجموعة'),
          180,
          scrollable: find.byType(Scrollable).first,
        );
        await t.tap(find.text('اعتماد المستوى ونقل المجموعة'));
        await t.pumpAndSettle();
      }
      expect(writes.length, 2);
      expect(writes[0], writes[1]);
      expect(
        find.textContaining('تم اعتماد المستوى ونقل المواعيد مع حفظ التاريخ'),
        findsNothing,
      );
    },
  );
  for (final width in [360.0, 390.0, 430.0]) {
    testWidgets('English200 percent at$width preserves preview controls', (
      t,
    ) async {
      await t.binding.setSurfaceSize(Size(width, 1000));
      addTearDown(() => t.binding.setSurfaceSize(null));
      final auth = await f.owner((m, p, b, k) async => options());
      await t.pumpWidget(
        MaterialApp(
          locale: const Locale('en'),
          supportedLocales: const [Locale('ar'), Locale('en')],
          localizationsDelegates: GlobalMaterialLocalizations.delegates,
          home: MediaQuery(
            data: const MediaQueryData(textScaler: TextScaler.linear(2)),
            child: TechnicalPromotionPage(auth: auth, requestId: 'r'),
          ),
        ),
      );
      await t.pumpAndSettle();
      await t.scrollUntilVisible(find.text('Preview level and schedule'), 150);
      expect(t.takeException(), isNull);
      expect(find.text('Preview level and schedule'), findsOneWidget);
    });
  }
  testWidgets(
    'Preview is required before an atomic confirmation; then reads the saved source',
    (t) async {
      final writes = <Map<String, Object?>>[];
      bool saved = false;
      final auth = await f.owner((m, p, b, k) async {
        if (m == 'POST') {
          writes.add({'path': p, 'body': b});
          if (p.endsWith('/preview')) return preview();
          saved = true;
          return {'id': 'h'};
        }
        return {
          ...options(),
          'applied': saved ? {'toLevelName': 'مستوى تالٍ'} : null,
        };
      });
      await t.pumpWidget(
        f.shell(TechnicalPromotionPage(auth: auth, requestId: 'r')),
      );
      await t.pumpAndSettle();
      expect(find.text('اعتماد المستوى ونقل المجموعة'), findsNothing);
      await t.tap(find.byType(DropdownButtonFormField<String>));
      await t.pumpAndSettle();
      await t.tap(find.text('مجموعة متقدمة').last);
      await t.pumpAndSettle();
      await t.tap(find.text('معاينة المستوى والمواعيد'));
      await t.pumpAndSettle();
      await t.scrollUntilVisible(
        find.text('اعتماد المستوى ونقل المجموعة'),
        180,
        scrollable: find.byType(Scrollable).first,
      );
      await t.tap(find.text('اعتماد المستوى ونقل المجموعة'));
      await t.pumpAndSettle();
      expect(writes.length, 2);
      expect(
        writes[0]['path'],
        '/api/v1/evaluations/technical-reviews/r/promotion/preview',
      );
      expect(
        writes[1]['path'],
        '/api/v1/evaluations/technical-reviews/r/promotion',
      );
      expect(writes[1]['body'], containsPair('fingerprint', 'fp'));
      expect(writes[1]['body'], containsPair('enrollmentVersion', 9));
      expect(find.textContaining('تم التنفيذ إلى'), findsOneWidget);
      expect(find.text('اعتماد المستوى ونقل المجموعة'), findsNothing);
    },
  );
  testWidgets('Changed source offers no mutation; logout hides data', (
    t,
  ) async {
    final auth = await f.owner(
      (m, p, b, k) async => {...options(), 'canPreview': false},
    );
    await t.pumpWidget(
      f.shell(TechnicalPromotionPage(auth: auth, requestId: 'r')),
    );
    await t.pumpAndSettle();
    expect(find.byType(DropdownButtonFormField<String>), findsNothing);
    expect(find.text('اعتماد المستوى ونقل المجموعة'), findsNothing);
    await auth.logout();
    await t.pumpAndSettle();
    expect(find.text('مستوى تالٍ'), findsNothing);
  });
  testWidgets('Long Arabic at200 percent is usable on narrow mobile widths', (
    t,
  ) async {
    for (final width in [360.0, 390.0, 430.0]) {
      t.view.resetPhysicalSize();
      t.view.physicalSize = Size(width, 1100);
      t.view.devicePixelRatio = 1;
      final auth = await f.owner(
        (m, p, b, k) async => {
          ...options(),
          'recommendedLevel':
              'مستوى تقدم طويل لا يغير الاشتراك ولا تاريخ الطفل',
        },
      );
      await t.pumpWidget(
        f.shell(
          MediaQuery(
            data: MediaQueryData(
              size: Size(width, 1100),
              textScaler: const TextScaler.linear(2),
            ),
            child: TechnicalPromotionPage(auth: auth, requestId: 'r'),
          ),
        ),
      );
      await t.pumpAndSettle();
      expect(t.takeException(), isNull);
      t.binding.handleAppLifecycleStateChanged(AppLifecycleState.inactive);
      await t.pumpAndSettle();
      expect(find.textContaining('مستوى تقدم طويل'), findsNothing);
      t.binding.handleAppLifecycleStateChanged(AppLifecycleState.resumed);
      await t.pumpAndSettle();
      await t.pumpWidget(const SizedBox.shrink());
    }
    t.view.resetPhysicalSize();
    t.view.resetDevicePixelRatio();
  });
}
