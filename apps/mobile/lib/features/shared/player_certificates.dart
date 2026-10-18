import 'dart:convert';

import 'package:flutter/material.dart';

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
  bool foreground = true, busy = false;
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
      error = '';
    });
  }

  Future<void> save(String url, Json body) async {
    if (busy || widget.playerId != null) return;
    final next = jsonEncode({'url': url, 'body': body});
    if (signature != next) {
      signature = next;
      command = goalCommandId();
    }
    setState(() {
      busy = true;
      error = '';
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
        message = l(
          'تم حفظ الإجراء للشهادة دون تغيير المستوى أو الاشتراك أو المال.',
          'Certificate action saved; level, subscription and money are unchanged.',
        );
      });
    } catch (_) {
      if (mounted && identity == original) {
        setState(
          () => error = l(
            'تعذر التأكيد. أعد فتح المصدر؛ لم نسجل نجاحًا.',
            'Could not confirm. Reopen the source; no success recorded.',
          ),
        );
      }
    } finally {
      if (mounted) {
        setState(() => busy = false);
      }
    }
  }

  Widget paper(Json c) => GCard(
    key: ValueKey('certificate-paper:${c['id']}'),
    child: Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        const Icon(
          Icons.workspace_premium_outlined,
          size: 48,
          color: Color(0xffe0b34e),
        ),
        Text(
          textOf(c['academyName']),
          textAlign: TextAlign.center,
          style: Theme.of(context).textTheme.titleMedium,
        ),
        Text(
          l('شهادة إنجاز فني', 'Academy achievement'),
          textAlign: TextAlign.center,
          style: Theme.of(context).textTheme.headlineSmall,
        ),
        const SizedBox(height: 16),
        Text(
          textOf(c['playerName']),
          textAlign: TextAlign.center,
          style: Theme.of(context).textTheme.titleLarge,
        ),
        Text(
          '${c['sportName']} · ${c['levelName']}',
          textAlign: TextAlign.center,
        ),
        const SizedBox(height: 12),
        Text('${l('الإنجاز', 'Milestone')}: ${c['milestoneOn']}'),
        Text('${l('الإصدار', 'Issued')}: ${c['issuedOn']} · ${c['actorName']}'),
        Text(
          l(
            'شهادة داخلية من الأكاديمية، لا اعتماد خارجي أو ضمان مهارة/سلامة مستقبلية.',
            'Internal academy certificate; not external accreditation or a guarantee of future skill or safety.',
          ),
        ),
        Directionality(
          textDirection: TextDirection.ltr,
          child: Text(textOf(c['number'])),
        ),
        if (c['state'] == 'Withdrawn') ...[
          const Divider(),
          Text(
            l('الشهادة مسحوبة — لا تعد سارية', 'Withdrawn — no longer valid'),
            style: const TextStyle(fontWeight: FontWeight.bold),
          ),
          Text(
            '${obj(c['withdrawal'])['actorName']} · ${textOf(obj(c['withdrawal'])['createdAtUtc']).split('T').first}',
          ),
          if (obj(c['withdrawal'])['familyNote'] != null)
            Text(textOf(obj(c['withdrawal'])['familyNote'])),
        ],
      ],
    ),
  );
  @override
  Widget build(BuildContext context) => ListenableBuilder(
    listenable: widget.auth,
    builder: (context, _) {
      if (!foreground || identity == null || identity != original) {
        return const SizedBox.shrink();
      }
      return GuardianPage(
        title: l('إنجازات وشهادات', 'Achievements and certificates'),
        child: RemoteBody(
          key: ValueKey('$identity:$revision'),
          auth: widget.auth,
          path: path,
          builder: (context, value) {
            final d = obj(value);
            if (widget.playerId != null && widget.id == null) {
              return gList([
                if (rows(d['items']).isEmpty)
                  Text(
                    l(
                      'لم تصدر شهادة بعد؛ كل تقدم له وقته.',
                      'No issued certificates yet; every child progresses at their own pace.',
                    ),
                  ),
                for (final c in rows(d['items']))
                  GLink(
                    '${textOf(c['levelName'])} · ${c['state'] == 'Issued' ? l('صادرة', 'Issued') : l('مسحوبة', 'Withdrawn')}',
                    Icons.workspace_premium_outlined,
                    () => openPage(
                      context,
                      PlayerCertificatesPage(
                        auth: widget.auth,
                        playerId: widget.playerId,
                        id: textOf(c['id']),
                      ),
                    ),
                  ),
                if (d['hasMore'] == true)
                  Text(
                    l(
                      'تظهر أحدث50 شهادة.',
                      'Showing the latest50 certificates.',
                    ),
                  ),
              ]);
            }
            final c = d['certificate'] == null ? null : obj(d['certificate']);
            return gList([
              if (message.isNotEmpty) Text(message),
              if (error.isNotEmpty) Text(error),
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
                          'معاينة إنجاز منفذ. الإصدار ينشر شهادة داخلية للأسرة المخولة، دون اعتماد خارجي أو أثر مالي.',
                          'Preview of an applied achievement. Issue an internal certificate to authorized family, without external accreditation or financial effect.',
                        ),
                      ),
                      if (widget.playerId == null && d['canIssue'] == true)
                        FilledButton(
                          onPressed: busy
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
                            'لا إصدار دون مصدر فني منفذ وصلاحية الإضافة.',
                            'Applied technical evidence and add permission are required.',
                          ),
                        ),
                    ],
                  ),
                ),
              if (c != null &&
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
                          'السحب لا يلغي الترقية ولا يمحو النسخ المطبوعة سابقًا.',
                          'Withdrawal does not reverse promotion or erase previously printed copies.',
                        ),
                      ),
                      FilledButton(
                        onPressed: busy
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
