import 'package:flutter/material.dart';

import '../../core/auth/auth_controller.dart';
import '../../core/api/api_client.dart';
import '../../core/theme/appearance.dart';
import '../../core/theme/ui_text.dart';
import '../shared/private_media.dart' show isPrivateAsset;
import '../shared/transfer_payment.dart';
import 'guardian_payments.dart' show receiptDateLabel;
import 'guardian_ui.dart';

class TournamentFees extends StatefulWidget {
  final AuthController auth;
  const TournamentFees({super.key, required this.auth});
  @override
  State<TournamentFees> createState() => _TournamentFeesState();
}

class _TournamentFeesState extends State<TournamentFees> {
  String? enrollment, tournament;
  String key = commandKey(), message = '';
  int revision = 0;
  bool busy = false;
  Future<void> create() async {
    if (busy || enrollment == null || tournament == null) return;
    setState(() => busy = true);
    try {
      final d = obj(
        await widget.auth.request(
          'POST',
          '/api/v1/guardian/tournament-payments/',
          body: {'enrollmentId': enrollment, 'tournamentId': tournament},
          idempotencyKey: key,
        ),
      );
      if (mounted) {
        await openPage(
          context,
          TournamentCheckout(auth: widget.auth, id: textOf(d['id'])),
        );
      }
      if (mounted) {
        setState(() {
          key = commandKey();
          revision++;
        });
      }
    } catch (e) {
      if (mounted) {
        setState(
          () =>
              message = e is ApiFailure ? e.message : 'تعذر إنشاء طلب البطولة.',
        );
      }
    } finally {
      if (mounted) setState(() => busy = false);
    }
  }

  @override
  Widget build(BuildContext context) => GuardianPage(
    title: localized(context, 'رسوم البطولات', 'Tournament fees'),
    child: RemoteBody(
      key: ValueKey(revision),
      auth: widget.auth,
      path: '/api/v1/guardian/tournament-payments/options',
      builder: (context, value) {
        final d = obj(value),
            enrollments = rows(d['enrollments']),
            tournaments = rows(d['tournaments']);
        final e = enrollments.where((e) => e['id'] == enrollment).firstOrNull;
        return gList([
          UiText(
            localized(
              context,
              'مشاركة إضافية مستقلة عن اشتراك التدريب',
              'An additional fee, separate from training subscriptions',
            ),
          ),
          if (message.isNotEmpty) UiText(message),
          GCard(
            child: Column(
              children: [
                DropdownButtonFormField<String>(
                  initialValue: enrollment,
                  decoration: InputDecoration(
                    labelText: localized(
                      context,
                      'الطفل / التسجيل',
                      'Child / enrollment',
                    ),
                  ),
                  items: [
                    for (final e in enrollments)
                      DropdownMenuItem(
                        value: textOf(e['id']),
                        child: UiText('${e['player']} · ${e['branch']}'),
                      ),
                  ],
                  onChanged: busy
                      ? null
                      : (v) => setState(() {
                          enrollment = v;
                          tournament = null;
                          key = commandKey();
                        }),
                ),
                const SizedBox(height: 12),
                DropdownButtonFormField<String>(
                  key: ValueKey(enrollment),
                  initialValue: tournament,
                  decoration: InputDecoration(
                    labelText: localized(context, 'البطولة', 'Tournament'),
                  ),
                  items: [
                    for (final t in tournaments.where(
                      (t) => e != null && t['sportId'] == e['sportId'],
                    ))
                      DropdownMenuItem(
                        value: textOf(t['id']),
                        child: UiText(
                          '${t['name']} · ${money(t['fee'], t['currency'])}',
                        ),
                      ),
                  ],
                  onChanged: busy
                      ? null
                      : (v) => setState(() {
                          tournament = v;
                          key = commandKey();
                        }),
                ),
                const SizedBox(height: 16),
                FilledButton(
                  onPressed: busy || tournament == null ? null : create,
                  child: UiText(
                    localized(
                      context,
                      'متابعة إلى الدفع',
                      'Continue to payment',
                    ),
                  ),
                ),
              ],
            ),
          ),
          if (tournaments.isEmpty)
            const EmptyMessage('لا توجد رسوم بطولات متاحة حاليًا.'),
          Heading(
            localized(
              context,
              'طلباتي وإيصالات البطولة',
              'My tournament requests & receipts',
            ),
          ),
          for (final p in rows(d['payments']))
            GLink(
              '${p['tournamentName']} · ${label(p['status'])}',
              Icons.receipt_long_outlined,
              () async {
                await openPage(
                  context,
                  TournamentCheckout(auth: widget.auth, id: textOf(p['id'])),
                );
                if (mounted) setState(() => revision++);
              },
            ),
        ]);
      },
    ),
  );
}

class TournamentCheckout extends StatefulWidget {
  final AuthController auth;
  final String id;
  const TournamentCheckout({super.key, required this.auth, required this.id});
  @override
  State<TournamentCheckout> createState() => _TournamentCheckoutState();
}

class _TournamentCheckoutState extends State<TournamentCheckout> {
  int revision = 0;
  @override
  Widget build(BuildContext context) => GuardianPage(
    title: localized(context, 'رسم البطولة', 'Tournament fee'),
    child: RemoteBody(
      key: ValueKey(revision),
      auth: widget.auth,
      path: '/api/v1/guardian/tournament-payments/${widget.id}',
      builder: (context, v) {
        final d = obj(v);
        final status = d['status'];
        final paid = status == 'Confirmed';
        final hasProof = isPrivateAsset(d['proofUrl']);
        final receiptDate = paid
            ? receiptDateLabel(context, d['confirmedAtUtc'])
            : null;
        return gList([
          GCard(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Heading(textOf(d['tournamentName'])),
                UiText(textOf(d['player'])),
                StatusPill(
                  status == 'Pending' && hasProof ? 'UnderReview' : status,
                ),
                UiText(paid ? 'المبلغ المحصّل' : 'رسم المشاركة'),
                UiText(
                  money(d['amount'], d['currency']),
                  style: Theme.of(context).textTheme.headlineSmall,
                ),
                if (d['reviewReason'] != null)
                  UiText(textOf(d['reviewReason'])),
              ],
            ),
          ),
          if (status == 'Pending' && hasProof)
            const UiText('وصل إثبات الدفع. انتظر المراجعة قبل إرسال مبلغ آخر.'),
          if (status == 'Pending' || status == 'Rejected')
            TransferPayment(
              auth: widget.auth,
              proofPath:
                  '/api/v1/guardian/tournament-payments/${widget.id}/proof',
              subscription: false,
              replacement: hasProof,
              showConfirmation: false,
              onSubmitted: () => setState(() => revision++),
            ),
          if (paid && d['receiptNumber'] != null)
            GCard(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  const Heading('إيصال رسم البطولة'),
                  SelectableText(
                    textOf(d['receiptNumber']),
                    textDirection: TextDirection.ltr,
                  ),
                  if (receiptDate != null) UiText(receiptDate),
                ],
              ),
            ),
        ]);
      },
    ),
  );
}
