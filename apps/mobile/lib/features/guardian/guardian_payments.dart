import 'package:flutter/material.dart';

import '../../core/api/api_client.dart';
import '../../core/auth/auth_controller.dart';
import 'guardian_ui.dart';

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
      const Text('الدفع لا يمنحك الوصول إلى ملف اللاعب أو ربطه بحسابك.'),
    if (items.isEmpty) const EmptyMessage('لا توجد باقات متاحة.'),
    for (final p in items)
      GCard(
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Heading(textOf(p['arabicName'])),
            Text(money(p['price'], p['currency'])),
            if (p['durationDays'] != null) Text('${p['durationDays']} يومًا'),
            if (p['sessionCount'] != null) Text('${p['sessionCount']} حصة'),
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
              child: const Text('اختيار الباقة'),
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
  List<Json> results = const [];
  bool busy = false;
  String? error;
  @override
  void dispose() {
    name.dispose();
    super.dispose();
  }

  Future<void> search() async {
    if (busy) return;
    final query = name.text.trim();
    if (query.length < 3) {
      setState(() => error = 'اكتب 3 أحرف على الأقل من اسم اللاعب.');
      return;
    }
    setState(() {
      busy = true;
      error = null;
    });
    try {
      final d = obj(
        await widget.auth.request(
          'GET',
          '/api/v1/guardian/subscriptions/external/search?query=${Uri.encodeQueryComponent(query)}',
        ),
      );
      if (mounted) setState(() => results = rows(d['items']));
    } on ApiFailure catch (e) {
      if (mounted) setState(() => error = e.message);
    } finally {
      if (mounted) setState(() => busy = false);
    }
  }

  Future<void> select(Json candidate) async {
    if (busy) return;
    setState(() {
      busy = true;
      error = null;
    });
    try {
      final d = obj(
        await widget.auth.request(
          'POST',
          '/api/v1/guardian/subscriptions/external/select',
          body: {'candidateId': candidate['candidateId']},
        ),
      );
      if (!mounted) return;
      await openPage(
        context,
        PlanPicker(
          auth: widget.auth,
          environment: widget.environment,
          childName: textOf(d['playerDisplayName']),
          reference: textOf(d['reference']),
          plans: rows(d['plans']),
        ),
      );
    } on ApiFailure catch (e) {
      if (mounted) setState(() => error = e.message);
    } finally {
      if (mounted) setState(() => busy = false);
    }
  }

  @override
  Widget build(BuildContext context) => GuardianPage(
    title: 'تجديد اشتراك لغيره',
    child: gList([
      const Heading('ابحث عن اللاعب'),
      const Text(
        'اكتب اسم اللاعب واختر الرياضة الصحيحة. تظهر نتائج الأكاديمية الحالية فقط، والدفع لا يتيح الوصول إلى ملف اللاعب.',
      ),
      const SizedBox(height: 24),
      TextField(
        controller: name,
        enabled: !busy,
        textInputAction: TextInputAction.search,
        onSubmitted: (_) => search(),
        decoration: const InputDecoration(
          labelText: 'اسم اللاعب',
          prefixIcon: Icon(Icons.search),
        ),
      ),
      if (error != null) Text(error!),
      const SizedBox(height: 16),
      FilledButton(
        onPressed: busy ? null : search,
        child: Text(busy ? 'جارٍ البحث…' : 'بحث'),
      ),
      if (!busy && results.isEmpty && name.text.trim().length >= 3)
        const EmptyMessage('لا توجد نتائج مطابقة داخل الأكاديمية.'),
      for (final candidate in results)
        GLink(
          textOf(candidate['playerDisplayName']),
          Icons.sports_soccer,
          () => select(candidate),
          subtitle:
              '${textOf(candidate['sport'])} · ${textOf(candidate['branch'])} · ${textOf(candidate['group'])}',
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
            Text(textOf(widget.plan['arabicName'])),
            Text(
              money(widget.plan['price'], widget.plan['currency']),
              style: const TextStyle(fontSize: 28),
            ),
            if (widget.plan['durationDays'] != null)
              Text('${widget.plan['durationDays']} يومًا'),
            if (widget.plan['sessionCount'] != null)
              Text('${widget.plan['sessionCount']} حصة'),
          ],
        ),
      ),
      const Text(
        'لن يتم تحصيل المبلغ أو تفعيل الاشتراك قبل تأكيد الدفع. تاريخ التفعيل والحصص يحددهما الخادم.',
      ),
      if (widget.reference != null)
        const Text('هذا الطلب لا ينشئ علاقة ولي أمر باللاعب.'),
      if (error != null) Text(error!),
      const SizedBox(height: 24),
      FilledButton(
        onPressed: busy ? null : submit,
        child: Text(busy ? 'جارٍ إنشاء الطلب…' : 'متابعة إلى الدفع'),
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
          Text('${d['sport']} · ${d['plan']}'),
          StatusPill(d['status']),
          const SizedBox(height: 18),
          GCard(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(
                  'السعر الأصلي: ${money(d['originalAmount'], d['currency'])}',
                ),
                Text('الخصم: ${money(d['discountAmount'], d['currency'])}'),
                Heading(money(d['finalAmount'], d['currency'])),
              ],
            ),
          ),
          if (error != null) Text(error!),
          TextButton(
            onPressed: busy ? null : () => setState(() => revision++),
            child: const Text('تحديث حالة الدفع'),
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
              child: const Text('عرض الإيصال'),
            ),
          if (pending && d['isCurrent'] == true) ...[
            if (widget.environment == 'Demo') ...[
              const Text('دفع تجريبي فقط · لن تُخصم أموال حقيقية'),
              const SizedBox(height: 12),
              FilledButton(
                onPressed: busy ? null : () => action('Success'),
                child: const Text('محاكاة دفع ناجح'),
              ),
              TextButton(
                onPressed: busy ? null : () => action('Failed'),
                child: const Text('محاكاة فشل الدفع'),
              ),
              TextButton(
                onPressed: busy ? null : () => action('Cancelled'),
                child: const Text('إلغاء الدفع التجريبي'),
              ),
            ] else
              const Text('الدفع الحقيقي غير مفعّل. تواصل مع الأكاديمية.'),
          ],
          if (['Failed', 'Cancelled', 'Expired'].contains(d['status']) &&
              d['isCurrent'] == true)
            FilledButton(
              onPressed: busy ? null : () => action('retry'),
              child: const Text('إعادة محاولة الدفع'),
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
          Text(
            '${d['playerNameSnapshot']}\n${d['sportNameSnapshot']} · ${d['planNameSnapshot']}',
          ),
          GCard(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(
                  'السعر الأصلي: ${money(d['originalAmount'], d['currency'])}',
                ),
                Text('الخصم: ${money(d['discountAmount'], d['currency'])}'),
                Heading('المدفوع: ${money(d['amount'], d['currency'])}'),
              ],
            ),
          ),
          Text('تاريخ الدفع: ${day(d['paidAtUtc'])}'),
          Text('مرجع الدفع: ${d['providerReference']}'),
          const Text('الإيصال صادر من سجل التحصيل على الخادم.'),
        ]);
      },
    ),
  );
}
