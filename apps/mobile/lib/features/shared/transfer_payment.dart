import 'package:academy_mobile/core/theme/ui_text.dart';
import 'package:flutter/material.dart';
import 'package:flutter/services.dart';

import '../../core/api/api_client.dart';
import '../../core/auth/auth_controller.dart';
import '../../core/theme/appearance.dart';
import '../guardian/guardian_ui.dart';
import 'private_media.dart';

class TransferPayment extends StatefulWidget {
  final AuthController auth;
  final String proofPath;
  final VoidCallback? onSubmitted;
  final bool subscription;
  final Json proofFields;
  const TransferPayment({
    super.key,
    required this.auth,
    required this.proofPath,
    this.onSubmitted,
    this.subscription = true,
    this.proofFields = const {},
  });
  @override
  State<TransferPayment> createState() => _TransferPaymentState();
}

class _TransferPaymentState extends State<TransferPayment> {
  bool busy = false;
  String? message;
  String? uploadedAssetId;
  @override
  void didUpdateWidget(covariant TransferPayment oldWidget) {
    super.didUpdateWidget(oldWidget);
    if (oldWidget.proofPath != widget.proofPath ||
        oldWidget.auth != widget.auth) {
      uploadedAssetId = null;
      message = null;
    }
  }

  Future<void> upload() async {
    if (busy) return;
    setState(() {
      busy = true;
      message = null;
    });
    try {
      if (uploadedAssetId == null) {
        final asset = await pickAndUpload(widget.auth, {
          'purpose': 'Proof',
        }, imageOnly: true);
        if (asset == null) return;
        uploadedAssetId = textOf(asset['id']);
      }
      await widget.auth.request(
        'POST',
        widget.proofPath,
        body: {...widget.proofFields, 'assetId': uploadedAssetId},
      );
      if (mounted) {
        setState(
          () => message = localized(
            context,
            'وصل إثبات الدفع. تنتظر العملية مراجعة الإدارة؛ لا تحتاج إلى الدفع مرة أخرى.',
            'Proof received. Awaiting academy review; do not pay again.',
          ),
        );
        widget.onSubmitted?.call();
      }
    } catch (e) {
      if (mounted) {
        setState(
          () =>
              message = e is ApiFailure ? e.message : 'تعذر إرسال إثبات الدفع.',
        );
      }
    } finally {
      if (mounted) setState(() => busy = false);
    }
  }

  @override
  Widget build(BuildContext context) => GCard(
    child: Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        UiText(
          localized(context, 'التحويل للأكاديمية', 'Transfer to your academy'),
          style: Theme.of(context).textTheme.titleLarge,
        ),
        const SizedBox(height: 8),
        RemoteBody(
          auth: widget.auth,
          path: '/api/v1/payment-settings',
          builder: (context, value) {
            final d = obj(value);
            if (d['configured'] != true) {
              return UiText(
                localized(
                  context,
                  'لم تضبط الأكاديمية حسابات التحويل بعد. تواصل مع الإدارة قبل الدفع.',
                  'Transfer accounts are not configured. Contact your academy before paying.',
                ),
              );
            }
            return Column(
              children: [
                if (d['recipientName'] != null)
                  UiText(textOf(d['recipientName'])),
                for (final entry in [
                  ('إنستا باي', 'InstaPay', d['instaPayAddress']),
                  ('المحفظة', 'Wallet', d['walletNumber']),
                ])
                  if (entry.$3 != null && '${entry.$3}'.isNotEmpty)
                    ListTile(
                      contentPadding: EdgeInsets.zero,
                      title: UiText(localized(context, entry.$1, entry.$2)),
                      subtitle: UiText(
                        textOf(entry.$3),
                        textDirection: TextDirection.ltr,
                      ),
                      trailing: IconButton(
                        tooltip: localized(context, 'نسخ', 'Copy'),
                        icon: const Icon(Icons.copy_outlined),
                        onPressed: () async {
                          await Clipboard.setData(
                            ClipboardData(text: textOf(entry.$3)),
                          );
                          if (context.mounted) {
                            ScaffoldMessenger.of(context).showSnackBar(
                              SnackBar(
                                content: UiText(
                                  localized(context, 'تم النسخ', 'Copied'),
                                ),
                              ),
                            );
                          }
                        },
                      ),
                    ),
                UiText(
                  localized(
                    context,
                    widget.subscription
                        ? 'حوّل المبلغ الموضح ثم ارفع صورة إيصالك هنا. يبدأ الاشتراك بعد موافقة الإدارة.'
                        : 'حوّل المبلغ الموضح ثم ارفع صورة إيصالك هنا. يتأكد التحصيل بعد مراجعة الإدارة.',
                    widget.subscription
                        ? 'Transfer the displayed amount, then upload your receipt. Activation follows academy approval.'
                        : 'Transfer the displayed amount, then upload your receipt. Payment is confirmed after staff review.',
                  ),
                ),
                const SizedBox(height: 14),
                FilledButton.icon(
                  onPressed: busy ? null : upload,
                  icon: const Icon(Icons.upload_file_outlined),
                  label: UiText(
                    localized(
                      context,
                      busy
                          ? 'جارٍ الإرسال'
                          : uploadedAssetId != null
                          ? 'إعادة تأكيد نفس الإثبات'
                          : 'رفع إيصال الدفع',
                      busy
                          ? 'Uploading'
                          : uploadedAssetId != null
                          ? 'Retry the same proof'
                          : 'Upload payment receipt',
                    ),
                  ),
                ),
              ],
            );
          },
        ),
        if (message != null)
          Padding(
            padding: const EdgeInsets.only(top: 12),
            child: UiText(
              message!,
              style: Theme.of(context).textTheme.bodySmall,
            ),
          ),
      ],
    ),
  );
}

class EnrollmentPaymentPage extends StatefulWidget {
  final AuthController auth;
  final String id;
  const EnrollmentPaymentPage({
    super.key,
    required this.auth,
    required this.id,
  });
  @override
  State<EnrollmentPaymentPage> createState() => _EnrollmentPaymentPageState();
}

class _EnrollmentPaymentPageState extends State<EnrollmentPaymentPage> {
  int revision = 0;
  @override
  Widget build(BuildContext context) => GuardianPage(
    title: localized(context, 'دفع الاشتراك الجديد', 'New enrollment payment'),
    child: RemoteBody(
      key: ValueKey(revision),
      auth: widget.auth,
      path: '/api/v1/guardian/enrollment-requests/${widget.id}/payment',
      builder: (context, value) {
        final d = obj(value);
        return gList([
          Heading(money(d['amount'], d['currency'])),
          if (d['status'] == 'Pending' ||
              d['status'] == 'UnderReview' ||
              d['status'] == 1 ||
              d['status'] == 2)
            TransferPayment(
              auth: widget.auth,
              proofPath:
                  '/api/v1/guardian/enrollment-requests/${widget.id}/proof',
              onSubmitted: () => setState(() => revision++),
            ),
          if (d['proofUrl'] != null)
            UiText(
              localized(
                context,
                'تم رفع إثبات دفع لهذا الطلب.',
                'Payment proof has been uploaded.',
              ),
            ),
          StatusPill(d['status']),
          if (d['guardianVisibleReason'] != null)
            UiText(textOf(d['guardianVisibleReason'])),
          TextButton(
            onPressed: () => setState(() => revision++),
            child: UiText(localized(context, 'تحديث الحالة', 'Refresh status')),
          ),
        ]);
      },
    ),
  );
}
