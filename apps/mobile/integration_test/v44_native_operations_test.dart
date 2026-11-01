import 'package:academy_mobile/app/app.dart';
import 'package:academy_mobile/core/api/api_client.dart';
import 'package:academy_mobile/core/auth/auth_controller.dart';
import 'package:academy_mobile/core/storage/refresh_store.dart';
import 'package:academy_mobile/core/theme/appearance.dart';
import 'package:academy_mobile/features/guardian/guardian_ui.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:integration_test/integration_test.dart';

void main() {
  final binding = IntegrationTestWidgetsFlutterBinding.ensureInitialized();
  testWidgets('approved native operations persist through the isolated API', (
    t,
  ) async {
    expect(const bool.fromEnvironment('ISOLATED_DEMO'), isTrue);
    const api = String.fromEnvironment('API_BASE_URL');
    expect(api, 'http://127.0.0.1:5278');
    Future<void> capture(String name) async {
      await t.pumpAndSettle();
      await binding.takeScreenshot(name);
    }

    Future<void> waitFor(Finder f) async {
      for (var i = 0; i < 240 && f.evaluate().isEmpty; i++) {
        await t.pump(const Duration(milliseconds: 150));
      }
      expect(f, findsWidgets);
      await t.pump(const Duration(milliseconds: 300));
    }

    Future<void> tap(String label) async {
      final f = find.text(label).last;
      await waitFor(f);
      await t.ensureVisible(f);
      await t.pump(const Duration(milliseconds: 300));
      await t.ensureVisible(f);
      await t.tap(f);
      await t.pump(const Duration(milliseconds: 400));
    }

    Future<void> choose(String label, String option) async {
      final f = find.widgetWithText(DropdownButtonFormField<String>, label);
      await waitFor(f);
      await t.ensureVisible(f);
      await t.pump(const Duration(milliseconds: 300));
      await t.ensureVisible(f);
      await t.tap(f);
      await t.pump(const Duration(milliseconds: 400));
      await tap(option);
    }

    Future<void> field(String label, String value) async {
      final f = find.widgetWithText(TextFormField, label);
      await waitFor(f);
      await t.ensureVisible(f);
      await t.pump(const Duration(milliseconds: 300));
      await t.enterText(f, value);
      await t.pump(const Duration(milliseconds: 200));
    }

    Future<void> back() async {
      await t.tap(find.byTooltip('رجوع').first);
      await t.pump(const Duration(milliseconds: 400));
    }

    final auth = AuthController(
      HttpApiTransport(AppConfig('Demo', api)),
      SecureRefreshStore('v44.5278.approved.operations.owner'),
    );
    await auth.restore();
    final challenge = await auth.requestOtp('01099900010');
    await auth.verify('01099900010', challenge, '246810');
    expect(
      auth.session!.selected!.academyId,
      '30000000-0000-0000-0000-000000000003',
    );
    expect(auth.session!.selected!.role, 'AcademyOwner');
    final appearance = Appearance();
    for (final theme in ['football', 'general']) {
      await appearance.completeRoleIntro('AcademyOwner', theme);
    }
    await t.pumpWidget(
      AcademyApp(auth: auth, environment: 'Demo', appearance: appearance),
    );
    await waitFor(find.text('ملخص الأكاديمية'));
    await tap('العمليات');
    await waitFor(find.text('اشتراك جديد'));
    await capture('phase-approved-native-operations-isolated');
    final options = obj(
      await auth.request('GET', '/api/v1/manage/structure/options'),
    );
    const coachName = 'مدرب قبول التصميم الاصطناعي';
    var coach = rows(options['coaches'])
        .where((c) => c['arabicName'] == coachName)
        .firstOrNull;
    final createdNow = coach == null;
    if (createdNow) {
      await tap('إضافة مدرب');
      await field('الاسم بالعربية', coachName);
      await field('رقم الهاتف', '01099900991');
      await capture('phase-approved-native-create-coach-isolated');
      await tap('حفظ المدرب');
      await waitFor(find.text('حفظ الإسناد'));
      final refreshed = obj(
        await auth.request('GET', '/api/v1/manage/structure/options'),
      );
      coach = rows(refreshed['coaches'])
          .singleWhere((c) => c['arabicName'] == coachName);
    } else {
      await tap('إسناد إلى مجموعة');
      await choose('المدرب', coachName);
    }
    final group = rows(options['groups']).first;
    await choose('المجموعة', textOf(group['arabicName']));
    await tap('حفظ الإسناد');
    await waitFor(find.text('تم حفظ الإسناد.'));
    final coachDetails = obj(
      await auth.request(
        'GET',
        '/api/v1/manage/structure/coaches/${coach['id']}',
      ),
    );
    expect(
      rows(coachDetails['assignments'])
          .where(
            (a) => a['trainingGroupId'] == group['id'] && a['isActive'] == true,
          )
          .length,
      1,
    );
    await capture('phase-approved-native-coach-assignment-isolated');
    await back();
    if (createdNow) await back();
    await tap('تعديل مجموعة');
    await tap(textOf(group['arabicName']));
    await waitFor(find.widgetWithText(TextFormField, 'اسم المجموعة'));
    final original = obj(
      await auth.request(
        'GET',
        '/api/v1/manage/structure/groups/${group['id']}',
      ),
    );
    final revised = '${original['arabicName']} · مراجعة اصطناعية';
    await field('اسم المجموعة', revised);
    await tap('حفظ التعديلات');
    await waitFor(find.text('تم حفظ المجموعة.'));
    expect(
      obj(
        await auth.request(
          'GET',
          '/api/v1/manage/structure/groups/${group['id']}',
        ),
      )['arabicName'],
      revised,
    );
    await field('اسم المجموعة', textOf(original['arabicName']));
    await tap('حفظ التعديلات');
    await t.pump(const Duration(seconds: 1));
    expect(
      obj(
        await auth.request(
          'GET',
          '/api/v1/manage/structure/groups/${group['id']}',
        ),
      )['arabicName'],
      original['arabicName'],
    );
    await capture('phase-approved-native-group-edit-isolated');
    await back();
    await back();
    await tap('اشتراك جديد');
    await waitFor(find.text('حساب المبلغ المستحق'));
    await capture('phase-approved-native-enrollment-form-isolated');
    await back();
    await tap('تجديد اشتراك');
    await waitFor(find.text('بحث'));
    await capture('phase-approved-native-renewal-form-isolated');
    expect(t.takeException(), isNull);
    // A second login/context is never required by the native forms.
    expect(
      obj(await auth.request('GET', '/api/v1/me'))['role'],
      'AcademyOwner',
    );
    await t.pumpWidget(const SizedBox());
    auth.dispose();
    appearance.dispose();
  });
}
