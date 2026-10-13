import 'package:academy_mobile/core/theme/ui_text.dart';
import 'package:flutter/material.dart';

import '../../core/api/api_client.dart';
import '../../core/auth/auth_controller.dart';
import '../../core/theme/appearance.dart';
import '../shared/transfer_payment.dart';
import 'guardian_ui.dart';

class GuardianDayPassPage extends StatefulWidget {
  final AuthController auth;
  const GuardianDayPassPage({super.key, required this.auth});

  @override
  State<GuardianDayPassPage> createState() => _GuardianDayPassPageState();
}

class _GuardianDayPassPageState extends State<GuardianDayPassPage> {
  List<Json> children = [], bookings = [], tickets = [];
  String? playerId, bookingId, planId, error, notice;
  Json? quote;
  String? intentKey;
  bool loading = true, busy = false;

  @override
  void initState() {
    super.initState();
    load();
  }

  Future<void> load() async {
    try {
      final options = obj(
        await widget.auth.request('GET', '/api/v1/guardian/day-pass/options'),
      );
      final mine = rows(
        await widget.auth.request('GET', '/api/v1/guardian/day-pass/orders'),
      );
      if (mounted) {
        setState(() {
          children = rows(options['children']);
          bookings = rows(options['bookings']);
          tickets = mine;
          loading = false;
          error = null;
        });
      }
    } catch (e) {
      if (mounted) {
        setState(() {
          loading = false;
          error = e is ApiFailure ? e.message : 'تعذر تحميل الزيارات اليومية.';
        });
      }
    }
  }

  Json? get booking =>
      bookings.where((b) => b['bookingId'] == bookingId).firstOrNull;
  List<Json> get plans => booking == null ? [] : rows(booking!['plans']);
  String timePart(Object? value) {
    final raw = textOf(value);
    return raw.length >= 5 ? raw.substring(0, 5) : raw;
  }

  void resetQuote() => setState(() {
    quote = null;
    intentKey = null;
    notice = null;
  });

  Future<void> review() async {
    if (busy || playerId == null || bookingId == null || planId == null) return;
    setState(() {
      busy = true;
      error = null;
      quote = null;
    });
    try {
      final next = obj(
        await widget.auth.request(
          'POST',
          '/api/v1/guardian/day-pass/quote',
          body: {
            'playerId': playerId,
            'bookingId': bookingId,
            'planId': planId,
          },
        ),
      );
      if (mounted) {
        setState(() {
          quote = next;
          intentKey = commandKey();
        });
      }
    } catch (e) {
      if (mounted) {
        setState(
          () => error = e is ApiFailure
              ? e.message
              : 'تعذر مراجعة السعر والموعد.',
        );
      }
    } finally {
      if (mounted) setState(() => busy = false);
    }
  }

  Future<void> order() async {
    if (busy || quote == null || intentKey == null) return;
    setState(() {
      busy = true;
      error = null;
    });
    try {
      await widget.auth.request(
        'POST',
        '/api/v1/guardian/day-pass/orders',
        body: {
          'playerId': playerId,
          'bookingId': bookingId,
          'planId': planId,
          'quoteHash': quote!['quoteHash'],
        },
        idempotencyKey: intentKey,
      );
      if (mounted) {
        setState(() {
          quote = null;
          notice = localized(
            context,
            'أُنشئ الطلب. حوّل المبلغ ثم ارفع الإثبات؛ الدخول بعد اعتماد الإدارة فقط.',
            'Request created. Transfer and upload proof; admission follows staff approval.',
          );
        });
      }
      await load();
    } catch (e) {
      if (mounted) {
        await load();
        if (mounted) {
          setState(
            () => error = e is ApiFailure
                ? e.message
                : 'تعذر حسم الطلب. راجع القائمة قبل المحاولة مجددًا.',
          );
        }
      }
    } finally {
      if (mounted) setState(() => busy = false);
    }
  }

  @override
  Widget build(BuildContext context) => GuardianPage(
    title: localized(context, 'الزيارة اليومية', 'Day pass'),
    child: loading
        ? const Center(child: CircularProgressIndicator())
        : gList([
            UiText(
              localized(
                context,
                'زيارة مستقلة عن التدريب؛ لا تُخصم حصة ولا يُسجل حضور تدريب.',
                'Independent visit: no training session or attendance is consumed.',
              ),
            ),
            if (error != null)
              GCard(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.stretch,
                  children: [
                    UiText(error!),
                    TextButton.icon(
                      onPressed: busy ? null : load,
                      icon: const Icon(Icons.refresh),
                      label: UiText(
                        localized(context, 'تحديث الطلبات', 'Refresh requests'),
                      ),
                    ),
                  ],
                ),
              ),
            if (notice != null) GCard(child: UiText(notice!)),
            if (bookings.isNotEmpty)
              GCard(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.stretch,
                  children: [
                    Heading(localized(context, 'طلب زيارة', 'Book a visit')),
                    DropdownButtonFormField<String>(
                      isExpanded: true,
                      key: ValueKey('day-pass-child-$playerId'),
                      initialValue: playerId,
                      decoration: InputDecoration(
                        labelText: localized(context, 'الطفل', 'Child'),
                      ),
                      items: [
                        for (final child in children)
                          DropdownMenuItem(
                            value: textOf(child['id']),
                            child: UiText(
                              textOf(child['name']),
                              overflow: TextOverflow.ellipsis,
                              maxLines: 1,
                            ),
                          ),
                      ],
                      onChanged: busy
                          ? null
                          : (v) {
                              setState(() => playerId = v);
                              resetQuote();
                            },
                    ),
                    const SizedBox(height: 12),
                    DropdownButtonFormField<String>(
                      isExpanded: true,
                      key: ValueKey('day-pass-booking-$bookingId'),
                      initialValue: bookingId,
                      decoration: InputDecoration(
                        labelText: localized(context, 'الموعد', 'Visit time'),
                      ),
                      items: [
                        for (final b in bookings)
                          DropdownMenuItem(
                            value: textOf(b['bookingId']),
                            child: UiText(
                              '${b['resource']} · ${day(b['date'])} · ${timePart(b['startTime'])}',
                              overflow: TextOverflow.ellipsis,
                              maxLines: 1,
                            ),
                          ),
                      ],
                      onChanged: busy
                          ? null
                          : (v) {
                              setState(() {
                                bookingId = v;
                                planId = null;
                              });
                              resetQuote();
                            },
                    ),
                    const SizedBox(height: 12),
                    DropdownButtonFormField<String>(
                      isExpanded: true,
                      key: ValueKey('day-pass-plan-$bookingId-$planId'),
                      initialValue: planId,
                      decoration: InputDecoration(
                        labelText: localized(
                          context,
                          'باقة الزيارة',
                          'Visit plan',
                        ),
                      ),
                      items: [
                        for (final plan in plans)
                          DropdownMenuItem(
                            value: textOf(plan['id']),
                            child: UiText(
                              '${plan['name']} · ${money(plan['amount'], plan['currency'])}',
                              overflow: TextOverflow.ellipsis,
                              maxLines: 1,
                            ),
                          ),
                      ],
                      onChanged: busy
                          ? null
                          : (v) {
                              setState(() => planId = v);
                              resetQuote();
                            },
                    ),
                    const SizedBox(height: 14),
                    FilledButton(
                      onPressed:
                          busy ||
                              playerId == null ||
                              bookingId == null ||
                              planId == null
                          ? null
                          : review,
                      child: UiText(
                        localized(
                          context,
                          'مراجعة السعر والموعد',
                          'Review price and time',
                        ),
                      ),
                    ),
                  ],
                ),
              ),
            if (bookings.isEmpty && error == null)
              const EmptyMessage('لا توجد مواعيد زيارة منشورة الآن.'),
            if (quote != null)
              GCard(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.stretch,
                  children: [
                    Heading(
                      localized(
                        context,
                        'تأكيد طلب الزيارة',
                        'Confirm visit request',
                      ),
                    ),
                    UiText('${quote!['player']} · ${quote!['resource']}'),
                    UiText(
                      '${day(quote!['bookingDate'])} · ${quote!['startTime']}–${quote!['endTime']}',
                    ),
                    UiText(
                      '${quote!['plan']} · ${money(quote!['amount'], quote!['currency'])}',
                      style: Theme.of(context).textTheme.titleMedium,
                    ),
                    const SizedBox(height: 8),
                    UiText(
                      localized(
                        context,
                        'صورة التحويل ليست تأكيدًا للدفع. ستراجع الإدارة وصول المبلغ.',
                        'A screenshot is not payment confirmation. Staff will verify receipt of funds.',
                      ),
                    ),
                    const SizedBox(height: 12),
                    FilledButton(
                      onPressed: busy ? null : order,
                      child: UiText(
                        localized(
                          context,
                          'إنشاء طلب واحد',
                          'Create one request',
                        ),
                      ),
                    ),
                  ],
                ),
              ),
            Heading(localized(context, 'طلبات الزيارة', 'Visit requests')),
            if (tickets.isEmpty) const EmptyMessage('لا توجد طلبات زيارة بعد.'),
            for (final ticket in tickets)
              GCard(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.stretch,
                  children: [
                    UiText(
                      '${ticket['planName']} · ${money(ticket['amount'], ticket['currency'])}',
                      style: Theme.of(context).textTheme.titleMedium,
                    ),
                    UiText(label(ticket['status'])),
                    if (ticket['reviewReason'] != null)
                      UiText(
                        '${localized(context, 'سبب الرفض', 'Reason')}: ${ticket['reviewReason']}',
                      ),
                    if (ticket['status'] == 'PendingProof')
                      TransferPayment(
                        key: ValueKey(ticket['id']),
                        auth: widget.auth,
                        subscription: false,
                        proofPath:
                            '/api/v1/guardian/day-pass/orders/${ticket['id']}/proof',
                        onSubmitted: load,
                      ),
                  ],
                ),
              ),
          ]),
  );
}
