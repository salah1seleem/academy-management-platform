import '../shared/directory_cards.dart';
import '../shared/operation_design.dart';

import 'package:flutter/material.dart';

import '../../core/api/api_client.dart';

import '../../core/auth/auth_controller.dart';
import '../../core/theme/ui_text.dart';
import '../guardian/guardian_ui.dart';
import '../shared/private_media.dart';
import '../shared/experience_ui.dart';
import '../guardian/guardian_payments.dart';
import '../shared/staff_capabilities.dart';
import '../shared/cash_collection_choice.dart';
import 'native_player_workspace.dart';
import 'native_operation_form.dart';

bool registrationMatchesPlan(String? format, String? product) =>
    (format ?? 'Group') == 'Group'
    ? product == null || product == 'Group' || product == 'Season'
    : format == product;

String? registrationAgeMessage(
  Json? group,
  List<Json> categories,
  String? birth,
) {
  final year = DateTime.tryParse(birth ?? '')?.year;
  final category = categories
      .where((c) => c['id'] == group?['ageCategoryId'])
      .firstOrNull;
  final min = category?['minimumBirthYear'] as int?,
      max = category?['maximumBirthYear'] as int?;
  if (year == null ||
      (min == null || year >= min) && (max == null || year <= max)) {
    return null;
  }
  final range = min != null && max != null
      ? 'من $min إلى $max'
      : min != null
      ? 'من $min فما بعد'
      : 'حتى $max';
  return 'المجموعة تقبل مواليد $range. اللاعب خارج الفئة العمرية لهذه المجموعة.';
}

class NativeNewEnrollment extends StatefulWidget {
  const NativeNewEnrollment({super.key, required this.auth, this.player});
  final AuthController auth;
  final Json? player;
  @override
  State<NativeNewEnrollment> createState() => _NativeNewEnrollmentState();
}

class _NativeNewEnrollmentState
    extends NativeOperationState<NativeNewEnrollment> {
  @override
  AuthController get auth => widget.auth;
  final playerName = TextEditingController(),
      guardianName = TextEditingController(),
      phone = TextEditingController(),
      discountValue = TextEditingController(),
      reason = TextEditingController();
  late Json? existingPlayer = widget.player;
  String? birth, branch, sport, groupId, planId, cashSession;
  int gender = 1, discountType = 2;
  bool exceptional = false,
      replaceDiscount = false,
      levelReviewed = false,
      received = false,
      completed = false,
      uncertain = false,
      review = false,
      allowException = false,
      ageOverrideConfirmed = false;
  String? ruleId, method, playerId;
  Json? quote;
  bool get isWaiver =>
      quote != null && num.tryParse('${quote!['finalAmount']}') == 0;
  Json? frozen, recorded;
  final key = commandKey();
  late Future<List<Object?>?> data = load();
  Widget registrationChoice(
    String label,
    String? value,
    List<Json> items,
    ValueChanged<String?>? change, {
    String name = 'arabicName',
    bool required = true,
  }) => choice(
    label,
    value,
    items,
    change,
    name: name,
    required: required,
    markRequired: required,
  );
  Future<List<Object?>?> load() async {
    try {
      return await loadOptions();
    } catch (_) {
      return null;
    }
  }

  Future<List<Object?>> loadOptions() async {
    final result = await Future.wait([
      auth.request('GET', '/api/v1/manage/structure/options'),
      auth.request('GET', '/api/v1/subscriptions/plans'),
      auth.request('GET', '/api/v1/discount-rules/').catchError((Object e) {
        if (e is ApiFailure && e.status == 403) {
          return <String, dynamic>{'items': <Json>[]};
        }
        throw e;
      }),
    ]);
    if (auth.session?.selected?.role == 'AcademyOwner') {
      allowException = true;
    } else {
      final access = obj(await auth.request('GET', '/api/v1/my-access'));
      allowException = rows(access['workflowActions']).any(
        (a) =>
            a['key'] == 'workflow.subscriptions.exceptional-discount' &&
            a['allowed'] == true,
      );
    }
    return result;
  }

  void refreshOptions() {
    data = load();
  }

  @override
  void dispose() {
    for (final c in [playerName, guardianName, phone, discountValue, reason]) {
      c.dispose();
    }
    super.dispose();
  }

  void invalidate() {
    quote = null;
    received = false;
    cashSession = null;
    ageOverrideConfirmed = false;
  }

  Json? get exception => exceptional
      ? {
          'type': discountType,
          'value': discountValue.text.trim(),
          'reason': reason.text.trim().isEmpty ? null : reason.text.trim(),
          'replaceExistingDiscount': replaceDiscount,
        }
      : null;
  Future<void> price() => run(() async {
    if (!form.currentState!.validate()) return;
    if (existingPlayer == null && birth == null) {
      setState(() => error = 'اختر تاريخ ميلاد اللاعب.');
      return;
    }
    final snapshot = await data;
    if (!current || snapshot == null) return;
    final options = obj(snapshot[0]);
    final selectedGroup = rows(options['groups'])
        .where((g) => g['id'] == groupId)
        .firstOrNull;
    final ageMessage = registrationAgeMessage(
      selectedGroup,
      rows(options['categories'] ?? <Json>[]),
      existingPlayer?['dateOfBirth'] as String? ?? birth,
    );
    if (ageMessage != null && !ageOverrideConfirmed) {
      if (!mounted || !current) return;
      final proceed = await showDialog<bool>(
        context: context,
        builder: (c) => AlertDialog(
          title: const UiText('تأكيد اختلاف الفئة العمرية'),
          content: UiText('$ageMessage هل تريد إكمال تسجيله في هذه المجموعة؟'),
          actions: [
            TextButton(
              onPressed: () => Navigator.pop(c, false),
              child: const UiText('إلغاء الطلب'),
            ),
            FilledButton(
              onPressed: () => Navigator.pop(c, true),
              child: const UiText('إكمال الطلب'),
            ),
          ],
        ),
      );
      if (proceed != true || !current) return;
      setState(() => ageOverrideConfirmed = true);
    }

    final discountNumber = num.tryParse(discountValue.text.trim());
    if (exceptional &&
        (discountNumber == null ||
            discountNumber <= 0 ||
            discountType == 1 && discountNumber > 100 ||
            reason.text.trim().length > 500)) {
      setState(
        () => error =
            'أدخل قيمة خصم موجبة؛ النسبة لا تتجاوز 100% والسبب اختياري.',
      );
      return;
    }
    if (planId == null || branch == null || phone.text.trim().isEmpty) {
      setState(() => error = 'اختر الفرع والباقة وأدخل هاتف ولي الأمر.');
      return;
    }
    setState(() {
      quote = null;
      received = false;
      cashSession = null;
    });
    final result = obj(
      await auth.request(
        'POST',
        '/api/v1/people/registration-quote',
        body: {
          'planId': planId,
          'branchId': branch,
          'guardianPhone': phone.text.trim(),
          if (existingPlayer != null) 'existingPlayerId': existingPlayer!['id'],
          'discountRuleId': ruleId,
          'exceptionalDiscount': exception,
        },
      ),
    );
    if (current) {
      setState(() => quote = result);
    }
  });
  Future<void> submit(Json? group) => run(() async {
    if (existingPlayer == null && birth == null) {
      setState(() => error = 'اختر تاريخ ميلاد اللاعب.');
      return;
    }
    if (!form.currentState!.validate() ||
        quote == null ||
        (!isWaiver && method == null)) {
      return;
    }
    final amount = num.tryParse('${quote!['finalAmount']}');
    if (amount == null ||
        !received ||
        method == 'Cash' && amount > 0 && cashSession == null) {
      setState(() => error = 'راجع التحصيل والمبلغ والوردية قبل التأكيد.');
      return;
    }
    final agreed = uncertain
        ? true
        : await showDialog<bool>(
            context: context,
            builder: (c) => AlertDialog(
              title: UiText(
                amount == 0
                    ? 'اعتماد إعفاء من الرسوم'
                    : 'تأكيد التحصيل والتسجيل',
              ),
              content: UiText(
                amount == 0
                    ? 'اعتماد اشتراك بقيمة نهائية صفر دون إنشاء تحصيل أو إيصال مالي؟'
                    : 'تأكيد استلام ${money(amount, quote!['currency'])} وتفعيل اشتراك اللاعب وإصدار الإيصال؟',
              ),
              actions: [
                TextButton(
                  onPressed: () => Navigator.pop(c, false),
                  child: const UiText('رجوع'),
                ),
                FilledButton(
                  onPressed: () => Navigator.pop(c, true),
                  child: const UiText('تأكيد'),
                ),
              ],
            ),
          );
    if (agreed != true || !current) return;
    frozen ??= {
      'existingPlayerId': existingPlayer?['id'],
      'playerArabicName': existingPlayer == null
          ? playerName.text.trim()
          : null,
      'dateOfBirth': existingPlayer == null ? birth : null,
      'gender': gender,
      'guardianName': guardianName.text.trim(),
      'guardianPhone': phone.text.trim(),
      'relationshipType': 'ولي أمر',
      'branchId': branch,
      'sportId': sport,
      'groupId': groupId,
      'ageOverrideConfirmed': ageOverrideConfirmed,
      'planId': planId,
      // The existing registration contract requires a method even for zero.
      // Approved waivers create no payment, collection or receipt server-side.
      'paymentMethod': isWaiver ? 'Online' : method,
      'cashSessionId': !isWaiver && method == 'Cash' ? cashSession : null,
      'discountRuleId': ruleId,
      'expectedAmount': quote!['finalAmount'],
      'trainingLevelId': group != null && group['registrationFormat'] != 'Group'
          ? group['trainingLevelId']
          : null,
      'levelReviewed': levelReviewed,
      'exceptionalDiscount': exception,
    };
    var written = recorded != null;
    try {
      recorded ??= obj(
        await auth.request(
          'POST',
          '/api/v1/people/registrations',
          idempotencyKey: key,
          body: frozen,
        ),
      );
      written = true;
      final result = recorded!;
      if (result['playerId'] == null ||
          result['sportEnrollmentId'] == null ||
          result['periodId'] == null ||
          result['planId'] != frozen!['planId'] ||
          result['subscriptionCreated'] != true) {
        throw const ApiFailure(
          0,
          'لم تتأكد هوية الاشتراك المحفوظ. أعد التحقق من الطلب نفسه.',
        );
      }
      final player = obj(
        await auth.request(
          'GET',
          '/api/v1/people/players/${result['playerId']}',
        ),
      );
      final period = obj(
        await auth.request(
          'GET',
          '/api/v1/subscriptions/periods/${result['periodId']}',
        ),
      );
      final enrollment = rows(player['enrollments'] ?? [])
          .where((e) => e['id'] == result['sportEnrollmentId'])
          .firstOrNull;
      final pricing = obj(result['pricing']);
      if (player['id'] != result['playerId'] ||
          existingPlayer != null &&
              result['playerId'] != existingPlayer!['id'] ||
          enrollment == null ||
          enrollment['branchId'] != frozen!['branchId'] ||
          enrollment['sportId'] != frozen!['sportId'] ||
          period['id'] != result['periodId'] ||
          period['sportEnrollmentId'] != result['sportEnrollmentId'] ||
          period['subscriptionPlanId'] != frozen!['planId'] ||
          !['Active', 'Scheduled'].contains(period['status']) ||
          [
            'originalAmount',
            'discountAmount',
            'finalAmount',
          ].any((k) => !storedAmountMatches(quote![k], pricing[k])) ||
          !storedAmountMatches(
            period['priceSnapshot'],
            pricing['finalAmount'],
          ) ||
          period['currencySnapshot'] != quote!['currency']) {
        throw const ApiFailure(
          0,
          'لم تتأكد مطابقة التسجيل والمبلغ المقروء. احتفظ بنفس الطلب وراجع السجل.',
        );
      }
      if (amount == 0) {
        if (result['waiverId'] == null ||
            result['paymentId'] != null ||
            result['receiptId'] != null ||
            period['origin'] != 'ApprovedWaiver' ||
            period['waiver'] == null ||
            obj(period['waiver'])['id'] != result['waiverId']) {
          throw const ApiFailure(
            0,
            'لم تتأكد قراءة الإعفاء المعتمد. لم يُعلن اكتمال التسجيل.',
          );
        }
      } else {
        if (result['paymentId'] == null ||
            result['receiptId'] == null ||
            period['receiptId'] != result['receiptId']) {
          throw const ApiFailure(0, 'لم تتأكد قراءة إيصال هذا الاشتراك.');
        }
        final receipt = obj(
          await auth.request(
            'GET',
            '/api/v1/subscriptions/receipts/${result['receiptId']}',
          ),
        );
        if (receipt['id'] != result['receiptId'] ||
            receipt['isVoided'] != false ||
            receipt['currency'] != quote!['currency'] ||
            receipt['paymentMethod'] != frozen!['paymentMethod'] ||
            !storedAmountMatches(receipt['amount'], quote!['finalAmount']) ||
            [
              'originalAmount',
              'discountAmount',
              'finalAmount',
            ].any((k) => !storedAmountMatches(receipt[k], quote![k]))) {
          throw const ApiFailure(
            0,
            'لم تتأكد مطابقة الإيصال للمبلغ والتحصيل. أعد التحقق من نفس الطلب.',
          );
        }
      }
      if (current) {
        setState(() {
          completed = true;
          uncertain = false;
          playerId = textOf(result['playerId']);
        });
      }
    } catch (e) {
      if (current) {
        final reviewExpired =
            e is ApiFailure &&
            (e.code == 'registration_price_changed' ||
                e.code == 'registration_plan_unavailable');
        setState(() {
          uncertain =
              written ||
              (!reviewExpired &&
                  (uncertain ||
                      e is! ApiFailure ||
                      [0, 408, 429, 499].contains(e.status) ||
                      e.status >= 500));
          if (!uncertain) {
            frozen = null;
            if (reviewExpired) {
              if (e.code == 'registration_plan_unavailable') planId = null;
              review = false;
              invalidate();
              refreshOptions();
            }
          }
        });
      }
      rethrow;
    }
  });
  @override
  Widget build(BuildContext context) => page('اشتراك جديد', [
    if (completed)
      GCard(
        child: Column(
          children: [
            const Icon(Icons.check_circle_outline),
            UiText(
              isWaiver ? 'تم التسجيل واعتماد الإعفاء.' : 'تم حفظ الاشتراك',
              style: Theme.of(context).textTheme.titleMedium,
            ),
            if (recorded?['receiptId'] != null)
              TextButton(
                onPressed: () => openPage(
                  context,
                  ReceiptPage(
                    auth: auth,
                    id: textOf(recorded!['receiptId']),
                    staff: true,
                  ),
                ),
                child: const UiText('عرض الإيصال'),
              ),
            TextButton(
              onPressed: () async {
                final access = await StaffCapabilities.load(auth);
                if (context.mounted && current) {
                  await openPage(
                    context,
                    NativeSubscriptionPeriod(
                      auth: auth,
                      id: textOf(recorded!['periodId']),
                      access: access,
                    ),
                  );
                }
              },
              child: const UiText('عرض الاشتراك'),
            ),
            TextButton(
              onPressed: () => run(() async {
                final photo = await pickAndUpload(auth, {
                  'purpose': 'Photo',
                  'playerId': playerId!,
                }, imageOnly: true);
                if (current && photo != null) {
                  final savedPhoto = obj(
                    await auth.request(
                      'GET',
                      '/api/v1/people/players/$playerId',
                    ),
                  );
                  if (savedPhoto['photoReference'] != photo['url']) {
                    throw const ApiFailure(
                      0,
                      'لم تتأكد قراءة صورة اللاعب المحفوظة.',
                    );
                  }
                  if (!current) return;
                  setState(() => message = 'تم حفظ صورة اللاعب.');
                }
              }),
              child: const UiText('إضافة صورة اللاعب'),
            ),
          ],
        ),
      )
    else
      FutureBuilder(
        future: data,
        builder: (context, s) {
          if (s.connectionState == ConnectionState.done && !s.hasData) {
            return Column(
              children: [
                const EmptyMessage('تعذر تحميل خيارات الاشتراك.'),
                OutlinedButton(
                  onPressed: () => setState(refreshOptions),
                  child: const UiText('إعادة تحميل خيارات الاشتراك'),
                ),
              ],
            );
          }
          if (s.connectionState != ConnectionState.done || !s.hasData) {
            return const Center(child: CircularProgressIndicator());
          }
          final options = obj(s.data![0]), plans = rows(s.data![1]);
          final groups = rows(options['groups'])
              .where((g) => g['branchId'] == branch && g['sportId'] == sport)
              .toList();
          final chosenGroup = groups
              .where((g) => g['id'] == groupId)
              .firstOrNull;
          final availablePlans = plans
              .where(
                (p) =>
                    p['isActive'] == true &&
                    (p['serviceKind'] == null ||
                        p['serviceKind'] == 'Training') &&
                    p['sportId'] == sport &&
                    (p['allBranches'] != false ||
                        (p['branchIds'] as List? ?? []).contains(branch)) &&
                    (registrationMatchesPlan(
                      chosenGroup?['registrationFormat'] as String?,
                      p['trainingFormat'] as String?,
                    )),
              )
              .toList();
          final rules = rows(obj(s.data![2])['items'])
              .where(
                (r) => r['isActive'] == true && r['isSiblingOffer'] != true,
              )
              .toList();
          if (review) {
            String name(List<Json> items, Object? id) => textOf(
              items.where((i) => i['id'] == id).firstOrNull?['arabicName'],
            );
            return Column(
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: [
                const OperationSteps(
                  labels: ['البيانات', 'المراجعة والتأكيد'],
                  current: 1,
                ),
                const SizedBox(height: 12),
                GCard(
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.stretch,
                    children: [
                      _EnrollmentReviewRow(
                        'اللاعب',
                        existingPlayer == null
                            ? playerName.text.trim()
                            : textOf(existingPlayer!['arabicName']),
                      ),
                      _EnrollmentReviewRow(
                        'ولي الأمر',
                        guardianName.text.trim(),
                      ),
                      _EnrollmentReviewRow('الهاتف', phone.text.trim()),
                      _EnrollmentReviewRow(
                        'الفرع',
                        name(rows(options['branches']), branch),
                      ),
                      _EnrollmentReviewRow(
                        'الرياضة',
                        name(rows(options['sports']), sport),
                      ),
                      _EnrollmentReviewRow(
                        'المجموعة',
                        groupId == null
                            ? 'في انتظار المجموعة'
                            : name(groups, groupId),
                      ),
                      _EnrollmentReviewRow('الباقة', name(plans, planId)),
                      if (exceptional) ...[
                        _EnrollmentReviewRow(
                          'الخصم الاستثنائي',
                          discountType == 1
                              ? '${discountValue.text.trim()}%'
                              : money(
                                  discountValue.text.trim(),
                                  quote!['currency'],
                                ),
                        ),
                        _EnrollmentReviewRow(
                          'سبب الخصم',
                          reason.text.trim().isEmpty
                              ? 'لم يُضف سبب'
                              : reason.text.trim(),
                        ),
                      ],
                      const Divider(),
                      OperationPriceSummary(quote: quote!),
                      _EnrollmentReviewRow(
                        isWaiver ? 'إعفاء من الرسوم' : 'طريقة التحصيل',
                        isWaiver
                            ? 'لا يوجد تحصيل أو إيصال مالي'
                            : method == 'Cash'
                            ? 'نقدي'
                            : 'تحويل إلكتروني تم استلامه',
                      ),
                      if (exceptional)
                        const Padding(
                          padding: EdgeInsets.symmetric(vertical: 12),
                          child: UiText(
                            'خصم لهذا الاشتراك فقط: لا يغيّر سعر الباقة، ويُحفظ باسم منفذه وسببه.',
                            style: TextStyle(fontSize: 12),
                          ),
                        ),
                    ],
                  ),
                ),
                if (!uncertain) ...[
                  OutlinedButton(
                    onPressed: () => setState(() => review = false),
                    child: const UiText('تعديل'),
                  ),
                  FilledButton(
                    onPressed: () => submit(chosenGroup),
                    child: UiText(
                      num.tryParse('${quote!['finalAmount']}') == 0
                          ? 'اعتماد الإعفاء والتسجيل'
                          : 'تأكيد وإنشاء الاشتراك',
                    ),
                  ),
                ] else ...[
                  const UiText(
                    'لم تتأكد النتيجة. التفاصيل ثابتة حتى قراءة التسجيل نفسه.',
                  ),
                  FilledButton(
                    onPressed: () => submit(chosenGroup),
                    child: const UiText('إعادة التحقق من التسجيل نفسه'),
                  ),
                ],
              ],
            );
          }
          return Column(
            children: [
              const Align(
                alignment: AlignmentDirectional.centerStart,
                child: OperationSteps(
                  labels: ['البيانات', 'المراجعة والتأكيد'],
                  current: 0,
                ),
              ),
              AbsorbPointer(
                absorbing: uncertain,
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.stretch,
                  children: [
                    GCard(
                      child: Column(
                        crossAxisAlignment: CrossAxisAlignment.stretch,
                        children: [
                          const Heading('اللاعب'),
                          OutlinedButton(
                            onPressed: () => openPage(
                              context,
                              NativePlayerPicker(
                                auth: auth,
                                onChoose: (p) {
                                  if (current) {
                                    setState(() {
                                      existingPlayer = p;
                                      invalidate();
                                    });
                                  }
                                },
                              ),
                            ),
                            child: const UiText('اختيار لاعب مسجل'),
                          ),
                          const SizedBox(height: 14),
                          if (existingPlayer == null) ...[
                            field('اسم اللاعب بالعربية', playerName),
                            OutlinedButton(
                              onPressed: () async {
                                final date = await showDatePicker(
                                  context: context,
                                  initialDate: DateTime(2016, 1, 1),
                                  firstDate: DateTime(1950),
                                  lastDate: DateTime.now(),
                                );
                                if (current && date != null) {
                                  setState(() {
                                    birth = isoDay(date);
                                    invalidate();
                                  });
                                }
                              },
                              child: RequiredFieldLabel(
                                birth == null
                                    ? 'تاريخ الميلاد'
                                    : 'تاريخ الميلاد · ${day(birth)}',
                              ),
                            ),
                            const SizedBox(height: 16),
                            registrationChoice('النوع', '$gender', [
                              {'id': '1', 'arabicName': 'ذكر'},
                              {'id': '2', 'arabicName': 'أنثى'},
                            ], (v) => setState(() => gender = int.parse(v!))),
                          ] else
                            ListTile(
                              title: UiText(
                                textOf(existingPlayer!['arabicName']),
                              ),
                              subtitle: UiText(
                                textOf(existingPlayer!['playerCode']),
                              ),
                              trailing: IconButton(
                                tooltip: 'اختيار لاعب آخر',
                                onPressed: () => setState(() {
                                  existingPlayer = null;
                                  invalidate();
                                }),
                                icon: const Icon(Icons.close),
                              ),
                            ),
                          const Heading('ولي الأمر'),
                          field(
                            'اسم ولي الأمر',
                            guardianName,
                            markRequired: true,
                          ),
                          TextFormField(
                            controller: phone,
                            keyboardType: TextInputType.phone,
                            decoration: const InputDecoration(
                              label: RequiredFieldLabel('رقم الهاتف'),
                            ),
                            validator: (v) => v == null || v.trim().isEmpty
                                ? 'أدخل هاتف ولي الأمر'
                                : null,
                            onChanged: (_) => setState(invalidate),
                          ),
                        ],
                      ),
                    ),
                    OperationSection(
                      title: 'التدريب والباقة',
                      icon: Icons.sports_soccer_outlined,
                      children: [
                        registrationChoice(
                          'الفرع',
                          branch,
                          rows(options['branches']),
                          (v) => setState(() {
                            branch = v;
                            groupId = null;
                            planId = null;
                            cashSession = null;
                            invalidate();
                          }),
                        ),
                        registrationChoice(
                          'الرياضة',
                          sport,
                          rows(options['sports']),
                          (v) => setState(() {
                            sport = v;
                            groupId = null;
                            planId = null;
                            cashSession = null;
                            invalidate();
                          }),
                        ),
                        registrationChoice(
                          'المجموعة',
                          groupId,
                          [
                            {'id': '', 'arabicName': 'تحديد المجموعة لاحقًا'},
                            ...groups,
                          ],
                          (v) => setState(() {
                            groupId = v == '' ? null : v;
                            planId = null;
                            levelReviewed = false;
                            invalidate();
                          }),
                          required: false,
                        ),
                        if (chosenGroup != null &&
                            chosenGroup['registrationFormat'] != 'Group')
                          CheckboxListTile(
                            title: UiText(
                              'راجعت مستوى اللاعب: ${chosenGroup['trainingLevel'] ?? 'غير محدد'}',
                            ),
                            value: levelReviewed,
                            onChanged: (v) =>
                                setState(() => levelReviewed = v == true),
                          ),
                        registrationChoice(
                          'الباقة',
                          planId,
                          availablePlans
                              .map(
                                (p) => {
                                  ...p,
                                  'arabicName':
                                      '${p['arabicName']} · ${money(p['price'], p['currency'])}',
                                },
                              )
                              .toList(),
                          (v) => setState(() {
                            planId = v;
                            cashSession = null;
                            invalidate();
                          }),
                        ),
                        registrationChoice(
                          'عرض دائم للاعب',
                          ruleId,
                          [
                            {'id': '', 'arabicName': 'بدون عرض إضافي'},
                            ...rules,
                          ],
                          (v) => setState(() {
                            ruleId = v == '' ? null : v;
                            invalidate();
                          }),
                          required: false,
                        ),
                      ],
                    ),
                    if (allowException)
                      GCard(
                        child: Column(
                          children: [
                            SwitchListTile(
                              title: const UiText('إضافة خصم استثنائي'),
                              value: exceptional,
                              onChanged: (v) => setState(() {
                                exceptional = v;
                                invalidate();
                              }),
                            ),
                            if (exceptional) ...[
                              registrationChoice(
                                'نوع الخصم',
                                '$discountType',
                                [
                                  {'id': '2', 'arabicName': 'قيمة ثابتة'},
                                  {'id': '1', 'arabicName': 'نسبة مئوية'},
                                ],
                                (v) => setState(() {
                                  discountType = int.parse(v!);
                                  invalidate();
                                }),
                              ),
                              TextFormField(
                                controller: discountValue,
                                keyboardType:
                                    const TextInputType.numberWithOptions(
                                      decimal: true,
                                    ),
                                decoration: const InputDecoration(
                                  label: RequiredFieldLabel('قيمة الخصم'),
                                ),
                                onChanged: (_) => setState(invalidate),
                              ),
                              TextFormField(
                                controller: reason,
                                maxLines: 2,
                                decoration: const InputDecoration(
                                  labelText: 'سبب الخصم',
                                  hintText: 'اختياري',
                                ),
                                onChanged: (_) => setState(invalidate),
                              ),
                              CheckboxListTile(
                                title: const UiText(
                                  'استبدال أي خصم قائم بعد المراجعة',
                                ),
                                value: replaceDiscount,
                                onChanged: (v) => setState(() {
                                  replaceDiscount = v == true;
                                  invalidate();
                                }),
                              ),
                            ],
                          ],
                        ),
                      ),
                    OutlinedButton(
                      onPressed: price,
                      child: const UiText('حساب المبلغ المستحق'),
                    ),
                    if (quote != null)
                      GCard(
                        child: Column(
                          children: [
                            OperationPriceSummary(quote: quote!),
                            if (quote!['discountReason'] != null)
                              UiText(textOf(quote!['discountReason'])),
                            if (isWaiver)
                              const UiText('لا يوجد تحصيل أو إيصال مالي')
                            else
                              registrationChoice(
                                'طريقة التحصيل',
                                method,
                                [
                                  {'id': 'Cash', 'arabicName': 'نقدي'},
                                  {
                                    'id': 'Online',
                                    'arabicName': 'تحويل إلكتروني تم استلامه',
                                  },
                                ],
                                (v) => setState(() {
                                  method = v;
                                  received = false;
                                  cashSession = null;
                                }),
                              ),
                            if (method == 'Cash' &&
                                planId != null &&
                                branch != null &&
                                num.tryParse('${quote!['finalAmount']}') != 0)
                              NativeRegistrationCashChoice(
                                key: ValueKey('$branch:$planId'),
                                auth: auth,
                                branch: branch!,
                                plan: planId!,
                                value: cashSession,
                                onChange: (v) =>
                                    setState(() => cashSession = v),
                              ),
                            CheckboxListTile(
                              title: UiText(
                                num.tryParse('${quote!['finalAmount']}') == 0
                                    ? 'راجعت الإعفاء من الرسوم'
                                    : 'أؤكد استلام المبلغ النهائي فعليًا',
                              ),
                              value: received,
                              onChanged: (v) =>
                                  setState(() => received = v == true),
                            ),
                            FilledButton(
                              onPressed: received
                                  ? () {
                                      if (existingPlayer == null &&
                                          birth == null) {
                                        setState(
                                          () => error =
                                              'اختر تاريخ ميلاد اللاعب.',
                                        );
                                      } else if (form.currentState!
                                              .validate() &&
                                          planId != null &&
                                          (isWaiver || method != null) &&
                                          (method != 'Cash' ||
                                              num.tryParse(
                                                    '${quote!['finalAmount']}',
                                                  ) ==
                                                  0 ||
                                              cashSession != null)) {
                                        FocusManager.instance.primaryFocus
                                            ?.unfocus();
                                        setState(() {
                                          review = true;
                                          error = null;
                                        });
                                      } else {
                                        setState(
                                          () => error = 'راجع بيانات اللاعب والباقة وطريقة التحصيل.',
                                        );
                                      }
                                    }
                                  : null,
                              child: UiText(
                                num.tryParse('${quote!['finalAmount']}') == 0
                                    ? 'اعتماد الإعفاء والتسجيل'
                                    : 'مراجعة الاشتراك',
                              ),
                            ),
                          ],
                        ),
                      ),
                  ],
                ),
              ),
              if (uncertain) ...[
                const UiText(
                  'لم تتأكد نتيجة التسجيل. التفاصيل ثابتة؛ أعد التحقق من نفس الطلب دون تسجيل لاعب أو تحصيل جديد.',
                ),
                FilledButton(
                  onPressed: () => submit(chosenGroup),
                  child: const UiText('إعادة التحقق من التسجيل نفسه'),
                ),
              ],
            ],
          );
        },
      ),
  ]);
}

class _EnrollmentReviewRow extends StatelessWidget {
  const _EnrollmentReviewRow(this.label, this.value);
  final String label, value;
  @override
  Widget build(BuildContext context) => Padding(
    padding: const EdgeInsets.symmetric(vertical: 8),
    child: Row(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Expanded(
          child: UiText(
            label,
            style: TextStyle(
              fontSize: 12,
              color: Theme.of(context).colorScheme.onSurfaceVariant,
            ),
          ),
        ),
        const SizedBox(width: 12),
        Expanded(
          flex: 2,
          child: UiText(
            value,
            style: const TextStyle(fontSize: 13, fontWeight: FontWeight.w600),
          ),
        ),
      ],
    ),
  );
}

class NativeRegistrationCashChoice extends StatelessWidget {
  const NativeRegistrationCashChoice({
    super.key,
    required this.auth,
    required this.branch,
    required this.plan,
    required this.value,
    required this.onChange,
  });
  final AuthController auth;
  final String branch, plan;
  final String? value;
  final ValueChanged<String?> onChange;
  @override
  Widget build(BuildContext context) => CashCollectionChoice(
    auth: auth,
    path:
        '/api/v1/people/registration-cash-options?${Uri(queryParameters: {'branchId': branch, 'planId': plan}).query}',
    value: value,
    onChange: onChange,
  );
}

class NativePlayerPicker extends StatefulWidget {
  const NativePlayerPicker({
    super.key,
    required this.auth,
    required this.onChoose,
  });
  final AuthController auth;
  final ValueChanged<Json> onChoose;
  @override
  State<NativePlayerPicker> createState() => _NativePlayerPickerState();
}

class _NativePlayerPickerState
    extends NativeOperationState<NativePlayerPicker> {
  @override
  AuthController get auth => widget.auth;
  final search = TextEditingController();
  int pageIndex = 1, revision = 0;
  @override
  void dispose() {
    search.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) => page('اختيار اللاعب', [
    field('الاسم أو الكود', search, required: false),
    OutlinedButton(
      onPressed: () => setState(() {
        pageIndex = 1;
        revision++;
      }),
      child: const UiText('بحث'),
    ),
    RemoteBody(
      key: ValueKey('$revision:$pageIndex'),
      auth: auth,
      path:
          '/api/v1/people/players?${Uri(queryParameters: {'search': search.text.trim(), 'page': '$pageIndex', 'pageSize': '25'}).query}',
      builder: (context, v) {
        final data = obj(v);
        return Column(
          children: [
            for (final p in rows(data['items']))
              PlayerDirectoryCard(
                auth: auth,
                player: p,
                enrollments: rows(p['enrollmentSummaries'] ?? []),
                openLabel: 'اختيار اللاعب',
                onOpen: () {
                  widget.onChoose(p);
                  Navigator.pop(context);
                },
              ),
            const SizedBox.shrink(),
          ],
        );
      },
    ),
  ]);
}
