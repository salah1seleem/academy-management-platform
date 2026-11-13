import '../shared/directory_cards.dart';
import '../shared/operation_design.dart';

import 'package:flutter/material.dart';

import '../../core/auth/auth_controller.dart';
import '../../core/api/api_client.dart';
import '../../core/theme/ui_text.dart';
import '../guardian/guardian_ui.dart';
import '../guardian/guardian_payments.dart';
import 'native_operation_form.dart';
import '../shared/staff_capabilities.dart';
import '../shared/cash_collection_choice.dart';

class NativeAdminRenewal extends StatefulWidget {
  const NativeAdminRenewal({super.key, required this.auth, this.enrollmentId});
  final AuthController auth;
  final String? enrollmentId;
  @override
  State<NativeAdminRenewal> createState() => _NativeAdminRenewalState();
}

class _NativeAdminRenewalState
    extends NativeOperationState<NativeAdminRenewal> {
  @override
  AuthController get auth => widget.auth;
  final search = TextEditingController();
  String appliedSearch = '';
  late String? enrollment = widget.enrollmentId;
  String? plan;
  int revision = 0, reviewRevision = 0, quoteRevision = 0;
  String key = commandKey();
  bool created = false, uncertain = false;
  Json? frozen, commandResult;
  Future<void> proceed({
    required bool canReadPayment,
    required String enrollmentId,
    required String planId,
    required Json quote,
  }) => run(() async {
    if (plan != planId) return;
    if (created && commandResult?['paymentId'] != null) {
      await openPage(
        context,
        NativeRenewalPayment(
          auth: auth,
          id: textOf(commandResult!['paymentId']),
        ),
      );
      return;
    }
    frozen ??= {
      'sportEnrollmentId': enrollmentId,
      'subscriptionPlanId': planId,
      'expectedQuoteHash': quote['quoteHash'],
    };
    var written = commandResult != null;
    try {
      if (commandResult == null) {
        final result = await auth.request(
          'POST',
          '/api/v1/subscriptions/admin-renewals',
          body: frozen,
          idempotencyKey: key,
        );
        if (result is! Map ||
            result['paymentId'] is! String ||
            (result['paymentId'] as String).isEmpty ||
            result['renewalId'] is! String ||
            (result['renewalId'] as String).isEmpty) {
          throw const ApiFailure(
            0,
            'لم تتأكد هوية طلب التجديد. أعد إرسال نفس الطلب.',
          );
        }
        commandResult = obj(result);
        written = true;
      }
      if (!canReadPayment) {
        if (current) {
          setState(() {
            created = true;
            uncertain = false;
          });
        }
        return;
      }
      final payment = obj(
        await auth.request(
          'GET',
          '/api/v1/subscriptions/payments/${commandResult!['paymentId']}',
        ),
      );
      if (payment['id'] != commandResult!['paymentId'] ||
          payment['renewalId'] != commandResult!['renewalId'] ||
          payment['sportEnrollmentId'] != frozen!['sportEnrollmentId'] ||
          payment['subscriptionPlanId'] != frozen!['subscriptionPlanId'] ||
          payment['amount'] != quote['finalAmount'] ||
          payment['currency'] != quote['currency'] ||
          payment['isCurrent'] != true ||
          !['Pending', 'Confirmed'].contains(payment['status'])) {
        throw const ApiFailure(
          0,
          'لم تتأكد مطابقة طلب التجديد للاعب والباقة. راجع نفس الطلب.',
        );
      }
      if (mounted && current) {
        setState(() {
          created = true;
          uncertain = false;
        });
        await openPage(
          context,
          NativeRenewalPayment(auth: auth, id: textOf(payment['id'])),
        );
      }
    } catch (e) {
      if (current) {
        setState(() {
          uncertain =
              written || e is! ApiFailure || e.status == 0 || e.status >= 500;
          if (!uncertain) frozen = null;
          if (!written && e is ApiFailure && [404, 409].contains(e.status)) {
            plan = null;
            key = commandKey();
            reviewRevision++;
          }
        });
      }
      rethrow;
    }
  });

  Widget reviewQuote(
    String enrollmentId,
    String planId,
    bool canReadPayment,
  ) => RemoteBody(
    key: ValueKey('renewal-quote-$enrollmentId-$planId-$quoteRevision'),
    auth: auth,
    path:
        '/api/v1/subscriptions/admin-renewals/enrollments/$enrollmentId/quote?${Uri(queryParameters: {'planId': planId}).query}',
    builder: (context, value) {
      final quote = value is Map ? obj(value) : const <String, dynamic>{};
      final hash = quote['quoteHash'];
      final start = receiptDateLabel(context, quote['start']);
      final end = receiptDateLabel(context, quote['end']);
      if (quote['enrollmentId'] != enrollmentId ||
          quote['planId'] != planId ||
          hash is! String ||
          !RegExp(r'^[a-fA-F0-9]{64}$').hasMatch(hash) ||
          start == null ||
          quote['currency'] is! String ||
          !['originalAmount', 'discountAmount', 'finalAmount'].every((field) {
            final amount = quote[field];
            return amount is num && amount.isFinite && amount >= 0;
          }) ||
          ((quote['originalAmount'] as num) * 100).round() -
                  ((quote['discountAmount'] as num) * 100).round() !=
              ((quote['finalAmount'] as num) * 100).round()) {
        return Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            const UiText('تعذر تأكيد تفاصيل عرض التجديد.'),
            OutlinedButton(
              onPressed: () => setState(() => quoteRevision++),
              child: const UiText('إعادة قراءة العرض'),
            ),
          ],
        );
      }
      final currency = quote['currency'];
      return Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          const SizedBox(height: 12),
          const Heading('المبلغ والفترة المتوقعة'),
          UiText('البداية $start'),
          if (end != null) UiText('النهاية $end'),
          if (quote['sessions'] is num) UiText('الحصص ${quote['sessions']}'),
          if ((quote['discountAmount'] as num) > 0) ...[
            UiText('سعر الباقة ${money(quote['originalAmount'], currency)}'),
            UiText('الخصم ${money(quote['discountAmount'], currency)}'),
          ],
          UiText(
            'المبلغ المطلوب ${money(quote['finalAmount'], currency)}',
            style: const TextStyle(fontWeight: FontWeight.w700),
          ),
          const SizedBox(height: 8),
          if (uncertain)
            const UiText(
              'اختيار التسجيل والباقة ثابت حتى التحقق من الطلب نفسه.',
            ),
          FilledButton(
            onPressed: () => proceed(
              canReadPayment: canReadPayment,
              enrollmentId: enrollmentId,
              planId: planId,
              quote: quote,
            ),
            child: UiText(switch ((uncertain, commandResult, created)) {
              (true, null, _) => 'إعادة إرسال طلب التجديد نفسه',
              (true, _, _) => 'تحقّق من طلب التجديد',
              (_, _, true) => 'استكمال طلب التجديد',
              _ => 'إنشاء طلب التجديد والدفع',
            }),
          ),
        ],
      );
    },
  );
  @override
  void dispose() {
    search.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) => page('تجديد اشتراك', [
    OperationSteps(
      labels: const ['اللاعب', 'مراجعة التسجيل'],
      current: enrollment == null ? 0 : 1,
    ),
    if (enrollment == null) ...[
      field('اسم اللاعب أو الكود أو هاتف ولي الأمر', search, required: false),
      OutlinedButton(
        onPressed: () => setState(() {
          appliedSearch = search.text.trim();
          revision++;
        }),
        child: const UiText('بحث'),
      ),
      RemoteBody(
        key: ValueKey(revision),
        auth: auth,
        path:
            '/api/v1/subscriptions/admin-renewals/enrollments?${Uri(queryParameters: {'search': appliedSearch}).query}',
        builder: (context, v) {
          final results = rows(v);
          return Column(
            children: [
              if (results.isEmpty)
                EmptyMessage(
                  appliedSearch.isEmpty
                      ? 'لا توجد تسجيلات متاحة للتجديد.'
                      : 'لا توجد تسجيلات مطابقة. جرّب الاسم أو كود اللاعب.',
                ),
              if (results.length >= 50)
                const UiText(
                  'تُعرض حتى 50 نتيجة. ابحث بالاسم أو كود اللاعب لتضييق النتائج.',
                ),
              for (final e in results)
                PlayerDirectoryCard(
                  auth: auth,
                  player: {
                    ...e,
                    'arabicName': e['player'],
                    'hasActiveSubscription': e['period']?['status'] == 'Active',
                  },
                  enrollments: [
                    {...e, 'subscription': e['period']},
                  ],
                  subscriptionStatusLabel: renewalPeriodLabel(
                    e['period']?['status'],
                  ),
                  openLabel: 'اختيار للتجديد',
                  onOpen: () => setState(() {
                    search.text = appliedSearch;
                    enrollment = textOf(e['id']);
                  }),
                ),
            ],
          );
        },
      ),
    ] else ...[
      if (!uncertain && !created)
        OutlinedButton(
          onPressed: () => setState(() {
            enrollment = null;
            plan = null;
            frozen = null;
            commandResult = null;
            key = commandKey();
            error = null;
          }),
          child: const UiText('تغيير تسجيل اللاعب'),
        ),
      RemoteBody(
        key: ValueKey(reviewRevision),
        auth: auth,
        path: '/api/v1/subscriptions/admin-renewals/enrollments/$enrollment',
        builder: (context, v) {
          final data = obj(v), e = obj(data['enrollment']);
          final open = data['openRenewal'];
          final existing = open is Map ? open['paymentId'] : null;
          final canReadPayment = data['canReadPayment'] != false;
          final currentPeriod = data['current'];
          final plans = rows(data['plans']);
          return GCard(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: [
                Heading(textOf(e['player'])),
                UiText('${e['sport']} · ${e['branch']} · ${e['group']}'),
                UiText(
                  renewalPeriodLabel(
                    currentPeriod is Map ? currentPeriod['status'] : null,
                  ),
                ),
                if (open is Map) ...[
                  UiText(
                    open['kind'] == 'Family'
                        ? 'هذا التسجيل ضمن طلب تجديد أسرة مفتوح.'
                        : 'يوجد طلب تجديد مفتوح لهذا التسجيل.',
                  ),
                  if (existing is String && existing.isNotEmpty)
                    FilledButton(
                      onPressed: () => openPage(
                        context,
                        NativeRenewalPayment(auth: auth, id: existing),
                      ),
                      child: const UiText('فتح طلب الدفع الحالي'),
                    )
                  else if (open['kind'] == 'Single')
                    const UiText('يتابع طلب الدفع موظف لديه صلاحية المدفوعات.'),
                ] else if (created && !canReadPayment) ...[
                  const UiText('تم إنشاء طلب التجديد.'),
                  const UiText('يتابع طلب الدفع موظف لديه صلاحية المدفوعات.'),
                ] else ...[
                  if (plans.isEmpty)
                    const EmptyMessage('لا توجد باقات متاحة لهذا التسجيل.')
                  else ...[
                    choice(
                      'الباقة',
                      plan,
                      plans
                          .map(
                            (p) => {
                              ...p,
                              'arabicName':
                                  '${p['arabicName']} · ${money(p['price'], p['currency'])}',
                            },
                          )
                          .toList(),
                      uncertain || created
                          ? null
                          : (v) => setState(() {
                              plan = v;
                              frozen = null;
                              key = commandKey();
                            }),
                    ),
                    const UiText(
                      'يبدأ التجديد بعد تأكيد الدفع. تُحدد تواريخ الفترة عند التأكيد.',
                    ),
                    if (plan case final selected?
                        when plans.any((item) => item['id'] == selected))
                      reviewQuote(textOf(e['id']), selected, canReadPayment)
                    else
                      const FilledButton(
                        onPressed: null,
                        child: UiText('إنشاء طلب التجديد والدفع'),
                      ),
                  ],
                ],
              ],
            ),
          );
        },
      ),
    ],
  ]);
}

String renewalPeriodLabel(Object? status) => switch (status) {
  'Active' => 'اشتراك فعّال',
  'Expired' => 'اشتراك منتهٍ',
  'Frozen' => 'اشتراك مجمّد',
  'Scheduled' => 'اشتراك لم يبدأ',
  'Cancelled' => 'اشتراك ملغى',
  _ => 'دون اشتراك سابق',
};

class NativeRenewalPayment extends StatefulWidget {
  const NativeRenewalPayment({
    super.key,
    required this.auth,
    required this.id,
    this.readOnly = false,
  });
  final AuthController auth;
  final String id;
  final bool readOnly;
  @override
  State<NativeRenewalPayment> createState() => _NativeRenewalPaymentState();
}

class _NativeRenewalPaymentState
    extends NativeOperationState<NativeRenewalPayment> {
  @override
  AuthController get auth => widget.auth;
  String? method, session;
  Json? collectionIntent, refreshedPayment;
  bool collectionAllowed = false, cashAllowed = false;
  String get paymentPath =>
      '/api/v1/subscriptions/payments/${Uri.encodeComponent(widget.id)}';
  @override
  void initState() {
    super.initState();
    if (widget.readOnly) return;
    StaffCapabilities.load(auth)
        .then((access) {
          if (current) {
            setState(() {
              collectionAllowed =
                  access.allows(
                    'subscription.renew.manage',
                    'subscriptions',
                    'Read',
                  ) &&
                  access.allows('payment.read', 'payments', 'Add', [
                    'workflow.payments.collect',
                  ]);
              cashAllowed = access.explicitAction('finance', 'Add');
            });
          }
        })
        .catchError((Object _) {
          if (current) {
            setState(
              () => error = 'تعذر التحقق من صلاحية التحصيل. أعد فتح الطلب.',
            );
          }
        });
  }

  bool canCollect(Json data) =>
      !widget.readOnly &&
      collectionAllowed &&
      data['canCollect'] == true &&
      data['isCurrent'] == true &&
      data['status'] == 'Pending';

  Future<Json> readDocument(String path) async {
    final value = await auth.request('GET', path);
    if (value is! Json) {
      throw const ApiFailure(0, 'تعذر التحقق من حالة طلب الدفع.');
    }
    return value;
  }

  Future<void> collect(Json data) => run(() async {
    if (collectionIntent != null ||
        !canCollect(data) ||
        method == null ||
        method == 'Cash' && session == null) {
      return;
    }
    final accepted = await showDialog<bool>(
      context: context,
      builder: (c) => AlertDialog(
        title: const UiText('تأكيد التحصيل'),
        content: UiText(
          'تأكيد استلام ${money(data['finalAmount'], data['currency'])} وتفعيل التجديد؟',
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
    if (accepted != true || !current) return;
    final intent = {
      'paymentMethod': method,
      'cashSessionId': method == 'Cash' ? session : null,
      'expectedAmount': textOf(data['finalAmount']),
    };
    collectionIntent = intent;
    await auth.request(
      'POST',
      '/api/v1/manage/payments/${widget.id}/collect',
      body: intent,
    );
    if (!current) return;
    final read = await readDocument(paymentPath);
    await verifyCollection(data, read, intent);
  });

  bool matchesRequest(Json data, Json read) =>
      read['id'] == widget.id &&
      [
        'renewalId',
        'sportEnrollmentId',
        'subscriptionPlanId',
      ].every((key) => read[key] != null && read[key] == data[key]);

  Future<void> checkPayment(Json data) => run(() async {
    final intent = collectionIntent;
    if (intent == null) return;
    final read = await readDocument(paymentPath);
    if (!current) return;
    if (!matchesRequest(data, read) ||
        !storedAmountMatches(read['finalAmount'], read['finalAmount']) ||
        read['currency'] is! String ||
        !const [
          'Created',
          'Pending',
          'Confirmed',
          'Failed',
          'Cancelled',
          'Expired',
        ].contains(read['status'])) {
      throw const ApiFailure(0, 'تعذر التحقق من حالة طلب الدفع.');
    }
    if (read['status'] == 'Confirmed') {
      await verifyCollection(data, read, intent);
      return;
    }
    final amountChanged =
        !storedAmountMatches(read['finalAmount'], intent['expectedAmount']) ||
        read['currency'] != data['currency'];
    setState(() {
      refreshedPayment = read;
      collectionIntent = null;
      if (amountChanged) {
        method = null;
        session = null;
      }
      if (canCollect(read)) {
        message = amountChanged
            ? 'تغيّر المبلغ. راجع الإجمالي واختر طريقة التحصيل.'
            : 'راجع المبلغ قبل إعادة محاولة التحصيل.';
      }
    });
  });

  Future<void> verifyCollection(Json data, Json read, Json intent) async {
    if (!current) return;
    if (!matchesRequest(data, read) ||
        read['status'] != 'Confirmed' ||
        read['receiptId'] == null ||
        read['periodId'] == null ||
        !storedAmountMatches(read['finalAmount'], intent['expectedAmount']) ||
        read['currency'] != data['currency']) {
      throw const ApiFailure(
        0,
        'لم تتأكد قراءة تحصيل هذا التجديد. راجع الطلب نفسه دون تحصيل جديد.',
      );
    }
    final receipt = await readDocument(
      '/api/v1/subscriptions/receipts/${read['receiptId']}',
    );
    if (!current) return;
    final period = await readDocument(
      '/api/v1/subscriptions/periods/${read['periodId']}',
    );
    if (receipt['id'] != read['receiptId'] ||
        receipt['isVoided'] != false ||
        receipt['paymentMethod'] != intent['paymentMethod'] ||
        receipt['currency'] != read['currency'] ||
        !storedAmountMatches(receipt['amount'], read['finalAmount']) ||
        [
          'originalAmount',
          'discountAmount',
          'finalAmount',
        ].any((k) => !storedAmountMatches(receipt[k], read[k])) ||
        period['id'] != read['periodId'] ||
        period['sportEnrollmentId'] != read['sportEnrollmentId'] ||
        period['subscriptionPlanId'] != read['subscriptionPlanId'] ||
        period['receiptId'] != read['receiptId'] ||
        !storedAmountMatches(period['priceSnapshot'], read['finalAmount']) ||
        !['Active', 'Scheduled'].contains(period['status'])) {
      throw const ApiFailure(
        0,
        'لم تتأكد مطابقة الإيصال وفترة التجديد. أعد التحقق من نفس الطلب.',
      );
    }
    if (current) {
      setState(() {
        refreshedPayment = read;
        collectionIntent = null;
        message = 'حُفظ التحصيل وفترة التجديد، وأُعيدت قراءة الإيصال.';
      });
    }
  }

  @override
  Widget build(BuildContext context) => page('طلب التجديد والدفع', [
    RemoteBody(
      auth: auth,
      path: paymentPath,
      builder: (context, v) {
        final data = refreshedPayment ?? obj(v);
        final checking = collectionIntent != null;
        final replacementId = data['currentPaymentId'];
        return GCard(
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              Heading(textOf(data['player'])),
              PaymentRequestReference(order: data),
              UiText('${data['sport']} · ${data['plan']}'),
              UiText(switch ((checking, data['isCurrent'], data['status'])) {
                (true, _, _) => 'نتيجة التحصيل غير مؤكدة',
                (_, _, 'Confirmed') => 'تم التحصيل',
                (_, false, _) => 'تم استبدال طلب الدفع',
                (_, _, 'Pending') => 'بانتظار التحصيل',
                (_, _, 'Failed') => 'تعذر الدفع',
                (_, _, 'Cancelled') => 'أُلغي طلب الدفع',
                (_, _, 'Expired') => 'انتهت صلاحية طلب الدفع',
                (_, _, 'Created') => 'لم يبدأ الدفع',
                _ => 'الطلب غير قابل للتحصيل حاليًا',
              }, style: const TextStyle(fontWeight: FontWeight.w600)),
              OperationPriceSummary(quote: data),
              if (checking) ...[
                const UiText('يعرض هذا الإجراء حالة الطلب فقط.'),
                OutlinedButton(
                  onPressed: () => checkPayment(data),
                  child: const UiText('تحقّق من حالة الدفع'),
                ),
              ] else if (canCollect(data)) ...[
                choice(
                  'طريقة التحصيل',
                  method,
                  [
                    {'id': 'Online', 'arabicName': 'تحويل إلكتروني'},
                    if (cashAllowed && data['canCollectCash'] != false)
                      {'id': 'Cash', 'arabicName': 'نقدي من ورديتي'},
                  ],
                  (v) => setState(() {
                    method = v;
                    session = null;
                  }),
                ),
                if (method == 'Cash')
                  CashCollectionChoice(
                    auth: auth,
                    path: '/api/v1/manage/payments/${widget.id}/cash-options',
                    value: session,
                    onChange: (v) => setState(() => session = v),
                  ),
                FilledButton(
                  onPressed:
                      method == null || method == 'Cash' && session == null
                      ? null
                      : () => collect(data),
                  child: const UiText('تأكيد التحصيل وإصدار الإيصال'),
                ),
              ] else if (!widget.readOnly &&
                  data['isCurrent'] == false &&
                  replacementId is String &&
                  replacementId.isNotEmpty &&
                  replacementId != widget.id) ...[
                OutlinedButton(
                  onPressed: () => openPage(
                    context,
                    NativeRenewalPayment(auth: auth, id: replacementId),
                  ),
                  child: const UiText('فتح طلب الدفع الحالي'),
                ),
              ],
              if (data['receiptId'] != null)
                TextButton(
                  onPressed: () => openPage(
                    context,
                    ReceiptPage(
                      auth: auth,
                      id: textOf(data['receiptId']),
                      staff: true,
                      onBack: widget.readOnly
                          ? () => Navigator.of(context).pop()
                          : null,
                    ),
                  ),
                  child: const UiText('عرض الإيصال'),
                ),
            ],
          ),
        );
      },
    ),
  ], onBack: widget.readOnly ? () => Navigator.of(context).pop() : null);
}
