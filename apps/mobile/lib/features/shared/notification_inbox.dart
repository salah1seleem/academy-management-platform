import 'audience_communication.dart';

import 'dart:async';

import 'package:flutter/material.dart';

import '../../core/api/api_client.dart';
import '../../core/auth/auth_controller.dart';
import '../../core/theme/ui_text.dart';
import '../guardian/guardian_ui.dart';
import 'matches.dart';
import 'vip_rewards.dart';
import 'receivable_balances.dart';
import '../guardian/guardian_payments.dart';
import '../guardian/sport_store.dart';
import '../admin/native_product_orders.dart';
import '../admin/native_renewal_proof_review.dart';
import '../admin/native_enrollment_payment_read.dart';
import '../guardian/guardian_report.dart';
import '../guardian/guardian_profile.dart';
import '../guardian/coach_feedback.dart';
import '../guardian/family_renewal_request.dart';
import '../guardian/tournament_fees.dart';
import 'transfer_payment.dart';
import 'role_agenda.dart';
import 'role_navigation.dart';
import '../owner/owner_feedback_detail.dart';
import 'attendance_corrections.dart';

// UI invalidation only, isolated by authentication controller. No notice data
// or identity is stored here; each consumer rereads its authorized API source.
final _noticeRevisions = Expando<ValueNotifier<int>>('notice revisions');
ValueNotifier<int> _noticeRevision(AuthController auth) =>
    _noticeRevisions[auth] ??= ValueNotifier<int>(0);
Listenable notificationChanges(AuthController auth) => _noticeRevision(auth);
String? _noticeIdentity(AuthController auth) => auth.session?.selected == null
    ? null
    : '${auth.session!.selected!.academyId}:${auth.session!.selected!.id}:${auth.session!.selected!.contextRevision}';

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
  String? snapshotKey;
  String? get contextKey => _noticeIdentity(widget.auth);
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
    snapshotKey = null;
    setState(() => unread = 0);
    load();
  }

  Future<void> load() async {
    final g = ++generation;
    if (contextKey == null) return;
    try {
      final d = obj(await widget.auth.request('GET', '/api/v1/notifications'));
      if (mounted && g == generation) {
        final next =
            '${d['unread']}:${rows(d['items'] ?? []).map((n) => '${n['id']}:${n['isRead']}').join('|')}';
        final changed = snapshotKey != null && snapshotKey != next;
        snapshotKey = next;
        setState(() => unread = d['unread'] as int? ?? 0);
        if (changed) {
          _noticeRevision(widget.auth).value++;
        }
      }
    } catch (failure) {
      if (mounted &&
          g == generation &&
          failure is ApiFailure &&
          (failure.status == 401 || failure.status == 403)) {
        setState(() => unread = 0);
      }
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
  const NotificationInbox({
    super.key,
    required this.auth,
    this.urgentMatchesOnly = false,
  });
  final AuthController auth;
  final bool urgentMatchesOnly;
  @override
  State<NotificationInbox> createState() => _NotificationInboxState();
}

class _NotificationInboxState extends State<NotificationInbox> {
  int revision = 0;
  late final openedIdentity = _noticeIdentity(widget.auth);
  final readOverrides = <String, bool>{};
  int? unreadOverride;
  String? pending, failedNotice;

  void refresh() => setState(() {
    revision++;
    readOverrides.clear();
    unreadOverride = null;
    failedNotice = null;
  });

  Future<void> read(String id, bool wasRead, int unread) async {
    if (pending != null ||
        failedNotice != null ||
        openedIdentity == null ||
        openedIdentity != _noticeIdentity(widget.auth)) {
      return;
    }
    setState(() => pending = id);
    try {
      await widget.auth.request(
        'PUT',
        '/api/v1/notifications/${Uri.encodeComponent(id)}/read',
        body: {'isRead': !wasRead},
      );
      if (mounted && openedIdentity == _noticeIdentity(widget.auth)) {
        if (widget.urgentMatchesOnly) {
          // The server filters out read items, so paging must start again.
          refresh();
        } else {
          setState(() {
            readOverrides[id] = !wasRead;
            unreadOverride = wasRead
                ? unread + 1
                : (unread > 0 ? unread - 1 : 0);
          });
        }
        _noticeRevision(widget.auth).value++;
      }
    } catch (_) {
      if (mounted && openedIdentity == _noticeIdentity(widget.auth)) {
        setState(() => failedNotice = id);
      }
    } finally {
      if (mounted) setState(() => pending = null);
    }
  }

  @override
  Widget build(BuildContext context) => ListenableBuilder(
    listenable: widget.auth,
    builder: (context, _) =>
        openedIdentity == null || openedIdentity != _noticeIdentity(widget.auth)
        ? const SizedBox.shrink()
        : GuardianPage(
            title: widget.urgentMatchesOnly
                ? 'تحديثات المواعيد العاجلة'
                : 'التنبيهات',
            child: RemoteBody(
              key: ValueKey(revision),
              auth: widget.auth,
              path:
                  '/api/v1/notifications?page=1${widget.urgentMatchesOnly ? '&urgentMatchesOnly=true' : ''}',
              builder: (context, value) {
                final d = obj(value);
                final items = rows(d['items']);
                final unread = unreadOverride ?? (d['unread'] as int? ?? 0);
                return gList([
                  if (widget.urgentMatchesOnly)
                    TextButton(
                      onPressed: () => openPage(
                        context,
                        NotificationInbox(auth: widget.auth),
                      ),
                      child: const UiText('كل التنبيهات'),
                    ),
                  if (items.isNotEmpty) ...[
                    UiText(
                      'غير المقروءة $unread',
                      style: const TextStyle(fontWeight: FontWeight.w800),
                    ),
                    const SizedBox(height: 12),
                  ],
                  if (items.isEmpty) const EmptyMessage('لا توجد تنبيهات.'),
                  ...items.map((n) {
                    final id = textOf(n['id']);
                    final isRead = readOverrides[id] ?? (n['isRead'] == true);
                    final date = DateTime.tryParse(textOf(n['createdAtUtc']));
                    return GCard(
                      key: ValueKey('notice-${n['id']}'),
                      child: Column(
                        crossAxisAlignment: CrossAxisAlignment.start,
                        children: [
                          Wrap(
                            crossAxisAlignment: WrapCrossAlignment.center,
                            spacing: 8,
                            runSpacing: 4,
                            children: [
                              ExcludeSemantics(
                                child: Icon(
                                  isRead
                                      ? Icons.notifications_none
                                      : Icons.mark_email_unread_outlined,
                                  size: 20,
                                ),
                              ),
                              UiText(isRead ? 'مقروء' : 'غير مقروء'),
                              if (date != null)
                                UiText(
                                  MaterialLocalizations.of(context)
                                      .formatShortDate(date.toLocal()),
                                ),
                            ],
                          ),
                          const SizedBox(height: 10),
                          UiText(
                            textOf(n['message']),
                            style: TextStyle(
                              fontSize: 14,
                              fontWeight: isRead
                                  ? FontWeight.w400
                                  : FontWeight.w700,
                            ),
                          ),
                          Wrap(
                            spacing: 8,
                            children: [
                              if (n['matchId'] != null ||
                                  n['targetKind'] == 'Matches')
                                TextButton(
                                  onPressed: () => openPage(
                                    context,
                                    MatchSchedule(
                                      auth: widget.auth,
                                      matchId: n['matchId'] == null
                                          ? null
                                          : textOf(n['matchId']),
                                    ),
                                  ),
                                  child: UiText(
                                    n['matchId'] == null
                                        ? 'عرض المباريات الحالية'
                                        : 'عرض المباراة',
                                  ),
                                ),
                              if (n['reward'] == true)
                                TextButton(
                                  onPressed: () => openPage(
                                    context,
                                    NativeVipRewards(
                                      auth: widget.auth,
                                      rewardId: n['targetId'] as String?,
                                    ),
                                  ),
                                  child: const UiText('عرض المكافأة'),
                                ),
                              if (n['targetKind'] == 'GroupAnnouncement' &&
                                  n['targetId'] != null)
                                TextButton(
                                  onPressed: () => openPage(
                                    context,
                                    AudienceAnnouncementPage(
                                      auth: widget.auth,
                                      id: textOf(n['targetId']),
                                    ),
                                  ),
                                  child: const UiText('عرض إشعار المجموعة'),
                                ),
                              if (n['targetKind'] == 'Receivables' &&
                                  widget.auth.session?.selected?.role ==
                                      'Guardian')
                                TextButton(
                                  onPressed: () => openPage(
                                    context,
                                    ReceivableBalancesPage(auth: widget.auth),
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
                              if (n['targetKind'] == 'SubscriptionPeriod' &&
                                  n['targetId'] != null)
                                TextButton(
                                  onPressed: () => openPage(
                                    context,
                                    PeriodPage(
                                      auth: widget.auth,
                                      id: textOf(n['targetId']),
                                    ),
                                  ),
                                  child: const UiText('عرض الاشتراك وتعديلاته'),
                                ),
                              if (n['targetKind'] == 'AttendanceCorrection' &&
                                  n['targetId'] != null &&
                                  ['AcademyOwner', 'AcademyAdmin'].contains(
                                    widget.auth.session?.selected?.role,
                                  ))
                                TextButton(
                                  onPressed: () => openPage(
                                    context,
                                    AttendanceCorrectionDetail(
                                      auth: widget.auth,
                                      id: textOf(n['targetId']),
                                    ),
                                  ),
                                  child: const UiText('عرض طلب تصحيح الحضور'),
                                ),
                              if (n['targetKind'] == 'Attendance' &&
                                  n['targetId'] != null)
                                TextButton(
                                  onPressed: () => openPage(
                                    context,
                                    ProfileRecords(
                                      auth: widget.auth,
                                      playerId: textOf(n['targetId']),
                                      section: 'attendance',
                                    ),
                                  ),
                                  child: const UiText('عرض سجل الحضور'),
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
                              if (n['targetKind'] == 'Stars' &&
                                  n['targetId'] != null)
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
                              if (n['targetKind'] == 'ProductReview' &&
                                  n['targetId'] != null)
                                TextButton(
                                  onPressed: () => openPage(
                                    context,
                                    NativeProductOrderReview(
                                      auth: widget.auth,
                                      id: textOf(n['targetId']),
                                    ),
                                  ),
                                  child: const UiText('مراجعة طلب المنتج'),
                                ),
                              if (n['targetKind'] == 'Product' &&
                                  n['targetId'] != null)
                                TextButton(
                                  onPressed: () => openPage(
                                    context,
                                    GuardianProductOrder(
                                      auth: widget.auth,
                                      id: textOf(n['targetId']),
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
                                    n['targetId'] != null &&
                                            n['paymentReviewType'] == 'Renewal'
                                        ? NativeRenewalProofReview(
                                            auth: widget.auth,
                                            id: textOf(n['targetId']),
                                          )
                                        : n['targetId'] != null &&
                                              n['paymentReviewType'] ==
                                                  'FamilyRenewal'
                                        ? NativeFamilyRenewalRequest(
                                            auth: widget.auth,
                                            id: textOf(n['targetId']),
                                            staff: true,
                                          )
                                        : n['targetId'] != null &&
                                              n['paymentReviewType'] ==
                                                  'Enrollment'
                                        ? NativeEnrollmentPaymentRead(
                                            auth: widget.auth,
                                            id: textOf(n['targetId']),
                                          )
                                        : NativePaymentRequests(
                                            auth: widget.auth,
                                            filters: const {'allDates': 'true'},
                                          ),
                                  ),
                                  child: UiText(
                                    n['targetId'] != null &&
                                            [
                                              'Renewal',
                                              'FamilyRenewal',
                                              'Enrollment',
                                            ].contains(n['paymentReviewType'])
                                        ? 'عرض طلب الدفع ومراجعته'
                                        : 'مراجعة طلبات الدفع',
                                  ),
                                ),
                              if (n['targetKind'] == 'Payment' &&
                                  n['targetId'] != null)
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
                              if (const [
                                    'Contact',
                                    'GuardianFeedback',
                                  ].contains(n['targetKind']) &&
                                  n['targetId'] != null)
                                TextButton(
                                  onPressed: () => openPage(
                                    context,
                                    CoachFeedbackPage(
                                      auth: widget.auth,
                                      playerId: textOf(n['targetId']),
                                      initialKind:
                                          n['targetKind'] == 'GuardianFeedback'
                                          ? 'Complaint'
                                          : 'Rating',
                                    ),
                                  ),
                                  child: UiText(
                                    n['targetKind'] == 'GuardianFeedback'
                                        ? 'تقديم شكوى أو اقتراح للمالك'
                                        : 'التواصل أو تقديم ملاحظة',
                                  ),
                                ),
                              if (n['targetKind'] == 'Schedule')
                                TextButton(
                                  onPressed: () => openPage(
                                    context,
                                    RoleAgenda(
                                      auth: widget.auth,
                                      guardian: true,
                                    ),
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
                              TextButton(
                                onPressed:
                                    pending != null || failedNotice != null
                                    ? null
                                    : () => read(id, isRead, unread),
                                child: UiText(
                                  pending == id
                                      ? 'جارٍ تحديث الحالة…'
                                      : isRead
                                      ? 'تحديد كغير مقروء'
                                      : 'تحديد كمقروء',
                                ),
                              ),
                            ],
                          ),
                          if (failedNotice == id) ...[
                            const UiText(
                              'تعذر تأكيد تحديث الحالة. حدّث القائمة للتحقق.',
                            ),
                            TextButton(
                              onPressed: refresh,
                              child: const UiText('تحديث القائمة'),
                            ),
                          ],
                        ],
                      ),
                    );
                  }),
                ]);
              },
            ),
          ),
  );
}
