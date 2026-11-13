import 'package:flutter/material.dart';

import '../../core/auth/auth_controller.dart';
import '../../core/theme/ui_text.dart';
import '../guardian/guardian_payments.dart' show ReceiptPage;
import '../guardian/guardian_ui.dart';
import '../owner/owner_operations.dart' show OwnerScopedRead;
import '../shared/payment_proof_image.dart';
import '../shared/private_media.dart' show isPrivateAsset;

/// An existing request, including processed history; registration and collection
/// remain with their existing authorized workflows.
class NativeEnrollmentPaymentRead extends StatelessWidget {
  const NativeEnrollmentPaymentRead({
    super.key,
    required this.auth,
    required this.id,
  });
  final AuthController auth;
  final String id;
  @override
  Widget build(BuildContext context) => GuardianPage(
    title: 'طلب الاشتراك الجديد',
    child: OwnerScopedRead(
      auth: auth,
      path: '/api/v1/manage/enrollment-requests/$id/payment',
      builder: (context, value) {
        final data = obj(value);
        if (data['id'] != id) {
          return const EmptyMessage('لم تتأكد هوية الطلب. أعد فتحه.');
        }
        final discount = data['discountAmount'];
        final hasDiscount = discount is num && discount > 0;
        final status = data['status'];
        final open = status == 'Pending' || status == 'UnderReview';
        final amount = data['amount'];
        final currency = data['currency'];
        final validAmount =
            amount is num &&
            amount.isFinite &&
            amount >= 0 &&
            currency is String &&
            RegExp(r'^[A-Z]{3}$').hasMatch(currency);
        final collected =
            status == 'Approved' &&
            data['paymentRequestId'] is String &&
            (data['paymentRequestId'] as String).isNotEmpty;
        final receipt = data['receiptId'];
        return gList([
          if (data['child'] case final String child when child.isNotEmpty)
            GCard(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  UiText(
                    child,
                    style: const TextStyle(
                      fontSize: 16,
                      fontWeight: FontWeight.bold,
                    ),
                  ),
                  PaymentRequestReference(order: data),
                  if (data['guardian'] case final String guardian
                      when guardian.isNotEmpty)
                    UiText('ولي الأمر: $guardian'),
                  if (data['sport'] != null || data['branch'] != null)
                    UiText(
                      [
                        data['sport'],
                        data['branch'],
                      ].whereType<String>().join(' · '),
                    ),
                ],
              ),
            ),
          _PaymentSummary([
            ('الحالة', label(status)),
            if (validAmount && hasDiscount && data['originalAmount'] != null)
              ('السعر الأصلي', money(data['originalAmount'], currency)),
            if (validAmount && hasDiscount)
              ('الخصم', money(discount, currency)),
            if (validAmount)
              (
                collected
                    ? 'المبلغ المحصّل'
                    : open
                    ? 'المبلغ المطلوب'
                    : 'مبلغ الطلب',
                money(amount, currency),
              ),
          ]),
          if (!validAmount)
            EmptyMessage(
              amount == null && data['subscriptionPlanId'] == null
                  ? 'لم تُحدد قيمة الدفع لهذا الطلب بعد.'
                  : 'تعذر التحقق من مبلغ الطلب وعملته.',
            ),
          if (collected && receipt is String && receipt.isNotEmpty)
            FilledButton(
              onPressed: () => openPage(
                context,
                ReceiptPage(
                  auth: auth,
                  id: receipt,
                  staff: true,
                  onBack: () => Navigator.of(context).pop(),
                ),
              ),
              child: const UiText('عرض الإيصال'),
            ),
          if (data['guardianVisibleReason'] != null)
            UiText(textOf(data['guardianVisibleReason'])),
          if (isPrivateAsset(data['proofUrl']))
            PaymentProofImage(
              key: ValueKey(data['proofUrl']),
              auth: auth,
              path: textOf(data['proofUrl']),
            ),
          if (!isPrivateAsset(data['proofUrl']) && open)
            const UiText('لم يصل إثبات دفع.'),
        ]);
      },
    ),
  );
}

class _PaymentSummary extends StatelessWidget {
  const _PaymentSummary(this.items);
  final List<(String, String)> items;
  @override
  Widget build(BuildContext context) => LayoutBuilder(
    builder: (context, constraints) {
      final single = MediaQuery.textScalerOf(context).scale(14) > 21;
      return GCard(
        child: Wrap(
          spacing: 12,
          runSpacing: 16,
          children: [
            for (final item in items)
              SizedBox(
                width: single
                    ? constraints.maxWidth - 28
                    : (constraints.maxWidth - 40) / 2,
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    UiText(item.$1, style: const TextStyle(fontSize: 12)),
                    UiText(
                      item.$2,
                      style: const TextStyle(
                        fontSize: 14,
                        fontWeight: FontWeight.bold,
                      ),
                    ),
                  ],
                ),
              ),
          ],
        ),
      );
    },
  );
}
