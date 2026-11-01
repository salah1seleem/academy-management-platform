import 'package:academy_mobile/core/api/api_client.dart';
import 'package:academy_mobile/core/auth/auth_controller.dart';
import 'package:academy_mobile/core/storage/refresh_store.dart';
import 'package:academy_mobile/core/theme/app_theme.dart';
import 'package:academy_mobile/features/guardian/guardian_profile.dart';
import 'package:academy_mobile/features/guardian/guardian_profile_header.dart';
import 'package:academy_mobile/features/guardian/guardian_ui.dart';
import 'package:flutter/material.dart';
import 'package:flutter_localizations/flutter_localizations.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:integration_test/integration_test.dart';

class ProfileReadOnlyTransport extends HttpApiTransport {
  ProfileReadOnlyTransport(super.config);
  @override
  Future<Object?> send(
    String method,
    String path, {
    Object? body,
    String? token,
    String? idempotencyKey,
  }) {
    if (method != 'GET' && !path.startsWith('/api/v1/mobile/auth/')) {
      throw StateError('Profile acceptance cannot mutate business data.');
    }
    return super.send(
      method,
      path,
      body: body,
      token: token,
      idempotencyKey: idempotencyKey,
    );
  }
}

void main() {
  final binding = IntegrationTestWidgetsFlutterBinding.ensureInitialized();
  testWidgets(
    'actual guardian profile keeps published performance and linked multi-sport context through all approved tabs',
    (t) async {
      expect(const bool.fromEnvironment('ISOLATED_DEMO'), true);
      const base = String.fromEnvironment('API_BASE_URL');
      expect(base, 'http://127.0.0.1:5278');
      const width = int.fromEnvironment(
        'PROFILE_ACCEPTANCE_WIDTH',
        defaultValue: 440,
      );
      expect([375, 440], contains(width));
      expect((t.view.physicalSize / t.view.devicePixelRatio).width, width);
      Future<void> capture(String name) async {
        bool decoded() {
          final images = find
              .byType(RawImage)
              .evaluate()
              .map((e) => e.widget as RawImage);
          return images.isNotEmpty &&
              images.every((image) => image.image != null);
        }

        for (var i = 0; i < 200 && !decoded(); i++) {
          await t.pump(const Duration(milliseconds: 100));
        }
        expect(
          decoded(),
          true,
          reason: 'Review loaded private images, not pending placeholders',
        );
        await t.pump(const Duration(milliseconds: 100));
        final prefix = width == 440
            ? 'phase-approved-guardian-profile-'
            : 'phase-approved-guardian-profile-375-';
        await binding.takeScreenshot(
          name.replaceFirst('phase-approved-guardian-profile-', prefix),
        );
      }

      Future<void> wait(Finder f) async {
        for (var i = 0; i < 240 && f.evaluate().isEmpty; i++) {
          await t.pump(const Duration(milliseconds: 150));
        }
        expect(f, findsWidgets);
        await t.pumpAndSettle();
      }

      Future<void> reveal(Finder f) async {
        if (f.evaluate().isEmpty) {
          await t.scrollUntilVisible(
            f,
            160,
            scrollable: find.byType(Scrollable).first,
            maxScrolls: 60,
          );
        }
        await t.ensureVisible(f);
        await t.pumpAndSettle();
      }

      Future<void> idle() async {
        for (
          var i = 0;
          i < 240 &&
              find.byType(CircularProgressIndicator).evaluate().isNotEmpty;
          i++
        ) {
          await t.pump(const Duration(milliseconds: 150));
        }
        expect(find.byType(CircularProgressIndicator), findsNothing);
        await t.pumpAndSettle();
        expect(find.text('تعذر عرض البيانات.'), findsNothing);
        expect(
          find.text('تعذر قراءة بيانات الشاشة. حدّثها وحاول مجددًا.'),
          findsNothing,
        );
      }

      String? firstPlayer;
      for (final phone in ['01099900100', '01200000001']) {
        final auth = AuthController(
          ProfileReadOnlyTransport(AppConfig('Demo', base)),
          SecureRefreshStore('v45.5278.guardian-workspace.$phone'),
        );
        await auth.restore();
        if (auth.session == null) {
          final challenge = await auth.requestOtp(phone);
          await auth.verify(phone, challenge, '246810');
        }
        expect(auth.session!.selected!.role, 'Guardian');
        expect(
          auth.session!.selected!.academyId,
          '30000000-0000-0000-0000-000000000003',
        );
        final home = obj(await auth.request('GET', '/api/v1/guardian/home'));
        final children = rows(home['children']);
        final child = phone == '01200000001'
            ? children.firstWhere((c) => rows(c['sports']).length > 1)
            : children.first;
        final id = textOf(child['id']);
        if (firstPlayer == null) {
          firstPlayer = id;
        } else {
          expect(children.any((c) => c['id'] == firstPlayer), false);
          try {
            await auth.request(
              'GET',
              '/api/v1/guardian/children/$firstPlayer/profile',
            );
            fail('Another family profile must be hidden');
          } on ApiFailure catch (e) {
            expect(e.status, 404);
          }
        }
        final data = obj(
          await auth.request('GET', '/api/v1/guardian/children/$id/profile'),
        );
        final enrollments = rows(data['enrollments']);
        expect(enrollments, isNotEmpty);
        final tag = phone == '01099900100' ? 'g100' : 'family1';
        for (final dark in [false, true]) {
          await t.pumpWidget(
            MaterialApp(
              locale: const Locale('ar'),
              supportedLocales: const [Locale('ar')],
              localizationsDelegates: GlobalMaterialLocalizations.delegates,
              theme: dark ? AcademyTheme.dark() : AcademyTheme.light(),
              home: ChildProfile(
                key: ValueKey('$phone-$dark'),
                auth: auth,
                environment: 'Demo',
                playerId: id,
                today: textOf(home['today']),
                initialEnrollmentId: textOf(enrollments.first['id']),
              ),
            ),
          );
          await wait(find.byType(GuardianProfileHeader));
          final header = t.widget<GuardianProfileHeader>(
            find.byType(GuardianProfileHeader),
          );
          expect(header.player['id'], id);
          expect(header.enrollment!['id'], enrollments.first['id']);
          await capture(
            'phase-approved-guardian-profile-$tag-${dark ? 'dark' : 'light'}',
          );
          if (enrollments.length > 1) {
            final next = enrollments[1];
            final key = find.byKey(
              ValueKey('profile-enrollment-${next['id']}'),
            );
            await reveal(key);
            await t.tap(key);
            await t.pumpAndSettle();
            expect(
              t
                  .widget<GuardianProfileHeader>(
                    find.byType(GuardianProfileHeader),
                  )
                  .enrollment!['id'],
              next['id'],
            );
            await capture(
              'phase-approved-guardian-profile-$tag-other-${dark ? 'dark' : 'light'}',
            );
          }
          for (final tab in ['att', 'prog', 'health', 'media', 'more']) {
            final target = find.byKey(ValueKey('profile-tab-$tab'));
            await reveal(target);
            await t.tap(target);
            await t.pumpAndSettle();
            await wait(find.byType(GuardianProfileHeader));
            await idle();
            expect(t.takeException(), isNull);
            if (tab == 'att' || tab == 'health' || tab == 'media') {
              await reveal(find.byKey(ValueKey('profile-tab-$tab')));
              await capture(
                'phase-approved-guardian-profile-$tag-$tab-${dark ? 'dark' : 'light'}',
              );
            }
          }
          expect(t.takeException(), isNull);
        }
        await t.pumpWidget(const SizedBox());
        auth.dispose();
      }
    },
  );
}
