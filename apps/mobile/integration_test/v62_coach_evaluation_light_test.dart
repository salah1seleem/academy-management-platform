import 'package:academy_mobile/app/app.dart';
import 'package:academy_mobile/core/api/api_client.dart';
import 'package:academy_mobile/core/auth/auth_controller.dart';
import 'package:academy_mobile/core/storage/refresh_store.dart';
import 'package:academy_mobile/core/theme/appearance.dart';
import 'package:academy_mobile/core/theme/ui_text.dart';
import 'package:academy_mobile/features/coach/coach_evaluation.dart';
import 'package:academy_mobile/features/coach/coach_home.dart';
import 'package:academy_mobile/features/guardian/guardian_ui.dart';
import 'package:academy_mobile/features/shared/role_navigation.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:integration_test/integration_test.dart';

import 'v57_role_footer_design_test.dart' show FooterReadTransport;

void main() {
  final binding = IntegrationTestWidgetsFlutterBinding.ensureInitialized();
  testWidgets(
    'real light coach form resumes existing draft, preserves sport criteria and discards unsaved intent without writes',
    (t) async {
      expect(const bool.fromEnvironment('ISOLATED_DEMO'), true);
      const base = String.fromEnvironment('API_BASE_URL');
      expect(base, 'http://127.0.0.1:5278');
      const width = int.fromEnvironment(
        'EVALUATION_EXPECTED_WIDTH',
        defaultValue: 440,
      );
      expect(
        (t.view.physicalSize / t.view.devicePixelRatio).width,
        width.toDouble(),
      );
      final transport = FooterReadTransport(AppConfig('Demo', base));
      final auth = AuthController(
        transport,
        SecureRefreshStore('v45.5278.os-media-entry.01099900020'),
      );
      await auth.restore();
      if (auth.session == null) {
        final challenge = await auth.requestOtp('01099900020');
        await auth.verify('01099900020', challenge, '246810');
      }
      expect(auth.session!.selected!.role, 'Coach');
      expect(
        auth.session!.selected!.academyId,
        '30000000-0000-0000-0000-000000000003',
      );
      final drafts = rows(
        await auth.request('GET', '/api/v1/evaluations?status=Draft'),
      );
      expect(
        drafts,
        isNotEmpty,
        reason: 'Reuse the existing draft; do not create another assessment for acceptance.',
      );
      final options = obj(
        await auth.request('GET', '/api/v1/evaluations/options'),
      );
      final draft = drafts.firstWhere(
        (d) =>
            rows(options['enrollments'])
                .any((e) => e['id'] == d['sportEnrollmentId']),
      );
      final enrollment = rows(options['enrollments'])
          .singleWhere((e) => e['id'] == draft['sportEnrollmentId']);
      final groups = rows(await auth.request('GET', '/api/v1/coach/groups'));
      final group = groups.singleWhere((g) => g['id'] == enrollment['groupId']);
      final path = '/api/v1/evaluations/${draft['id']}';
      final original = obj(await auth.request('GET', path));
      final scores = rows(original['scores']);
      expect(scores, isNotEmpty);
      final unassessed = scores.where((s) => s['score'] == null).toList();
      expect(unassessed.length, greaterThanOrEqualTo(2));
      Future<void> wait(Finder f) async {
        for (var i = 0; i < 240 && f.evaluate().isEmpty; i++) {
          await t.pump(const Duration(milliseconds: 150));
        }
        expect(f, findsWidgets);
        await t.pumpAndSettle();
      }

      Future<void> locate(Finder f) async {
        final scroll = find.byType(Scrollable).first;
        t.state<ScrollableState>(scroll).position.jumpTo(0);
        await t.pumpAndSettle();
        await t.scrollUntilVisible(f, 160, scrollable: scroll, maxScrolls: 150);
        await t.ensureVisible(f);
        await t.pumpAndSettle();
      }

      Future<void> tapText(String label) async {
        final f = find.text(label).last;
        await wait(f);
        await locate(f);
        await t.tap(f);
        await t.pumpAndSettle();
      }

      Future<void> openDraft() async {
        final row = find.byKey(ValueKey('evaluation-history-${draft['id']}'));
        await wait(row);
        await locate(row);
        await t.tap(row);
        await wait(find.byType(CoachEvaluation));
        await wait(find.byType(Slider));
        expect(
          t.widget<CoachEvaluation>(find.byType(CoachEvaluation)).evaluationId,
          draft['id'],
        );
      }

      final appearance = Appearance();
      for (final theme in ['football', 'general']) {
        await appearance.completeRoleIntro('Coach', theme);
      }
      await appearance.update(night: false);
      await t.pumpWidget(
        AcademyApp(auth: auth, environment: 'Demo', appearance: appearance),
      );
      await wait(find.text('تدريباتي'));
      await t.tap(
        find.descendant(
          of: find.byType(NavigationBar),
          matching: find.text('الحضور\nوالتقييم'),
        ),
      );
      await wait(find.byType(CoachGroupsPage));
      await tapText(textOf(group['arabicName']));
      await wait(find.byType(CoachGroup));
      final name = find.text(textOf(enrollment['player']));
      await locate(name);
      final card = find.ancestor(of: name, matching: find.byType(Card)).first;
      final playerDetails = find.descendant(
        of: card,
        matching: find.textContaining(RegExp(r'^(تفاصيل اللاعب|عرض الملف)$')),
      );
      await t.ensureVisible(playerDetails);
      await t.tap(playerDetails);
      await tapText('تقييمات اللاعب');
      await wait(find.byType(CoachPlayerEvaluations));
      await openDraft();
      expect(
        Theme.of(t.element(find.byType(CoachEvaluation))).brightness,
        Brightness.light,
      );
      await binding.takeScreenshot(
        'phase-approved-coach-evaluation-light-header-w$width',
      );
      for (final score in scores) {
        final slider = find.byKey(
          ValueKey('evaluation-score-${score['criterionId']}'),
        );
        final heading = find.byKey(
          ValueKey('evaluation-criterion-${score['criterionId']}'),
        );
        await locate(heading);
        expect(t.widget<UiText>(heading).data, textOf(score['name']));
        final widget = t.widget<Slider>(slider);
        expect(widget.value, (score['score'] as num?)?.toDouble() ?? 50);
        expect(widget.min, 0);
        expect(widget.max, 100);
        expect(widget.onChanged, isNotNull);
      }
      final notes = find.widgetWithText(TextField, 'ملاحظات المدرب');
      await locate(notes);
      expect(
        t.widget<TextField>(notes).controller!.text,
        textOf(original['generalNotes'] ?? ''),
      );
      await binding.takeScreenshot(
        'phase-approved-coach-evaluation-light-notes-w$width',
      );
      final first = find.byKey(
        ValueKey('evaluation-score-${unassessed[0]['criterionId']}'),
      );
      await locate(first);
      await t.drag(first, const Offset(500, 0));
      await t.pumpAndSettle();
      expect(t.widget<Slider>(first).value, 0);
      final second = find.byKey(
        ValueKey('evaluation-assess-${unassessed[1]['criterionId']}'),
      );
      await locate(second);
      await t.tap(second);
      await t.pumpAndSettle();
      expect(
        find.descendant(of: second, matching: find.text('إلغاء التقييم')),
        findsOneWidget,
      );
      await locate(
        find.byKey(
          ValueKey('evaluation-criterion-${unassessed[0]['criterionId']}'),
        ),
      );
      await binding.takeScreenshot(
        'phase-approved-coach-evaluation-light-local-zero-w$width',
      );
      await t.tap(find.byType(BackButton));
      await t.pumpAndSettle();
      await t.tap(find.text('خروج دون حفظ'));
      await t.pumpAndSettle();
      await wait(find.byType(CoachPlayerEvaluations));
      await openDraft();
      await locate(first);
      expect(t.widget<Slider>(first).value, 50);
      await locate(second);
      expect(
        find.descendant(of: second, matching: find.text('اعتماد 50')),
        findsOneWidget,
      );
      final after = obj(await auth.request('GET', path));
      expect(after, original);
      expect(transport.writes, isEmpty);
      expect(t.takeException(), isNull);
      await t.pumpWidget(const SizedBox());
      auth.dispose();
    },
  );
}
