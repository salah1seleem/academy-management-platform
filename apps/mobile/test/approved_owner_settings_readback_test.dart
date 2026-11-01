import 'dart:async';

import 'package:academy_mobile/core/api/api_client.dart';
import 'package:academy_mobile/features/owner/owner_home_settings.dart';
import 'package:academy_mobile/features/owner/collection_target.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

import 'v2_role_workspaces_test.dart' as f;

Map<String, dynamic> config({
  bool top = false,
  int? target,
  List<Object> targets = const [],
  int revision = 4,
}) => {
  'academyCurrency': 'EGP',
  'versions': [
    {
      'state': 'Active',
      'revision': revision,
      'settings': {
        'ownerHome': {
          'quickActionsAtTop': true,
          'highlightPortraits': {
            'coach': top ? 'coach-02' : 'coach-01',
            'member': 'guardian-01',
            'player': 'child-01',
          },
          'coachAssignmentTarget': target,
          'targets': targets,
        },
      },
    },
  ],
};
void main() {
  for (final missing in ['ownerHome', 'targets']) {
    testWidgets(
      'first collection target opens and saves when $missing is absent',
      (t) async {
        var writes = 0;
        bool? result;
        final auth = await f.authenticate('AcademyOwner', (
          m,
          p,
          b,
          token,
        ) async {
          if (m == 'PUT') {
            writes++;
            expect(p, '/api/v1/manage/configuration/collection-target');
            expect((b as Map)['expectedRevision'], 4);
            expect(b['amount'], 1250);
            return {};
          }
          final data = config(
            targets: writes == 0
                ? []
                : [
                    {'from': '2026-09-28', 'to': '2026-10-28', 'amount': 1250},
                  ],
          );
          if (writes == 0) {
            final settings =
                ((data['versions'] as List).single as Map)['settings'] as Map;
            if (missing == 'ownerHome') {
              settings.remove('ownerHome');
            } else {
              (settings['ownerHome'] as Map).remove('targets');
            }
          }
          return data;
        });
        await t.pumpWidget(
          f.app(
            Builder(
              builder: (context) => Scaffold(
                body: TextButton(
                  onPressed: () async {
                    result = await Navigator.push<bool>(
                      context,
                      MaterialPageRoute(
                        builder: (_) => CollectionTargetPage(
                          auth: auth,
                          from: '2026-09-28',
                          to: '2026-10-28',
                        ),
                      ),
                    );
                  },
                  child: const Text('فتح الهدف'),
                ),
              ),
            ),
          ),
        );
        await t.tap(find.text('فتح الهدف'));
        await t.pumpAndSettle();
        expect(find.text('إعادة تحميل الإعدادات الحالية'), findsNothing);
        expect(writes, 0);
        expect(t.takeException(), isNull);
        await t.enterText(find.byType(TextFormField), '1250');
        await t.tap(find.text('حفظ الهدف'));
        await t.pumpAndSettle();
        expect(result, isTrue);
        expect(writes, 1);
        expect(t.takeException(), isNull);
        await t.pumpWidget(const SizedBox());
        auth.dispose();
      },
    );
  }

  testWidgets(
    'mismatched home read freezes controls and GET retry confirms without another PUT',
    (t) async {
      var writes = 0, saved = false;
      final auth = await f.authenticate('AcademyOwner', (m, p, b, token) async {
        if (m == 'PUT') {
          writes++;
          return {};
        }
        return config(top: saved, target: saved ? 3 : null);
      });
      await t.pumpWidget(f.app(OwnerHomeSettingsPage(auth: auth)));
      await t.pumpAndSettle();
      await t.tap(find.byKey(const ValueKey('honor-fallback-coach')));
      await t.pumpAndSettle();
      await t.tap(find.text('صورة عرض 2').last);
      await t.pumpAndSettle();
      await t.enterText(find.byType(TextFormField), '3');
      await t.ensureVisible(find.text('حفظ الإعدادات'));
      await t.ensureVisible(find.text('حفظ الإعدادات'));
      await t.tap(find.text('حفظ الإعدادات'));
      await t.pumpAndSettle();
      expect(
        find.textContaining('لم تتطابق إعدادات الرئيسية المقروءة'),
        findsOneWidget,
      );
      expect(find.text('تم حفظ إعدادات الرئيسية.'), findsNothing);
      expect(
        t
            .widget<DropdownButtonFormField<String>>(
              find.byKey(const ValueKey('honor-fallback-coach')),
            )
            .onChanged,
        isNull,
      );
      expect(
        t.widget<EditableText>(find.byType(EditableText)).readOnly,
        isTrue,
      );
      saved = true;
      await t.scrollUntilVisible(
        find.text('إعادة التحقق من الحفظ'),
        150,
        scrollable: find.byType(Scrollable).first,
      );
      await t.pumpAndSettle();
      await t.tap(find.text('إعادة التحقق من الحفظ'));
      await t.pumpAndSettle();
      expect(find.text('تم حفظ إعدادات الرئيسية.'), findsOneWidget);
      expect(writes, 1);
      expect(
        t
            .widget<DropdownButtonFormField<String>>(
              find.byKey(const ValueKey('honor-fallback-coach')),
            )
            .onChanged,
        isNotNull,
      );
      await t.pumpWidget(const SizedBox());
      auth.dispose();
    },
  );
  testWidgets('lost write response is resolved with GET only', (t) async {
    var writes = 0, top = false;
    final auth = await f.authenticate('AcademyOwner', (m, p, b, token) async {
      if (m == 'PUT') {
        writes++;
        top = true;
        throw const ApiFailure(0, 'تعذر الاتصال.');
      }
      return config(top: top);
    });
    await t.pumpWidget(f.app(OwnerHomeSettingsPage(auth: auth)));
    await t.pumpAndSettle();
    await t.tap(find.byKey(const ValueKey('honor-fallback-coach')));
    await t.pumpAndSettle();
    await t.tap(find.text('صورة عرض 2').last);
    await t.pumpAndSettle();
    await t.ensureVisible(find.text('حفظ الإعدادات'));
    await t.tap(find.text('حفظ الإعدادات'));
    await t.pumpAndSettle();
    expect(find.text('تم حفظ إعدادات الرئيسية.'), findsNothing);
    await t.scrollUntilVisible(
      find.text('إعادة التحقق من الحفظ'),
      150,
      scrollable: find.byType(Scrollable).first,
    );
    await t.pumpAndSettle();
    await t.tap(find.text('إعادة التحقق من الحفظ'));
    await t.pumpAndSettle();
    expect(writes, 1);
    expect(find.text('تم حفظ إعدادات الرئيسية.'), findsOneWidget);
    await t.pumpWidget(const SizedBox());
    auth.dispose();
  });
  testWidgets(
    'another branch target cannot close the scoped page; matching GET retry can',
    (t) async {
      var branch = 'other', writes = 0;
      bool? result;
      final auth = await f.authenticate('AcademyOwner', (m, p, b, token) async {
        if (m == 'PUT') {
          writes++;
          expect((b as Map)['branchId'], 'b1');
          return {};
        }
        return config(
          targets: [
            {
              'from': '2026-09-01',
              'to': '2026-09-30',
              'branchId': branch,
              'amount': 3000.5,
            },
          ],
        );
      });
      await t.pumpWidget(
        f.app(
          Builder(
            builder: (context) => Scaffold(
              body: TextButton(
                onPressed: () async {
                  result = await Navigator.push<bool>(
                    context,
                    MaterialPageRoute(
                      builder: (_) => CollectionTargetPage(
                        auth: auth,
                        from: '2026-09-01',
                        to: '2026-09-30',
                        branchId: 'b1',
                        branchName: 'فرع اصطناعي',
                        initialAmount: 1000,
                      ),
                    ),
                  );
                },
                child: const Text('فتح'),
              ),
            ),
          ),
        ),
      );
      await t.tap(find.text('فتح'));
      await t.pumpAndSettle();
      expect(
        find.text('فرع اصطناعي · 2026-09-01 — 2026-09-30'),
        findsOneWidget,
      );
      await t.enterText(find.byType(TextFormField), '3000.50');
      await t.tap(find.text('حفظ الهدف'));
      await t.pumpAndSettle();
      expect(result, isNull);
      expect(
        find.textContaining('لم يتطابق هدف الفترة المقروء'),
        findsOneWidget,
      );
      branch = 'b1';
      await t.scrollUntilVisible(
        find.text('إعادة التحقق من الحفظ'),
        150,
        scrollable: find.byType(Scrollable).first,
      );
      await t.pumpAndSettle();
      await t.tap(find.text('إعادة التحقق من الحفظ'));
      await t.pumpAndSettle();
      expect(result, isTrue);
      expect(writes, 1);
      await t.pumpWidget(const SizedBox());
      auth.dispose();
    },
  );
  testWidgets('logout during write prevents readback and settings disclosure', (
    t,
  ) async {
    final write = Completer<Object?>();
    var reads = 0;
    final auth = await f.authenticate('AcademyOwner', (m, p, b, token) async {
      if (m == 'PUT') return write.future;
      if (p.endsWith('/configuration')) reads++;
      return config();
    });
    await t.pumpWidget(f.app(OwnerHomeSettingsPage(auth: auth)));
    await t.pumpAndSettle();
    await t.tap(find.byKey(const ValueKey('honor-fallback-coach')));
    await t.pumpAndSettle();
    await t.tap(find.text('صورة عرض 2').last);
    await t.pumpAndSettle();
    await t.ensureVisible(find.text('حفظ الإعدادات'));
    await t.tap(find.text('حفظ الإعدادات'));
    await t.pump();
    await auth.logout();
    write.complete({});
    await t.pumpAndSettle();
    expect(reads, 1);
    expect(find.text('المظهر والرئيسية'), findsNothing);
    expect(t.takeException(), isNull);
    await t.pumpWidget(const SizedBox());
    auth.dispose();
  });
  testWidgets(
    'reload after conflict updates values and revision before another save',
    (t) async {
      var changed = false;
      final revisions = <Object?>[];
      final auth = await f.authenticate('AcademyOwner', (m, p, b, token) async {
        if (m == 'PUT') {
          revisions.add((b as Map)['expectedRevision']);
          if (!changed) {
            changed = true;
            throw const ApiFailure(409, 'تغيرت الإعدادات.');
          }
          return {};
        }
        return config(
          top: changed,
          target: changed ? 5 : null,
          revision: changed ? 8 : 4,
        );
      });
      await t.pumpWidget(f.app(OwnerHomeSettingsPage(auth: auth)));
      await t.pumpAndSettle();
      await t.tap(find.byKey(const ValueKey('honor-fallback-coach')));
      await t.pumpAndSettle();
      await t.tap(find.text('صورة عرض 2').last);
      await t.pumpAndSettle();
      await t.ensureVisible(find.text('حفظ الإعدادات'));
      await t.tap(find.text('حفظ الإعدادات'));
      await t.pumpAndSettle();
      await t.ensureVisible(find.text('إعادة تحميل الإعدادات الحالية'));
      await t.tap(find.text('إعادة تحميل الإعدادات الحالية'));
      await t.pumpAndSettle();
      expect(
        t.widget<TextFormField>(find.byType(TextFormField)).controller!.text,
        '5',
      );
      await t.ensureVisible(find.text('حفظ الإعدادات'));
      await t.ensureVisible(find.text('حفظ الإعدادات'));
      await t.tap(find.text('حفظ الإعدادات'));
      await t.pumpAndSettle();
      expect(revisions, [4, 8]);
      expect(find.text('تم حفظ إعدادات الرئيسية.'), findsOneWidget);
      await t.pumpWidget(const SizedBox());
      auth.dispose();
    },
  );
}
