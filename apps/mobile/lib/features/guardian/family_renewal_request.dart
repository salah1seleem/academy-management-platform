import 'package:flutter/material.dart';

import '../../core/auth/auth_controller.dart';
import '../../core/api/api_client.dart';
import '../../core/theme/appearance.dart';
import '../../core/theme/ui_text.dart';
import '../shared/private_media.dart';
import '../shared/help_center.dart';
import '../shared/transfer_payment.dart';
import 'guardian_payments.dart' show ReceiptPage;
import 'guardian_ui.dart';

class NativeFamilyRenewalRequests extends StatelessWidget {
  final AuthController auth;
  const NativeFamilyRenewalRequests({super.key, required this.auth});
  @override
  Widget build(BuildContext context) => GuardianPage(
    title: localized(context, 'طلبات تجديد الأسرة', 'Family renewal requests'),
    child: RemoteBody(
      auth: auth,
      path: '/api/v1/guardian/subscriptions/family/requests',
      builder: (context, value) => gList([
        HelpEntry(auth: auth, lesson: 'family-renewal'),
        if (rows(obj(value)['items']).isEmpty)
          EmptyMessage(
            localized(
              context,
              'لا توجد طلبات أسرة بعد.',
              'No family requests yet.',
            ),
          ),
        for (final r in rows(obj(value)['items']))
          GLink(
            '${familyStatus(context, r['status'])} · ${r['lineCount']}',
            Icons.family_restroom_outlined,
            () => openPage(
              context,
              NativeFamilyRenewalRequest(auth: auth, id: textOf(r['id'])),
            ),
            subtitle: money(r['amount'], r['currency']),
          ),
      ]),
    ),
  );
}

String familyStatus(BuildContext context, Object? status) => switch (status) {
  'Approved' => localized(context, 'تم تجديد الأسرة', 'Family renewed'),
  'Rejected' => localized(context, 'لم يُقبل الطلب', 'Request rejected'),
  'Cancelled' => localized(context, 'ملغي', 'Cancelled'),
  _ => localized(context, 'بانتظار المراجعة', 'Awaiting review'),
};

class NativeFamilyRenewalRequest extends StatefulWidget {
  final AuthController auth;
  final String id;
  final bool staff;
  const NativeFamilyRenewalRequest({
    super.key,
    required this.auth,
    required this.id,
    this.staff = false,
  });
  @override
  State<NativeFamilyRenewalRequest> createState() =>
      _NativeFamilyRenewalRequestState();
}

class _NativeFamilyRenewalRequestState
    extends State<NativeFamilyRenewalRequest> {
  int refresh = 0;
  bool busy = false;
  String? error;
  Future<void> cancel(Json data) async {
    final confirmed = await showDialog<bool>(
      context: context,
      builder: (context) => AlertDialog(
        title: UiText(
          localized(context, 'إلغاء طلب الأسرة؟', 'Cancel family request?'),
        ),
        content: UiText(
          localized(
            context,
            'الإلغاء لا يسترد أي تحويل أرسلته. تواصل مع الإدارة إذا دفعت بالفعل.',
            'Cancellation does not refund a transfer. Contact your academy if you already paid.',
          ),
        ),
        actions: [
          TextButton(
            onPressed: () => Navigator.pop(context, false),
            child: UiText(
              localized(context, 'الاحتفاظ بالطلب', 'Keep request'),
            ),
          ),
          FilledButton(
            onPressed: () => Navigator.pop(context, true),
            child: UiText(
              localized(context, 'تأكيد الإلغاء', 'Confirm cancellation'),
            ),
          ),
        ],
      ),
    );
    if (confirmed != true || !mounted || busy) return;
    setState(() {
      busy = true;
      error = null;
    });
    try {
      await widget.auth.request(
        'POST',
        '/api/v1/guardian/subscriptions/family/requests/${widget.id}/cancel',
        body: {'revision': data['revision']},
      );
      if (mounted) setState(() => refresh++);
    } catch (e) {
      if (mounted) {
        setState(
          () => error = e is ApiFailure
              ? e.message
              : localized(
                  context,
                  'تعذر تأكيد الإلغاء. حدّث الطلب قبل المحاولة.',
                  'Could not confirm cancellation. Refresh before retrying.',
                ),
        );
      }
    } finally {
      if (mounted) setState(() => busy = false);
    }
  }

  @override
  Widget build(BuildContext context) => GuardianPage(
    title: localized(context, 'طلب تجديد الأسرة', 'Family renewal request'),
    child: RemoteBody(
      key: ValueKey(refresh),
      auth: widget.auth,
      path:
          '${widget.staff ? '/api/v1/manage/family-renewals' : '/api/v1/guardian/subscriptions/family/requests'}/${widget.id}',
      builder: (context, value) {
        final d = obj(value);
        return gList([
          Heading(familyStatus(context, d['status'])),
          GCard(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                UiText(
                  '${localized(context, 'قبل الخصم', 'Before discount')}: ${money(d['originalAmount'], d['currency'])}',
                ),
                UiText(
                  '${localized(context, 'الخصم المؤهل', 'Eligible discount')}: ${money(d['discountAmount'], d['currency'])}',
                ),
                Heading(money(d['totalAmount'], d['currency'])),
              ],
            ),
          ),
          for (final line in rows(d['items']))
            GCard(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Heading(textOf(line['player'])),
                  UiText(
                    '${line['sport']} · ${line['branch']} · ${line['plan']}',
                  ),
                  UiText(money(line['finalAmount'], line['currency'])),
                  if (line['discountReason'] != null)
                    UiText(textOf(line['discountReason'])),
                  if (!widget.staff && line['receiptId'] != null)
                    TextButton(
                      onPressed: () => openPage(
                        context,
                        ReceiptPage(
                          auth: widget.auth,
                          id: textOf(line['receiptId']),
                        ),
                      ),
                      child: UiText(
                        localized(context, 'عرض الإيصال', 'View receipt'),
                      ),
                    ),
                ],
              ),
            ),
          if (d['decisionReason'] != null) UiText(textOf(d['decisionReason'])),
          if (d['proofUrl'] != null)
            GCard(
              child: Column(
                children: [
                  Heading(localized(context, 'إثبات الأسرة', 'Family proof')),
                  PrivateImage(
                    auth: widget.auth,
                    path: textOf(d['proofUrl']),
                    height: 260,
                  ),
                  UiText(
                    localized(
                      context,
                      'وصل الإثبات؛ لا تكرر التحويل.',
                      'Proof received; do not transfer again.',
                    ),
                  ),
                ],
              ),
            ),
          if (d['status'] == 'Pending' &&
              !widget.staff &&
              d['proofUrl'] == null)
            TransferPayment(
              auth: widget.auth,
              proofPath:
                  '/api/v1/guardian/subscriptions/family/requests/${widget.id}/proof',
              proofFields: {'revision': d['revision']},
              onSubmitted: () => setState(() => refresh++),
            ),
          if (widget.staff)
            UiText(
              localized(
                context,
                'مراجعة الإثبات وإسناد المجموعات والاعتماد الجماعي متاحة من لوحة الويب.',
                'Review proof, assign groups and approve all renewals from the web dashboard.',
              ),
            ),
          if (error != null)
            UiText(
              error!,
              style: TextStyle(color: Theme.of(context).colorScheme.error),
            ),
          if (d['status'] == 'Pending' && !widget.staff)
            OutlinedButton(
              onPressed: busy ? null : () => cancel(d),
              child: UiText(
                localized(
                  context,
                  'إلغاء طلب التجديد',
                  'Cancel renewal request',
                ),
              ),
            ),
        ]);
      },
    ),
  );
}
