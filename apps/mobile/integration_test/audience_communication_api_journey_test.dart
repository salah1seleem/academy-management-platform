import 'package:academy_mobile/features/shared/notification_inbox.dart';
import 'package:academy_mobile/core/api/api_client.dart';
import 'package:academy_mobile/core/auth/auth_controller.dart';
import 'package:academy_mobile/core/theme/app_theme.dart';
import 'package:academy_mobile/features/shared/audience_communication.dart';
import 'package:flutter/material.dart';
import 'package:flutter_localizations/flutter_localizations.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:integration_test/integration_test.dart';

import 'startup_motion_journey_test.dart' show StartupJourneyStore;

void main() {
  final binding = IntegrationTestWidgetsFlutterBinding.ensureInitialized();
  testWidgets(
    'native actual imported group preview to stored guardian inbox and reauthorized detail',
    (t) async {
      expect(const bool.fromEnvironment('ISOLATED_DEMO'), true);
      const group = String.fromEnvironment('TEST_GROUP_ID');
      expect(group, isNotEmpty);
      final api = HttpApiTransport(AppConfig('Demo', 'http://127.0.0.1:5278'));
      Future<AuthController> login(String phone) async {
        final auth = AuthController(api, StartupJourneyStore());
        final challenge = await auth.requestOtp(phone);
        await auth.verify(phone, challenge, '246810');
        return auth;
      }

      final owner = await login('01084720253');
      expect(
        owner.session?.selected?.academyId,
        '6fd31a1c-4c74-4726-81fe-a9fce59d0c73',
      );
      Widget frame(Widget child) => MaterialApp(
        locale: const Locale('ar'),
        supportedLocales: const [Locale('ar')],
        localizationsDelegates: GlobalMaterialLocalizations.delegates,
        theme: AcademyTheme.dark(),
        home: child,
      );
      Future<void> wait(Finder target) async {
        for (var i = 0; i < 240 && target.evaluate().isEmpty; i++) {
          await t.pump(const Duration(milliseconds: 100));
        }
        expect(target, findsWidgets);
      }

      await t.pumpWidget(
        frame(AudienceCommunicationPage(auth: owner, id: group)),
      );
      await wait(find.text('معاينة الجمهور والرسالة'));
      final message =
          'تجربة داخلية اصطناعية لإشعار المجموعة ${DateTime.now().millisecondsSinceEpoch}';
      await t.enterText(find.byType(TextFormField).first, message);
      await t.pump();
      await t.ensureVisible(find.text('معاينة الجمهور والرسالة'));
      await t.pumpAndSettle();
      await t.tap(find.text('معاينة الجمهور والرسالة'));
      await wait(find.text('تأكيد الإرسال'));
      await t.ensureVisible(find.text('تأكيد الإرسال'));
      await t.pumpAndSettle();
      await binding.takeScreenshot('phase-communication-real-preview');
      await t.tap(find.text('تأكيد الإرسال'));
      await wait(find.textContaining('تم حفظ'));
      await t.ensureVisible(find.textContaining('تم حفظ'));
      await t.pumpAndSettle();
      await binding.takeScreenshot('phase-communication-real-stored');
      await t.pumpWidget(const SizedBox());
      final guardian = await login('01201105757');
      expect(guardian.session?.selected?.role, 'Guardian');
      final inbox = await guardian.request(
        'GET',
        '/api/v1/notifications',
      ) as Map<String, dynamic>;
      final notices = (inbox['items'] as List)
          .where((n) => n['message'].toString().contains(message))
          .toList();
      expect(notices.length, 1);
      final notice = notices.single as Map;
      expect(notice['targetKind'], 'GroupAnnouncement');
      await t.pumpWidget(frame(NotificationInbox(auth: guardian)));
      await wait(find.byKey(ValueKey('notice-${notice['id']}')));
      final link = find.descendant(
        of: find.byKey(ValueKey('notice-${notice['id']}')),
        matching: find.text('عرض إشعار المجموعة'),
      );
      await t.ensureVisible(link);
      await t.pumpAndSettle();
      await t.tap(link);
      await wait(find.byType(AudienceAnnouncementPage));
      await wait(find.text(message));
      await t.pumpAndSettle();
      await binding.takeScreenshot('phase-communication-real-guardian-detail');
      expect(t.takeException(), isNull);
      await t.pumpWidget(const SizedBox());
      owner.dispose();
      guardian.dispose();
    },
  );
}
