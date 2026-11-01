import 'support/visibility.dart';

import 'package:academy_mobile/features/shared/native_match_management.dart';
import 'package:academy_mobile/core/api/api_client.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

import 'v2_role_workspaces_test.dart' show authenticate, app;

final options = <String, dynamic>{
  'teams': [
    {
      'id': 'team',
      'name': 'فريق اختبار',
      'sportId': 'football',
      'branchId': 'branch',
      'coachMembershipId': 'coach',
      'isActive': true,
    },
  ],
  'tournaments': [
    {
      'id': 'tournament',
      'name': 'بطولة اختبار',
      'sportId': 'football',
      'isActive': true,
    },
  ],
  'coaches': [
    {
      'id': 'coach',
      'name': 'مدرب اختبار',
      'allBranches': true,
      'allSports': true,
    },
  ],
};
Future<void> completeForm(WidgetTester t) async {
  t
      .widget<DropdownButtonFormField<String>>(
        find.byType(DropdownButtonFormField<String>).first,
      )
      .onChanged!('team');
  await t.pumpAndSettle();
  t
      .widget<DropdownButtonFormField<String>>(
        find.byType(DropdownButtonFormField<String>).at(1),
      )
      .onChanged!('tournament');
  await t.pumpAndSettle();
  await t.enterText(
    find.widgetWithText(TextFormField, 'المنافس'),
    'منافس اصطناعي',
  );
  await t.enterText(
    find.widgetWithText(TextFormField, 'الملعب'),
    'ملعب اختبار',
  );
}

Future<void> save(WidgetTester t) async {
  await t.scrollUntilVisible(
    find.text('حفظ المباراة'),
    200,
    scrollable: find.byType(Scrollable).first,
  );
  await revealTestTarget(t, find.text('حفظ المباراة'));
  await t.pumpAndSettle();
  await t.tap(find.text('حفظ المباراة'));
  await t.pumpAndSettle();
}

void main() {
  for (final fault in ['success', 'lostRead', 'wrongDate', 'wrongPublish']) {
    final loseRead = fault != 'success';
    testWidgets(
      'Match creation verifies persisted data and verification resumes without another write ($fault)',
      (t) async {
        await t.binding.setSurfaceSize(const Size(390, 844));
        addTearDown(() => t.binding.setSurfaceSize(null));
        final writes = <String>[];
        Map<String, dynamic>? sent;
        var reads = 0;
        final auth = await authenticate('AcademyOwner', (m, p, b, token) async {
          if (m == 'POST' || m == 'PUT') {
            writes.add(m);
            sent = b as Map<String, dynamic>;
            return {'id': 'saved-match'};
          }
          reads++;
          if (fault == 'lostRead' && reads == 1) {
            throw const ApiFailure(0, 'تعذر التحقق من الحفظ.');
          }
          return {
            ...sent!,
            'id': 'saved-match',
            'matchDate': fault == 'wrongDate' && reads == 1
                ? '2001-01-01'
                : sent!['matchDate'],
            'publishedAtUtc': fault == 'wrongPublish' && reads == 1
                ? '2026-10-21T12:00:00Z'
                : null,
            'players': <Map<String, dynamic>>[],
            'opponent': sent!['opponent'],
            'ourScore': sent!['ourScore'],
            'opponentScore': sent!['opponentScore'],
          };
        });
        await t.pumpWidget(
          app(
            Builder(
              builder: (context) => Scaffold(
                body: FilledButton(
                  onPressed: () => Navigator.of(context).push(
                    MaterialPageRoute(
                      builder: (_) =>
                          NativeMatchEditor(auth: auth, options: options),
                    ),
                  ),
                  child: const Text('فتح النموذج'),
                ),
              ),
            ),
          ),
        );
        await t.tap(find.text('فتح النموذج'));
        await t.pumpAndSettle();
        await completeForm(t);
        await save(t);
        if (loseRead) {
          expect(find.text('فتح النموذج'), findsNothing);
          expect(
            t
                .widgetList<AbsorbPointer>(find.byType(AbsorbPointer))
                .any((w) => w.absorbing),
            isTrue,
          );
          final retry = find.text('إعادة التحقق من المباراة نفسها');
          await t.scrollUntilVisible(
            retry,
            160,
            scrollable: find.byType(Scrollable).first,
          );
          await t.tap(retry);
          await t.pumpAndSettle();
        }
        expect(writes, ['POST']);
        expect(reads, loseRead ? 2 : 1);
        expect(sent!['opponent'], 'منافس اصطناعي');
        expect(sent!['teamId'], 'team');
        expect(sent!['tournamentId'], 'tournament');
        expect(sent!['ourScore'], isNull);
        expect(sent!['opponentScore'], isNull);
        expect(sent!['publish'], isFalse);
        expect(find.text('فتح النموذج'), findsOneWidget);
        expect(t.takeException(), isNull);
        await t.pumpWidget(const SizedBox());
        auth.dispose();
      },
    );
  }
  testWidgets(
    'Uncertain creation prevents a second POST and asks for list reconciliation',
    (t) async {
      await t.binding.setSurfaceSize(const Size(390, 844));
      addTearDown(() => t.binding.setSurfaceSize(null));
      var writes = 0;
      final auth = await authenticate('AcademyOwner', (m, p, b, token) async {
        writes++;
        throw const ApiFailure(0, 'تعذر الاتصال');
      });
      await t.pumpWidget(app(NativeMatchEditor(auth: auth, options: options)));
      await t.pumpAndSettle();
      await completeForm(t);
      await save(t);
      expect(writes, 1);
      expect(find.textContaining('لم يتأكد إنشاء المباراة'), findsOneWidget);
      await revealTestTarget(
        t,
        find.widgetWithText(FilledButton, 'حفظ المباراة'),
      );
      expect(
        t
            .widget<FilledButton>(
              find.widgetWithText(FilledButton, 'حفظ المباراة'),
            )
            .onPressed,
        isNull,
      );
      expect(t.takeException(), isNull);
      await t.pumpWidget(const SizedBox());
      auth.dispose();
    },
  );
  testWidgets(
    'Read-only match administrator cannot add or edit despite structure permission',
    (t) async {
      final auth = await authenticate('AcademyAdmin', (m, p, b, token) async {
        if (p.endsWith('/my-permissions')) return ['structure.manage'];
        if (p.endsWith('/my-access')) {
          return {
            'useActionGrants': true,
            'actions': [
              {'module': 'matches', 'read': true, 'add': false, 'edit': false},
            ],
          };
        }
        if (p.endsWith('/options')) return options;
        return [
          {
            'id': 'existing',
            'team': 'فريق اختبار',
            'opponent': 'منافس',
            'tournament': 'بطولة اختبار',
            'status': 'Draft',
            'matchDate': '2026-10-21',
            'startTime': '17:00',
            'venue': 'ملعب',
          },
        ];
      });
      await t.pumpWidget(app(NativeMatchManagement(auth: auth)));
      await t.pumpAndSettle();
      expect(find.text('إضافة مباراة'), findsNothing);
      expect(find.text('تفاصيل المباراة والنتيجة'), findsNothing);
      expect(find.text('فريق اختبار'), findsOneWidget);
      expect(t.takeException(), isNull);
      await t.pumpWidget(const SizedBox());
      auth.dispose();
    },
  );
}
