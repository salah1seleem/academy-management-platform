import 'dart:async';

import 'package:flutter/material.dart';

import '../../core/auth/auth_controller.dart';
import '../../core/theme/ui_text.dart';
import '../guardian/guardian_ui.dart';
import 'matches.dart';
import 'vip_rewards.dart';
import 'receivable_balances.dart';
import '../guardian/guardian_payments.dart';
import '../guardian/guardian_report.dart';
import '../guardian/guardian_profile.dart';
import '../guardian/coach_feedback.dart';
import '../guardian/family_renewal_request.dart';
import '../guardian/tournament_fees.dart';
import 'transfer_payment.dart';
import 'role_agenda.dart';
import 'role_navigation.dart';
import '../owner/owner_feedback_detail.dart';

class NotificationBell extends StatefulWidget {
  const NotificationBell({super.key, required this.auth});
  final AuthController auth;
  @override
  State<NotificationBell> createState() => _NotificationBellState();
}

class _NotificationBellState extends State<NotificationBell>
    with WidgetsBindingObserver {
  int unread = 0;
  late String? identity;
  int generation = 0;
  Timer? timer;
  bool foreground = true;
  String? get contextKey => widget.auth.session?.selected == null
      ? null
      : '${widget.auth.session!.selected!.id}:${widget.auth.session!.selected!.contextRevision}';
  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addObserver(this);
    timer = Timer.periodic(const Duration(seconds: 30), (_) {
      if (foreground) load();
    });
    identity = contextKey;
    widget.auth.addListener(changed);
    load();
  }

  void changed() {
    if (identity == contextKey) return;
    identity = contextKey;
    unread = 0;
    load();
  }

  Future<void> load() async {
    final g = ++generation;
    try {
      final d = obj(await widget.auth.request('GET', '/api/v1/notifications'));
      if (mounted && g == generation) {
        setState(() => unread = d['unread'] as int? ?? 0);
      }
    } catch (_) {
      if (mounted && g == generation) setState(() => unread = 0);
    }
  }

  @override
  void dispose() {
    timer?.cancel();
    WidgetsBinding.instance.removeObserver(this);
    generation++;
    widget.auth.removeListener(changed);
    super.dispose();
  }

  @override
  void didChangeAppLifecycleState(AppLifecycleState state) {
    foreground = state == AppLifecycleState.resumed;
    if (foreground) load();
  }

  @override
  Widget build(BuildContext context) => IconButton(
    tooltip: 'التنبيهات',
    icon: Badge(
      isLabelVisible: unread > 0,
      label: UiText('$unread', style: const TextStyle(fontSize: 9)),
      child: const Icon(Icons.notifications_none_rounded),
    ),
    onPressed: () async {
      await openPage(context, NotificationInbox(auth: widget.auth));
      if (mounted) load();
    },
  );
}

class NotificationInbox extends StatefulWidget {
  const NotificationInbox({super.key, required this.auth});
  final AuthController auth;
  @override
  State<NotificationInbox> createState() => _NotificationInboxState();
}

class _NotificationInboxState extends State<NotificationInbox> {
  int page = 1, revision = 0;
  bool busy = false;
  Future<void> read(Json n) async {
    if (busy) return;
    setState(() => busy = true);
    try {
      await widget.auth.request(
        'PUT',
        '/api/v1/notifications/${n['id']}/read',
        body: {'isRead': n['isRead'] != true},
      );
      if (mounted) setState(() => revision++);
    } catch (_) {
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          const SnackBar(content: UiText('تعذر تحديث حالة التنبيه.')),
        );
      }
    } finally {
      if (mounted) setState(() => busy = false);
    }
  }

  @override
  Widget build(BuildContext context) => GuardianPage(
    title: 'التنبيهات',
    child: RemoteBody(
      key: ValueKey(revision),
      auth: widget.auth,
      path: '/api/v1/notifications?page=$page',
      builder: (context, value) {
        final d = obj(value);
        return gList([
          UiText(
            '${d['unread']} غير مقروءة',
            style: const TextStyle(fontWeight: FontWeight.w800),
          ),
          const SizedBox(height: 12),
          if (rows(d['items']).isEmpty) const EmptyMessage('لا توجد تنبيهات.'),
          for (final n in rows(d['items']))
            GCard(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Row(
                    children: [
                      Icon(
                        n['isRead'] == true
                            ? Icons.notifications_none
                            : Icons.mark_email_unread_outlined,
                        size: 20,
                      ),
                      const SizedBox(width: 8),
                      Expanded(
                        child: UiText(
                          day(n['createdAtUtc']),
                          style: const TextStyle(fontSize: 10),
                        ),
                      ),
                    ],
                  ),
                  const SizedBox(height: 10),
                  UiText(
                    textOf(n['message']),
                    style: TextStyle(
                      fontSize: 13,
                      fontWeight: n['isRead'] == true
                          ? FontWeight.w400
                          : FontWeight.w700,
                    ),
                  ),
                  Wrap(
                    spacing: 8,
                    children: [
                      TextButton(
                        onPressed: busy ? null : () => read(n),
                        child: UiText(
                          n['isRead'] == true
                              ? 'تحديد كغير مقروء'
                              : 'تمت القراءة',
                          style: const TextStyle(fontSize: 11),
                        ),
                      ),
                      if (n['matchId'] != null)
                        TextButton(
                          onPressed: () => openPage(
                            context,
                            MatchSchedule(
                              auth: widget.auth,
                              matchId: textOf(n['matchId']),
                            ),
                          ),
                          child: const UiText('عرض المباراة'),
                        ),
                      if (n['reward'] == true)
                        TextButton(
                          onPressed: () => openPage(
                            context,
                            NativeVipRewards(auth: widget.auth),
                          ),
                          child: const UiText('عرض المكافأة'),
                        ),
                      if (n['targetKind'] == 'Receivables')
                        TextButton(
                          onPressed: () => openPage(
                            context,
                            ReceivableBalances(auth: widget.auth),
                          ),
                          child: const UiText('كشف المستحقات'),
                        ),
                      if (n['targetKind'] == 'Renewal')
                        TextButton(
                          onPressed: () => openPage(
                            context,
                            RenewalPicker(
                              auth: widget.auth,
                              environment: const String.fromEnvironment(
                                'APP_ENV',
                                defaultValue: 'Production',
                              ),
                            ),
                          ),
                          child: const UiText('تجديد الاشتراك'),
                        ),
                      if (n['targetKind'] == 'Evaluation' &&
                          n['targetId'] != null)
                        TextButton(
                          onPressed: () => openPage(
                            context,
                            PlayerReport(
                              auth: widget.auth,
                              id: textOf(n['targetId']),
                            ),
                          ),
                          child: const UiText('عرض التقييم'),
                        ),
                      if (n['targetKind'] == 'Stars' && n['targetId'] != null)
                        TextButton(
                          onPressed: () => openPage(
                            context,
                            ChildProfile(
                              auth: widget.auth,
                              environment: const String.fromEnvironment(
                                'APP_ENV',
                                defaultValue: 'Production',
                              ),
                              playerId: textOf(n['targetId']),
                              today: day(n['createdAtUtc']),
                            ),
                          ),
                          child: const UiText('عرض إنجاز اللاعب'),
                        ),
                      if (n['targetKind'] == 'Product' && n['targetId'] != null)
                        TextButton(
                          onPressed: () => openPage(
                            context,
                            GuardianPage(
                              title: 'طلب المنتج الرياضي',
                              child: RemoteBody(
                                auth: widget.auth,
                                path:
                                    '/api/v1/guardian/product-sales/${n['targetId']}',
                                builder: (context, value) {
                                  final r = obj(value);
                                  return gList([
                                    Heading(textOf(r['productName'])),
                                    StatusPill(r['status']),
                                    Facts([
                                      ('الكمية', textOf(r['quantity'])),
                                      (
                                        'المبلغ',
                                        money(r['amount'], r['currency']),
                                      ),
                                      (
                                        'رد الإدارة',
                                        textOf(
                                          r['reviewReason'] ?? 'لا توجد ملاحظة',
                                        ),
                                      ),
                                    ]),
                                  ]);
                                },
                              ),
                            ),
                          ),
                          child: const UiText('عرض طلب المنتج'),
                        ),
                      if (n['targetKind'] == 'Tournament' &&
                          n['targetId'] != null)
                        TextButton(
                          onPressed: () => openPage(
                            context,
                            TournamentCheckout(
                              auth: widget.auth,
                              id: textOf(n['targetId']),
                            ),
                          ),
                          child: const UiText('عرض طلب البطولة'),
                        ),
                      if (n['targetKind'] == 'PaymentQueue')
                        TextButton(
                          onPressed: () => openPage(
                            context,
                            NativePaymentRequests(
                              auth: widget.auth,
                              filters: const {'allDates': 'true'},
                            ),
                          ),
                          child: const UiText('مراجعة طلبات الدفع'),
                        ),
                      if (n['targetKind'] == 'Payment' && n['targetId'] != null)
                        TextButton(
                          onPressed: () => openPage(
                            context,
                            CheckoutPage(
                              auth: widget.auth,
                              environment: const String.fromEnvironment(
                                'APP_ENV',
                                defaultValue: 'Production',
                              ),
                              id: textOf(n['targetId']),
                            ),
                          ),
                          child: const UiText('عرض طلب الدفع'),
                        ),
                      if (n['targetKind'] == 'Enrollment' &&
                          n['targetId'] != null)
                        TextButton(
                          onPressed: () => openPage(
                            context,
                            EnrollmentPaymentPage(
                              auth: widget.auth,
                              id: textOf(n['targetId']),
                            ),
                          ),
                          child: const UiText('عرض طلب الاشتراك'),
                        ),
                      if (n['targetKind'] == 'FamilyRenewal' &&
                          n['targetId'] != null)
                        TextButton(
                          onPressed: () => openPage(
                            context,
                            NativeFamilyRenewalRequest(
                              auth: widget.auth,
                              id: textOf(n['targetId']),
                            ),
                          ),
                          child: const UiText('عرض طلب الأسرة'),
                        ),
                      if (n['targetKind'] == 'Contact' && n['targetId'] != null)
                        TextButton(
                          onPressed: () => openPage(
                            context,
                            CoachFeedbackPage(
                              auth: widget.auth,
                              playerId: textOf(n['targetId']),
                            ),
                          ),
                          child: const UiText('التواصل أو تقديم ملاحظة'),
                        ),
                      if (n['targetKind'] == 'Schedule')
                        TextButton(
                          onPressed: () => openPage(
                            context,
                            RoleAgenda(auth: widget.auth, guardian: true),
                          ),
                          child: const UiText('عرض المواعيد'),
                        ),
                      if (n['targetKind'] == 'OwnerFeedback' &&
                          n['targetId'] != null)
                        TextButton(
                          onPressed: () => openPage(
                            context,
                            OwnerFeedbackDetail(
                              auth: widget.auth,
                              id: textOf(n['targetId']),
                            ),
                          ),
                          child: const UiText('عرض الملاحظة'),
                        ),
                    ],
                  ),
                ],
              ),
            ),
          Row(
            mainAxisAlignment: MainAxisAlignment.spaceBetween,
            children: [
              TextButton(
                onPressed: page <= 1 ? null : () => setState(() => page--),
                child: const UiText('السابق'),
              ),
              UiText('$page'),
              TextButton(
                onPressed: page * 25 >= (d['total'] as num? ?? 0)
                    ? null
                    : () => setState(() => page++),
                child: const UiText('التالي'),
              ),
            ],
          ),
        ]);
      },
    ),
  );
}
