import 'package:flutter/material.dart';

import '../../core/auth/auth_controller.dart';
import '../../core/theme/ui_text.dart';
import '../guardian/guardian_ui.dart';
import '../owner/owner_operations.dart' show OwnerScopedRead;
import '../shared/private_media.dart';

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
    title: 'طلب دفع الاشتراك الجديد',
    child: OwnerScopedRead(
      auth: auth,
      path: '/api/v1/manage/enrollment-requests/$id/payment',
      builder: (context, value) {
        final data = obj(value);
        if (data['id'] != id) {
          return const EmptyMessage(
            'لم تتأكد هوية الطلب. أعد فتحه من التنبيه.',
          );
        }
        return gList([
          if (textOf(data['child']).isNotEmpty)
            GCard(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  UiText(
                    textOf(data['child']),
                    style: const TextStyle(
                      fontSize: 16,
                      fontWeight: FontWeight.bold,
                    ),
                  ),
                  if (textOf(data['guardian']).isNotEmpty)
                    UiText('ولي الأمر: ${data['guardian']}'),
                  UiText(
                    [
                      textOf(data['sport']),
                      textOf(data['branch']),
                    ].where((v) => v.isNotEmpty).join(' · '),
                  ),
                ],
              ),
            ),
          _PaymentSummary([
            ('الحالة', label(data['status'])),
            if (data['originalAmount'] != null)
              ('السعر الأصلي', money(data['originalAmount'], data['currency'])),
            if (data['amount'] != null)
              ('الخصم', money(data['discountAmount'], data['currency'])),
            if (data['amount'] != null)
              ('المبلغ النهائي', money(data['amount'], data['currency'])),
          ]),
          if (data['amount'] == null)
            const EmptyMessage('لم تُحدد قيمة الدفع لهذا الطلب بعد.'),
          if (data['guardianVisibleReason'] != null)
            UiText(textOf(data['guardianVisibleReason'])),
          if (isPrivateAsset(data['proofUrl'])) ...[
            const Heading('إثبات الدفع'),
            PrivateImage(
              auth: auth,
              path: textOf(data['proofUrl']),
              height: 280,
              fit: BoxFit.contain,
            ),
          ],
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
