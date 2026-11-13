import 'dart:convert';

import 'package:flutter/material.dart';

import '../../core/api/api_client.dart';
import '../../core/auth/auth_controller.dart';
import '../../core/theme/appearance.dart';
import '../guardian/guardian_ui.dart';
import 'player_goals.dart' show goalCommandId;

class PlayerCertificatesPage extends StatefulWidget {
  final AuthController auth;
  final String? sourceId, id, playerId;
  const PlayerCertificatesPage({
    super.key,
    required this.auth,
    this.sourceId,
    this.id,
    this.playerId,
  });
  @override
  State<PlayerCertificatesPage> createState() => _CertificatesState();
}

class _CertificatesState extends State<PlayerCertificatesPage>
    with WidgetsBindingObserver {
  String? get identity => widget.auth.session?.selected == null
      ? null
      : '${widget.auth.session!.selected!.academyId}:${widget.auth.session!.selected!.id}:${widget.auth.session!.selected!.contextRevision}';
  late final original = identity;
  bool foreground = true, busy = false, conflict = false;
  bool get locked => busy || conflict;
  bool get isList => widget.playerId != null && widget.id == null;
  int revision = 0;
  String message = '', error = '';
  String? command, signature;
  final note = TextEditingController();
  static const root = '/api/v1/evaluations/certificates';
  String l(String ar, String en) => localized(context, ar, en);
  String get path => widget.playerId != null
      ? '/api/v1/guardian/children/${widget.playerId}/certificates${widget.id == null ? '' : '/${widget.id}'}'
      : widget.sourceId != null
      ? '$root/sources/${widget.sourceId}'
      : '$root/${widget.id}';
  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addObserver(this);
  }

  @override
  void dispose() {
    WidgetsBinding.instance.removeObserver(this);
    note.dispose();
    super.dispose();
  }

  @override
  void didChangeAppLifecycleState(AppLifecycleState s) {
    setState(() {
      foreground = s == AppLifecycleState.resumed;
      revision++;
      note.clear();
      message = '';
    });
  }

  Future<void> save(String url, Json body) async {
    if (locked ||
        !foreground ||
        identity == null ||
        identity != original ||
        widget.playerId != null) {
      return;
    }
    final next = jsonEncode({'url': url, 'body': body});
    if (signature != next) {
      signature = next;
      command = goalCommandId();
    }
    setState(() {
      busy = true;
      error = '';
      message = '';
    });
    try {
      await widget.auth.request(
        'POST',
        url,
        body: {...body, 'requestId': command},
      );
      if (!mounted || identity != original) return;
      signature = null;
      command = null;
      setState(() {
        revision++;
        note.clear();
        message = url.endsWith('/withdraw')
            ? l(
                'سُحبت الشهادة. تظهر حالتها الجديدة في سجل الأسرة.',
                'The certificate was withdrawn. The family record shows its new status.',
              )
            : l(
                'صدرت الشهادة وأصبحت متاحة لولي الأمر.',
                'The certificate was issued and is available to the guardian.',
              );
      });
    } catch (failure) {
      if (mounted && identity == original) {
        setState(() {
          conflict = failure is ApiFailure && failure.status == 409;
          error = failure is ApiFailure
              ? failure.message
              : l(
                  'تعذر تأكيد الإجراء. حدّث سجل الشهادة للتحقق.',
                  'Could not confirm the action. Refresh the certificate to check.',
                );
        });
      }
    } finally {
      if (mounted) {
        setState(() => busy = false);
      }
    }
  }

  String displayDate(Object? value) {
    final date = DateTime.tryParse(textOf(value));
    return date == null
        ? textOf(value)
        : MaterialLocalizations.of(context).formatMediumDate(date);
  }

  Future<void> openCertificate(String id) async {
    await openPage(
      context,
      PlayerCertificatesPage(
        auth: widget.auth,
        playerId: widget.playerId,
        id: id,
      ),
    );
    if (mounted && foreground && identity == original) {
      setState(() => revision++);
    }
  }

  Widget paper(Json certificate) {
    final withdrawn = certificate['state'] == 'Withdrawn';
    final withdrawal = certificate['withdrawal'] == null
        ? null
        : obj(certificate['withdrawal']);
    final theme = Theme.of(context);
    return GCard(
      key: ValueKey("certificate-paper:${certificate['id']}"),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          if (withdrawn) ...[
            DecoratedBox(
              decoration: BoxDecoration(
                color: theme.colorScheme.errorContainer,
                borderRadius: BorderRadius.circular(10),
              ),
              child: Padding(
                padding: const EdgeInsets.all(12),
                child: DefaultTextStyle.merge(
                  style: TextStyle(color: theme.colorScheme.onErrorContainer),
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      Text(
                        l('الشهادة مسحوبة', 'Certificate withdrawn'),
                        style: const TextStyle(fontWeight: FontWeight.bold),
                      ),
                      if (withdrawal != null) ...[
                        Text(
                          l(
                            "سحبها ${withdrawal['actorName']} · ${displayDate(textOf(withdrawal['createdAtUtc']).split('T').first)}",
                            "Withdrawn by ${withdrawal['actorName']} · ${displayDate(textOf(withdrawal['createdAtUtc']).split('T').first)}",
                          ),
                        ),
                        if (withdrawal['familyNote'] != null)
                          Text(textOf(withdrawal['familyNote'])),
                      ],
                    ],
                  ),
                ),
              ),
            ),
            const SizedBox(height: 14),
          ] else
            Icon(
              Icons.workspace_premium_outlined,
              size: 36,
              color: theme.colorScheme.primary,
            ),
          Text(
            textOf(certificate['academyName']),
            textAlign: TextAlign.center,
            style: theme.textTheme.titleMedium,
          ),
          Text(
            l('شهادة إنجاز فني', 'Academy achievement'),
            textAlign: TextAlign.center,
            style: theme.textTheme.titleLarge,
          ),
          const SizedBox(height: 12),
          Text(
            textOf(certificate['playerName']),
            textAlign: TextAlign.center,
            style: theme.textTheme.titleLarge,
          ),
          Text(
            "${certificate['sportName']} · ${certificate['levelName']}",
            textAlign: TextAlign.center,
          ),
          const SizedBox(height: 12),
          Text(
            l(
              "تاريخ الإنجاز ${displayDate(certificate['milestoneOn'])}",
              "Milestone date ${displayDate(certificate['milestoneOn'])}",
            ),
          ),
          Text(
            l(
              "أصدرها ${certificate['actorName']} · ${displayDate(certificate['issuedOn'])}",
              "Issued by ${certificate['actorName']} · ${displayDate(certificate['issuedOn'])}",
            ),
          ),
          const SizedBox(height: 8),
          Text(
            l(
              'شهادة داخلية من الأكاديمية، وليست اعتمادًا خارجيًا.',
              'An internal academy certificate, not external accreditation.',
            ),
          ),
          const SizedBox(height: 8),
          Text(l('مرجع الشهادة', 'Certificate reference')),
          Align(
            alignment: AlignmentDirectional.centerStart,
            child: Directionality(
              textDirection: TextDirection.ltr,
              child: Text(textOf(certificate['number'])),
            ),
          ),
        ],
      ),
    );
  }

  @override
  Widget build(BuildContext context) => ListenableBuilder(
    listenable: widget.auth,
    builder: (context, _) {
      if (!foreground || identity == null || identity != original) {
        return const SizedBox.shrink();
      }
      return GuardianPage(
        title: isList
            ? l('شهادات الإنجاز', 'Achievement certificates')
            : l('شهادة الإنجاز', 'Achievement certificate'),
        onBack: () => Navigator.of(context).maybePop(),
        child: RemoteBody(
          key: ValueKey('$identity:$revision'),
          auth: widget.auth,
          path: path,
          builder: (context, value) {
            final d = obj(value);
            if (isList) {
              return gList(
                [
                  if (rows(d['items']).isEmpty)
                    EmptyMessage(
                      l(
                        'لا توجد شهادات في سجل اللاعب.',
                        'There are no certificates in this player’s record.',
                      ),
                    ),
                  for (final certificate in rows(d['items']))
                    GLink(
                      textOf(certificate['levelName']),
                      certificate['state'] == 'Issued'
                          ? Icons.workspace_premium_outlined
                          : Icons.event_busy_outlined,
                      () => openCertificate(textOf(certificate['id'])),
                      subtitle:
                          "${certificate['sportName']} · ${displayDate(certificate['milestoneOn'])}\n${certificate['state'] == 'Issued' ? l('شهادة صادرة', 'Certificate issued') : l('شهادة مسحوبة', 'Certificate withdrawn')}",
                    ),
                  if (d['hasMore'] == true)
                    Text(
                      l(
                        'تظهر أحدث 50 شهادة.',
                        'Showing the latest 50 certificates.',
                      ),
                    ),
                ],
                key: PageStorageKey(
                  'certificate-list:$identity:${widget.playerId}',
                ),
              );
            }
            final c = d['certificate'] == null ? null : obj(d['certificate']);
            return gList([
              if (message.isNotEmpty)
                Semantics(liveRegion: true, child: Text(message)),
              if (error.isNotEmpty) ...[
                Semantics(liveRegion: true, child: Text(error)),
                OutlinedButton(
                  onPressed: busy
                      ? null
                      : () => setState(() {
                          conflict = false;
                          error = '';
                          revision++;
                        }),
                  child: Text(l('تحديث سجل الشهادة', 'Refresh certificate')),
                ),
              ],
              if (c != null) paper(c),
              if (c == null && d['source'] != null)
                GCard(
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.stretch,
                    children: [
                      Text(
                        textOf(obj(d['source'])['player']),
                        style: Theme.of(context).textTheme.titleLarge,
                      ),
                      Text(
                        '${obj(d['source'])['sport']} · ${obj(d['source'])['toLevelName']}',
                      ),
                      Text(
                        l(
                          'بعد الإصدار، تظهر الشهادة في سجل ولي الأمر.',
                          'After issue, the certificate appears in the guardian’s record.',
                        ),
                      ),
                      if (widget.playerId == null && d['canIssue'] == true)
                        FilledButton(
                          onPressed: locked
                              ? null
                              : () => save(
                                  '$root/sources/${widget.sourceId}/issue',
                                  {
                                    'sourceVersion': obj(
                                      d['source'],
                                    )['version'],
                                  },
                                ),
                          child: Text(
                            l(
                              'إصدار شهادة الإنجاز لولي الأمر',
                              'Issue achievement certificate',
                            ),
                          ),
                        ),
                      if (d['canIssue'] != true)
                        Text(
                          l(
                            d['eligible'] == true
                                ? 'إصدار الشهادة يحتاج صلاحية الإضافة.'
                                : 'هذا السجل غير مؤهل لإصدار شهادة إنجاز.',
                            d['eligible'] == true
                                ? 'You need add permission to issue this certificate.'
                                : 'This record is not eligible for a certificate.',
                          ),
                        ),
                    ],
                  ),
                ),
              if (c != null &&
                  c['state'] == 'Issued' &&
                  d['canWithdraw'] == true &&
                  widget.playerId == null)
                GCard(
                  key: ValueKey('certificate-withdraw:${c['id']}'),
                  child: ExpansionTile(
                    title: Text(
                      l(
                        'سحب الشهادة مع حفظ سجلها',
                        'Withdraw and preserve history',
                      ),
                    ),
                    children: [
                      TextField(
                        controller: note,
                        enabled: !locked,
                        maxLength: 500,
                        decoration: InputDecoration(
                          labelText: l(
                            'ملاحظة السحب للأسرة (اختياري)',
                            'Family withdrawal note (optional)',
                          ),
                        ),
                      ),
                      Text(
                        l(
                          'سيظهر السحب في سجل الأسرة. تبقى النسخ التي سبق طباعتها لدى أصحابها.',
                          'The family record will show the withdrawal. Previously printed copies stay with their holders.',
                        ),
                      ),
                      FilledButton(
                        onPressed: locked
                            ? null
                            : () => save('$root/${c['id']}/withdraw', {
                                'version': c['version'],
                                'familyNote': note.text.trim().isEmpty
                                    ? null
                                    : note.text.trim(),
                              }),
                        child: Text(
                          l('تأكيد سحب الشهادة', 'Confirm withdrawal'),
                        ),
                      ),
                    ],
                  ),
                ),
            ]);
          },
        ),
      );
    },
  );
}
