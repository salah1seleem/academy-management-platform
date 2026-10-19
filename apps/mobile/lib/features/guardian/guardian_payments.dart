import 'package:academy_mobile/core/theme/ui_text.dart';
import 'package:academy_mobile/core/theme/appearance.dart';
import 'package:flutter/material.dart';

import '../../core/api/api_client.dart';
import '../../core/auth/auth_controller.dart';
import 'guardian_ui.dart';
import '../shared/transfer_payment.dart';
import 'family_renewal_quote.dart';
import 'family_renewal_request.dart';

class RenewalPicker extends StatelessWidget {
  final AuthController auth;
  final String environment;
  const RenewalPicker({
    super.key,
    required this.auth,
    required this.environment,
  });
  @override
  Widget build(BuildContext context) => GuardianPage(
    title: 'تجديد الاشتراك',
    child: RemoteBody(
      auth: auth,
      path: '/api/v1/guardian/subscriptions/enrollments',
      builder: (context, value) => gList([
        GLink(
          'مراجعة تجديد الأبناء معًا',
          Icons.family_restroom_outlined,
          () => openPage(context, NativeFamilyRenewalQuote(auth: auth)),
          subtitle: 'الباقات والخصم وإثبات واحد للأسرة',
        ),
        GLink(
          'طلبات تجديد الأسرة السابقة',
          Icons.receipt_long_outlined,
          () => openPage(context, NativeFamilyRenewalRequests(auth: auth)),
        ),
        const Heading('اختر الطفل والرياضة'),
        if (rows(value).isEmpty)
          const EmptyMessage('لا توجد تسجيلات متاحة للتجديد.'),
        for (final e in rows(value))
          GLink(
            '${e['player']} · ${e['sport']}',
            Icons.sports_soccer,
            () => openPage(
              context,
              PlanPicker(
                auth: auth,
                environment: environment,
                enrollmentId: e['id'],
                childName: e['player'],
              ),
            ),
            subtitle: e['period'] == null
                ? 'بدون اشتراك'
                : label(e['period']['status']),
          ),
      ]),
    ),
  );
}

class PlanPicker extends StatelessWidget {
  final AuthController auth;
  final String environment, childName;
  final String? enrollmentId, reference;
  final List<Json>? plans;
  const PlanPicker({
    super.key,
    required this.auth,
    required this.environment,
    required this.childName,
    this.enrollmentId,
    this.reference,
    this.plans,
  });
  @override
  Widget build(BuildContext context) => GuardianPage(
    title: 'اختيار الباقة',
    child: plans != null
        ? content(context, plans!)
        : RemoteBody(
            auth: auth,
            path:
                '/api/v1/guardian/subscriptions/enrollments/$enrollmentId/plans',
            builder: (context, value) => content(context, rows(value)),
          ),
  );
  Widget content(BuildContext context, List<Json> items) => gList([
    Heading(childName),
    if (reference != null)
      const UiText('الدفع لا يمنحك الوصول إلى ملف اللاعب أو ربطه بحسابك.'),
    if (items.isEmpty) const EmptyMessage('لا توجد باقات متاحة.'),
    for (final p in items)
      GCard(
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Heading(textOf(p['arabicName'])),
            UiText(money(p['price'], p['currency'])),
            if (p['durationDays'] != null) UiText('${p['durationDays']} يومًا'),
            if (p['sessionCount'] != null) UiText('${p['sessionCount']} حصة'),
            const SizedBox(height: 12),
            FilledButton(
              onPressed: () => openPage(
                context,
                RenewalReview(
                  auth: auth,
                  environment: environment,
                  plan: p,
                  childName: childName,
                  enrollmentId: enrollmentId,
                  reference: reference,
                ),
              ),
              child: const UiText('اختيار الباقة'),
            ),
          ],
        ),
      ),
  ]);
}

class ExternalRenewal extends StatefulWidget {
  final AuthController auth;
  final String environment;
  const ExternalRenewal({
    super.key,
    required this.auth,
    required this.environment,
  });
  @override
  State<ExternalRenewal> createState() => _ExternalRenewalState();
}

class _ExternalRenewalState extends State<ExternalRenewal> {
  final name = TextEditingController();
  List<Json> matches = [];
  bool busy = false, searched = false;
  String? error;
  @override
  void dispose() {
    name.dispose();
    super.dispose();
  }

  Future<void> search() async {
    if (busy) return;
    if (name.text.trim().length < 3) {
      setState(() => error = 'اكتب ثلاثة أحرف على الأقل من اسم اللاعب.');
      return;
    }
    setState(() {
      busy = true;
      error = null;
      matches = [];
      searched = false;
    });
    try {
      final result = await widget.auth.request(
        'POST',
        '/api/v1/guardian/subscriptions/external/search',
        body: {'name': name.text.trim()},
      );
      if (mounted) {
        setState(() {
          matches = rows(result);
          searched = true;
        });
      }
    } catch (e) {
      if (mounted) {
        setState(
          () => error = e is ApiFailure ? e.message : 'تعذر البحث عن اللاعب.',
        );
      }
    } finally {
      if (mounted) setState(() => busy = false);
    }
  }

  Future<void> select(Json match) async {
    if (busy) return;
    setState(() {
      busy = true;
      error = null;
    });
    try {
      final d = obj(
        await widget.auth.request(
          'POST',
          '/api/v1/guardian/subscriptions/external/resolve',
          body: {'reference': match['reference']},
        ),
      );
      if (!mounted) return;
      await openPage(
        context,
        PlanPicker(
          auth: widget.auth,
          environment: widget.environment,
          childName: d['playerDisplayName'],
          reference: match['reference'],
          plans: rows(d['plans']),
        ),
      );
    } catch (e) {
      if (mounted) {
        setState(
          () => error = e is ApiFailure
              ? e.message
              : 'انتهى الاختيار. أعد البحث.',
        );
      }
    } finally {
      if (mounted) setState(() => busy = false);
    }
  }

  @override
  Widget build(BuildContext context) => GuardianPage(
    title: 'تجديد اشتراك لغيره',
    child: gList([
      const Heading('هدية تدعم رحلة لاعب'),
      const UiText(
        'ابحث داخل أكاديميتك واختر اللاعب والرياضة. الدفع لا يمنحك الوصول إلى ملفه أو ربطه بحسابك.',
      ),
      const SizedBox(height: 16),
      TextField(
        controller: name,
        enabled: !busy,
        maxLength: 100,
        decoration: InputDecoration(labelText: ui(context, 'اسم اللاعب')),
        onSubmitted: (_) => search(),
      ),
      FilledButton.icon(
        onPressed: busy ? null : search,
        icon: const Icon(Icons.search),
        label: UiText(busy ? 'جارٍ البحث…' : 'بحث عن لاعب'),
      ),
      if (error != null) UiText(error!),
      if (searched && matches.isEmpty)
        const EmptyMessage('لا توجد تسجيلات مطابقة للاسم.'),
      for (final match in matches)
        GLink(
          textOf(match['name']),
          Icons.person_outline,
          () => select(match),
          subtitle: '${match['sport']} · ${match['branch']}',
        ),
    ]),
  );
}

class RenewalReview extends StatefulWidget {
  final AuthController auth;
  final String environment, childName;
  final Json plan;
  final String? enrollmentId, reference;
  const RenewalReview({
    super.key,
    required this.auth,
    required this.environment,
    required this.childName,
    required this.plan,
    this.enrollmentId,
    this.reference,
  });
  @override
  State<RenewalReview> createState() => _RenewalReviewState();
}

class _RenewalReviewState extends State<RenewalReview> {
  final key = commandKey();
  bool busy = false;
  String? error;
  Future<void> submit() async {
    if (busy) return;
    setState(() {
      busy = true;
      error = null;
    });
    try {
      final d = obj(
        await widget.auth.request(
          'POST',
          widget.reference == null
              ? '/api/v1/guardian/subscriptions/renewals'
              : '/api/v1/guardian/subscriptions/external/renewals',
          body: {
            if (widget.reference == null)
              'sportEnrollmentId': widget.enrollmentId
            else
              'reference': widget.reference,
            'subscriptionPlanId': widget.plan['id'],
          },
          idempotencyKey: key,
        ),
      );
      if (mounted) {
        Navigator.of(context).pushReplacement(
          MaterialPageRoute<void>(
            builder: (_) => CheckoutPage(
              auth: widget.auth,
              environment: widget.environment,
              id: d['paymentId'],
            ),
          ),
        );
      }
    } on ApiFailure catch (e) {
      if (mounted) {
        setState(
          () => error =
              '${e.message} أعد المحاولة من نفس الشاشة لتجنب تكرار الطلب.',
        );
      }
    } finally {
      if (mounted) setState(() => busy = false);
    }
  }

  @override
  Widget build(BuildContext context) => GuardianPage(
    title: 'مراجعة التجديد',
    child: gList([
      Heading(widget.childName),
      GCard(
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            UiText(textOf(widget.plan['arabicName'])),
            UiText(
              money(widget.plan['price'], widget.plan['currency']),
              style: const TextStyle(fontSize: 28),
            ),
            if (widget.plan['durationDays'] != null)
              UiText('${widget.plan['durationDays']} يومًا'),
            if (widget.plan['sessionCount'] != null)
              UiText('${widget.plan['sessionCount']} حصة'),
          ],
        ),
      ),
      const UiText(
        'لن يتم تحصيل المبلغ أو تفعيل الاشتراك قبل تأكيد الدفع. تاريخ التفعيل والحصص يحددهما الخادم.',
      ),
      if (widget.reference != null)
        const UiText('هذا الطلب لا ينشئ علاقة ولي أمر باللاعب.'),
      if (error != null) UiText(error!),
      const SizedBox(height: 24),
      FilledButton(
        onPressed: busy ? null : submit,
        child: UiText(busy ? 'جارٍ إنشاء الطلب…' : 'متابعة إلى الدفع'),
      ),
    ]),
  );
}

class CheckoutPage extends StatefulWidget {
  final AuthController auth;
  final String environment, id;
  const CheckoutPage({
    super.key,
    required this.auth,
    required this.environment,
    required this.id,
  });
  @override
  State<CheckoutPage> createState() => _CheckoutPageState();
}

class _CheckoutPageState extends State<CheckoutPage> {
  bool busy = false;
  String? error;
  int revision = 0;
  final retryKey = commandKey();
  Future<void> action(String outcome) async {
    if (busy) return;
    setState(() {
      busy = true;
      error = null;
    });
    try {
      final retry = outcome == 'retry';
      final result = await widget.auth.request(
        'POST',
        '/api/v1/guardian/subscriptions/payments/${widget.id}/${retry ? 'retry' : 'simulate'}',
        body: retry ? {} : {'outcome': outcome},
        idempotencyKey: retry ? retryKey : null,
      );
      if (!mounted) return;
      if (retry) {
        Navigator.of(context).pushReplacement(
          MaterialPageRoute<void>(
            builder: (_) => CheckoutPage(
              auth: widget.auth,
              environment: widget.environment,
              id: obj(result)['paymentId'],
            ),
          ),
        );
      } else {
        setState(() => revision++);
      }
    } on ApiFailure catch (e) {
      if (mounted) {
        setState(
          () => error = '${e.message} حدّث حالة الدفع قبل المحاولة مجددًا.',
        );
      }
    } finally {
      if (mounted) setState(() => busy = false);
    }
  }

  @override
  Widget build(BuildContext context) => GuardianPage(
    title: 'الدفع',
    child: RemoteBody(
      key: ValueKey(revision),
      auth: widget.auth,
      path: '/api/v1/guardian/subscriptions/payments/${widget.id}',
      builder: (context, value) {
        final d = obj(value);
        final pending = d['status'] == 'Pending';
        return gList([
          Heading(textOf(d['player'])),
          UiText('${d['sport']} · ${d['plan']}'),
          StatusPill(d['status']),
          const SizedBox(height: 18),
          GCard(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                UiText(
                  'السعر الأصلي: ${money(d['originalAmount'], d['currency'])}',
                ),
                UiText('الخصم: ${money(d['discountAmount'], d['currency'])}'),
                if (d['discountType'] == 'Percentage')
                  UiText('نسبة الخصم: ${d['discountValue']}%'),
                if (d['discountReason'] != null)
                  UiText(textOf(d['discountReason'])),
                Heading(money(d['finalAmount'], d['currency'])),
              ],
            ),
          ),
          if (error != null) UiText(error!),
          TextButton(
            onPressed: busy ? null : () => setState(() => revision++),
            child: const UiText('تحديث حالة الدفع'),
          ),
          if (d['isCurrent'] == false && d['currentPaymentId'] != null)
            GLink(
              'عرض محاولة الدفع الحالية',
              Icons.refresh,
              () => openPage(
                context,
                CheckoutPage(
                  auth: widget.auth,
                  environment: widget.environment,
                  id: d['currentPaymentId'],
                ),
              ),
            ),
          if (d['receiptId'] != null)
            FilledButton(
              onPressed: () => openPage(
                context,
                ReceiptPage(auth: widget.auth, id: d['receiptId']),
              ),
              child: const UiText('عرض الإيصال'),
            ),
          if (pending && d['isCurrent'] == true) ...[
            TransferPayment(
              auth: widget.auth,
              proofPath: '/api/v1/guardian/payments/${widget.id}/proof',
              onSubmitted: () => setState(() => revision++),
            ),
            if (widget.environment == 'Demo' && d['provider'] != 'Manual') ...[
              const UiText('دفع تجريبي فقط · لن تُخصم أموال حقيقية'),
              const SizedBox(height: 12),
              FilledButton(
                onPressed: busy ? null : () => action('Success'),
                child: const UiText('محاكاة دفع ناجح'),
              ),
              TextButton(
                onPressed: busy ? null : () => action('Failed'),
                child: const UiText('محاكاة فشل الدفع'),
              ),
              TextButton(
                onPressed: busy ? null : () => action('Cancelled'),
                child: const UiText('إلغاء الدفع التجريبي'),
              ),
            ] else
              const SizedBox.shrink(),
          ],
          if (['Failed', 'Cancelled', 'Expired'].contains(d['status']) &&
              d['isCurrent'] == true)
            FilledButton(
              onPressed: busy ? null : () => action('retry'),
              child: const UiText('إعادة محاولة الدفع'),
            ),
        ]);
      },
    ),
  );
}

class ReceiptsPage extends StatelessWidget {
  final AuthController auth;
  const ReceiptsPage({super.key, required this.auth});
  @override
  Widget build(BuildContext context) => GuardianPage(
    title: 'إيصالاتي',
    child: RemoteBody(
      auth: auth,
      path: '/api/v1/guardian/subscriptions/receipts',
      builder: (context, value) {
        final items = rows(obj(value)['items']);
        return gList([
          if (items.isEmpty) const EmptyMessage('لا توجد إيصالات لحسابك بعد.'),
          for (final d in items)
            GLink(
              '${d['receiptNumber']} · ${d['player']}',
              Icons.receipt_long,
              () => openPage(context, ReceiptPage(auth: auth, id: d['id'])),
              subtitle:
                  '${money(d['amount'], d['currency'])} · ${day(d['paidAtUtc'])}',
            ),
        ]);
      },
    ),
  );
}

class ReceiptPage extends StatelessWidget {
  final AuthController auth;
  final String id;
  const ReceiptPage({super.key, required this.auth, required this.id});
  @override
  Widget build(BuildContext context) => GuardianPage(
    title: 'إيصال التحصيل',
    child: RemoteBody(
      auth: auth,
      path: '/api/v1/guardian/subscriptions/receipts/$id',
      builder: (context, value) {
        final d = obj(value);
        return gList([
          const Icon(
            Icons.verified_outlined,
            size: 64,
            color: Colors.greenAccent,
          ),
          Heading(textOf(d['academyName'])),
          Heading(textOf(d['receiptNumber'])),
          if (d['isVoided'] == true)
            GCard(
              child: UiText(
                localized(
                  context,
                  'إيصال معكوس؛ رُدت قيمته وأُلغيت فترة الاشتراك المرتبطة. السجل الأصلي محفوظ.',
                  'Voided receipt: refunded and linked subscription period cancelled. Original record retained.',
                ),
              ),
            ),
          UiText(
            '${d['playerNameSnapshot']}\n${d['sportNameSnapshot']} · ${d['planNameSnapshot']}',
          ),
          GCard(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                UiText(
                  'السعر الأصلي: ${money(d['originalAmount'], d['currency'])}',
                ),
                UiText('الخصم: ${money(d['discountAmount'], d['currency'])}'),
                Heading('المدفوع: ${money(d['amount'], d['currency'])}'),
              ],
            ),
          ),
          UiText('تاريخ الدفع: ${day(d['paidAtUtc'])}'),
          if (d['enrollmentBranch'] is String &&
              (d['enrollmentBranch'] as String).isNotEmpty)
            UiText(
              '${localized(context, 'فرع التسجيل', 'Enrollment branch')}: ${d['enrollmentBranch']}',
            ),
          if (d['collectionBranch'] is String &&
              (d['collectionBranch'] as String).isNotEmpty)
            UiText(
              '${localized(context, 'فرع التحصيل', 'Collecting branch')}: ${d['collectionBranch']}',
            ),
          UiText('مرجع الدفع: ${d['providerReference']}'),
          const UiText('الإيصال صادر من سجل التحصيل على الخادم.'),
        ]);
      },
    ),
  );
}
