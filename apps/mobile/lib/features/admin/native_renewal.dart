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
  late String? enrollment = widget.enrollmentId;
  String? plan;
  int revision = 0;
  String key = commandKey();
  bool created = false, uncertain = false;
  Json? frozen, commandResult;
  Future<void> proceed() => run(() async {
    if (enrollment == null || plan == null) return;
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
    frozen ??= {'sportEnrollmentId': enrollment, 'subscriptionPlanId': plan};
    var written = commandResult != null;
    try {
      commandResult ??= obj(
        await auth.request(
          'POST',
          '/api/v1/subscriptions/admin-renewals',
          body: frozen,
          idempotencyKey: key,
        ),
      );
      written = true;
      if (commandResult!['paymentId'] == null ||
          commandResult!['renewalId'] == null) {
        throw const ApiFailure(
          0,
          'لم تتأكد هوية طلب التجديد. أعد التحقق من نفس الطلب.',
        );
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
        });
      }
      rethrow;
    }
  });
  @override
  void dispose() {
    search.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) => page('تجديد اشتراك', [
    OperationSteps(
      labels: const ['اللاعب', 'الباقة والتأكيد'],
      current: enrollment == null ? 0 : 1,
    ),
    if (enrollment == null) ...[
      field('اسم اللاعب أو الكود أو هاتف ولي الأمر', search, required: false),
      OutlinedButton(
        onPressed: () => setState(() => revision++),
        child: const UiText('بحث'),
      ),
      RemoteBody(
        key: ValueKey(revision),
        auth: auth,
        path:
            '/api/v1/subscriptions/admin-renewals/enrollments?${Uri(queryParameters: {'search': search.text.trim()}).query}',
        builder: (context, v) => Column(
          children: [
            for (final e in rows(v))
              PlayerDirectoryCard(
                auth: auth,
                player: {...e, 'arabicName': e['player']},
                enrollments: [
                  {...e, 'subscription': e['period']},
                ],
                openLabel: 'اختيار للتجديد',
                onOpen: () => setState(() => enrollment = textOf(e['id'])),
              ),
          ],
        ),
      ),
    ] else
      RemoteBody(
        auth: auth,
        path: '/api/v1/subscriptions/admin-renewals/enrollments/$enrollment',
        builder: (context, v) {
          final data = obj(v), e = obj(data['enrollment']);
          return GCard(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: [
                Heading(textOf(e['player'])),
                UiText('${e['sport']} · ${e['branch']} · ${e['group']}'),
                UiText('بداية الفترة المتوقعة: ${data['previewStart']}'),
                choice(
                  'الباقة',
                  plan,
                  rows(data['plans'])
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
                  'إنشاء الطلب لا يُحصّل مالًا ولا يمدد الاشتراك قبل تأكيد الدفع.',
                ),
                if (uncertain)
                  const UiText(
                    'لم تتأكد نتيجة إنشاء الطلب. اختيار اللاعب والباقة ثابت حتى قراءة الطلب نفسه.',
                  ),
                FilledButton(
                  onPressed: plan == null ? null : proceed,
                  child: UiText(
                    uncertain
                        ? 'إعادة التحقق من طلب التجديد'
                        : created
                        ? 'استكمال طلب التجديد'
                        : 'إنشاء طلب التجديد والدفع',
                  ),
                ),
              ],
            ),
          );
        },
      ),
  ]);
}

class NativeRenewalPayment extends StatefulWidget {
  const NativeRenewalPayment({super.key, required this.auth, required this.id});
  final AuthController auth;
  final String id;
  @override
  State<NativeRenewalPayment> createState() => _NativeRenewalPaymentState();
}

class _NativeRenewalPaymentState
    extends NativeOperationState<NativeRenewalPayment> {
  @override
  AuthController get auth => widget.auth;
  String? method, session;
  int revision = 0;
  Json? frozen, verified;
  bool uncertain = false;
  bool collectionAllowed = false, cashAllowed = false;
  @override
  void initState() {
    super.initState();
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

  Future<void> collect(Json data) => run(() async {
    if (method == null || method == 'Cash' && session == null) return;
    final accepted = uncertain
        ? true
        : await showDialog<bool>(
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
    frozen ??= {
      'paymentMethod': method,
      'cashSessionId': method == 'Cash' ? session : null,
      'expectedAmount': textOf(data['finalAmount']),
    };
    var written = false;
    try {
      Json? read;
      if (uncertain) {
        final currentPayment = obj(
          await auth.request(
            'GET',
            '/api/v1/subscriptions/payments/${widget.id}',
          ),
        );
        if (currentPayment['status'] == 'Confirmed') read = currentPayment;
      }
      if (read == null) {
        await auth.request(
          'POST',
          '/api/v1/manage/payments/${widget.id}/collect',
          body: frozen,
        );
        written = true;
        read = obj(
          await auth.request(
            'GET',
            '/api/v1/subscriptions/payments/${widget.id}',
          ),
        );
      }
      if (read['id'] != widget.id ||
          read['status'] != 'Confirmed' ||
          read['receiptId'] == null ||
          read['periodId'] == null ||
          read['sportEnrollmentId'] == null ||
          read['subscriptionPlanId'] == null ||
          read['renewalId'] != data['renewalId'] ||
          read['sportEnrollmentId'] != data['sportEnrollmentId'] ||
          read['subscriptionPlanId'] != data['subscriptionPlanId'] ||
          !storedAmountMatches(
            read['finalAmount'],
            frozen!['expectedAmount'],
          ) ||
          read['currency'] != data['currency']) {
        throw const ApiFailure(
          0,
          'لم تتأكد قراءة تحصيل هذا التجديد. راجع الطلب نفسه دون تحصيل جديد.',
        );
      }
      final receipt = obj(
        await auth.request(
          'GET',
          '/api/v1/subscriptions/receipts/${read['receiptId']}',
        ),
      );
      final period = obj(
        await auth.request(
          'GET',
          '/api/v1/subscriptions/periods/${read['periodId']}',
        ),
      );
      if (receipt['id'] != read['receiptId'] ||
          receipt['isVoided'] != false ||
          receipt['paymentMethod'] != frozen!['paymentMethod'] ||
          receipt['currency'] != read['currency'] ||
          !storedAmountMatches(receipt['amount'], read['finalAmount']) ||
          [
            'originalAmount',
            'discountAmount',
            'finalAmount',
          ].any((k) => !storedAmountMatches(receipt[k], read![k])) ||
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
          verified = read;
          uncertain = false;
          message = 'حُفظ التحصيل وفترة التجديد، وأُعيدت قراءة الإيصال.';
        });
      }
    } catch (e) {
      if (current) {
        setState(() {
          uncertain =
              written ||
              uncertain ||
              e is! ApiFailure ||
              e.status == 0 ||
              e.status >= 500;
          if (!uncertain) frozen = null;
        });
      }
      rethrow;
    }
  });
  @override
  Widget build(BuildContext context) => page('طلب التجديد والدفع', [
    RemoteBody(
      key: ValueKey(revision),
      auth: auth,
      path: '/api/v1/subscriptions/payments/${widget.id}',
      builder: (context, v) {
        final data = verified ?? obj(v);
        return GCard(
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              Heading(textOf(data['player'])),
              UiText('${data['sport']} · ${data['plan']}'),
              UiText(
                data['status'] == 'Confirmed'
                    ? 'تم التحصيل'
                    : data['status'] == 'Pending'
                    ? 'بانتظار التحصيل'
                    : 'الطلب غير قابل للتحصيل حاليًا',
              ),
              OperationPriceSummary(quote: data),
              if (collectionAllowed &&
                  data['canCollect'] == true &&
                  data['isCurrent'] == true &&
                  data['status'] == 'Pending') ...[
                choice(
                  'طريقة التحصيل',
                  method,
                  [
                    {'id': 'Online', 'arabicName': 'تحويل إلكتروني تم استلامه'},
                    if (cashAllowed)
                      {'id': 'Cash', 'arabicName': 'نقدي من ورديتي'},
                  ],
                  uncertain
                      ? null
                      : (v) => setState(() {
                          method = v;
                          session = null;
                        }),
                ),
                if (method == 'Cash')
                  CashCollectionChoice(
                    auth: auth,
                    path: '/api/v1/manage/payments/${widget.id}/cash-options',
                    value: session,
                    onChange: uncertain
                        ? null
                        : (v) => setState(() => session = v),
                  ),
                FilledButton(
                  onPressed:
                      method == null || method == 'Cash' && session == null
                      ? null
                      : () => collect(data),
                  child: UiText(
                    uncertain
                        ? 'إعادة التحقق من التحصيل نفسه'
                        : 'تأكيد التحصيل وإصدار الإيصال',
                  ),
                ),
                if (uncertain)
                  const UiText(
                    'الطريقة والمبلغ ثابتان حتى تتأكد قراءة الطلب والإيصال. لا تؤكد استلام مبلغ جديد.',
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
                    ),
                  ),
                  child: const UiText('عرض الإيصال'),
                ),
            ],
          ),
        );
      },
    ),
  ]);
}
