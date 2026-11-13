import 'package:flutter/material.dart';

import '../../core/api/api_client.dart';
import '../../core/auth/auth_controller.dart';
import '../../core/theme/appearance.dart' show localized;
import '../../core/theme/ui_text.dart';
import '../guardian/guardian_payments.dart';
import '../guardian/guardian_ui.dart';
import '../shared/private_media.dart';
import '../shared/staff_capabilities.dart';
import 'native_operation_form.dart';

/// Reviews an existing transfer proof. Collection and activation stay on the server.
class NativeRenewalProofReview extends StatefulWidget {
  const NativeRenewalProofReview({
    super.key,
    required this.auth,
    required this.id,
    this.fromPaymentQueue = false,
  });
  final AuthController auth;
  final String id;
  final bool fromPaymentQueue;
  @override
  State<NativeRenewalProofReview> createState() =>
      _NativeRenewalProofReviewState();
}

class _NativeRenewalProofReviewState
    extends NativeOperationState<NativeRenewalProofReview> {
  @override
  AuthController get auth => widget.auth;
  final reason = TextEditingController();
  final reasonField = GlobalKey();
  final reasonFocus = FocusNode();
  double keyboardInset = 0;
  Json? payment, proof, original, sourceProof, intent;
  List<Json> groups = [];
  String? group;
  bool acknowledged = false, canReview = false;
  bool? proofImageAvailable;
  int imageAttempt = 0;
  String get paymentPath => '/api/v1/subscriptions/payments/${widget.id}';
  String get root => '/api/v1/manage/payments/${widget.id}';
  @override
  void initState() {
    super.initState();
    load();
  }

  @override
  void didChangeDependencies() {
    super.didChangeDependencies();
    final inset = MediaQuery.viewInsetsOf(context).bottom;
    final increase = inset - keyboardInset;
    keyboardInset = inset;
    if (increase <= 0 || !reasonFocus.hasFocus) return;
    WidgetsBinding.instance.addPostFrameCallback((_) {
      if (!current || !reasonFocus.hasFocus) return;
      final field = reasonField.currentContext;
      if (field != null && field.findRenderObject()?.attached == true) {
        Scrollable.ensureVisible(
          field,
          alignmentPolicy: ScrollPositionAlignmentPolicy.keepVisibleAtEnd,
        );
      } else if (operationScroll.hasClients) {
        final position = operationScroll.position;
        position.jumpTo(
          (position.pixels + increase).clamp(0.0, position.maxScrollExtent),
        );
      }
    });
  }

  @override
  void dispose() {
    reasonFocus.dispose();
    reason.dispose();
    super.dispose();
  }

  Future<Json> readPayment() async {
    final value = obj(await auth.request('GET', paymentPath));
    if (value['id'] != widget.id) {
      throw const ApiFailure(
        0,
        'لم تتأكد هوية طلب التجديد. أعد فتحه من القائمة.',
      );
    }
    return value;
  }

  Future<Json> readProof() async {
    final evidence = obj(await auth.request('GET', '$root/proof'));
    if (evidence['id'] == null ||
        !isPrivateAsset(evidence['url']) ||
        evidence['url'] != "/api/v1/assets/${evidence['assetId']}") {
      throw const ApiFailure(
        0,
        'تعذر التحقق من ملف إثبات التحويل. أعد تحميل الطلب.',
      );
    }
    return evidence;
  }

  Future<void> load() => run(() async {
    try {
      final rights = await StaffCapabilities.load(auth);
      if (!rights.allows('subscription.read', 'subscriptions', 'Read') ||
          !rights.allows(
            'subscription.renew.manage',
            'subscriptions',
            'Read',
          ) ||
          !rights.allows('payment.read', 'payments', 'Read')) {
        throw const ApiFailure(403, 'ليست لديك صلاحية عرض تفاصيل هذا التجديد.');
      }
      final value = await readPayment();
      final evidence = await readProof();
      final mayReview =
          value['canReviewProof'] != false &&
          value['isCurrent'] == true &&
          value['status'] == 'Pending' &&
          evidence['status'] == 'Pending' &&
          rights.allows('payment.read', 'payments', 'Edit', [
            'workflow.payments.collect',
          ]);
      final options = mayReview
          ? obj(await auth.request('GET', '/api/v1/manage/structure/options'))
          : <String, dynamic>{};
      if (current) {
        setState(() {
          payment = value;
          proof = evidence;
          proofImageAvailable = null;
          imageAttempt++;
          group = value['groupId'] as String?;
          groups = rows(options['groups'] ?? const [])
              .where(
                (g) =>
                    g['branchId'] == value['branchId'] &&
                    g['sportId'] == value['sportId'] &&
                    (group == null || g['id'] == group),
              )
              .toList();
          canReview = mayReview;
        });
      }
    } on ApiFailure catch (e) {
      hideDenied(e);
      rethrow;
    }
  });
  void hideDenied(ApiFailure e) {
    if (current && [401, 403, 404].contains(e.status)) {
      setState(() {
        payment = null;
        proof = null;
        proofImageAvailable = null;
        canReview = false;
        groups = [];
      });
    }
  }

  Future<void> decide(bool accept) async {
    if (!current ||
        busy ||
        intent != null ||
        !canReview ||
        proofImageAvailable != true ||
        payment?['isCurrent'] != true ||
        payment?['status'] != 'Pending' ||
        proof?['status'] != 'Pending' ||
        !isPrivateAsset(proof?['url'])) {
      return;
    }
    if (accept && !groups.any((g) => g['id'] == group)) return;
    if (!form.currentState!.validate()) return;
    final source = Map<String, dynamic>.from(payment!);
    final evidence = Map<String, dynamic>.from(proof!);
    final note = reason.text.trim();
    final decision = {
      'accept': accept,
      'groupId': accept ? group : null,
      'reason': note.isEmpty ? null : note,
      'expectedProofAssetId': evidence['assetId'],
    };
    final confirmed = await showDialog<bool>(
      context: context,
      builder: (c) => AlertDialog(
        title: UiText(accept ? 'قبول إثبات التجديد' : 'رفض إثبات التجديد'),
        content: UiText(
          accept
              ? 'أكد استلام ${money(source['finalAmount'], source['currency'])}. سيُسجّل التحصيل وتصدر فترة التجديد.'
              : 'سيُحفظ الرفض وملاحظتك للأسرة. لا يعني الرفض رد تحويل مالي.',
        ),
        actions: [
          TextButton(
            onPressed: () => Navigator.pop(c, false),
            child: const UiText('رجوع'),
          ),
          FilledButton(
            onPressed: () => Navigator.pop(c, true),
            child: const UiText('تأكيد المراجعة'),
          ),
        ],
      ),
    );
    if (!current || confirmed != true || proofImageAvailable != true) return;
    setState(() {
      original = source;
      sourceProof = evidence;
      intent = decision;
    });
    await finish();
  }

  Future<void> finish() => run(() async {
    try {
      if (!acknowledged) {
        try {
          await auth.request('POST', '$root/review', body: intent);
          acknowledged = true;
        } on ApiFailure catch (e) {
          if (e.status >= 400 && e.status < 500 && e.status != 408) {
            intent = null;
            original = null;
            sourceProof = null;
            if (e.status == 409 && current) {
              reason.clear();
              setState(() {
                payment = null;
                proof = null;
                proofImageAvailable = null;
                canReview = false;
                groups = [];
              });
              throw const ApiFailure(
                409,
                'تغيّر طلب التجديد أو إثباته. أعد تحميل الطلب وراجعه قبل تأكيد القرار.',
              );
            }
          }
          rethrow;
        }
      }
      final value = await readPayment();
      final evidence = await readProof();
      final accept = intent!['accept'] == true;
      final same = [
        'renewalId',
        'sportEnrollmentId',
        'subscriptionPlanId',
        'branchId',
        'sportId',
        'currency',
      ].every((k) => value[k] == original![k]);
      final amounts = [
        'amount',
        'originalAmount',
        'discountAmount',
        'finalAmount',
      ].every((k) => storedAmountMatches(value[k], original![k]));
      if (!same ||
          !amounts ||
          evidence['id'] != sourceProof!['id'] ||
          evidence['assetId'] != intent!['expectedProofAssetId'] ||
          evidence['url'] != sourceProof!['url'] ||
          evidence['status'] != (accept ? 'Accepted' : 'Rejected') ||
          evidence['reviewReason'] != intent!['reason'] ||
          value['status'] != (accept ? 'Confirmed' : 'Cancelled') ||
          (accept
              ? value['groupId'] != intent!['groupId'] ||
                    value['receiptId'] == null ||
                    value['periodId'] == null
              : value['receiptId'] != null || value['periodId'] != null)) {
        throw const ApiFailure(
          0,
          'لم تتأكد مطابقة قرار التجديد ومصدره. أعد التحقق من الطلب نفسه.',
        );
      }
      if (accept) {
        final receipt = obj(
          await auth.request(
            'GET',
            '/api/v1/subscriptions/receipts/${value['receiptId']}',
          ),
        );
        final period = obj(
          await auth.request(
            'GET',
            '/api/v1/subscriptions/periods/${value['periodId']}',
          ),
        );
        if (receipt['id'] != value['receiptId'] ||
            receipt['isVoided'] != false ||
            receipt['paymentMethod'] != 'Online' ||
            receipt['currency'] != value['currency'] ||
            !storedAmountMatches(receipt['amount'], value['finalAmount']) ||
            [
              'originalAmount',
              'discountAmount',
              'finalAmount',
            ].any((k) => !storedAmountMatches(receipt[k], value[k])) ||
            period['id'] != value['periodId'] ||
            period['receiptId'] != value['receiptId'] ||
            period['sportEnrollmentId'] != value['sportEnrollmentId'] ||
            period['subscriptionPlanId'] != value['subscriptionPlanId'] ||
            !storedAmountMatches(
              period['priceSnapshot'],
              value['finalAmount'],
            ) ||
            !['Active', 'Scheduled'].contains(period['status'])) {
          throw const ApiFailure(
            0,
            'لم تتأكد مطابقة الإيصال وفترة التجديد. أعد التحقق من الطلب نفسه.',
          );
        }
      }
      if (current) {
        setState(() {
          payment = value;
          proof = evidence;
          intent = null;
          original = null;
          sourceProof = null;
          acknowledged = false;
          message = accept
              ? 'تم قبول التجديد وإصدار الإيصال.'
              : 'تم رفض إثبات التحويل.';
        });
      }
    } on ApiFailure catch (e) {
      hideDenied(e);
      rethrow;
    }
  });
  void enlargeProof() {
    if (proofImageAvailable != true || !isPrivateAsset(proof?['url'])) return;
    showDialog<void>(
      context: context,
      builder: (c) => Dialog.fullscreen(
        child: SafeArea(
          child: Column(
            children: [
              Padding(
                padding: const EdgeInsets.symmetric(
                  horizontal: 16,
                  vertical: 8,
                ),
                child: Row(
                  children: [
                    const Expanded(child: Heading('إثبات التحويل')),
                    IconButton(
                      tooltip: 'إغلاق',
                      onPressed: () => Navigator.pop(c),
                      icon: const Icon(Icons.close_rounded),
                    ),
                  ],
                ),
              ),
              Expanded(
                child: LayoutBuilder(
                  builder: (_, size) => InteractiveViewer(
                    maxScale: 5,
                    child: PrivateImage(
                      auth: auth,
                      path: textOf(proof!['url']),
                      height: size.maxHeight,
                      fit: BoxFit.contain,
                    ),
                  ),
                ),
              ),
            ],
          ),
        ),
      ),
    );
  }

  @override
  Widget build(BuildContext context) {
    final imagePath = textOf(proof?['url']);
    final attempt = imageAttempt;
    final title = payment != null && !canReview
        ? 'إثبات التجديد'
        : 'مراجعة إثبات التجديد';
    return page(title, [
      if (widget.fromPaymentQueue)
        Align(
          alignment: AlignmentDirectional.centerStart,
          child: TextButton.icon(
            onPressed: () => Navigator.of(context).pop(),
            icon: const Icon(Icons.format_list_bulleted_rounded),
            label: UiText(
              localized(
                context,
                'العودة لقائمة طلبات الدفع',
                'Back to payment requests',
              ),
            ),
          ),
        ),
      if (payment == null && !busy)
        OutlinedButton(
          onPressed: load,
          child: const UiText('إعادة تحميل الطلب'),
        ),
      if (payment != null) ...[
        Heading(textOf(payment!['player'])),
        PaymentRequestReference(order: payment!),
        UiText('${payment!['sport']} · ${payment!['plan']}'),
        StatusPill(payment!['status']),
        GCard(child: OperationPriceSummary(quote: payment!)),
        if (isPrivateAsset(proof?['url']))
          GCard(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: [
                Row(
                  children: [
                    const Expanded(
                      child: UiText(
                        'إثبات التحويل',
                        style: TextStyle(fontWeight: FontWeight.w700),
                      ),
                    ),
                    if (proofImageAvailable == true)
                      TextButton.icon(
                        onPressed: enlargeProof,
                        icon: const Icon(Icons.zoom_in_rounded),
                        label: const UiText('تكبير'),
                      ),
                  ],
                ),
                const SizedBox(height: 8),
                GestureDetector(
                  onTap: proofImageAvailable == true ? enlargeProof : null,
                  child: PrivateImage(
                    key: ValueKey((imagePath, attempt)),
                    auth: auth,
                    path: imagePath,
                    height: proofImageAvailable == false ? 120 : 300,
                    fit: BoxFit.contain,
                    onAvailabilityChanged: (available) {
                      if (current &&
                          imageAttempt == attempt &&
                          proof?['url'] == imagePath &&
                          proofImageAvailable != available) {
                        setState(() => proofImageAvailable = available);
                      }
                    },
                    fallback: Column(
                      mainAxisAlignment: MainAxisAlignment.center,
                      children: [
                        const UiText('تعذر فتح صورة الإثبات.'),
                        TextButton(
                          onPressed: () => setState(() {
                            proofImageAvailable = null;
                            imageAttempt++;
                          }),
                          child: const UiText('إعادة تحميل الصورة'),
                        ),
                      ],
                    ),
                  ),
                ),
              ],
            ),
          ),
        if (proof?['reviewReason'] != null)
          UiText('ملاحظة المراجعة: ${proof!['reviewReason']}'),
        if (intent != null) ...[
          const UiText('القرار والمجموعة والمبلغ ثابتة حتى التأكد من الحفظ.'),
          FilledButton(
            onPressed: finish,
            child: UiText(
              acknowledged
                  ? 'إعادة التحقق من الطلب نفسه'
                  : 'استكمال مراجعة الطلب نفسه',
            ),
          ),
        ] else if (canReview &&
            payment!['isCurrent'] == true &&
            payment!['status'] == 'Pending' &&
            proof?['status'] == 'Pending' &&
            isPrivateAsset(proof?['url'])) ...[
          if (payment!['groupId'] != null)
            UiText(
              groups.isEmpty
                  ? 'المجموعة الحالية غير متاحة للمراجعة.'
                  : 'المجموعة الحالية · ${groups.first['arabicName']}',
            )
          else
            choice(
              'مجموعة اللاعب عند القبول',
              group,
              groups,
              (v) => setState(() => group = v),
              required: false,
            ),
          if (proofImageAvailable == true) ...[
            field(
              'ملاحظة لولي الأمر',
              reason,
              fieldKey: reasonField,
              focusNode: reasonFocus,
              required: false,
              maxLines: 2,
              validate: (note) =>
                  note.length > 500 ? 'اكتب ملاحظة من 500 حرف أو أقل.' : null,
            ),
            FilledButton(
              onPressed: !groups.any((g) => g['id'] == group)
                  ? null
                  : () => decide(true),
              child: const UiText('قبول وتفعيل التجديد'),
            ),
            OutlinedButton(
              onPressed: () => decide(false),
              child: const UiText('رفض الإثبات'),
            ),
          ],
        ],
        if (payment!['receiptId'] != null)
          TextButton(
            onPressed: () => openPage(
              context,
              ReceiptPage(
                auth: auth,
                id: textOf(payment!['receiptId']),
                staff: true,
              ),
            ),
            child: const UiText('عرض الإيصال'),
          ),
      ],
    ], screen: 'payments');
  }
}
