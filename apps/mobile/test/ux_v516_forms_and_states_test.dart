import 'dart:async';

import 'package:academy_mobile/core/api/api_client.dart';
import 'package:academy_mobile/core/auth/auth_controller.dart';
import 'package:academy_mobile/core/theme/appearance.dart';
import 'package:academy_mobile/core/widgets/arabic_number_input.dart';
import 'package:academy_mobile/core/widgets/guided_form.dart';
import 'package:academy_mobile/core/widgets/page_status.dart';
import 'package:academy_mobile/features/admin/native_player_workspace.dart';
import 'package:academy_mobile/features/admin/native_academy_setup.dart';
import 'package:academy_mobile/features/guardian/guardian_ui.dart';
import 'package:academy_mobile/features/shared/directory_cards.dart';
import 'package:academy_mobile/features/owner/home_destinations.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

import 'auth_controller_test.dart' show FakeApi, MemoryStore, credentials;
import 'approved_login_test.dart' as login_fixture;
import 'modern_experience_v514_test.dart' show frame;
import 'v2_role_workspaces_test.dart' as fixture;
import 'operations_v512_test.dart' show options;

void smallPhone(WidgetTester t, {double height = 667}) {
  t.view.physicalSize = Size(375, height);
  t.view.devicePixelRatio = 1;
  addTearDown(t.view.reset);
}

void main() {
  testWidgets(
    'home player list opens the explicitly global authorized search',
    (t) async {
      final reads = <String>[];
      final auth = await fixture.authenticate('AcademyOwner', (
        m,
        p,
        b,
        token,
      ) async {
        expect(m, 'GET');
        reads.add(p);
        return {'total': 0, 'items': <Object>[]};
      });
      await t.pumpWidget(
        fixture.app(
          HomeSubscriptionPlayers(
            auth: auth,
            filters: const {'branchId': 'selected-branch'},
            uniquePlayers: true,
          ),
        ),
      );
      await t.pumpAndSettle();
      expect(reads.single, contains('branchId=selected-branch'));
      await t.tap(find.text('بحث في جميع اللاعبين'));
      await t.pumpAndSettle();
      expect(find.byType(NativePlayerDirectory), findsOneWidget);
      expect(reads.last, startsWith('/api/v1/people/players?'));
      expect(t.takeException(), isNull);
      await t.pumpWidget(const SizedBox());
      auth.dispose();
    },
  );
  testWidgets(
    'sticky plan review validates the required off-screen price at 200 percent',
    (t) async {
      smallPhone(t);
      var writes = 0;
      final auth = await fixture.authenticate('AcademyOwner', (
        m,
        p,
        b,
        token,
      ) async {
        if (m != 'GET') writes++;
        return options;
      });
      await t.pumpWidget(fixture.app(NativePlanCreate(auth: auth), scale: 2));
      await t.pumpAndSettle();
      final name = find.widgetWithText(TextFormField, 'اسم الباقة');
      await t.ensureVisible(name);
      await t.pumpAndSettle();
      await t.enterText(name, 'باقة خيالية');
      await t.pumpAndSettle();
      await t.tap(find.text('مراجعة الباقة'));
      await t.pumpAndSettle();
      expect(find.text('تأكيد حفظ الباقة'), findsNothing);
      expect(find.text('أدخل سعر الباقة'), findsOneWidget);
      expect(t.getCenter(find.text('أدخل سعر الباقة')).dy, lessThan(667));
      expect(writes, 0);
      expect(t.takeException(), isNull);
      await t.pumpWidget(const SizedBox());
      auth.dispose();
    },
  );
  test(
    'Arabic and Persian numeric input preserves zeros and editing range',
    () {
      const original = TextEditingValue(
        text: '٠١٠۱۲۳٫۵٠',
        selection: TextSelection.collapsed(offset: 9),
        composing: TextRange(start: 3, end: 6),
      );
      final result = const ArabicNumberInputFormatter().formatEditUpdate(
        TextEditingValue.empty,
        original,
      );
      expect(result.text, '010123.50');
      expect(result.selection, original.selection);
      expect(result.composing, original.composing);
      expect(canonicalNumericInput('-٠٫٥ / ١٬٠٠٠'), '-0.5 / 1٬000');
    },
  );

  testWidgets('Arabic phone and OTP reach the existing six-digit contract', (
    t,
  ) async {
    smallPhone(t);
    final bodies = <Map<String, dynamic>>[];
    final auth = AuthController(
      FakeApi((m, p, body, token) async {
        bodies.add(Map<String, dynamic>.from(body as Map));
        return p.endsWith('/request')
            ? {'challengeId': 'synthetic-challenge'}
            : credentials();
      }),
      MemoryStore(),
    )..restoring = false;
    await t.pumpWidget(login_fixture.login(auth));
    await t.pumpAndSettle();
    await t.enterText(find.byType(TextFormField), '٠١٠٩٩٩٠٠١٠٠');
    await login_fixture.submit(t);
    expect(bodies.single['phoneNumber'], '01099900100');
    await t.enterText(find.byType(TextFormField), '۲۴۶۸۱۰');
    await login_fixture.submit(t);
    expect(bodies.last['code'], '246810');
    expect(auth.session, isNotNull);
    expect(t.takeException(), isNull);
    await t.pumpWidget(const SizedBox());
    auth.dispose();
  });

  testWidgets(
    'validation reveals the first error without losing entered data',
    (t) async {
      smallPhone(t);
      final form = GlobalKey<FormState>();
      final scroll = ScrollController();
      final entered = TextEditingController(text: 'قيمة محفوظة');
      final missing = GlobalKey();
      var writes = 0;
      await t.pumpWidget(
        fixture.app(
          Scaffold(
            body: GuidedForm(
              key: form,
              child: SingleChildScrollView(
                controller: scroll,
                child: Column(
                  children: [
                    TextFormField(controller: entered),
                    const SizedBox(height: 800),
                    TextFormField(
                      key: missing,
                      decoration: const InputDecoration(
                        labelText: 'اسم المجموعة',
                      ),
                      validator: (v) => v?.isNotEmpty == true ? null : 'مطلوب',
                    ),
                    const SizedBox(height: 800),
                    TextFormField(validator: (_) => 'مطلوب أيضًا'),
                  ],
                ),
              ),
            ),
            bottomNavigationBar: FilledButton(
              onPressed: () {
                if (form.currentState!.validate()) writes++;
              },
              child: const Text('متابعة'),
            ),
          ),
        ),
      );
      await t.pumpAndSettle();
      await t.tap(find.text('متابعة'));
      await t.pumpAndSettle();
      expect(scroll.offset, greaterThan(300));
      expect(t.getTopLeft(find.byKey(missing)).dy, greaterThanOrEqualTo(0));
      expect(t.getBottomRight(find.byKey(missing)).dy, lessThan(610));
      expect(entered.text, 'قيمة محفوظة');
      expect(writes, 0);
      expect(t.takeException(), isNull);
      await t.pumpWidget(const SizedBox());
      entered.dispose();
      scroll.dispose();
    },
  );

  for (final model in VisualModel.values) {
    testWidgets('error, loading, long title and action fit large text $model', (
      t,
    ) async {
      smallPhone(t);
      var retries = 0;
      await t.pumpWidget(
        frame(
          GuardianPage(
            title: 'مراجعة بيانات اشتراك الأبناء والمرفقات',
            bottom: FilledButton(
              onPressed: () {},
              child: const Text('مراجعة الطلب'),
            ),
            child: PageStatus(
              child: StatusPanel(
                title: 'تعذر تحميل البيانات',
                message: 'راجع الاتصال وحاول تحميل البيانات مرة أخرى.',
                urgent: true,
                actionLabel: 'إعادة المحاولة',
                onAction: () => retries++,
              ),
            ),
          ),
          model,
        ),
      );
      await t.pumpAndSettle();
      await t.ensureVisible(find.text('إعادة المحاولة'));
      await t.tap(find.text('إعادة المحاولة'));
      expect(retries, 1);
      t.view.viewInsets = const FakeViewPadding(bottom: 290);
      await t.pumpAndSettle();
      final action = t.getRect(find.text('مراجعة الطلب'));
      expect(action.bottom, lessThanOrEqualTo(667 - 290));
      expect(action.top, greaterThanOrEqualTo(0));
      expect(t.takeException(), isNull);
      await t.pumpWidget(frame(const Scaffold(body: PageLoading()), model));
      await t.pump();
      expect(find.bySemanticsLabel('جارٍ التحميل'), findsOneWidget);
      expect(t.takeException(), isNull);
      await t.pumpWidget(const SizedBox());
    });
  }

  testWidgets('directory finds Arabic variants and codes then clears search', (
    t,
  ) async {
    await t.pumpWidget(
      fixture.app(
        Scaffold(
          body: SearchableDirectory(
            items: const [
              {'id': '1', 'name': 'أَحْمَد علي', 'code': 'GR01'},
              {'id': '2', 'name': 'سلمى', 'code': 'GR02'},
            ],
            itemBuilder: (_, row) => Text(row['name'] as String),
          ),
        ),
      ),
    );
    await t.pumpAndSettle();
    await t.enterText(find.byType(TextField), 'احمد');
    await t.pump();
    expect(find.text('أَحْمَد علي'), findsOneWidget);
    expect(find.text('سلمى'), findsNothing);
    await t.enterText(find.byType(TextField), 'gr٠٢');
    await t.pump();
    expect(find.text('سلمى'), findsOneWidget);
    expect(find.text('أَحْمَد علي'), findsNothing);
    await t.tap(find.byTooltip('مسح البحث'));
    await t.pump();
    expect(find.text('2 نتيجة'), findsOneWidget);
    expect(t.takeException(), isNull);
    await t.pumpWidget(const SizedBox());
  });

  testWidgets('player search keeps the latest term while a read is pending', (
    t,
  ) async {
    final old = Completer<Object?>();
    final calls = <String>[];
    final auth = await fixture.authenticate('AcademyOwner', (m, p, b, token) {
      expect(m, 'GET');
      final query = Uri.parse(p).queryParameters['search']!;
      calls.add(query);
      if (query == 'قديم') return old.future;
      return Future.value({
        'total': 1,
        'items': [
          {
            'id': query.isEmpty ? 'initial' : query,
            'arabicName': query.isEmpty ? 'البداية' : 'النتيجة $query',
            'enrollmentSummaries': [],
          },
        ],
      });
    });
    await t.pumpWidget(fixture.app(NativePlayerDirectory(auth: auth)));
    await t.pumpAndSettle();
    await t.enterText(find.byType(TextFormField), 'قديم');
    await t.pump(const Duration(milliseconds: 300));
    await t.enterText(find.byType(TextFormField), 'أحدث');
    await t.pump(const Duration(milliseconds: 300));
    await t.enterText(find.byType(TextFormField), 'PL٠١');
    old.complete({
      'total': 1,
      'items': [
        {'id': 'stale', 'arabicName': 'نتيجة قديمة', 'enrollmentSummaries': []},
      ],
    });
    await t.pump();
    await t.pump(const Duration(milliseconds: 300));
    await t.pumpAndSettle();
    expect(calls, ['', 'قديم', 'PL01']);
    expect(find.text('نتيجة قديمة'), findsNothing);
    expect(find.text('النتيجة PL01'), findsOneWidget);
    expect(find.textContaining('السابق'), findsNothing);
    expect(t.takeException(), isNull);
    await t.pumpWidget(const SizedBox());
    auth.dispose();
  });

  testWidgets('pending player read cannot reappear after permission denial', (
    t,
  ) async {
    var calls = 0;
    final auth = await fixture.authenticate('AcademyOwner', (
      m,
      p,
      b,
      token,
    ) async {
      calls++;
      if (calls > 1) {
        throw const ApiFailure(403, 'ليست لديك صلاحية هذا الإجراء.');
      }
      return {
        'total': 1,
        'items': [
          {'id': 'one', 'arabicName': 'بيانات خاصة', 'enrollmentSummaries': []},
        ],
      };
    });
    await t.pumpWidget(fixture.app(NativePlayerDirectory(auth: auth)));
    await t.pumpAndSettle();
    expect(find.text('بيانات خاصة'), findsOneWidget);
    await t.enterText(find.byType(TextFormField), 'بحث آخر');
    await t.pump(const Duration(milliseconds: 300));
    await t.pumpAndSettle();
    expect(find.text('بيانات خاصة'), findsNothing);
    expect(find.text('ليست لديك صلاحية هذا الإجراء.'), findsOneWidget);
    expect(t.takeException(), isNull);
    await t.pumpWidget(const SizedBox());
    auth.dispose();
  });
}
