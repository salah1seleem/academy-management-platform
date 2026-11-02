import 'dart:convert';

import 'package:academy_mobile/app/app.dart';
import 'package:academy_mobile/core/api/api_client.dart';
import 'package:academy_mobile/core/auth/auth_controller.dart';
import 'package:academy_mobile/core/storage/refresh_store.dart';
import 'package:academy_mobile/core/theme/appearance.dart';
import 'package:academy_mobile/features/shared/role_navigation.dart';
import 'package:academy_mobile/features/guardian/guardian_payments.dart';
import 'package:academy_mobile/features/guardian/guardian_content.dart';
import 'package:academy_mobile/features/guardian/guardian_profile.dart';
import 'package:academy_mobile/features/guardian/guardian_ui.dart';
import 'package:academy_mobile/features/admin/native_renewal_proof_review.dart';
import 'package:academy_mobile/features/admin/native_enrollment.dart';
import 'package:academy_mobile/features/shared/matches.dart';
import 'package:academy_mobile/features/owner/collection_reminders.dart';
import 'package:academy_mobile/features/owner/native_staff_management.dart';
import 'package:academy_mobile/features/owner/owner_followups.dart';
import 'package:academy_mobile/features/owner/inventory_preview.dart';
import 'package:academy_mobile/features/owner/owner_reports.dart';
import 'package:academy_mobile/features/guardian/coach_feedback.dart';
import 'package:academy_mobile/features/coach/coach_attendance.dart';
import 'package:academy_mobile/features/coach/coach_evaluation.dart';
import 'package:file_picker/file_picker.dart';
import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:integration_test/integration_test.dart';

void mark(String kind, Map<String, Object?> data) => debugPrint(
  'PROMO_${kind.toUpperCase()} ${jsonEncode({'utc': DateTime.now().toUtc().toIso8601String(), ...data})}',
);

class FilmApi extends HttpApiTransport {
  FilmApi() : super(AppConfig('Demo', 'http://127.0.0.1:5289'));
  static String paymentId = const String.fromEnvironment('PROMO_PAYMENT_ID');
  static final writes = <String>[];
  @override
  Future<Object?> send(
    String method,
    String path, {
    Object? body,
    String? token,
    String? idempotencyKey,
  }) async {
    final result = await super.send(
      method,
      path,
      body: body,
      token: token,
      idempotencyKey: idempotencyKey,
    );
    if (method != 'GET' && !path.contains('/auth/')) {
      writes.add('$method $path');
      mark('write', {'path': '$method $path'});
    }
    if (method == 'POST' && path == '/api/v1/guardian/subscriptions/renewals') {
      paymentId = obj(result)['paymentId'] as String;
      mark('data', {'paymentId': paymentId});
    }
    return result;
  }
}

final class FilmProof extends PlatformFile {
  FilmProof(this.bytes);
  final Uint8List bytes;
  @override
  String get name => 'demo-transfer-1000.png';
  @override
  Future<int?> length() async => bytes.length;
  @override
  Future<Uint8List> readAsBytes() async => bytes;
  @override
  dynamic noSuchMethod(Invocation i) => super.noSuchMethod(i);
}

class FilmPicker extends FilePickerPlatform {
  @override
  Future<PlatformFile?> pickFile({
    String? dialogTitle,
    String? initialDirectory,
    FileType type = FileType.any,
    List<String>? allowedExtensions,
    Function(FilePickerStatus)? onFileLoading,
    int compressionQuality = 0,
    AndroidOptions androidOptions = const AndroidOptions(),
    DarwinOptions darwinOptions = const DarwinOptions(),
    WindowsOptions windowsOptions = const WindowsOptions(),
    LinuxOptions linuxOptions = const LinuxOptions(),
    WebOptions webOptions = const WebOptions(),
  }) async => FilmProof(
    (await rootBundle.load('assets/demo/promo-proof.png')).buffer.asUint8List(),
  );
}

class FilmBinding extends IntegrationTestWidgetsFlutterBinding {
  @override
  void handlePointerEvent(PointerEvent event) {
    final before = shouldPropagateDevicePointerEvents;
    shouldPropagateDevicePointerEvents = true;
    try {
      withPointerEventSource(
        TestBindingEventSource.device,
        () => super.handlePointerEvent(event),
      );
    } finally {
      shouldPropagateDevicePointerEvents = before;
    }
  }
}

void main() {
  final binding = FilmBinding();
  testWidgets('V5.20 isolated real HTTP marketing journeys', (t) async {
    expect(const bool.fromEnvironment('ISOLATED_MARKETING_CAPTURE'), true);
    expect(
      const String.fromEnvironment('API_BASE_URL'),
      'http://127.0.0.1:5289',
    );
    const academy = '30000000-0000-0000-0000-000000000003';
    const phase = String.fromEnvironment('PROMO_PHASE');
    final picker = FilePickerPlatform.instance;
    FilePickerPlatform.instance = FilmPicker();
    addTearDown(() => FilePickerPlatform.instance = picker);
    Future<void> pause([int ms = 800]) async {
      for (var i = 0; i < ms; i += 40) {
        await t.runAsync(
          () => Future<void>.delayed(const Duration(milliseconds: 40)),
        );
        await t.pump();
      }
    }

    Future<void> wait(Finder f) async {
      for (var i = 0; i < 300 && f.evaluate().isEmpty; i++) {
        await pause(80);
      }
      expect(f, findsWidgets);
      await pause(350);
    }

    Future<void> tap(Finder f) async {
      await wait(f);
      await t.ensureVisible(f.first);
      await pause(450);
      final p = t.getCenter(f.first);
      mark('tap', {'x': p.dx, 'y': p.dy});
      await t.tap(f.first);
      await pause(1000);
    }

    Future<void> scroll([double px = 420]) async {
      final v = binding.renderViews.first.flutterView;
      final s = v.physicalSize / v.devicePixelRatio;
      final g = await t.startGesture(Offset(s.width * .5, s.height * .77));
      for (var i = 0; i < 16; i++) {
        await g.moveBy(Offset(0, -px / 16));
        await pause(35);
      }
      await g.up();
      await pause(700);
    }

    Future<void> type(Finder f, String value) async {
      await tap(f);
      for (var i = 1; i <= value.length; i++) {
        await t.enterText(f.first, value.substring(0, i));
        await pause(75);
      }
      await pause(600);
    }

    Future<void> snap(String id) async {
      await pause(800);
      await binding.takeScreenshot('phase-promo-$id');
    }

    Future<void> scene(String id, Future<void> Function() action) async {
      mark('scene', {'id': id, 'edge': 'start'});
      await action();
      await pause(1200);
      mark('scene', {'id': id, 'edge': 'end'});
    }

    NavigatorState? navigator;
    Future<AuthController> mount(
      String role,
      String phone,
      VisualModel model,
    ) async {
      await t.pumpWidget(const SizedBox());
      await pause(300);
      final auth = AuthController(
        FilmApi(),
        SecureRefreshStore('academy.marketing.capture-v520.$role.5289'),
      );
      await t.runAsync(auth.restore);
      if (auth.session == null) {
        final c = await t.runAsync(() => auth.requestOtp(phone));
        await t.runAsync(() => auth.verify(phone, c!, '246810'));
      }
      expect(auth.session!.selected!.academyId, academy);
      expect(auth.session!.selected!.role, role);
      final a = Appearance();
      a.bindIntroUser(auth.session!.userId);
      await a.completeRoleIntro(role, 'football');
      await a.update(visualModel: model);
      await t.pumpWidget(
        AcademyApp(
          auth: auth,
          environment: 'Production',
          appearance: a,
          key: UniqueKey(),
        ),
      );
      await wait(find.byType(NativeRoleTabs));
      navigator = Navigator.of(t.element(find.byType(NativeRoleTabs)));
      await pause(3200);
      return auth;
    }

    Future<void> open(Widget page) async {
      navigator!.push(MaterialPageRoute<void>(builder: (_) => page));
      await pause(1800);
    }

    Future<Object> read(AuthController auth, String path) async =>
        (await auth.request('GET', path))!;
    Future<void> home() async {
      navigator!.popUntil((r) => r.isFirst);
      await pause(1500);
    }

    mark('ready', {});
    await pause(9000);
    if (phase.isEmpty || phase == 'owner' || phase == 'final') {
      final auth = await mount(
        'AcademyOwner',
        '01099900010',
        VisualModel.light,
      );
      await scene('owner-home', () async {
        await wait(find.text('اشتراكات الأكاديمية'));
        await snap('owner-home');
        await scroll();
        await pause(1800);
        await snap('owner-collections');
      });
      await home();
      await scene('owner-filters', () async {
        await tap(find.byKey(const ValueKey('subscription-scope-toggle')));
        await tap(find.text('فرع مدينتي').first);
        await pause(1700);
        await snap('owner-branch');
        await tap(find.text('كل الفروع').last);
      });
      await scene('owner-decisions', () async {
        await home();
        for (var i = 0; i < 5; i++) {
          await scroll();
        }
        await snap('owner-decisions');
      });
      await home();
      await scene('owner-retention', () async {
        await open(CollectionReminders(auth: auth, kind: 'Renewal'));
        await pause(2000);
        await snap('owner-retention');
        await tap(find.byType(CheckboxListTile).first);
        await tap(find.textContaining('معاينة').last);
        await pause(1800);
        await snap('owner-reminder-preview');
        await tap(find.textContaining('تأكيد إرسال').last);
        await pause(1400);
      });
      await home();
      await scene('owner-enrollment', () async {
        await open(NativeNewEnrollment(auth: auth));
        await wait(find.text('اسم اللاعب بالعربية'));
        await type(find.byType(TextFormField).first, 'آدم خالد سليم');
        await t.testTextInput.receiveAction(TextInputAction.done);
        await pause(1000);
        await scroll(350);
        await snap('owner-enrollment');
      });
      await home();
      await scene('owner-business', () async {
        await open(
          GuardianPage(
            title: 'المنتجات الرياضية',
            child: gList([OwnerInventoryPreview(auth: auth)]),
          ),
        );
        await pause(2300);
        await snap('owner-inventory');
        final products = rows(
          await t.runAsync(
            () => read(auth, '/api/v1/content/catalog/?active=true'),
          ),
        );
        final product = products.firstWhere(
          (p) => textOf(p['imageReference']).contains('professional-kit-'),
        );
        await open(OwnerInventoryRead(auth: auth, product: product));
        await pause(2300);
        await snap('owner-stock-sizes');
      });
      await home();
      await scene('owner-permissions', () async {
        await open(
          NativeStaffAccessEditor(
            auth: auth,
            membershipId: 'cdfe9094-2771-2730-77e5-2a6228ed9880',
            name: 'مريم عادل',
          ),
        );
        await pause(2500);
        await scroll(370);
        await snap('owner-permissions');
        await scroll(370);
      });
      await home();
      await scene('owner-absence', () async {
        final options = obj(
          await t.runAsync(
            () => read(auth, '/api/v1/manage/owner-followups/options'),
          ),
        );
        for (final candidate in rows(options['candidates']).where(
          (c) =>
              c['condition'] == 'GroupRecordedAbsences' &&
              c['activeFollowUpId'] == null,
        )) {
          await t.runAsync(() async {
            await auth.request(
              'POST',
              '/api/v1/manage/owner-followups',
              body: {
                'trainingGroupId': candidate['source']['groupId'],
                'condition': 'GroupRecordedAbsences',
                'requestId':
                    '20261030-0000-4000-8000-${candidate['source']['groupId'].toString().substring(0, 8)}0000',
                'assignedToMembershipId': auth.session!.selected!.id,
                'dueOn': '2026-10-31',
                'note': 'التواصل مع الأسرة لمعرفة سبب الغياب ومتابعة العودة',
              },
            );
          });
        }
        await open(OwnerFollowUpsPage(auth: auth));
        await pause(2200);
        await snap('owner-followups');
      });
      await home();
      await scene('owner-reports', () async {
        await open(OwnerReports(auth: auth, initialReport: 'operating'));
        await pause(2500);
        await scroll(260);
        await snap('owner-reports');
      });
      await home();
      await scene('owner-matches', () async {
        await home();
        await open(MatchSchedule(auth: auth));
        await pause(2200);
        await scroll(280);
        await snap('owner-matches');
      });
    }
    if (phase.isEmpty ||
        phase == 'guardian' ||
        phase == 'renewal' ||
        phase == 'final') {
      final auth = await mount('Guardian', '01099900100', VisualModel.dark);
      final es = rows(
        await t.runAsync(
          () => read(auth, '/api/v1/guardian/subscriptions/enrollments'),
        ),
      );
      final e = es.firstWhere(
        (e) => textOf(e['player']).startsWith(
          (phase == 'renewal' || phase == 'final') ? 'سليم' : 'يوسف',
        ),
      );
      final enrollment = e['id'] as String;
      final cs = rows(
        await t.runAsync(() => read(auth, '/api/v1/guardian/children')),
      );
      final c = cs.firstWhere(
        (e) => textOf(e['arabicName'] ?? e['name']).startsWith('يوسف'),
        orElse: () => cs.first,
      );
      final player = (c['id'] ?? c['playerId']) as String;
      mark('data', {'playerId': player, 'enrollmentId': enrollment});
      if (phase != 'renewal' && phase != 'final') {
        await scene('guardian-home', () async {
          await pause(2500);
          await snap('guardian-home');
          await scroll(410);
          await pause(2000);
          await snap('guardian-fifa');
          await scroll(370);
        });
        await scene('guardian-profile', () async {
          await home();
          await open(
            ChildProfile(
              auth: auth,
              environment: 'Production',
              playerId: player,
              today: '2026-10-30',
            ),
          );
          await pause(2200);
          await scroll(310);
          await snap('guardian-profile');
        });
        await home();
        await scene('guardian-nutrition', () async {
          await open(
            NutritionLibrary(
              auth: auth,
              playerId: player,
              today: '2026-10-30',
              playerName: 'يوسف',
            ),
          );
          await pause(2200);
          await scroll(380);
          await tap(find.text('الغداء'));
          await pause(1800);
          await snap('guardian-nutrition');
          final meals = rows(
            await t.runAsync(
              () => read(auth, '/api/v1/guardian/nutrition?category=Lunch'),
            ),
          );
          await open(MealDetails(auth: auth, id: meals.first['id']));
          await pause(2000);
          await scroll(230);
          await snap('guardian-meal');
        });
        await home();
        await scene('guardian-matches', () async {
          await open(MatchSchedule(auth: auth, playerId: player));
          await pause(2000);
          await scroll(280);
          await snap('guardian-matches');
        });
        await home();
        await scene('guardian-feedback', () async {
          await open(
            CoachFeedbackPage(
              auth: auth,
              playerId: player,
              initialKind: 'Suggestion',
            ),
          );
          await pause(2200);
          await scroll(320);
          await snap('guardian-feedback');
        });
        await home();
      }
      if (FilmApi.paymentId.isEmpty) {
        await scene('guardian-renewal', () async {
          final ps = rows(
            await t.runAsync(
              () => read(
                auth,
                '/api/v1/guardian/subscriptions/enrollments/$enrollment/plans',
              ),
            ),
          );
          final p = ps.firstWhere((p) => (p['price'] as num) == 1000);
          await open(
            PlanPicker(
              auth: auth,
              environment: 'Production',
              childName: textOf(e['player']),
              enrollmentId: enrollment,
              plans: [p],
            ),
          );
          await pause(1300);
          await tap(find.text('اختيار الباقة'));
          await pause(1400);
          await snap('guardian-review');
          await tap(find.text('متابعة إلى الدفع'));
          await wait(find.text('رفع إيصال الدفع'));
          final payment = obj(
            await t.runAsync(
              () => read(
                auth,
                '/api/v1/guardian/subscriptions/payments/${FilmApi.paymentId}',
              ),
            ),
          );
          expect(payment['amount'], 1000);
          mark('verified', {
            'quotedAmount': payment['amount'],
            'proofAmount': 1000,
          });
          await snap('guardian-transfer');
          await tap(find.text('رفع إيصال الدفع'));
          await wait(find.text('تم إرسال الطلب للمراجعة'));
          await pause(2200);
          await snap('guardian-submitted');
          await tap(find.text('حسنًا'));
        });
      }
    }
    if (phase.isEmpty || phase == 'admin' || phase == 'final') {
      final auth = await mount(
        'AcademyAdmin',
        '01099900011',
        VisualModel.premium,
      );
      await scene('admin-home', () async {
        await pause(2000);
        await snap('admin-home');
        await scroll(300);
      });
      expect(FilmApi.paymentId, isNotEmpty);
      await scene('admin-approval', () async {
        await open(NativeRenewalProofReview(auth: auth, id: FilmApi.paymentId));
        await wait(find.text('مراجعة إثبات التجديد'));
        await pause(2500);
        await snap('admin-proof');
        await scroll(400);
        await tap(find.text('قبول وتفعيل التجديد'));
        await tap(find.text('تأكيد المراجعة'));
        await wait(find.text('عرض الإيصال'));
        await pause(1500);
        await snap('admin-approved');
        await tap(find.text('عرض الإيصال'));
        await pause(2000);
        await snap('admin-receipt');
      });
    }
    if (phase.isEmpty || phase == 'after' || phase == 'final') {
      final auth = await mount('Guardian', '01099900100', VisualModel.dark);
      await scene('guardian-feedback', () async {
        await open(
          CoachFeedbackPage(
            auth: auth,
            playerId: '46eac86f-4f04-d1bb-00c7-b3749e2210a4',
            initialKind: 'Suggestion',
          ),
        );
        await pause(1800);
        await scroll(240);
        await snap('guardian-feedback');
      });
      await home();
      await expectLater(
        auth.request(
          'GET',
          '/api/v1/guardian/children/86b21a34-ea10-5c64-8a35-dc36c557d291',
        ),
        throwsA(
          isA<ApiFailure>().having((e) => e.status, 'status', anyOf(403, 404)),
        ),
      );
      mark('verified', {'guardianOtherAcademyDenied': true});
      await scene('guardian-after', () async {
        await pause(2000);
        await snap('guardian-after');
        await open(
          CheckoutPage(
            auth: auth,
            environment: 'Production',
            id: FilmApi.paymentId,
          ),
        );
        await pause(1800);
        await snap('guardian-confirmed-payment');
      });
      final p = obj(
        await t.runAsync(
          () => read(
            auth,
            '/api/v1/guardian/subscriptions/payments/${FilmApi.paymentId}',
          ),
        ),
      );
      expect(p['status'], 'Confirmed');
      if (phase == 'final') {
        expect(p['amount'], 1000);
      }
      mark('verified', {
        'paymentId': FilmApi.paymentId,
        'status': p['status'],
        'amount': p['amount'],
      });
      final owner = await mount(
        'AcademyOwner',
        '01099900010',
        VisualModel.light,
      );
      await expectLater(
        owner.request(
          'GET',
          '/api/v1/content/catalog/cca86767-444d-504c-be41-c9788c9bef3e/inventory',
        ),
        throwsA(
          isA<ApiFailure>().having((e) => e.status, 'status', anyOf(403, 404)),
        ),
      );
      mark('verified', {'ownerOtherAcademyDenied': true});
      await scene('owner-after', () async {
        await scroll(400);
        await pause(2400);
        await snap('owner-after');
      });
    }
    if (phase.isEmpty || phase == 'coach' || phase == 'final') {
      final coach = await mount('Coach', '01099900020', VisualModel.pitch);
      await scene('coach-home', () async {
        await pause(2000);
        await snap('coach-home');
        await scroll(380);
        await snap('coach-tasks');
      });
      await home();
      await scene('coach-attendance', () async {
        final sessions = rows(
          await t.runAsync(
            () => read(
              coach,
              '/api/v1/attendance/sessions?from=2026-10-30&to=2026-10-30',
            ),
          ),
        );
        expect(sessions, isNotEmpty);
        await open(
          CoachAttendance(auth: coach, sessionId: sessions.first['id']),
        );
        await pause(2000);
        await scroll(240);
        await snap('coach-session');
      });
      await home();
      await scene('coach-evaluation', () async {
        final options = obj(
          await t.runAsync(() => read(coach, '/api/v1/evaluations/options')),
        );
        final enrollments = rows(options['enrollments']);
        expect(enrollments, isNotEmpty);
        await open(CoachEvaluation(auth: coach, enrollment: enrollments.last));
        await wait(find.byType(Slider));
        await pause(700);
        await t.drag(find.byType(Slider).first, const Offset(70, 0));
        await pause(1000);
        await snap('coach-evaluation');
      });
      final admin = await mount(
        'AcademyAdmin',
        '01099900011',
        VisualModel.premium,
      );
      await scene('admin-attendance', () async {
        final sessions = rows(
          await t.runAsync(
            () => read(
              admin,
              '/api/v1/attendance/sessions?from=2026-10-30&to=2026-10-30',
            ),
          ),
        );
        await open(
          CoachAttendance(auth: admin, sessionId: sessions.first['id']),
        );
        await pause(1700);
        await scroll(260);
        if (find.text('حاضر').evaluate().isNotEmpty) {
          await tap(find.text('حاضر').first);
          await tap(find.text('حفظ الحضور'));
          await pause(1000);
        }
        await snap('admin-attendance');
      });
    }
    if (phase == 'sports') {
      final owner = await mount(
        'AcademyOwner',
        '01099900010',
        VisualModel.light,
      );
      await scene('owner-themes', () async {
        await tap(find.byKey(const ValueKey('header-appearance-toggle')));
        await pause(1600);
        await snap('owner-themes');
        await tap(find.text('فاتح').last);
      });
      await scene('owner-matches', () async {
        await open(MatchSchedule(auth: owner));
        await pause(1800);
        await scroll(420);
        await snap('owner-results');
        await scroll(340);
        await snap('owner-results-more');
      });
      final guardian = await mount('Guardian', '01099900100', VisualModel.dark);
      await scene('guardian-matches', () async {
        await open(
          MatchSchedule(
            auth: guardian,
            playerId: '46eac86f-4f04-d1bb-00c7-b3749e2210a4',
          ),
        );
        await pause(1800);
        await scroll(340);
        await snap('guardian-results');
      });
    }
    binding.reportData = {
      ...?binding.reportData,
      'source':
          'V5.20 isolated API 5289, real HTTP; synthetic picker selection',
    };
    mark('complete', {'writes': FilmApi.writes.length});
  }, timeout: const Timeout(Duration(minutes: 22)));
}
