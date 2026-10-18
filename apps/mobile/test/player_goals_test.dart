import 'package:academy_mobile/core/api/api_client.dart';
import 'package:academy_mobile/features/shared/player_goals.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:flutter_localizations/flutter_localizations.dart';

import 'owner_test.dart' as fixture;

Map<String, dynamic> goal() => {
  'id': 'g',
  'sportEnrollmentId': 'e',
  'title': 'تمرير دقيق اصطناعي',
  'target': 'خطوة فنية منشورة للأسرة',
  'reviewOn': '2026-10-31',
  'state': 'Published',
  'version': 7,
  'revision': 2,
};
Map<String, dynamic> detail() => {
  'enrollment': {'id': 'e', 'player': 'لاعب اصطناعي', 'sport': 'كرة القدم'},
  'asOfDate': '2026-10-18',
  'item': goal(),
  'canEdit': true,
  'history': <Object>[],
};
void main() {
  testWidgets(
    'English goal controls preserve the original Arabic academy content in LTR',
    (t) async {
      final auth = await fixture.owner((m, p, b, k) async => detail());
      await t.pumpWidget(
        MaterialApp(
          locale: const Locale('en'),
          supportedLocales: const [Locale('ar'), Locale('en')],
          localizationsDelegates: GlobalMaterialLocalizations.delegates,
          theme: ThemeData.dark(),
          home: PlayerGoalsPage(auth: auth, id: 'g'),
        ),
      );
      await t.pumpAndSettle();
      expect(find.text('Player development goals'), findsOneWidget);
      expect(find.text('In progress · shared with family'), findsOneWidget);
      expect(find.text('تمرير دقيق اصطناعي'), findsOneWidget);
      await t.ensureVisible(find.text('Save review'));
      expect(
        Directionality.of(t.element(find.text('Save review'))),
        TextDirection.ltr,
      );
      expect(t.takeException(), isNull);
    },
  );
  testWidgets(
    'staff goal summaries without a review array keep the new and open goal actions usable',
    (t) async {
      final auth = await fixture.owner(
        (m, p, b, k) async => {
          'enrollment': {
            'id': 'e',
            'player': 'لاعب اصطناعي',
            'sport': 'كرة القدم',
          },
          'asOfDate': '2026-10-18',
          'canCreate': true,
          'items': [goal()],
        },
      );
      await t.pumpWidget(
        fixture.shell(PlayerGoalsPage(auth: auth, enrollmentId: 'e')),
      );
      await t.pumpAndSettle();
      expect(find.text('هدف جديد'), findsOneWidget);
      expect(find.text('تمرير دقيق اصطناعي'), findsOneWidget);
      expect(find.text('فتح الهدف وسجل المراجعة'), findsOneWidget);
      expect(find.textContaining('تعذر قراءة بيانات الشاشة'), findsNothing);
      expect(t.takeException(), isNull);
    },
  );
  testWidgets(
    'Published goal review writes the exact source version without changing levels or finances',
    (t) async {
      var reviewed = false;
      final auth = await fixture.owner((m, p, b, k) async {
        if (m == 'POST') {
          expect(p, '/api/v1/evaluations/development-goals/g/transition');
          expect(b, containsPair('action', 'Reviewed'));
          expect(b, containsPair('version', 7));
          expect(b, containsPair('note', 'تحسن فني اصطناعي'));
          expect((b as Map)['requestId'], matches(RegExp(r'^[a-f0-9-]{36}$')));
          reviewed = true;
          return {'id': 'g'};
        }
        return {
          ...detail(),
          'item': {...goal(), if (reviewed) 'version': 8},
        };
      });
      await t.pumpWidget(fixture.shell(PlayerGoalsPage(auth: auth, id: 'g')));
      await t.pumpAndSettle();
      expect(find.text('تمرير دقيق اصطناعي'), findsOneWidget);
      expect(find.widgetWithText(TextFormField, 'عنوان الهدف'), findsNothing);
      final field = find.widgetWithText(
        TextField,
        'ملاحظة التقدم للأسرة (اختياري)',
      );
      await t.ensureVisible(field);
      await t.enterText(field, 'تحسن فني اصطناعي');
      await t.testTextInput.receiveAction(TextInputAction.done);
      await t.pumpAndSettle();
      await t.ensureVisible(find.text('حفظ المراجعة'));
      await t.tap(find.text('حفظ المراجعة'));
      await t.pumpAndSettle();
      expect(reviewed, isTrue);
      expect(find.text('تم حفظ تحديث الهدف وسجل المراجعة.'), findsOneWidget);
      expect(t.takeException(), isNull);
    },
  );
  testWidgets(
    'private goal is discarded on background and unauthorized resume, and logout hides it',
    (t) async {
      var allowed = true;
      final auth = await fixture.owner((m, p, b, k) async {
        if (!allowed) {
          throw const ApiFailure(404, 'الهدف غير متاح.');
        }
        return detail();
      });
      await t.pumpWidget(fixture.shell(PlayerGoalsPage(auth: auth, id: 'g')));
      await t.pumpAndSettle();
      expect(find.text('تمرير دقيق اصطناعي'), findsOneWidget);
      t.binding.handleAppLifecycleStateChanged(AppLifecycleState.inactive);
      await t.pump();
      expect(find.text('تمرير دقيق اصطناعي'), findsNothing);
      allowed = false;
      t.binding.handleAppLifecycleStateChanged(AppLifecycleState.resumed);
      await t.pumpAndSettle();
      expect(find.text('تمرير دقيق اصطناعي'), findsNothing);
      expect(find.text('الهدف غير متاح.'), findsOneWidget);
      await auth.logout();
      await t.pumpAndSettle();
      expect(find.text('أهداف تطور اللاعب'), findsNothing);
      expect(t.takeException(), isNull);
    },
  );
  for (final width in [360.0, 390.0, 430.0]) {
    testWidgets(
      'Guardian reads real goal reviews without draft or staff controls at RTL $width and 200 percent text',
      (t) async {
        t.view.physicalSize = Size(width, 900);
        t.view.devicePixelRatio = 1;
        addTearDown(t.view.resetPhysicalSize);
        addTearDown(t.view.resetDevicePixelRatio);
        final auth = await fixture.owner((m, p, b, k) async {
          expect(m, 'GET');
          expect(p, '/api/v1/guardian/children/child/development-goals');
          return {
            'items': [
              {
                'goal': goal(),
                'sport': 'كرة القدم',
                'reviews': [
                  {
                    'action': 'Reviewed',
                    'note': 'مراجعة منشورة حقيقية',
                    'actorName': 'مدرب اصطناعي',
                    'createdAtUtc': '2026-10-18T06:00:00Z',
                  },
                ],
              },
            ],
          };
        });
        await t.pumpWidget(
          fixture.shell(
            MediaQuery(
              data: MediaQueryData(
                size: Size(width, 900),
                textScaler: const TextScaler.linear(2),
              ),
              child: PlayerGoalsPage(auth: auth, playerId: 'child'),
            ),
          ),
        );
        await t.pumpAndSettle();
        expect(find.text('تمرير دقيق اصطناعي'), findsOneWidget);
        expect(find.text('هدف جديد'), findsNothing);
        expect(find.text('حفظ المسودة'), findsNothing);
        expect(find.text('حفظ المراجعة'), findsNothing);
        await t.ensureVisible(find.text('مراجعة منشورة حقيقية'));
        await t.pumpAndSettle();
        expect(find.text('مراجعة منشورة حقيقية'), findsOneWidget);
        expect(t.takeException(), isNull);
      },
    );
  }
}
