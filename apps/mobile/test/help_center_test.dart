import 'dart:async';

import 'package:academy_mobile/features/shared/help_center.dart';
import 'package:academy_mobile/core/api/api_client.dart';
import 'package:academy_mobile/core/theme/app_theme.dart';
import 'package:flutter/material.dart';
import 'package:flutter_localizations/flutter_localizations.dart';
import 'package:flutter_test/flutter_test.dart';

import 'owner_test.dart' as fixture;

Map<String, dynamic> guide() => {
  'id': 'family-renewal',
  'section': 'subscriptions',
  'title': {'ar': 'تجديد اشتراك أبنائك', 'en': 'Renew your children'},
  'summary': {
    'ar': 'راجع الباقة والإجمالي.',
    'en': 'Review package and total.',
  },
  'version': 1,
  'contentKind': 'WrittenGuide',
  'videoStatus': 'AwaitingUpload',
  'videoUrl': null,
  'steps': [
    {
      'ar': 'ارفع إثبات الدفع وانتظر المراجعة.',
      'en': 'Upload proof and await review.',
    },
  ],
};
void main() {
  testWidgets(
    'context help rechecks the exact lesson and opens its real reader',
    (t) async {
      final calls = <String>[];
      final auth = await fixture.owner((m, p, b, k) async {
        calls.add(p);
        return guide();
      });
      await t.pumpWidget(
        fixture.shell(HelpEntry(auth: auth, lesson: 'family-renewal')),
      );
      await t.pumpAndSettle();
      expect(calls, ['/api/v1/help/lessons/family-renewal']);
      await t.tap(find.text('شرح هذه الخطوة'));
      await t.pumpAndSettle();
      expect(find.text('تجديد اشتراك أبنائك'), findsOneWidget);
      expect(calls.length, 2);
    },
  );
  testWidgets(
    'withdrawn audience hides contextual help without opening a replacement guide',
    (t) async {
      final auth = await fixture.owner(
        (m, p, b, k) async => throw const ApiFailure(404, 'hidden'),
      );
      await t.pumpWidget(
        fixture.shell(HelpEntry(auth: auth, lesson: 'family-renewal')),
      );
      await t.pumpAndSettle();
      expect(find.text('شرح هذه الخطوة'), findsNothing);
    },
  );
  for (final width in [360.0, 390.0, 430.0]) {
    testWidgets('Help guide fits Arabic RTL at $width with 200% text', (
      t,
    ) async {
      t.view.physicalSize = Size(width, 1000);
      t.view.devicePixelRatio = 1;
      addTearDown(() => t.view.reset());
      final auth = await fixture.owner((m, p, b, k) async => guide());
      await t.pumpWidget(
        fixture.shell(
          MediaQuery(
            data: MediaQueryData(
              size: Size(width, 1000),
              textScaler: const TextScaler.linear(2),
            ),
            child: HelpPage(auth: auth, id: 'family-renewal'),
          ),
        ),
      );
      await t.pumpAndSettle();
      expect(find.text('تجديد اشتراك أبنائك'), findsOneWidget);
      expect(find.text('ارفع إثبات الدفع وانتظر المراجعة.'), findsOneWidget);
      expect(t.takeException(), isNull);
    });
  }
  testWidgets(
    'English uses the same authorized content and honest no-video status',
    (t) async {
      final auth = await fixture.owner((m, p, b, k) async => guide());
      await t.pumpWidget(
        MaterialApp(
          locale: const Locale('en'),
          supportedLocales: const [Locale('ar'), Locale('en')],
          localizationsDelegates: GlobalMaterialLocalizations.delegates,
          theme: AcademyTheme.light(),
          home: HelpPage(auth: auth, id: 'family-renewal'),
        ),
      );
      await t.pumpAndSettle();
      expect(find.text('Renew your children'), findsOneWidget);
      expect(find.textContaining('No video published yet'), findsOneWidget);
      expect(t.takeException(), isNull);
    },
  );
  testWidgets(
    'search clear resets fields and late response cannot replace latest catalog',
    (t) async {
      final first = Completer<Object?>();
      var count = 0;
      String path = '';
      final auth = await fixture.owner((m, p, b, k) async {
        path = p;
        count++;
        if (count == 1) return first.future;
        return {
          'items': [guide()],
        };
      });
      await t.pumpWidget(fixture.shell(HelpPage(auth: auth)));
      await t.pump();
      await t.enterText(find.byType(TextField), 'تجديد');
      await t.tap(find.text('بحث'));
      await t.pumpAndSettle();
      expect(Uri.parse(path).queryParameters['search'], 'تجديد');
      expect(find.text('تجديد اشتراك أبنائك'), findsOneWidget);
      first.complete({'items': []});
      await t.pumpAndSettle();
      expect(find.text('تجديد اشتراك أبنائك'), findsOneWidget);
      await t.tap(find.text('مسح'));
      await t.pumpAndSettle();
      expect(Uri.parse(path).queryParameters['search'], '');
      expect(find.textContaining('لا توجد أدلة'), findsNothing);
    },
  );
  testWidgets(
    'forbidden detail is not success and revoked content clears on refresh',
    (t) async {
      var allowed = true;
      final auth = await fixture.owner((m, p, b, k) async {
        if (allowed) return guide();
        throw const ApiFailure(404, 'not accessible');
      });
      await t.pumpWidget(
        fixture.shell(HelpPage(auth: auth, id: 'family-renewal')),
      );
      await t.pumpAndSettle();
      allowed = false;
      await t.scrollUntilVisible(
        find.byTooltip('تحديث الأدلة'),
        300,
        scrollable: find.byType(Scrollable).first,
      );
      await t.tap(find.byTooltip('تحديث الأدلة'));
      await t.pumpAndSettle();
      expect(find.text('تجديد اشتراك أبنائك'), findsNothing);
      expect(find.textContaining('تعذر عرض الدليل'), findsOneWidget);
      expect(find.text('not accessible'), findsNothing);
    },
  );
  testWidgets(
    'disabled license hides entry without granting or provisioning anything',
    (t) async {
      final calls = <String>[];
      final auth = await fixture.owner((m, p, b, k) async {
        calls.add('$m $p');
        return {'available': false};
      });
      await t.pumpWidget(fixture.shell(HelpEntry(auth: auth)));
      await t.pumpAndSettle();
      expect(find.text('دليل الاستخدام'), findsNothing);
      expect(calls, ['GET /api/v1/help/availability']);
    },
  );
  testWidgets(
    'logout while loading cannot reveal a guide after the session is gone',
    (t) async {
      final pending = Completer<Object?>();
      final auth = await fixture.owner((m, p, b, k) => pending.future);
      await t.pumpWidget(
        fixture.shell(HelpPage(auth: auth, id: 'family-renewal')),
      );
      await t.pump();
      auth.session = null;
      auth.notifyListeners();
      await t.pump();
      pending.complete(guide());
      await t.pumpAndSettle();
      expect(find.text('تجديد اشتراك أبنائك'), findsNothing);
      expect(find.text('سجل الدخول لعرض الدليل.'), findsOneWidget);
    },
  );
}
