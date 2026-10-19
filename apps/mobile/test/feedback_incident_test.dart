import 'dart:convert';

import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:academy_mobile/features/shared/feedback_incident.dart';

import 'guardian_test.dart' as family;

void main() {
  testWidgets(
    'dated picker uses occurrence evidence and keeps no coach accusation by default',
    (t) async {
      final auth = await family.logged((m, p, b, k) async {
        expect(
          p,
          '/api/v1/guardian/children/child/feedback-incidents?incidentOn=2026-09-28',
        );
        return {
          'sessions': [
            {
              'id': 's',
              'groupId': 'old',
              'groupName': 'مجموعة سابقة اصطناعية',
              'date': '2026-09-28',
              'startTime': '16:00:00',
              'endTime': '17:00:00',
              'coaches': [],
            },
          ],
          'notice': 'لا نخمن المدرب الحالي',
          'hasMore': false,
        };
      });
      Map<String, dynamic>? selected;
      await t.pumpWidget(
        family.shell(
          Scaffold(
            body: SingleChildScrollView(
              child: StatefulBuilder(
                builder: (context, update) => FeedbackIncidentPicker(
                  auth: auth,
                  playerId: 'child',
                  day: '2026-09-28',
                  selected: selected,
                  changed: (s) => update(() => selected = s),
                ),
              ),
            ),
          ),
        ),
      );
      await t.pumpAndSettle();
      expect(selected, isNull);
      await t.tap(find.byType(DropdownButtonFormField<String>));
      await t.pumpAndSettle();
      await t.tap(find.text('مجموعة سابقة اصطناعية · 16:00–17:00').last);
      await t.pumpAndSettle();
      expect(selected?['id'], 's');
      expect(find.textContaining('لا يوجد دليل مدرب مؤرخ'), findsOneWidget);
      expect(t.takeException(), isNull);
    },
  );
  testWidgets(
    'owner snapshot shows the saved source with its explicit limitation',
    (t) async {
      final evidence = jsonEncode({
        'groupName': 'مجموعة سابقة',
        'date': '2026-09-28',
        'startTime': '16:00',
        'endTime': '17:00',
        'selectedCoachId': 'coach',
        'coaches': [
          {
            'id': 'coach',
            'name': 'اسم وقت التقديم',
            'source': 'OccurrenceDecision',
          },
        ],
      });
      await t.pumpWidget(
        family.shell(
          Scaffold(
            body: SingleChildScrollView(
              child: FeedbackIncidentEvidence(evidence: evidence),
            ),
          ),
        ),
      );
      await t.pumpAndSettle();
      expect(
        find.textContaining('اسم وقت التقديم · قرار مدرب للحصة'),
        findsOneWidget,
      );
      expect(find.textContaining('ليست إثبات حضور فعلي'), findsOneWidget);
      expect(t.takeException(), isNull);
    },
  );
}
