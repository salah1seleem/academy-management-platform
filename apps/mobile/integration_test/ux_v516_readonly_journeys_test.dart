import 'dart:typed_data';

import 'package:academy_mobile/app/app.dart';
import 'package:academy_mobile/core/api/api_client.dart';
import 'package:academy_mobile/core/auth/auth_controller.dart';
import 'package:academy_mobile/core/storage/refresh_store.dart';
import 'package:academy_mobile/core/theme/appearance.dart';
import 'package:academy_mobile/features/admin/native_academy_setup.dart';
import 'package:academy_mobile/features/admin/native_player_workspace.dart';
import 'package:academy_mobile/features/shared/role_navigation.dart';
import 'package:academy_mobile/features/shared/owner_brand_navigation.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:integration_test/integration_test.dart';

class _MemorySession implements RefreshStore {
  String? value;
  @override
  Future<String?> read() async => value;
  @override
  Future<void> write(String token) async => value = token;
  @override
  Future<void> clear() async => value = null;
}

/// This journey may authenticate and read. It cannot change academy records,
/// upload files, send notices, or create a payment/evaluation/attendance record.
class _ReadOnlyLive extends HttpApiTransport {
  _ReadOnlyLive() : super(AppConfig('Demo', 'http://127.0.0.1:5278'));
  final reads = <String>[];
  final blocked = <String>[];
  Never deny(String command) {
    blocked.add(command);
    throw const ApiFailure(403, 'هذه الجولة للقراءة فقط.');
  }

  @override
  Future<Object?> send(
    String method,
    String path, {
    Object? body,
    String? token,
    String? idempotencyKey,
  }) {
    const auth = {
      '/api/v1/mobile/auth/otp/request',
      '/api/v1/mobile/auth/otp/verify',
      '/api/v1/mobile/auth/refresh',
      '/api/v1/mobile/auth/logout',
    };
    if (method != 'GET' && !(method == 'POST' && auth.contains(path))) {
      deny('$method $path');
    }
    if (method == 'GET') reads.add(path);
    return super.send(
      method,
      path,
      body: body,
      token: token,
      idempotencyKey: idempotencyKey,
    );
  }

  @override
  Future<Object?> upload(
    Uint8List bytes,
    String contentType,
    Map<String, String> fields,
    String token,
  ) async => deny('upload');
  @override
  Future<Object?> importWorkbook(
    Uint8List bytes,
    bool commit,
    Map<String, String> fields,
    String token, {
    String? idempotencyKey,
    String? validRowsFromHash,
  }) async => deny('workbook');
}

void main() {
  final binding = IntegrationTestWidgetsFlutterBinding.ensureInitialized();
  testWidgets(
    'V5.16 live role journeys and four models never mutate academy records',
    (t) async {
      expect(const bool.fromEnvironment('READ_ONLY_LIVE_QA'), true);
      expect(
        const String.fromEnvironment('API_BASE_URL'),
        'http://127.0.0.1:5278',
      );
      const roleFilter = String.fromEnvironment('QA_ROLE_FILTER');
      const mainAcademy = '219b8780-a4f6-47aa-a3dc-afc3b58eba90';
      Future<void> wait(Finder finder) async {
        for (var i = 0; i < 240 && finder.evaluate().isEmpty; i++) {
          await t.pump(const Duration(milliseconds: 150));
        }
        expect(finder, findsWidgets);
        await t.pump(const Duration(milliseconds: 500));
      }

      Future<void> tap(Finder finder) async {
        await wait(finder);
        await t.ensureVisible(finder.first);
        await t.pump(const Duration(milliseconds: 300));
        await t.tap(finder.first);
        await t.pump(const Duration(milliseconds: 500));
      }

      Future<void> photo(String name) async {
        // Allow real network portraits to finish, not only virtual frame time.
        await t.runAsync(
          () => Future<void>.delayed(const Duration(seconds: 3)),
        );
        await t.pump(const Duration(seconds: 1));
        await binding.takeScreenshot('phase-ux-v516-$name');
        expect(t.takeException(), isNull);
      }

      var roles = 0, readCount = 0;
      for (final sample in [
        ('AcademyOwner', '01000204010', 'owner', 'اشتراكات الأكاديمية'),
        ('Guardian', '01000003130', 'guardian', 'يوسف خالد'),
        ('Coach', '01000003131', 'coach', 'تدريباتي'),
        ('AcademyAdmin', '01084720301', 'admin', 'يوم الأكاديمية'),
      ]) {
        if (roleFilter.isNotEmpty && roleFilter != sample.$1) continue;
        final transport = _ReadOnlyLive();
        final auth = AuthController(transport, _MemorySession())
          ..restoring = false;
        final appearance =
            Appearance(); // No storage: user's preferences stay intact.
        try {
          final challenge = await auth.requestOtp(sample.$2);
          await auth.verify(sample.$2, challenge, '246810');
          expect(auth.session!.selected!.role, sample.$1);
          if (sample.$1 == 'AcademyAdmin') {
            expect(auth.session!.selected!.academyId, isNot(mainAcademy));
          } else {
            expect(auth.session!.selected!.academyId, mainAcademy);
          }
          appearance.bindIntroUser(auth.session!.userId);
          await appearance.completeRoleIntro(sample.$1, 'football');
          for (final model in VisualModel.values) {
            await t.pumpWidget(const SizedBox());
            await t.pump(const Duration(milliseconds: 200));
            await appearance.update(visualModel: model);
            await t.pumpWidget(
              AcademyApp(
                key: ValueKey('${sample.$3}-${model.name}'),
                auth: auth,
                environment: 'Demo',
                appearance: appearance,
              ),
            );
            await wait(find.byType(NativeRoleTabs));
            await wait(find.textContaining(sample.$4));
            expect(find.byTooltip('حسابي'), findsOneWidget);
            expect(find.byTooltip('التنبيهات'), findsOneWidget);
            await photo('${sample.$3}-${model.name}-home');
            if (sample.$1 == 'AcademyOwner') {
              await tap(find.text('إضافة باقة'));
              await wait(find.byType(NativePlanCreate));
              await tap(find.text('مراجعة الباقة'));
              await wait(find.text('أدخل اسم الباقة'));
              expect(find.text('تأكيد حفظ الباقة'), findsNothing);
              await photo('owner-${model.name}-required-field');
              await tap(find.byType(BackButton));
              await wait(find.byType(NativeRoleTabs));
              expect(
                t
                    .widget<OwnerBrandNavigation>(
                      find.byType(OwnerBrandNavigation),
                    )
                    .selected,
                0,
              );
            }
          }
          // Run actual root navigation after all four appearance captures.
          final destinations = sample.$1 == 'AcademyOwner'
              ? ['العمليات', 'المباريات', 'التقارير', 'المزيد']
              : sample.$1 == 'Guardian'
              ? ['الحصص', 'المباريات', 'التقييمات']
              : sample.$1 == 'Coach'
              ? ['الحضور\nوالتقييم', 'المباريات', 'المزيد']
              : ['العمليات', 'الحضور', 'المزيد'];
          for (final destination in destinations) {
            final bar = find.byType(NavigationBar);
            final target = sample.$1 == 'AcademyOwner'
                ? find.byKey(
                    ValueKey(
                      'owner-nav-${destinations.indexOf(destination) + 1}',
                    ),
                  )
                : find.descendant(of: bar, matching: find.text(destination));
            await tap(target);
            final expectedIndex = destinations.indexOf(destination) + 1;
            if (sample.$1 == 'AcademyOwner') {
              expect(
                t
                    .widget<OwnerBrandNavigation>(
                      find.byType(OwnerBrandNavigation),
                    )
                    .selected,
                expectedIndex,
              );
            } else {
              expect(t.widget<NavigationBar>(bar).selectedIndex, expectedIndex);
            }
            expect(find.byTooltip('حسابي'), findsOneWidget);
            expect(find.byTooltip('التنبيهات'), findsOneWidget);
            await photo(
              '${sample.$3}-premium-tab-${destinations.indexOf(destination) + 1}',
            );
          }
          if (sample.$1 == 'AcademyOwner') {
            await tap(find.byKey(const ValueKey('owner-nav-0')));
            await tap(find.textContaining('قائمة اللاعبين'));
            await tap(find.text('بحث في جميع اللاعبين'));
            await wait(find.byType(NativePlayerDirectory));
            await t.enterText(find.byType(TextFormField).first, 'PL٠١');
            for (
              var i = 0;
              i < 200 && !transport.reads.any((p) => p.contains('search=PL01'));
              i++
            ) {
              await t.pump(const Duration(milliseconds: 150));
            }
            expect(transport.reads.any((p) => p.contains('search=PL01')), true);
            await wait(find.text('PL01'));
            await photo('owner-premium-player-search');
          }
          expect(transport.blocked, isEmpty);
          roles++;
          readCount += transport.reads.length;
          debugPrint(
            'V5.16 native ${sample.$3}: four models and navigation verified; ${transport.reads.length} reads, zero business writes.',
          );
        } finally {
          await t.pumpWidget(const SizedBox());
          await t.pump(const Duration(milliseconds: 200));
          await auth.logout();
          auth.dispose();
          appearance.dispose();
        }
      }
      expect(roles, roleFilter.isEmpty ? 4 : 1);
      binding.reportData = {
        ...?binding.reportData,
        'source': 'V5.16 read-only live role journeys;4 roles;4 models;zero business writes',
        'roles': roles,
        'readRequests': readCount,
      };
    },
    timeout: const Timeout(Duration(minutes: 15)),
  );
}
