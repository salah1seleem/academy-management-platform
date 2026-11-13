import 'package:academy_mobile/features/admin/native_enrollment.dart';
import 'package:academy_mobile/core/api/api_client.dart';
import 'package:academy_mobile/features/admin/native_operation_form.dart';

import 'auth_controller_test.dart' show FakeApi;

import 'package:academy_mobile/features/admin/native_structure_operations.dart';
import 'package:academy_mobile/features/guardian/guardian_ui.dart';
import 'package:flutter/material.dart';
import 'package:flutter/rendering.dart';

import 'dart:io';
import 'dart:ui' as ui;

import 'package:flutter/services.dart';

import 'package:flutter_test/flutter_test.dart';

import 'v2_role_workspaces_test.dart' as fixtures;

Future<void> text(WidgetTester t, String label, String value) async {
  final f = find.widgetWithText(TextFormField, label);
  await t.ensureVisible(f);
  await t.pumpAndSettle();
  await t.enterText(f, value);
  await t.pump();
}

Future<void> choose(WidgetTester t, String label, String option) async {
  final f = find.widgetWithText(DropdownButtonFormField<String>, label);
  await t.ensureVisible(f);
  await t.pumpAndSettle();
  await t.ensureVisible(f);
  await t.pumpAndSettle();
  await t.tap(f);
  await t.pumpAndSettle();
  await t.tap(find.text(option).last);
  await t.pumpAndSettle();
}

Future<void> tap(WidgetTester t, String label) async {
  await t.ensureVisible(find.text(label));
  await t.pumpAndSettle();
  await t.ensureVisible(find.text(label));
  await t.pumpAndSettle();
  await t.tap(find.text(label));
  await t.pumpAndSettle();
}

Future<void> captureIntake(WidgetTester t, String name) async {
  if (!const bool.fromEnvironment('CAPTURE_INTAKE_UI')) return;
  final boundary = t.renderObject<RenderRepaintBoundary>(
    find.byKey(const ValueKey('intake-proof')),
  );
  await t.runAsync(() async {
    final image = await boundary.toImage(pixelRatio: 2);
    final data = await image.toByteData(format: ui.ImageByteFormat.png);
    image.dispose();
    await File('../../tmp/registration-fix-20261027/$name.png')
        .writeAsBytes(data!.buffer.asUint8List());
  });
}

void main() {
  TestWidgetsFlutterBinding.ensureInitialized();
  if (const bool.fromEnvironment('CAPTURE_INTAKE_UI')) {
    setUpAll(() async {
      final arabic = FontLoader('AcademyArabic')
        ..addFont(rootBundle.load('assets/fonts/AcademyArabic-400.ttf'))
        ..addFont(rootBundle.load('assets/fonts/AcademyArabic-700.ttf'));
      final cairo = FontLoader('Cairo')
        ..addFont(rootBundle.load('assets/fonts/Cairo.ttf'));
      final icons = FontLoader('MaterialIcons')
        ..addFont(rootBundle.load('fonts/MaterialIcons-Regular.otf'));
      await Future.wait([arabic.load(), cairo.load(), icons.load()]);
    });
  }

  test(
    'Age confirmation supports bounded, open-ended and unrestricted categories',
    () {
      const group = {'ageCategoryId': 'age'};
      expect(
        registrationAgeMessage(group, [
          {'id': 'age', 'maximumBirthYear': 2018},
        ], '2020-01-01'),
        isNotNull,
      );
      expect(
        registrationAgeMessage(group, [
          {'id': 'age', 'minimumBirthYear': 2014},
        ], '2010-01-01'),
        isNotNull,
      );
      expect(
        registrationAgeMessage(group, [
          {'id': 'age', 'minimumBirthYear': 2014},
        ], '2020-01-01'),
        isNull,
      );
      expect(
        registrationAgeMessage(group, [
          {'id': 'age'},
        ], '2020-01-01'),
        isNull,
      );
    },
  );
  test('Stored amount equality preserves decimal strings and rejects rounding or missing values', () {
    expect(
      storedAmountMatches('9999999999999999.99', '9999999999999999.99'),
      isTrue,
    );
    expect(
      storedAmountMatches('9999999999999999.99', '9999999999999999.98'),
      isFalse,
    );
    expect(storedAmountMatches('10.001', '10.00'), isFalse);
    expect(storedAmountMatches(null, null), isFalse);
    expect(storedAmountMatches(900, '900.00'), isTrue);
  });
  for (final sample in [
    (false, 'success', false),
    (false, 'optional', false),
    (true, 'ageContinue', false),
    (true, 'ageCancel', false),
    (true, 'success', false),
    (false, 'lostCreate', false),
    (false, 'lostRead', false),
    (false, 'wrongReceipt', false),
    (true, 'success', true),
    (true, 'lostRead', true),
    (true, 'wrongWaiver', true),
  ]) {
    final percentage = sample.$1, fault = sample.$2, waived = sample.$3;
    final optional = fault == 'optional', ageCase = fault.startsWith('age');
    testWidgets(
      waived
          ? 'owner confirms a full waiver without collecting or inventing a receipt ($fault)'
          : percentage
          ? 'native percentage discount uses server quote then confirmed audited registration ($fault)'
          : 'native fixed discount invalidates price changes and submits confirmed registration ($fault)',
      (t) async {
        if (const bool.fromEnvironment('CAPTURE_INTAKE_UI')) {
          t.view.physicalSize = const Size(375, 900);
          t.view.devicePixelRatio = 1;
          addTearDown(t.view.reset);
        }
        Json? registration;
        final registrations = <Json>[];
        var failed = false, recover = false;
        final quotes = <Json>[];
        final auth = await fixtures.authenticate(
          optional || ageCase ? 'AcademyAdmin' : 'AcademyOwner',
          (m, p, b, token) async {
            if (p.endsWith('/my-access')) {
              return {
                'workflowActions': [
                  {
                    'key': 'workflow.subscriptions.exceptional-discount',
                    'allowed': true,
                  },
                ],
              };
            }
            if (p.endsWith('/structure/options')) {
              return {
                'branches': [
                  {'id': 'branch', 'arabicName': 'فرع اصطناعي'},
                ],
                'sports': [
                  {'id': 'sport', 'arabicName': 'رياضة تجريبية'},
                ],
                'categories': ageCase
                    ? [
                        {
                          'id': 'age',
                          'minimumBirthYear': 2014,
                          'maximumBirthYear': 2018,
                        },
                      ]
                    : [],
                'groups': [
                  {
                    'ageCategoryId': 'age',
                    'id': 'group',
                    'arabicName': 'مجموعة اختبار',
                    'branchId': 'branch',
                    'sportId': 'sport',
                    'registrationFormat': 'Group',
                  },
                ],
              };
            }
            if (p.endsWith('/subscriptions/plans')) {
              return [
                {
                  'id': 'plan',
                  'sportId': 'sport',
                  'arabicName': 'باقة اختبار',
                  'isActive': true,
                  'allBranches': true,
                  'trainingFormat': 'Group',
                  'price': 1000,
                  'currency': 'EGP',
                },
              ];
            }
            if (p.endsWith('/discount-rules/')) return {'items': []};
            if (p.endsWith('/registration-quote')) {
              quotes.add(obj(b));
              return {
                'originalAmount': '1000.00',
                'discountAmount': waived
                    ? '1000.00'
                    : percentage
                    ? '150.00'
                    : '100.00',
                'finalAmount': waived
                    ? '0.00'
                    : percentage
                    ? '850.00'
                    : '900.00',
                'currency': 'EGP',
              };
            }
            if (p.endsWith('/registrations')) {
              registration = obj(b);
              registrations.add(Map.from(registration!));
              if (fault == 'lostCreate' && !failed) {
                failed = true;
                throw const ApiFailure(0, 'انقطع الاتصال بعد إرسال التسجيل.');
              }
              return {
                'playerId': 'existing-player',
                'sportEnrollmentId': 'enrollment',
                'periodId': 'period',
                'planId': 'plan',
                'paymentId': waived ? null : 'payment',
                'receiptId': waived ? null : 'receipt',
                'waiverId': waived ? 'waiver' : null,
                'subscriptionCreated': true,
                'pricing': {
                  'originalAmount': '1000.00',
                  'discountAmount': waived
                      ? '1000.00'
                      : percentage
                      ? '150.00'
                      : '100.00',
                  'finalAmount': waived
                      ? '0.00'
                      : percentage
                      ? '850.00'
                      : '900.00',
                  'currency': 'EGP',
                },
              };
            }
            if (p.endsWith('/people/players/existing-player')) {
              return {
                'id': 'existing-player',
                'enrollments': [
                  {
                    'id': 'enrollment',
                    'branchId': 'branch',
                    'sportId': 'sport',
                  },
                ],
              };
            }
            if (p.endsWith('/subscriptions/periods/period')) {
              if (fault == 'lostRead' && !failed) {
                failed = true;
                throw const ApiFailure(503, 'تعذر إتمام قراءة السجل.');
              }
              return {
                'id': 'period',
                'sportEnrollmentId': 'enrollment',
                'subscriptionPlanId': 'plan',
                'status': 'Active',
                'priceSnapshot': waived
                    ? 0
                    : percentage
                    ? 850
                    : 900,
                if (waived) 'origin': 'ApprovedWaiver',
                if (waived)
                  'waiver': fault == 'wrongWaiver' && !recover
                      ? {'id': 'different-waiver'}
                      : {'id': 'waiver'},
                'currencySnapshot': 'EGP',
                'receiptId': waived ? null : 'receipt',
              };
            }
            if (p.endsWith('/subscriptions/receipts/receipt')) {
              return {
                'id': 'receipt',
                'isVoided': false,
                'currency': 'EGP',
                'paymentMethod': 'Online',
                'amount': fault == 'wrongReceipt' && !recover
                    ? 901
                    : percentage
                    ? 850
                    : 900,
                'originalAmount': 1000,
                'discountAmount': percentage ? 150 : 100,
                'finalAmount': percentage ? 850 : 900,
              };
            }
            throw StateError('Unexpected $m $p');
          },
        );
        await t.pumpWidget(
          RepaintBoundary(
            key: const ValueKey('intake-proof'),
            child: fixtures.app(
              NativeNewEnrollment(
                auth: auth,
                player: const {
                  'id': 'existing-player',
                  'arabicName': 'طفل اصطناعي',
                  'playerCode': 'TEST-1',
                  'dateOfBirth': '2020-01-01',
                },
              ),
            ),
          ),
        );
        await t.pumpAndSettle();
        if (optional) await captureIntake(t, 'required-fields-widget-preview');
        await text(t, 'اسم ولي الأمر', 'ولي أمر اصطناعي');
        await text(t, 'رقم الهاتف', '01000000000');
        await choose(t, 'الفرع', 'فرع اصطناعي');
        await choose(t, 'الرياضة', 'رياضة تجريبية');
        if (!optional) await choose(t, 'المجموعة', 'مجموعة اختبار');
        await choose(t, 'الباقة', 'باقة اختبار · 1,000 ج.م');
        await tap(t, 'إضافة خصم استثنائي');
        if (percentage) await choose(t, 'نوع الخصم', 'نسبة مئوية');
        await text(
          t,
          'قيمة الخصم',
          waived
              ? '100'
              : percentage
              ? '15'
              : '100',
        );
        if (!optional) await text(t, 'سبب الخصم', 'سبب اختبار اصطناعي');
        if (ageCase) {
          await t.ensureVisible(find.text('حساب المبلغ المستحق'));
          await t.pumpAndSettle();
          await t.tap(find.text('حساب المبلغ المستحق'));
          await t.pump(const Duration(milliseconds: 500));
        } else {
          await tap(t, 'حساب المبلغ المستحق');
        }
        if (ageCase) {
          expect(find.text('تأكيد اختلاف الفئة العمرية'), findsOneWidget);
          await t.pump(const Duration(milliseconds: 500));
          await captureIntake(t, 'age-confirmation-widget-preview');
          await t.tap(
            find.text(fault == 'ageCancel' ? 'إلغاء الطلب' : 'إكمال الطلب'),
          );
          await t.pumpAndSettle();
          if (fault == 'ageCancel') {
            expect(quotes, isEmpty);
            expect(registrations, isEmpty);
            await t.pumpWidget(const SizedBox());
            auth.dispose();
            return;
          }
        }
        expect(quotes.last['exceptionalDiscount'], {
          'type': percentage ? 1 : 2,
          'value': waived
              ? '100'
              : percentage
              ? '15'
              : '100',
          'reason': optional ? null : 'سبب اختبار اصطناعي',
          'replaceExistingDiscount': false,
        });
        if (!percentage) {
          await text(t, 'رقم الهاتف', '01000000001');
          expect(find.text('المبلغ النهائي'), findsNothing);
          await tap(t, 'حساب المبلغ المستحق');
        }
        if (waived) {
          expect(
            find.widgetWithText(
              DropdownButtonFormField<String>,
              'طريقة التحصيل',
            ),
            findsNothing,
          );
          expect(find.text('أؤكد استلام المبلغ النهائي فعليًا'), findsNothing);
          await tap(t, 'راجعت الإعفاء من الرسوم');
          await tap(t, 'اعتماد الإعفاء والتسجيل');
          expect(find.text('تحويل إلكتروني تم استلامه'), findsNothing);
          expect(find.text('لا يوجد تحصيل أو إيصال مالي'), findsOneWidget);
        } else {
          await choose(t, 'طريقة التحصيل', 'تحويل إلكتروني تم استلامه');
          await tap(t, 'أؤكد استلام المبلغ النهائي فعليًا');
          await tap(t, 'مراجعة الاشتراك');
        }
        expect(find.text('المراجعة والتأكيد'), findsOneWidget);
        final finalAction = waived
            ? 'اعتماد الإعفاء والتسجيل'
            : 'تأكيد وإنشاء الاشتراك';
        await t.ensureVisible(find.text(finalAction));
        await t.pumpAndSettle();
        await t.tap(find.text(finalAction));
        await t.pump(const Duration(milliseconds: 500));
        expect(registration, isNull);
        await t.tap(find.text('تأكيد'));
        await t.pumpAndSettle();
        expect(
          registration!['expectedAmount'],
          waived
              ? '0.00'
              : percentage
              ? '850.00'
              : '900.00',
        );
        expect(registration!['existingPlayerId'], 'existing-player');
        expect(quotes.last['existingPlayerId'], registration!['existingPlayerId']);
        expect(registration!['groupId'], optional ? null : 'group');
        expect(registration!['ageOverrideConfirmed'], ageCase);
        expect(registration!['paymentMethod'], 'Online');
        expect(
          registration!['exceptionalDiscount'],
          quotes.last['exceptionalDiscount'],
        );
        if (fault != 'success' && !optional && !ageCase) {
          expect(find.text('تم حفظ الاشتراك'), findsNothing);
          expect(find.byType(TextFormField), findsNothing);
          expect(find.text('تعديل'), findsNothing);
          expect(find.text('المراجعة والتأكيد'), findsOneWidget);
          recover = true;
          await tap(t, 'إعادة التحقق من التسجيل نفسه');
          expect(registrations.length, fault == 'lostCreate' ? 2 : 1);
          if (registrations.length == 2) {
            expect(registrations[1], registrations[0]);
          }
          final transport = auth.transport as FakeApi;
          final keys = [
            for (var i = 0; i < transport.paths.length; i++)
              if (transport.paths[i].endsWith('/registrations'))
                transport.keys[i],
          ];
          expect(keys.every((k) => k == keys.first && k != null), isTrue);
        }
        expect(
          find.text(waived ? 'تم التسجيل واعتماد الإعفاء.' : 'تم حفظ الاشتراك'),
          findsOneWidget,
        );
        expect(find.text('عرض الاشتراك'), findsOneWidget);
        expect(
          find.text('عرض الإيصال'),
          waived ? findsNothing : findsOneWidget,
        );
        if (waived) {
          final transport = auth.transport as FakeApi;
          expect(
            transport.paths.where((p) => p.contains('/receipts/')),
            isEmpty,
          );
          expect(find.text('تم التسجيل واعتماد الإعفاء.'), findsOneWidget);
        }
        await t.pumpWidget(const SizedBox());
        auth.dispose();
      },
    );
  }

  for (final unavailable in [false, true]) {
    testWidgets(
      'New registration clears a stale review and reloads its options ($unavailable)',
      (t) async {
        var listReads = 0, submissions = 0, quotes = 0;
        final auth = await fixtures.authenticate('AcademyOwner', (
          method,
          path,
          body,
          token,
        ) async {
          if (path.endsWith('/structure/options')) {
            return {
              'branches': [
                {'id': 'branch', 'arabicName': 'فرع اصطناعي'},
              ],
              'sports': [
                {'id': 'sport', 'arabicName': 'رياضة تجريبية'},
              ],
              'groups': [],
              'categories': [],
            };
          }
          if (path.endsWith('/discount-rules/')) return {'items': []};
          if (path.endsWith('/subscriptions/plans')) {
            listReads++;
            if (listReads == 2) {
              throw const ApiFailure(503, 'تعذر تحميل الباقات.');
            }
            return [
              {
                'id': 'plan',
                'sportId': 'sport',
                'arabicName': 'باقة اختبار',
                'isActive': !unavailable || listReads == 1,
                'allBranches': true,
                'trainingFormat': 'Group',
                'price': listReads == 1 ? 900 : 950,
                'currency': 'EGP',
              },
              if (listReads > 1)
                {
                  'id': 'other',
                  'sportId': 'sport',
                  'arabicName': 'باقة بديلة',
                  'isActive': true,
                  'allBranches': true,
                  'trainingFormat': 'Group',
                  'price': 950,
                  'currency': 'EGP',
                },
            ];
          }
          if (path.endsWith('/registration-quote')) {
            quotes++;
            return {
              'originalAmount': listReads == 1 ? '900.00' : '950.00',
              'discountAmount': '0.00',
              'finalAmount': listReads == 1 ? '900.00' : '950.00',
              'currency': 'EGP',
            };
          }
          if (path.endsWith('/registrations')) {
            submissions++;
            expect(obj(body)['expectedAmount'], '900.00');
            throw ApiFailure(
              unavailable ? 422 : 409,
              unavailable
                  ? 'الباقة لم تعد متاحة لهذا التسجيل.'
                  : 'تغيّر المبلغ المستحق.',
              code: unavailable
                  ? 'registration_plan_unavailable'
                  : 'registration_price_changed',
            );
          }
          throw StateError('Unexpected $method $path');
        });
        await t.pumpWidget(
          fixtures.app(
            NativeNewEnrollment(
              auth: auth,
              player: const {
                'id': 'existing',
                'arabicName': 'لاعب اصطناعي',
                'dateOfBirth': '2020-01-01',
              },
            ),
          ),
        );
        await t.pumpAndSettle();
        await text(t, 'اسم ولي الأمر', 'ولي أمر اصطناعي');
        await text(t, 'رقم الهاتف', '01000000000');
        await choose(t, 'الفرع', 'فرع اصطناعي');
        await choose(t, 'الرياضة', 'رياضة تجريبية');
        await choose(t, 'الباقة', 'باقة اختبار · 900 ج.م');
        await tap(t, 'حساب المبلغ المستحق');
        await choose(t, 'طريقة التحصيل', 'تحويل إلكتروني تم استلامه');
        await tap(t, 'أؤكد استلام المبلغ النهائي فعليًا');
        await tap(t, 'مراجعة الاشتراك');
        await t.ensureVisible(find.text('تأكيد وإنشاء الاشتراك'));
        await t.pumpAndSettle();
        await t.tap(find.text('تأكيد وإنشاء الاشتراك'));
        await t.pump(const Duration(milliseconds: 500));
        await t.tap(find.text('تأكيد'));
        await t.pumpAndSettle();
        expect(submissions, 1);
        expect(find.text('تأكيد وإنشاء الاشتراك'), findsNothing);
        expect(find.text('المبلغ النهائي'), findsNothing);
        await tap(t, 'إعادة تحميل خيارات الاشتراك');
        expect(listReads, 3);
        expect(find.text('ولي أمر اصطناعي'), findsOneWidget);
        expect(find.text('01000000000'), findsOneWidget);
        if (unavailable) {
          expect(find.text('باقة اختبار · 900 ج.م'), findsNothing);
          await choose(t, 'الباقة', 'باقة بديلة · 950 ج.م');
        } else {
          expect(find.text('باقة اختبار · 950 ج.م'), findsOneWidget);
        }
        await tap(t, 'حساب المبلغ المستحق');
        expect(
          find.widgetWithText(FilledButton, 'مراجعة الاشتراك'),
          findsOneWidget,
        );
        expect(
          t
              .widget<FilledButton>(
                find.widgetWithText(FilledButton, 'مراجعة الاشتراك'),
              )
              .onPressed,
          isNull,
        );
        await tap(t, 'أؤكد استلام المبلغ النهائي فعليًا');
        await tap(t, 'حساب المبلغ المستحق');
        expect(
          t
              .widget<FilledButton>(
                find.widgetWithText(FilledButton, 'مراجعة الاشتراك'),
              )
              .onPressed,
          isNull,
        );
        expect(quotes, 3);
        expect(submissions, 1);
        await t.pumpWidget(const SizedBox());
        auth.dispose();
      },
    );
  }
  testWidgets(
    'coach assignment persists the selected references without a browser',
    (t) async {
      Json? body;
      final auth = await fixtures.authenticate('AcademyOwner', (
        m,
        p,
        b,
        token,
      ) async {
        if (p == '/api/v1/manage/groups/g/coaches/c') {
          return {
            'id': 'assignment',
            'trainingGroupId': 'g',
            'academyMembershipId': 'c',
            'isActive': true,
          };
        }
        if (m == 'GET') {
          return {
            'groups': [
              {'id': 'g', 'arabicName': 'مجموعة تجريبية'},
            ],
            'coaches': [
              {'id': 'c', 'arabicName': 'مدرب اصطناعي'},
            ],
          };
        }
        body = obj(b);
        return 'assignment';
      });
      await t.pumpWidget(fixtures.app(NativeCoachAssignment(auth: auth)));
      await t.pumpAndSettle();
      await choose(t, 'المدرب', 'مدرب اصطناعي');
      await choose(t, 'المجموعة', 'مجموعة تجريبية');
      await tap(t, 'حفظ الإسناد');
      expect(body, {'membershipId': 'c'});
      expect(find.text('تم حفظ الإسناد.'), findsOneWidget);
      await t.pumpWidget(const SizedBox());
      auth.dispose();
    },
  );
}
