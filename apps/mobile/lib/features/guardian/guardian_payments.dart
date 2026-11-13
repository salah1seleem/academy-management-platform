import 'package:academy_mobile/core/theme/ui_text.dart';
import 'package:academy_mobile/core/theme/appearance.dart';
import 'package:flutter/material.dart';

import '../../core/api/api_client.dart';
import '../../core/auth/auth_controller.dart';
import '../../core/widgets/page_status.dart';
import 'guardian_ui.dart';
import 'family_child_filter.dart';
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
    operation: true,
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
            switch (e['sport']) {
              'كرة القدم' => Icons.sports_soccer,
              'السباحة' => Icons.pool,
              'كرة السلة' => Icons.sports_basketball,
              _ => Icons.event_repeat_outlined,
            },
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

class PlanPicker extends StatefulWidget {
  final AuthController auth;
  final String environment, childName;
  final String? enrollmentId, reference, externalRegistration;
  final List<Json>? plans;
  final Json? externalOpen;
  const PlanPicker({
    super.key,
    required this.auth,
    required this.environment,
    required this.childName,
    this.enrollmentId,
    this.reference,
    this.externalRegistration,
    this.plans,
    this.externalOpen,
  });
  @override
  State<PlanPicker> createState() => _PlanPickerState();
}

class _PlanPickerState extends State<PlanPicker> {
  int revision = 0;

  Future<void> choose(Json plan) async {
    var refresh = false;
    await openPage<void>(
      context,
      RenewalReview(
        auth: widget.auth,
        environment: widget.environment,
        plan: plan,
        childName: widget.childName,
        enrollmentId: widget.enrollmentId,
        reference: widget.reference,
        externalRegistration: widget.externalRegistration,
        onUnavailable: () => refresh = true,
        onSaved: widget.plans == null ? () => refresh = true : null,
      ),
    );
    if (!mounted || !refresh) return;
    if (widget.plans == null) {
      setState(() => revision++);
    } else {
      Navigator.of(context).pop();
    }
  }

  @override
  Widget build(BuildContext context) => GuardianPage(
    operation: true,
    title: widget.plans == null || widget.externalOpen != null
        ? 'تجديد الاشتراك'
        : 'اختيار الباقة',
    child: widget.plans != null
        ? content(context, widget.plans!, open: widget.externalOpen)
        : RemoteBody(
            key: ValueKey(revision),
            auth: widget.auth,
            path:
                '/api/v1/guardian/subscriptions/enrollments/${widget.enrollmentId}/renewal-review',
            builder: (context, value) {
              final review = value is Json ? value : const <String, dynamic>{};
              final enrollment = review['enrollment'];
              final plans = review['plans'];
              final open = review['openRenewal'];
              if (enrollment is! Json ||
                  enrollment['id'] != widget.enrollmentId ||
                  plans is! List ||
                  plans.any((plan) => plan is! Json) ||
                  (open != null &&
                      (open is! Json ||
                          !['Payment', 'Family'].contains(open['kind'])))) {
                return gList([
                  const EmptyMessage('تعذر تأكيد بيانات تسجيل التجديد.'),
                  OutlinedButton(
                    onPressed: () => setState(() => revision++),
                    child: const UiText('إعادة المحاولة'),
                  ),
                ]);
              }
              return content(
                context,
                plans.cast<Json>(),
                enrollment: enrollment,
                open: open as Json?,
              );
            },
          ),
  );
  Widget content(
    BuildContext context,
    List<Json> items, {
    Json? enrollment,
    Json? open,
  }) => gList([
    Heading(textOf(enrollment?['player'] ?? widget.childName)),
    if (enrollment != null)
      UiText('${enrollment['sport']} · ${enrollment['branch']}'),
    if (widget.externalRegistration case final registration?)
      UiText(registration),
    if (widget.reference != null && open == null)
      const UiText('الدفع لا يمنحك الوصول إلى ملف اللاعب أو ربطه بحسابك.'),
    if (open != null)
      _OpenRenewalCard(
        auth: widget.auth,
        environment: widget.environment,
        open: open,
        onReturn: widget.reference == null
            ? null
            : () => Navigator.of(context).pop(),
      )
    else if (items.isEmpty)
      const EmptyMessage('لا توجد باقات متاحة.'),
    if (open == null)
      for (final p in items)
        GCard(
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Row(
                children: [
                  Expanded(
                    flex: 3,
                    child: UiText(
                      textOf(p['arabicName']),
                      style: const TextStyle(
                        fontWeight: FontWeight.w800,
                        fontSize: 16,
                      ),
                    ),
                  ),
                  const SizedBox(width: 12),
                  Flexible(
                    flex: 2,
                    child: UiText(
                      money(p['price'], p['currency']),
                      style: TextStyle(
                        fontWeight: FontWeight.w800,
                        color: Theme.of(context).colorScheme.primary,
                      ),
                    ),
                  ),
                ],
              ),
              const SizedBox(height: 8),
              _RenewalPlanTerms(p),
              const SizedBox(height: 12),
              FilledButton(
                onPressed: () => choose(p),
                child: const UiText('اختيار الباقة'),
              ),
            ],
          ),
        ),
  ]);
}

class _OpenRenewalCard extends StatelessWidget {
  final AuthController auth;
  final String environment;
  final Json open;
  final VoidCallback? onReturn;

  const _OpenRenewalCard({
    required this.auth,
    required this.environment,
    required this.open,
    this.onReturn,
  });

  @override
  Widget build(BuildContext context) {
    final family = open['kind'] == 'Family';
    final id = family ? open['familyRequestId'] : open['paymentId'];
    return GCard(
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Heading(switch (open['kind']) {
            'Family' => 'طلب تجديد أسرة مفتوح',
            'Payment' => 'طلب دفع مفتوح',
            _ => 'طلب تجديد مفتوح',
          }),
          if (id is String && id.isNotEmpty)
            FilledButton(
              onPressed: () async {
                await openPage<void>(
                  context,
                  family
                      ? NativeFamilyRenewalRequest(auth: auth, id: id)
                      : CheckoutPage(
                          auth: auth,
                          environment: environment,
                          id: id,
                        ),
                );
                if (context.mounted) onReturn?.call();
              },
              child: UiText(family ? 'متابعة طلب الأسرة' : 'متابعة طلب الدفع'),
            )
          else
            const UiText('تواصل مع الأكاديمية لمتابعة الطلب.'),
        ],
      ),
    );
  }
}

class _RenewalPlanTerms extends StatelessWidget {
  const _RenewalPlanTerms(this.plan);
  final Json plan;

  int count(String key) => int.tryParse('${plan[key]}') ?? 0;

  @override
  Widget build(BuildContext context) {
    final months = count('durationMonths');
    final days = count('durationDays');
    final sessions = count('sessionCount');
    final paidMonths = count('chargedMonths');
    final terms = [
      if (months > 0)
        switch (months) {
          1 => 'شهر تقويمي',
          2 => 'شهران تقويميان',
          <= 10 => '$months أشهر تقويمية',
          _ => '$months شهرًا تقويميًا',
        }
      else if (days > 0)
        switch (days) {
          1 => 'يوم واحد',
          2 => 'يومان',
          <= 10 => '$days أيام',
          _ => '$days يومًا',
        },
      if (sessions > 0)
        switch (sessions) {
          1 => 'حصة واحدة',
          2 => 'حصتان',
          <= 10 => '$sessions حصص',
          _ => '$sessions حصة',
        },
      if (plan['trainingFormat'] == 'Season' && paidMonths > 0)
        'الأشهر المدفوعة: $paidMonths',
    ];
    return terms.isEmpty ? const SizedBox.shrink() : UiText(terms.join(' · '));
  }
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

typedef _ExternalIdentity = ({String name, String sport, String branch});

Json? _readExternalOpen(Object? value) {
  if (value == null) return null;
  if (value is Json) {
    if (value['kind'] == 'Unavailable') return {'kind': 'Unavailable'};
    final id = switch (value['kind']) {
      'Payment' => value['paymentId'],
      'Family' => value['familyRequestId'],
      _ => null,
    };
    if (id is String &&
        RegExp(r'^[a-fA-F0-9]{8}(?:-[a-fA-F0-9]{4}){3}-[a-fA-F0-9]{12}$')
            .hasMatch(id)) {
      return value;
    }
  }
  throw const FormatException();
}

class _ExternalRenewalState extends State<ExternalRenewal> {
  final name = TextEditingController();
  Map<_ExternalIdentity, String?> matches = {};
  bool busy = false, searched = false;
  String? error;
  @override
  void dispose() {
    name.dispose();
    super.dispose();
  }

  Future<void> search() async {
    if (busy) return;
    final term = name.text.trim();
    if (term.length < 3) {
      setState(() => error = 'اكتب ثلاثة أحرف على الأقل من اسم اللاعب.');
      return;
    }
    FocusScope.of(context).unfocus();
    setState(() {
      busy = true;
      error = null;
      matches = {};
      searched = false;
    });
    try {
      final result = await widget.auth.request(
        'POST',
        '/api/v1/guardian/subscriptions/external/search',
        body: {'name': term},
      );
      if (result is! List) throw const FormatException();
      final grouped = <_ExternalIdentity, String?>{};
      for (final row in result) {
        if (row case {
          'name': String displayName,
          'sport': String sport,
          'branch': String branch,
          'reference': String? reference,
        }) {
          final identity = (
            name: displayName.trim(),
            sport: sport.trim(),
            branch: branch.trim(),
          );
          if (identity.name.isEmpty ||
              identity.sport.isEmpty ||
              identity.branch.isEmpty ||
              reference?.isEmpty == true) {
            throw const FormatException();
          }
          grouped[identity] = grouped.containsKey(identity) ? null : reference;
        } else {
          throw const FormatException();
        }
      }
      if (mounted) {
        setState(() {
          matches = grouped;
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

  Future<void> select(_ExternalIdentity identity, String reference) async {
    if (busy) return;
    setState(() {
      busy = true;
      error = null;
    });
    try {
      final d = await widget.auth.request(
        'POST',
        '/api/v1/guardian/subscriptions/external/resolve',
        body: {'reference': reference},
      );
      if (!mounted) return;
      if (d is! Json ||
          d['playerDisplayName'] is! String ||
          d['plans'] is! List) {
        throw const FormatException();
      }
      final sport = d['sport'];
      final branch = d['branch'];
      if ((d['playerDisplayName'] as String).trim() != identity.name ||
          (sport != null &&
              (sport is! String || sport.trim() != identity.sport)) ||
          (branch != null &&
              (branch is! String || branch.trim() != identity.branch))) {
        throw const ApiFailure(
          404,
          'انتهى الاختيار أو تغيّر التسجيل. أعد البحث.',
        );
      }
      await openPage(
        context,
        PlanPicker(
          auth: widget.auth,
          environment: widget.environment,
          childName: d['playerDisplayName'],
          reference: reference,
          externalRegistration: '${identity.sport} · ${identity.branch}',
          plans: rows(d['plans']),
          externalOpen: _readExternalOpen(d['openRenewal']),
        ),
      );
    } catch (e) {
      if (mounted) {
        setState(() {
          if (e is ApiFailure && e.status == 404) {
            matches = {};
            searched = false;
            error = 'انتهى الاختيار أو تغيّر التسجيل. أعد البحث.';
          } else {
            error = e is ApiFailure
                ? e.message
                : 'تعذر تأكيد التسجيل. أعد المحاولة.';
          }
        });
      }
    } finally {
      if (mounted) setState(() => busy = false);
    }
  }

  @override
  Widget build(BuildContext context) => GuardianPage(
    operation: true,
    title: 'تجديد اشتراك لشخص آخر',
    child: gList([
      TextField(
        controller: name,
        enabled: !busy,
        maxLength: 100,
        decoration: InputDecoration(labelText: ui(context, 'اسم اللاعب')),
        textInputAction: TextInputAction.search,
        onChanged: (_) => setState(() {
          error = null;
          searched = false;
          matches = {};
        }),
        onSubmitted: (_) => search(),
      ),
      FilledButton.icon(
        onPressed: busy ? null : search,
        icon: const Icon(Icons.search),
        label: UiText(busy ? 'جارٍ البحث…' : 'بحث عن لاعب'),
      ),
      if (error != null) Semantics(liveRegion: true, child: UiText(error!)),
      if (searched && matches.isEmpty)
        const EmptyMessage('لا توجد تسجيلات مطابقة للاسم.'),
      if (matches.isNotEmpty) const SizedBox(height: 12),
      for (final MapEntry(key: identity, value: reference) in matches.entries)
        if (reference != null)
          GLink(
            identity.name,
            Icons.person_outline,
            () => select(identity, reference),
            subtitle: '${identity.sport} · ${identity.branch}',
          )
        else
          GCard(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                UiText(
                  identity.name,
                  style: Theme.of(context).textTheme.titleMedium,
                ),
                UiText('${identity.sport} · ${identity.branch}'),
                const SizedBox(height: 8),
                const UiText(
                  'يوجد أكثر من تسجيل بهذه البيانات. تواصل مع الأكاديمية لتحديد التسجيل.',
                ),
              ],
            ),
          ),
    ]),
  );
}

class RenewalReview extends StatefulWidget {
  final AuthController auth;
  final String environment, childName;
  final Json plan;
  final String? enrollmentId, reference, externalRegistration;
  final VoidCallback? onUnavailable, onSaved;
  const RenewalReview({
    super.key,
    required this.auth,
    required this.environment,
    required this.childName,
    required this.plan,
    this.enrollmentId,
    this.reference,
    this.externalRegistration,
    this.onUnavailable,
    this.onSaved,
  });
  @override
  State<RenewalReview> createState() => _RenewalReviewState();
}

class _RenewalReviewState extends State<RenewalReview> {
  final key = commandKey();
  bool busy = false;
  bool unavailable = false;
  bool openConflict = false;
  bool checkingOpen = false, openCheckFailed = false;
  Json? externalOpen;
  String? error;
  bool get externalOpenRequest => openConflict && widget.reference != null;

  Future<void> readExternalOpen() async {
    if (checkingOpen) return;
    setState(() {
      checkingOpen = true;
      openCheckFailed = false;
    });
    try {
      final response = await widget.auth.request(
        'POST',
        '/api/v1/guardian/subscriptions/external/resolve',
        body: {'reference': widget.reference},
      );
      if (response is! Json || response['plans'] is! List) {
        throw const FormatException();
      }
      final open = _readExternalOpen(response['openRenewal']);
      if (mounted) setState(() => externalOpen = open);
    } on ApiFailure catch (e) {
      if (mounted) setState(() => openCheckFailed = e.status != 404);
    } catch (_) {
      if (mounted) setState(() => openCheckFailed = true);
    } finally {
      if (mounted) setState(() => checkingOpen = false);
    }
  }

  Future<void> submit() async {
    if (busy || unavailable) return;
    setState(() {
      busy = true;
      error = null;
    });
    try {
      final response = await widget.auth.request(
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
      );
      final paymentId = response is Json ? response['paymentId'] : null;
      if (paymentId is! String || paymentId.isEmpty) {
        throw const ApiFailure(0, 'تعذر تأكيد طلب الدفع.');
      }
      if (mounted) {
        widget.onSaved?.call();
        Navigator.of(context).pushReplacement(
          MaterialPageRoute<void>(
            builder: (_) => CheckoutPage(
              auth: widget.auth,
              environment: widget.environment,
              id: paymentId,
            ),
          ),
        );
      }
    } on ApiFailure catch (e) {
      if (mounted) {
        final conflict = e.status == 409 && e.code == 'renewal_already_open';
        if (e.status == 404 || conflict) widget.onUnavailable?.call();
        setState(() {
          unavailable = e.status == 404 || conflict;
          openConflict = conflict;
          error = e.status == 404
              ? 'الباقة أو التسجيل غير متاح الآن.'
              : e.status == 0 || e.status >= 500
              ? 'لم يصل تأكيد طلب الدفع. أعد المحاولة من نفس الشاشة.'
              : e.message;
        });
        if (conflict && widget.reference != null) await readExternalOpen();
      }
    } finally {
      if (mounted) setState(() => busy = false);
    }
  }

  @override
  Widget build(BuildContext context) => GuardianPage(
    operation: true,
    title: 'مراجعة التجديد',
    bottom: FilledButton(
      onPressed: busy && !externalOpenRequest
          ? null
          : unavailable
          ? () => Navigator.of(context).pop()
          : submit,
      child: UiText(
        unavailable
            ? widget.reference == null
                  ? openConflict
                        ? 'عرض حالة التجديد'
                        : 'تحديث الباقات'
                  : 'العودة إلى البحث'
            : busy
            ? 'جارٍ إنشاء الطلب…'
            : 'متابعة إلى الدفع',
      ),
    ),
    child: gList([
      Heading(widget.childName),
      if (widget.externalRegistration case final registration?)
        UiText(registration),
      if (externalOpenRequest)
        Semantics(
          liveRegion: true,
          child: checkingOpen
              ? const GCard(child: UiText('جارٍ التحقق من حالة الطلب…'))
              : openCheckFailed
              ? GCard(
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      const UiText('تعذر التحقق من حالة الطلب.'),
                      OutlinedButton(
                        onPressed: readExternalOpen,
                        child: const UiText('إعادة التحقق'),
                      ),
                    ],
                  ),
                )
              : externalOpen != null
              ? _OpenRenewalCard(
                  auth: widget.auth,
                  environment: widget.environment,
                  open: externalOpen!,
                  onReturn: () => Navigator.of(context).pop(),
                )
              : const GCard(
                  child: UiText('تغيّرت حالة الطلب. أعد البحث عن التسجيل.'),
                ),
        )
      else
        GCard(
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              UiText(textOf(widget.plan['arabicName'])),
              const SizedBox(height: 6),
              _RenewalPlanTerms(widget.plan),
              const SizedBox(height: 12),
              const UiText('سعر الباقة قبل الخصم'),
              UiText(
                money(widget.plan['price'], widget.plan['currency']),
                style: const TextStyle(fontSize: 28),
              ),
              if (widget.plan['trainingFormat'] == 'Season' &&
                  widget.plan['referenceMonthlyPrice'] != null)
                UiText(
                  'السعر الشهري المرجعي: ${money(widget.plan['referenceMonthlyPrice'], widget.plan['currency'])}',
                ),
            ],
          ),
        ),
      if (widget.reference != null && !externalOpenRequest)
        const UiText('هذا التجديد لا يربط اللاعب بحسابك.'),
      if (error != null && !externalOpenRequest)
        Semantics(
          liveRegion: true,
          child: UiText(
            error!,
            style: TextStyle(color: Theme.of(context).colorScheme.error),
          ),
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
  bool needsCheck = false;
  bool retryUncertain = false;
  String? error;
  int revision = 0;

  String get path =>
      '/api/v1/guardian/subscriptions/payments/${Uri.encodeComponent(widget.id)}';

  // Reopening the same failed payment must find the same retry request.
  String get retryKey => 'guardian-checkout-retry:${widget.id.toLowerCase()}';

  void refresh() {
    setState(() {
      revision++;
      error = null;
      needsCheck = false;
    });
  }

  void openPayment(String id) {
    Navigator.of(context).pushReplacement(
      MaterialPageRoute<void>(
        builder: (_) => CheckoutPage(
          auth: widget.auth,
          environment: widget.environment,
          id: id,
        ),
      ),
    );
  }

  Future<void> submit({String? outcome}) async {
    if (busy || needsCheck) return;
    final retry = outcome == null;
    setState(() {
      busy = true;
      error = null;
    });
    var navigating = false;
    try {
      final result = await widget.auth.request(
        'POST',
        '$path/${retry ? 'retry' : 'simulate'}',
        body: retry ? {} : {'outcome': outcome},
        idempotencyKey: retry ? retryKey : null,
      );
      if (!mounted) return;
      if (retry) {
        final next = result is Json ? result['paymentId'] : null;
        if (next is! String || next.trim().isEmpty || next == widget.id) {
          throw const ApiFailure(0, 'استجابة الخادم غير صالحة.');
        }
        openPayment(next);
        navigating = true;
      } else {
        refresh();
      }
    } catch (failure) {
      if (!mounted) return;
      final status = failure is ApiFailure ? failure.status : 0;
      final uncertain = status == 0 || status == 408 || status >= 500;
      setState(() {
        retryUncertain = retry && uncertain;
        needsCheck = !retryUncertain;
        error = retryUncertain
            ? 'لم يصل تأكيد إنشاء طلب الدفع. أعد إرسال الطلب نفسه.'
            : uncertain
            ? 'لم تتأكد نتيجة الدفع. تحقّق من الحالة قبل المحاولة مجددًا.'
            : failure is ApiFailure
            ? failure.message
            : 'تعذر إكمال إجراء الدفع.';
      });
    } finally {
      if (mounted && !navigating) setState(() => busy = false);
    }
  }

  @override
  Widget build(BuildContext context) => GuardianPage(
    operation: true,
    title: 'طلب دفع التجديد',
    child: RemoteBody(
      key: ValueKey((widget.id, revision)),
      auth: widget.auth,
      path: path,
      notFound: const PageStatus(
        child: StatusPanel(message: 'طلب الدفع غير متاح لهذا الحساب.'),
      ),
      builder: (context, value) {
        final d = value is Json ? value : const <String, dynamic>{};
        final paymentId = d['id'];
        final amount = _receiptAmount(d['amount']);
        final currency = d['currency'];
        final status = d['status'];
        final isCurrent = d['isCurrent'];
        if (paymentId is! String ||
            paymentId.toLowerCase() != widget.id.toLowerCase() ||
            amount == null ||
            currency is! String ||
            !RegExp(r'^[A-Z]{3}$').hasMatch(currency) ||
            status is! String ||
            isCurrent is! bool) {
          return PageStatus(
            child: StatusPanel(
              message: 'تعذر قراءة طلب الدفع.',
              actionLabel: 'تحديث حالة الدفع',
              onAction: refresh,
            ),
          );
        }

        final replaced = !isCurrent;
        final paid = status == 'Confirmed';
        final pending = isCurrent && status == 'Pending';
        final awaitingReview = pending && d['proofStatus'] == 'Pending';
        final rejected = d['proofStatus'] == 'Rejected';
        final simulation =
            widget.environment == 'Demo' &&
            d['provider'] == 'InternalTest' &&
            d['simulationAvailable'] == true;
        final canRetry =
            isCurrent &&
            !rejected &&
            const ['Failed', 'Cancelled', 'Expired'].contains(status);
        final currentId = d['currentPaymentId'];
        final receiptId = d['receiptId'];
        final discount = _receiptAmount(d['discountAmount']) ?? 0;
        final original = _receiptAmount(d['originalAmount']);
        final amountLabel = replaced
            ? 'مبلغ الطلب السابق'
            : paid
            ? 'المبلغ المدفوع'
            : 'المبلغ المطلوب';
        final theme = Theme.of(context);

        return gList([
          GCard(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Heading(textOf(d['player'])),
                UiText('${d['sport']} · ${d['plan']}'),
                const SizedBox(height: 12),
                if (replaced)
                  const UiText('استُبدل طلب الدفع')
                else
                  StatusPill(
                    paid
                        ? 'Paid'
                        : awaitingReview
                        ? 'UnderReview'
                        : status,
                  ),
                const SizedBox(height: 14),
                UiText(amountLabel, style: theme.textTheme.bodyMedium),
                MoneyText(amount, currency),
                if (!replaced && !paid && original != null && discount > 0) ...[
                  const SizedBox(height: 10),
                  UiText(
                    '${ui(context, 'السعر الأصلي')} · ${money(original, currency)}',
                  ),
                  UiText(
                    '${ui(context, 'الخصم')} · ${money(discount, currency)}',
                  ),
                ],
              ],
            ),
          ),
          if (error != null) Semantics(liveRegion: true, child: UiText(error!)),
          if (busy) const UiText('جارٍ تنفيذ الإجراء…'),
          if (needsCheck)
            FilledButton(
              onPressed: busy ? null : refresh,
              child: const UiText('التحقق من حالة الدفع'),
            )
          else if (replaced) ...[
            if (currentId is String &&
                currentId.isNotEmpty &&
                currentId != widget.id)
              FilledButton(
                onPressed: () => openPayment(currentId),
                child: const UiText('فتح طلب الدفع الحالي'),
              ),
          ] else if (paid && receiptId is String && receiptId.isNotEmpty)
            FilledButton(
              onPressed: () => openPage(
                context,
                ReceiptPage(auth: widget.auth, id: receiptId),
              ),
              child: const UiText('عرض الإيصال'),
            )
          else if (awaitingReview)
            const GCard(
              child: UiText(
                'وصل إثبات الدفع. تنتظر العملية مراجعة الإدارة؛ لا تحتاج إلى الدفع مرة أخرى.',
              ),
            )
          else if (rejected)
            const GCard(
              child: UiText(
                'لم تعتمد الأكاديمية إثبات الدفع. راجعها بشأن التحويل السابق قبل إرسال مبلغ جديد.',
              ),
            )
          else if (pending && d['provider'] == 'Manual')
            TransferPayment(
              auth: widget.auth,
              proofPath:
                  '/api/v1/guardian/payments/${Uri.encodeComponent(widget.id)}/proof',
              showConfirmation: false,
              onSubmitted: refresh,
            )
          else if (pending && simulation) ...[
            const UiText('دفع تجريبي فقط · لن تُخصم أموال حقيقية'),
            FilledButton(
              onPressed: busy ? null : () => submit(outcome: 'Success'),
              child: const UiText('محاكاة دفع ناجح'),
            ),
            TextButton(
              onPressed: busy ? null : () => submit(outcome: 'Failed'),
              child: const UiText('محاكاة فشل الدفع'),
            ),
            TextButton(
              onPressed: busy ? null : () => submit(outcome: 'Cancelled'),
              child: const UiText('إلغاء الدفع التجريبي'),
            ),
          ] else if (pending)
            const UiText(
              'طريقة الدفع غير متاحة لهذا الطلب. تواصل مع الأكاديمية.',
            )
          else if (canRetry)
            FilledButton(
              onPressed: busy ? null : () => submit(),
              child: UiText(
                retryUncertain
                    ? 'إعادة إرسال طلب الدفع نفسه'
                    : 'إعادة محاولة الدفع',
              ),
            ),
          if (!needsCheck)
            TextButton(
              onPressed: busy ? null : refresh,
              child: const UiText('تحديث حالة الدفع'),
            ),
        ]);
      },
    ),
  );
}

String? receiptDateLabel(BuildContext context, Object? value) {
  if (value is! String) return null;
  final dateOnly = RegExp(r'^\d{4}-\d{2}-\d{2}$').hasMatch(value);
  final date = dateOnly
      ? DateTime.tryParse('${value}T00:00:00Z')
      : DateTime.tryParse(value)?.toLocal();
  if (date == null) return null;
  if (!value.contains('T')) {
    return MaterialLocalizations.of(context).formatShortDate(date);
  }
  return _receiptDateTime(context, date);
}

String _receiptDateTime(BuildContext context, DateTime date) {
  final localization = MaterialLocalizations.of(context);
  final day = localization.formatShortDate(date);
  return '$day · ${localization.formatTimeOfDay(TimeOfDay.fromDateTime(date))}';
}

final _offsetTimestamp = RegExp(
  r'^(\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d{1,7})?)(?:Z|[+-]\d{2}:\d{2})$',
);

({String text, String zone})? receiptTimestamp(
  BuildContext context,
  Object? utcValue,
  Object? academyValue,
) {
  if (utcValue is! String || !_offsetTimestamp.hasMatch(utcValue)) return null;
  final utc = DateTime.tryParse(utcValue);
  if (utc == null) return null;
  if (academyValue is String) {
    final wallTime = _offsetTimestamp.firstMatch(academyValue)?.group(1);
    final academy = DateTime.tryParse(academyValue);
    if (wallTime != null && academy != null && academy.isAtSameMomentAs(utc)) {
      return (
        text: _receiptDateTime(context, DateTime.parse('${wallTime}Z')),
        zone: localized(context, 'بتوقيت الأكاديمية', 'Academy time'),
      );
    }
  }
  return (
    text: _receiptDateTime(context, utc.toUtc()),
    zone: localized(context, 'بالتوقيت العالمي', 'UTC'),
  );
}

String? _receiptMethod(BuildContext context, Object? method) {
  if (method is! String || method.isEmpty) return null;
  return switch (method) {
    'Cash' || 'cash' => localized(context, 'نقدًا', 'Cash'),
    'Online' => localized(context, 'الدفع الإلكتروني', 'Online payment'),
    'InternalTest' => localized(context, 'دفع تجريبي', 'Test payment'),
    'ManualTransfer' ||
    'BankTransfer' => localized(context, 'تحويل بنكي', 'Bank transfer'),
    _ =>
      RegExp(r'[\u0600-\u06ff]').hasMatch(method)
          ? method
          : localized(
              context,
              'غير محددة في الإيصال',
              'Unspecified on receipt',
            ),
  };
}

num? _receiptAmount(Object? value) {
  final number =
      value is String && RegExp(r'^\d+(?:\.\d{1,2})?$').hasMatch(value.trim())
      ? num.tryParse(value)
      : value;
  return number is num && number.isFinite && number >= 0 ? number : null;
}

class ReceiptsPage extends StatefulWidget {
  const ReceiptsPage({super.key, required this.auth, this.playerId});
  final AuthController auth;
  final String? playerId;

  @override
  State<ReceiptsPage> createState() => _ReceiptsPageState();
}

class _ReceiptsPageState extends State<ReceiptsPage> {
  late String selected = widget.playerId ?? '';

  void selectPlayer(String id) {
    if (id != selected) setState(() => selected = id);
  }

  @override
  Widget build(BuildContext context) => GuardianPage(
    operation: true,
    title: 'إيصالاتي',
    child: RemoteBody(
      key: ValueKey(selected),
      auth: widget.auth,
      path:
          '/api/v1/guardian/subscriptions/receipts?page=1&allIssued=true'
          '${selected.isEmpty ? '' : '&playerId=${Uri.encodeQueryComponent(selected)}'}',
      builder: (context, value) {
        final data = obj(value);
        final items = rows(data['items']);
        final children = rows(data['children'] ?? const []);
        final useMenu =
            children.length > 4 ||
            children.any((child) => _receiptPlayerCode(child) != null);
        final selectedName =
            children
                    .where((child) => child['id'] == selected)
                    .firstOrNull?['arabicName']
                as String?;
        return gList([
          if (useMenu)
            Padding(
              padding: const EdgeInsets.only(bottom: 8),
              child: _ReceiptChildMenu(
                children: children,
                selected: selected,
                onSelected: selectPlayer,
              ),
            )
          else if (children.isNotEmpty)
            FamilyChildFilter(
              children: children,
              selected: selected,
              allLabel: localized(context, 'كل الإيصالات', 'All receipts'),
              onSelected: selectPlayer,
            ),
          if (items.isEmpty)
            EmptyMessage(
              selected.isEmpty
                  ? 'لا توجد إيصالات لحسابك بعد.'
                  : 'لا توجد إيصالات لهذا اللاعب.',
            ),
          for (final receipt in items)
            _ReceiptListCard(
              receipt: receipt,
              selectedName: selectedName,
              onTap: () => openPage(
                context,
                ReceiptPage(
                  auth: widget.auth,
                  id: textOf(receipt['id']),
                  tournament: receipt['source'] == 'Tournament',
                ),
              ),
            ),
        ]);
      },
    ),
  );
}

String? _receiptPlayerCode(Json player) {
  final code = player['playerCode'];
  return code is String && code.trim().isNotEmpty ? code : null;
}

class _ReceiptChildMenu extends StatelessWidget {
  const _ReceiptChildMenu({
    required this.children,
    required this.selected,
    required this.onSelected,
  });

  final List<Json> children;
  final String selected;
  final ValueChanged<String> onSelected;

  @override
  Widget build(BuildContext context) {
    final all = localized(context, 'كل الإيصالات', 'All receipts');
    final codeLabel = localized(context, 'كود اللاعب', 'Player code');
    final secondary = Theme.of(context).textTheme.labelMedium;
    return DropdownButtonFormField<String>(
      key: const ValueKey('receipt-child-menu'),
      initialValue: children.any((child) => child['id'] == selected)
          ? selected
          : '',
      decoration: InputDecoration(
        labelText: localized(context, 'اللاعب', 'Player'),
      ),
      isDense: false,
      isExpanded: true,
      itemHeight: null,
      menuMaxHeight: MediaQuery.sizeOf(context).height * .65,
      items: [
        DropdownMenuItem(value: '', child: UiText(all)),
        for (final child in children)
          DropdownMenuItem(
            value: textOf(child['id']),
            child: Padding(
              padding: const EdgeInsets.symmetric(vertical: 6),
              child: Column(
                mainAxisSize: MainAxisSize.min,
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  UiText(
                    textOf(child['arabicName']),
                    maxLines: 2,
                    overflow: TextOverflow.ellipsis,
                  ),
                  if (_receiptPlayerCode(child) case final code?)
                    UiText(
                      code,
                      style: secondary,
                      textDirection: TextDirection.ltr,
                      semanticsLabel: '$codeLabel $code',
                    ),
                ],
              ),
            ),
          ),
      ],
      onChanged: (id) {
        if (id != null) onSelected(id);
      },
    );
  }
}

class _ReceiptListCard extends StatelessWidget {
  const _ReceiptListCard({
    required this.receipt,
    required this.onTap,
    this.selectedName,
  });
  final Json receipt;
  final VoidCallback onTap;
  final String? selectedName;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final historical = receiptDateLabel(context, receipt['historicalPaidOn']);
    final date =
        historical ??
        (receipt['source'] == 'Tournament'
            ? receiptDateLabel(context, receipt['paidAtUtc'])
            : receiptTimestamp(
                context,
                receipt['paidAtUtc'],
                receipt['paidAtAcademy'],
              )?.text);
    final originalName = textOf(receipt['player']);
    final name =
        (receipt['currentPlayer'] as String?) ?? selectedName ?? originalName;
    final subtitle = [
      if (receipt['source'] == 'Tournament') ui(context, 'رسم بطولة'),
      if (receipt['linkedChild'] == false) ui(context, 'تجديد لغير الأبناء'),
      if (receipt['historicalPaidOn'] is String) ui(context, 'تحصيل سابق'),
      receipt['sport'],
      receipt['plan'],
    ].whereType<String>().where((value) => value.isNotEmpty).join(' · ');
    return GCard(
      padding: const EdgeInsets.all(14),
      onTap: onTap,
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Wrap(
            alignment: WrapAlignment.spaceBetween,
            crossAxisAlignment: WrapCrossAlignment.center,
            spacing: 12,
            runSpacing: 4,
            children: [
              UiText(name, style: theme.textTheme.titleMedium),
              UiText(
                money(
                  receipt['finalAmount'] ?? receipt['amount'],
                  receipt['currency'],
                ),
                style: theme.textTheme.titleMedium?.copyWith(
                  color: theme.colorScheme.primary,
                ),
              ),
            ],
          ),
          if (_receiptPlayerCode(receipt) case final code?)
            UiText(
              '${localized(context, 'كود اللاعب', 'Player code')} \u2066$code\u2069',
              style: theme.textTheme.labelMedium,
            ),
          if (name != originalName)
            UiText(
              '${ui(context, 'الاسم وقت الدفع')}: $originalName',
              style: theme.textTheme.labelMedium,
            ),
          if (subtitle.isNotEmpty)
            UiText(subtitle, style: theme.textTheme.bodyMedium),
          if (receipt['isVoided'] == true)
            UiText(
              'إيصال معكوس',
              style: TextStyle(color: theme.colorScheme.error),
            ),
          const SizedBox(height: 4),
          Row(
            children: [
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    if (date != null)
                      UiText(date, style: theme.textTheme.labelMedium),
                    UiText(
                      textOf(receipt['receiptNumber']),
                      textDirection: TextDirection.ltr,
                      style: theme.textTheme.labelMedium,
                    ),
                  ],
                ),
              ),
              ExcludeSemantics(
                child: Icon(
                  Icons.arrow_forward_ios_rounded,
                  size: 16,
                  color: theme.colorScheme.primary,
                ),
              ),
            ],
          ),
        ],
      ),
    );
  }
}

class ReceiptPage extends StatelessWidget {
  final AuthController auth;
  final String id;
  final bool staff, tournament;
  final VoidCallback? onBack;
  const ReceiptPage({
    super.key,
    required this.auth,
    required this.id,
    this.staff = false,
    this.tournament = false,
    this.onBack,
  });
  @override
  Widget build(BuildContext context) => GuardianPage(
    operation: true,
    title: tournament ? 'إيصال رسم البطولة' : 'إيصال دفع',
    onBack: onBack,
    child: RemoteBody(
      auth: auth,
      path: tournament
          ? '/api/v1/guardian/tournament-payments/${Uri.encodeComponent(id)}'
          : staff
          ? '/api/v1/subscriptions/receipts/${Uri.encodeComponent(id)}'
          : '/api/v1/guardian/subscriptions/receipts/${Uri.encodeComponent(id)}',
      builder: (context, value) {
        final d = obj(value);
        final historical = receiptDateLabel(context, d['historicalPaidOn']);
        final paymentTime = historical == null && !tournament
            ? receiptTimestamp(context, d['paidAtUtc'], d['paidAtAcademy'])
            : null;
        final date =
            historical ??
            (tournament
                ? receiptDateLabel(context, d['confirmedAtUtc'])
                : paymentTime?.text);
        final period = d['historicalPeriod'];
        final start = period is Map
            ? receiptDateLabel(context, period['startDate'])
            : null;
        final end = period is Map
            ? receiptDateLabel(context, period['endDate'])
            : null;
        final paid = _receiptAmount(d['finalAmount'] ?? d['amount']);
        final currency = d['currency'];
        if (d['receiptNumber'] is! String ||
            (d['receiptNumber'] as String).trim().isEmpty ||
            paid == null ||
            currency is! String ||
            !RegExp(r'^[A-Z]{3}$').hasMatch(currency) ||
            date == null ||
            (tournament && d['status'] != 'Confirmed')) {
          return gList([const EmptyMessage('الإيصال غير متاح.')]);
        }
        final theme = Theme.of(context);
        final enrollment = d['enrollmentBranch'];
        final collecting = d['collectionBranch'];
        final paymentMethod = d['paymentMethod'];
        final method = _receiptMethod(
          context,
          paymentMethod is String ? paymentMethod.trim() : null,
        );
        final amount = money(paid, currency);
        final original = _receiptAmount(d['originalAmount']);
        final discount = _receiptAmount(d['discountAmount']);
        final candidate = d['providerReference'];
        final reference = candidate is String ? candidate.trim() : '';
        final cash =
            paymentMethod is String &&
            paymentMethod.trim().toLowerCase() == 'cash';
        final showReference =
            historical == null &&
            !cash &&
            reference.isNotEmpty &&
            !RegExp(
              r'^manual-[a-f0-9]{32}$',
              caseSensitive: false,
            ).hasMatch(reference);
        final reversalTime = receiptTimestamp(
          context,
          d['voidedAtUtc'],
          d['voidedAtAcademy'],
        );
        final player = textOf(d[tournament ? 'player' : 'playerNameSnapshot']);
        final linkedName = d['currentPlayer'];
        return gList([
          if (d['isVoided'] == true)
            GCard(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  UiText(
                    'إيصال معكوس',
                    style: theme.textTheme.titleMedium?.copyWith(
                      color: theme.colorScheme.error,
                    ),
                  ),
                  const UiText(
                    'رُدت القيمة كاملة وأُلغيت فترة الاشتراك المرتبطة.',
                  ),
                  if (reversalTime != null)
                    UiText(
                      '${ui(context, 'تاريخ العكس')} ${reversalTime.text} · ${reversalTime.zone}',
                    ),
                ],
              ),
            ),
          GCard(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: [
                if (d['academyName'] is String &&
                    (d['academyName'] as String).isNotEmpty)
                  UiText(
                    textOf(d['academyName']),
                    style: theme.textTheme.labelLarge,
                  ),
                Heading(player),
                if (historical != null)
                  UiText('تحصيل سابق', style: theme.textTheme.labelLarge),
                if (linkedName is String && linkedName != player)
                  UiText('${ui(context, 'اللاعب المرتبط الآن')}: $linkedName'),
                const SizedBox(height: 8),
                UiText('المبلغ المدفوع', style: theme.textTheme.labelLarge),
                UiText(amount, style: theme.textTheme.titleLarge),
                const Divider(height: 22),
                _ReceiptNumber(
                  label: 'رقم الإيصال',
                  value: d['receiptNumber'] as String,
                ),
                UiText(
                  '${ui(context, historical != null ? 'تاريخ القبض' : 'تاريخ ووقت الدفع')}: $date',
                ),
                if (paymentTime != null)
                  UiText(paymentTime.zone, style: theme.textTheme.labelMedium),
                if (start != null)
                  UiText(
                    'فترة الاشتراك من $start${end == null ? '' : ' إلى $end'}',
                  ),
                if (d[tournament ? 'tournamentName' : 'planNameSnapshot']
                    is String)
                  UiText(
                    '${ui(context, tournament ? 'البطولة' : 'الباقة')}: ${d[tournament ? 'tournamentName' : 'planNameSnapshot']}',
                  ),
                if (d['sportNameSnapshot'] is String)
                  UiText(
                    '${ui(context, 'الرياضة')}: ${d['sportNameSnapshot']}',
                  ),
                if (enrollment is String && enrollment.isNotEmpty)
                  UiText(
                    '${ui(context, collecting == enrollment ? 'فرع التسجيل والتحصيل' : 'فرع التسجيل')}: $enrollment',
                  ),
                if (collecting is String &&
                    collecting.isNotEmpty &&
                    collecting != enrollment)
                  UiText('${ui(context, 'فرع التحصيل')}: $collecting'),
                if (discount != null && discount > 0) ...[
                  if (original != null)
                    UiText(
                      '${ui(context, 'السعر الأصلي')}: ${money(original, currency)}',
                    ),
                  UiText(
                    '${ui(context, 'الخصم')}: ${money(discount, currency)}',
                  ),
                ],
                if (method != null)
                  UiText('${ui(context, 'طريقة الدفع')}: $method'),
                if (showReference)
                  _ReceiptNumber(label: 'مرجع الدفع', value: reference),
              ],
            ),
          ),
        ]);
      },
    ),
  );
}

class _ReceiptNumber extends StatelessWidget {
  const _ReceiptNumber({required this.label, required this.value});
  final String label, value;

  @override
  Widget build(BuildContext context) => Padding(
    padding: const EdgeInsets.only(bottom: 6),
    child: Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        UiText(label, style: Theme.of(context).textTheme.labelMedium),
        SelectableText(
          value,
          textDirection: TextDirection.ltr,
          textAlign: TextAlign.right,
        ),
      ],
    ),
  );
}
