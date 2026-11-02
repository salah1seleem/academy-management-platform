import 'package:academy_mobile/core/api/api_client.dart';
import 'package:academy_mobile/core/theme/app_theme.dart';
import 'package:academy_mobile/features/guardian/guardian_ui.dart';
import 'package:academy_mobile/features/shared/academy_news.dart';
import 'package:academy_mobile/features/shared/native_academy_content.dart';
import 'package:flutter/material.dart';
import 'package:flutter_localizations/flutter_localizations.dart';
import 'package:flutter_test/flutter_test.dart';

import 'v2_role_workspaces_test.dart' show authenticate, app;

const options = {
  'today': '2026-10-21',
  'allBranchesAllowed': true,
  'branches': [
    {'id': 'branch', 'name': 'فرع العرض'},
  ],
  'canCreate': true,
  'canEdit': true,
  'canDelete': false,
};

Future<void> tap(WidgetTester t, String title) async {
  final f = find.text(title);
  final position = t
      .state<ScrollableState>(find.byType(Scrollable).first)
      .position;
  position.jumpTo(0);
  await t.pumpAndSettle();
  await t.scrollUntilVisible(f, 160, scrollable: find.byType(Scrollable).first);
  await t.ensureVisible(f.last);
  await t.tap(f.last);
  await t.pumpAndSettle();
}

Future<void> fill(WidgetTester t) async {
  await t.enterText(
    find.widgetWithText(TextFormField, 'العنوان'),
    'خبر اصطناعي',
  );
  await t.enterText(
    find.widgetWithText(TextFormField, 'المحتوى'),
    'تفاصيل عامة بلا بيانات أطفال',
  );
  await tap(t, 'معاينة');
}

void main() {
  for (final width in [360.0, 390.0, 430.0]) {
    for (final scale in [1.0, 2.0]) {
      for (final light in [false, true]) {
        testWidgets(
          'News and long content header fit $width at $scale in ${light ? 'light' : 'dark'}',
          (t) async {
            t.view.physicalSize = Size(width, 844);
            t.view.devicePixelRatio = 1;
            addTearDown(() {
              t.view.resetPhysicalSize();
              t.view.resetDevicePixelRatio();
            });
            final auth = await authenticate(
              'Guardian',
              (m, p, b, token) async => [
                {
                  'id': 'news',
                  'title': 'فعالية الأكاديمية الرياضية لأولياء الأمور والمدربين بالفرع المختار',
                  'body': 'تفاصيل عامة',
                  'publishedAtUtc': '2026-10-21T12:00:00Z',
                },
              ],
            );
            for (final locale in ['ar', 'en']) {
              await t.pumpWidget(
                MaterialApp(
                  locale: Locale(locale),
                  supportedLocales: const [Locale('ar'), Locale('en')],
                  localizationsDelegates: GlobalMaterialLocalizations.delegates,
                  theme: light ? AcademyTheme.light() : AcademyTheme.dark(),
                  home: Builder(
                    builder: (context) => MediaQuery(
                      data: MediaQuery.of(context)
                          .copyWith(textScaler: TextScaler.linear(scale)),
                      child: GuardianPage(
                        title: 'الأخبار والفعاليات والإعلانات',
                        child: SingleChildScrollView(
                          padding: const EdgeInsets.all(18),
                          child: NewsStrip(auth: auth),
                        ),
                      ),
                    ),
                  ),
                ),
              );
              await t.pumpAndSettle();
              expect(
                find.text(locale == 'ar' ? 'من الأكاديمية' : 'Academy news'),
                findsOneWidget,
              );
              expect(find.textContaining('فعالية الأكاديمية'), findsOneWidget);
              expect(t.takeException(), isNull);
              await t.pumpWidget(const SizedBox());
            }
            auth.dispose();
          },
        );
      }
    }
  }
  testWidgets(
    'Content list comes first, uses stored scheduled state and hides delete without its grant',
    (t) async {
      final calls = <String>[];
      final auth = await authenticate('AcademyAdmin', (m, p, b, token) async {
        calls.add(p);
        if (p.endsWith('/options')) return options;
        return {
          'total': 1,
          'items': [
            {
              'id': 'one',
              'title': 'فعالية قادمة',
              'kind': 'Event',
              'state': 'Scheduled',
              'isActive': true,
              'version': 1,
              'localStartsOn': '2026-11-01',
              'localEndsOn': '2026-11-03',
              'targetBranchIds': ['branch'],
              'audienceRoles': ['Coach'],
            },
          ],
        };
      });
      await t.pumpWidget(app(NativeAcademyContent(auth: auth)));
      await t.pumpAndSettle();
      expect(find.text('فعالية قادمة'), findsOneWidget);
      expect(find.text('مجدول'), findsOneWidget);
      expect(find.text('يعرض الآن'), findsNothing);
      expect(find.text('حذف'), findsNothing);
      expect(find.textContaining('المدربون'), findsOneWidget);
      expect(find.text('فرع العرض'), findsOneWidget);
      expect(calls, contains('/api/v1/content/news/?paged=true&page=1'));
      await t.tap(find.text('الفعاليات'));
      await t.pumpAndSettle();
      expect(calls.last, contains('kind=Event'));
      expect(t.takeException(), isNull);
      await t.pumpWidget(const SizedBox());
      auth.dispose();
    },
  );
  testWidgets(
    'Publishing requires the review step and reads back the exact audience and local dates',
    (t) async {
      Map<String, dynamic>? sent;
      var reads = 0;
      final auth = await authenticate('AcademyOwner', (m, p, b, token) async {
        if (p.endsWith('/options')) return options;
        if (m == 'POST') {
          sent = Map<String, dynamic>.from(b as Map);
          return {'id': 'saved'};
        }
        reads++;
        return {
          ...sent!,
          'id': 'saved',
          'publishedAtUtc': '2026-10-21T12:00:00Z',
          'version': 1,
        };
      });
      await t.pumpWidget(app(NativeContentEditor(auth: auth)));
      await t.pumpAndSettle();
      await fill(t);
      expect(sent, isNull);
      expect(find.textContaining('الخطوة 2 من 2'), findsOneWidget);
      expect(find.textContaining('الجمهور:'), findsOneWidget);
      expect(find.text('تعديل البيانات'), findsOneWidget);
      await tap(t, 'نشر');
      expect(sent, containsPair('localStartsOn', '2026-10-21'));
      expect(sent, containsPair('localEndsOn', '2026-10-27'));
      expect(sent!['audienceRoles'], ['AcademyAdmin', 'Coach', 'Guardian']);
      expect(sent!['requestId'], isNotEmpty);
      expect(reads, 1);
      expect(t.takeException(), isNull);
      await t.pumpWidget(const SizedBox());
      auth.dispose();
    },
  );
  testWidgets(
    'Lost create response freezes reviewed details and retries one request with the same body',
    (t) async {
      final writes = <Map>[];
      final auth = await authenticate('AcademyOwner', (m, p, b, token) async {
        if (p.endsWith('/options')) return options;
        if (m == 'POST') {
          writes.add(Map.from(b as Map));
          if (writes.length == 1) {
            throw const ApiFailure(0, 'انقطع الاتصال بعد إرسال الحفظ.');
          }
          return {'id': 'one'};
        }
        return {
          ...writes.last,
          'id': 'one',
          'version': 1,
          'publishedAtUtc': '2026-10-21T12:00:00Z',
        };
      });
      await t.pumpWidget(app(NativeContentEditor(auth: auth)));
      await t.pumpAndSettle();
      await fill(t);
      await tap(t, 'نشر');
      expect(writes, hasLength(1));
      expect(find.text('تعديل البيانات'), findsNothing);
      expect(find.byType(TextFormField), findsNothing);
      await tap(t, 'التحقق من نفس الطلب');
      expect(writes, hasLength(2));
      expect(writes.last, writes.first);
      expect(t.takeException(), isNull);
      await t.pumpWidget(const SizedBox());
      auth.dispose();
    },
  );
}
