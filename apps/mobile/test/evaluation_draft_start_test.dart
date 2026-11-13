import 'package:academy_mobile/core/api/api_client.dart';
import 'package:academy_mobile/features/coach/coach_evaluation.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

import 'v2_role_workspaces_test.dart' as fixtures;

const player = {
  'id': 'enrollment',
  'playerId': 'player',
  'player': 'لاعب اصطناعي',
  'group': 'مجموعة العرض',
};

Map<String, dynamic> draft(String date) => {
  'id': 'draft',
  'sportEnrollmentId': 'enrollment',
  'player': 'لاعب اصطناعي',
  'status': 'Draft',
  'version': 1,
  'evaluationDate': date,
  'scores': [],
};

void main() {
  testWidgets(
    'an invalid academy day can be read again before any draft write',
    (t) async {
      var calendarReads = 0, writes = 0;
      final auth = await fixtures.authenticate('Coach', (
        method,
        path,
        body,
        _,
      ) async {
        if (method == 'GET' && path == '/api/v1/academy/calendar') {
          return {'today': ++calendarReads == 1 ? '2026-02-31' : '2026-11-05'};
        }
        if (method == 'POST' && path == '/api/v1/evaluations') {
          writes++;
          expect(body, containsPair('evaluationDate', '2026-11-05'));
          return {'id': 'draft'};
        }
        expect(path, '/api/v1/evaluations/draft');
        return draft('2026-11-05');
      });
      await t.pumpWidget(
        fixtures.app(CoachEvaluation(auth: auth, enrollment: player)),
      );
      await t.pumpAndSettle();
      expect(writes, 0);
      expect(find.text('تعذر قراءة تاريخ الأكاديمية.'), findsOneWidget);
      expect(find.text('عرض تقييمات اللاعب'), findsNothing);
      await t.tap(find.text('إعادة محاولة بدء التقييم'));
      await t.pumpAndSettle();
      expect(calendarReads, 2);
      expect(writes, 1);
      expect(find.text('5 نوفمبر 2026'), findsOneWidget);
      await t.pumpWidget(const SizedBox());
      auth.dispose();
    },
  );

  testWidgets(
    'the assigned session date is read when the incoming roster omitted it',
    (t) async {
      final calls = <String>[];
      final auth = await fixtures.authenticate('Coach', (
        method,
        path,
        body,
        _,
      ) async {
        calls.add('$method $path');
        if (path == '/api/v1/attendance/sessions/session') {
          return {'id': 'session', 'sessionDate': '2026-11-03'};
        }
        if (method == 'POST') {
          expect(body, containsPair('trainingSessionId', 'session'));
          expect(body, containsPair('evaluationDate', '2026-11-03'));
          return {'id': 'draft'};
        }
        return draft('2026-11-03');
      });
      await t.pumpWidget(
        fixtures.app(
          CoachEvaluation(
            auth: auth,
            enrollment: const {...player, 'sessionId': 'session'},
          ),
        ),
      );
      await t.pumpAndSettle();
      expect(calls, [
        'GET /api/v1/attendance/sessions/session',
        'POST /api/v1/evaluations',
        'GET /api/v1/evaluations/draft',
      ]);
      expect(find.text('3 نوفمبر 2026'), findsOneWidget);
      await t.pumpWidget(const SizedBox());
      auth.dispose();
    },
  );

  testWidgets(
    'a successful response without a draft id keeps recovery available',
    (t) async {
      var writes = 0;
      final auth = await fixtures.authenticate('AcademyAdmin', (
        method,
        path,
        _,
        _,
      ) async {
        if (path == '/api/v1/academy/calendar') return {'today': '2026-11-05'};
        expect(method, 'POST');
        expect(path, '/api/v1/evaluations');
        writes++;
        return {'id': null};
      });
      await t.pumpWidget(
        fixtures.app(CoachEvaluation(auth: auth, enrollment: player)),
      );
      await t.pumpAndSettle();
      expect(writes, 1);
      expect(find.text('تعذر تأكيد إنشاء المسودة.'), findsOneWidget);
      expect(find.text('عرض تقييمات اللاعب'), findsOneWidget);
      expect(find.text('بدء تقييم جديد'), findsNothing);
      await t.pumpWidget(const SizedBox());
      auth.dispose();
    },
  );

  testWidgets('a forbidden session date read cannot create a draft', (t) async {
    final auth = await fixtures.authenticate('Coach', (
      method,
      path,
      _,
      _,
    ) async {
      expect(method, 'GET');
      expect(path, '/api/v1/attendance/sessions/session');
      throw const ApiFailure(404, 'السجل غير متاح');
    });
    await t.pumpWidget(
      fixtures.app(
        CoachEvaluation(
          auth: auth,
          enrollment: const {...player, 'sessionId': 'session'},
        ),
      ),
    );
    await t.pumpAndSettle();
    expect(find.text('هذا التقييم غير متاح لحسابك.'), findsOneWidget);
      expect(find.text('إعادة محاولة بدء التقييم'), findsNothing);
    expect(find.text('عرض تقييمات اللاعب'), findsNothing);
    await t.pumpWidget(const SizedBox());
    auth.dispose();
  });
}
