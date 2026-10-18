import 'package:academy_mobile/features/shared/session_lesson.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:flutter_localizations/flutter_localizations.dart';

import 'owner_test.dart' as fixture;

Map<String, dynamic> lesson({bool write = false}) => {
  'session': {
    'group': 'مجموعة حصة اصطناعية',
    'sessionDate': '2026-10-18',
    'startTime': '15:00:00',
    'endTime': '16:00:00',
  },
  'sessionMinutes': 60,
  'revision': 2,
  'canAssign': write,
  'available': [
    {'id': 'p', 'title': 'خطة حصة60 دقيقة'},
  ],
  'active': {
    'item': {'title': 'خطة حصة60 دقيقة', 'declaredMinutes': 60},
    'computedMinutes': 60,
    'durationMatches': true,
    'steps': [
      {
        'minutes': 60,
        'exercise': {
          'title': 'تمرين اصطناعي طويل لمراجعة التدفق',
          'skill': 'تحكم',
          'equipment': 'كرة',
          'instructions': 'نص اختبار فقط وليس تعليمات تدريب معتمدة',
          'rightsKind': 'Original',
          'rightsReference': 'مؤلف الاختبار',
        },
      },
    ],
  },
  'history': [
    {
      'id': 'a',
      'revision': 2,
      'actorName': 'مدرب اصطناعي',
      'createdAtUtc': '2026-10-18T12:00:00Z',
    },
  ],
};
void main() {
  for (final width in [360.0, 390.0, 430.0]) {
    testWidgets(
      'Session plan English at $width and200% has computed source without overflow',
      (t) async {
        await t.binding.setSurfaceSize(Size(width, 850));
        addTearDown(() => t.binding.setSurfaceSize(null));
        final auth = await fixture.owner((m, p, b, k) async => lesson());
        await t.pumpWidget(
          MaterialApp(
            locale: const Locale('en'),
            supportedLocales: const [Locale('en'), Locale('ar')],
            localizationsDelegates: GlobalMaterialLocalizations.delegates,
            home: MediaQuery(
              data: const MediaQueryData(textScaler: TextScaler.linear(2)),
              child: SessionLessonPage(auth: auth, sessionId: 's'),
            ),
          ),
        );
        await t.pumpAndSettle();
        expect(find.text('Session plan'), findsOneWidget);
        await t.scrollUntilVisible(
          find.text('60 computed minutes · declared 60'),
          200,
        );
        expect(find.text('60 computed minutes · declared 60'), findsOneWidget);
        expect(t.takeException(), isNull);
      },
    );
  }
  testWidgets(
    'Reads computed source with history and no attendance or assignment mutation for a reader',
    (t) async {
      final auth = await fixture.owner((m, p, b, k) async => lesson());
      await t.pumpWidget(
        fixture.shell(SessionLessonPage(auth: auth, sessionId: 's')),
      );
      await t.pumpAndSettle();
      expect(find.text('60 دقيقة محسوبة · المعلن 60'), findsOneWidget);
      expect(find.byType(DropdownButtonFormField<String>), findsNothing);
      expect(find.text('حاضر'), findsNothing);
      await t.scrollUntilVisible(find.text('سجل إسناد الخطة'), 250);
      await t.tap(find.text('سجل إسناد الخطة'));
      await t.pumpAndSettle();
      expect(find.text('مراجعة 2 · مدرب اصطناعي'), findsOneWidget);
    },
  );
  testWidgets(
    'Actual save uses exact session and prior revision then reads saved source',
    (t) async {
      final writes = <Map<String, dynamic>>[];
      final auth = await fixture.owner((m, p, b, k) async {
        if (m == 'POST') {
          expect(p, '/api/v1/evaluations/lessons/sessions/s');
          writes.add(Map<String, dynamic>.from(b as Map));
          return {'id': 'a'};
        }
        return lesson(write: true);
      });
      await t.pumpWidget(
        fixture.shell(SessionLessonPage(auth: auth, sessionId: 's')),
      );
      await t.pumpAndSettle();
      await t.tap(find.byType(DropdownButtonFormField<String>));
      await t.pumpAndSettle();
      await t.tap(find.text('خطة حصة60 دقيقة').last);
      await t.pumpAndSettle();
      await t.tap(find.text('استبدال مع حفظ السابق'));
      await t.pumpAndSettle();
      expect(writes.single, containsPair('previousRevision', 2));
      expect(writes.single, containsPair('templateId', 'p'));
      expect(writes.single['requestId'], matches(RegExp(r'^[0-9a-f-]{36}$')));
      expect(find.text('تم حفظ خطة الحصة مع تاريخ الإسناد.'), findsOneWidget);
    },
  );
  testWidgets('360px200% fits long Arabic instructions without overflow', (
    t,
  ) async {
    await t.binding.setSurfaceSize(const Size(360, 850));
    addTearDown(() => t.binding.setSurfaceSize(null));
    final auth = await fixture.owner((m, p, b, k) async => lesson());
    await t.pumpWidget(
      fixture.shell(
        MediaQuery(
          data: const MediaQueryData(textScaler: TextScaler.linear(2)),
          child: SessionLessonPage(auth: auth, sessionId: 's'),
        ),
      ),
    );
    await t.pumpAndSettle();
    await t.scrollUntilVisible(
      find.text('تمرين اصطناعي طويل لمراجعة التدفق'),
      250,
    );
    expect(t.takeException(), isNull);
    expect(find.text('تمرين اصطناعي طويل لمراجعة التدفق'), findsOneWidget);
  });
  testWidgets('Background and authority change hide the delivered content', (
    t,
  ) async {
    final auth = await fixture.owner((m, p, b, k) async => lesson());
    await t.pumpWidget(
      fixture.shell(SessionLessonPage(auth: auth, sessionId: 's')),
    );
    await t.pumpAndSettle();
    expect(find.text('مجموعة حصة اصطناعية'), findsOneWidget);
    t.binding.handleAppLifecycleStateChanged(AppLifecycleState.inactive);
    await t.pumpAndSettle();
    expect(find.text('مجموعة حصة اصطناعية'), findsNothing);
    t.binding.handleAppLifecycleStateChanged(AppLifecycleState.resumed);
    await t.pumpAndSettle();
    await auth.logout();
    await t.pumpAndSettle();
    expect(find.text('مجموعة حصة اصطناعية'), findsNothing);
  });
}
