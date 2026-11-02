import 'dart:convert';

import 'package:flutter/material.dart';

import '../../core/auth/auth_controller.dart';
import '../../core/theme/appearance.dart';
import '../guardian/guardian_ui.dart';
import 'player_goals.dart' show goalCommandId;

class PlayerBadgesPage extends StatefulWidget {
  final AuthController auth;
  final String? enrollmentId, playerId;
  const PlayerBadgesPage({
    super.key,
    required this.auth,
    this.enrollmentId,
    this.playerId,
  });
  @override
  State<PlayerBadgesPage> createState() => _BadgesState();
}

class _BadgesState extends State<PlayerBadgesPage> with WidgetsBindingObserver {
  String? get identity => widget.auth.session?.selected == null
      ? null
      : '${widget.auth.session!.selected!.academyId}:${widget.auth.session!.selected!.id}:${widget.auth.session!.selected!.contextRevision}';
  late final original = identity;
  bool foreground = true, busy = false;
  int revision = 0;
  String message = '', error = '';
  String? signature, request, choice, observed;
  final note = TextEditingController();
  String l(String ar, String en) => localized(context, ar, en);
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
      choice = null;
      observed = null;
      note.clear();
      message = '';
      error = '';
    });
  }

  Future<void> save(String path, Json body) async {
    if (busy || widget.playerId != null) return;
    final next = jsonEncode({'path': path, 'body': body});
    if (signature != next) {
      signature = next;
      request = goalCommandId();
    }
    setState(() {
      busy = true;
      error = '';
    });
    try {
      await widget.auth.request(
        'POST',
        path,
        body: {...body, 'requestId': request},
      );
      if (!mounted || identity != original) return;
      signature = null;
      request = null;
      setState(() {
        revision++;
        choice = null;
        observed = null;
        note.clear();
        message = l(
          'تم حفظ الشارة وسجلها للأسرة، دون تغيير المستوى أو الاشتراك أو المال.',
          'Badge action saved for authorized family; level, subscription and money unchanged.',
        );
      });
    } catch (_) {
      if (mounted && identity == original) {
        setState(
          () => error = l(
            'تعذر تأكيد الحفظ؛ لم نسجل نجاحًا. أعد فتح المصدر والصلاحية.',
            'Could not confirm; no success recorded. Reopen the source and permissions.',
          ),
        );
      }
    } finally {
      if (mounted) setState(() => busy = false);
    }
  }

  static const root = '/api/v1/evaluations/badges';
  @override
  Widget build(BuildContext context) => ListenableBuilder(
    listenable: widget.auth,
    builder: (context, _) {
      if (!foreground || identity == null || identity != original) {
        return const SizedBox.shrink();
      }
      return GuardianPage(
        title: l('شارات المجهود والتحسن', 'Effort and improvement badges'),
        child: RemoteBody(
          key: ValueKey('$identity:$revision'),
          auth: widget.auth,
          path: widget.playerId != null
              ? '/api/v1/guardian/children/${widget.playerId}/badges'
              : '$root/enrollments/${widget.enrollmentId}',
          builder: (context, v) {
            final d = obj(v);
            return gList([
              Text(
                l(
                  'تشجيع خاص لطفلك، لا ترتيب بين الأطفال أو أموال أو خصم اشتراك.',
                  'Private encouragement, not a child leaderboard, money or subscription discount.',
                ),
              ),
              if (message.isNotEmpty) Text(message),
              if (error.isNotEmpty) Text(error),
              for (final journey in rows(d['enrollments']))
                ...journeyWidgets(journey),
              if (d['hasMore'] == true)
                Text(
                  l(
                    'تظهر أول 20 تسجيلًا رياضيًا مخولًا.',
                    'Showing the first20 authorized sport enrollments.',
                  ),
                ),
            ]);
          },
        ),
      );
    },
  );
  List<Widget> journeyWidgets(Json d) {
    final e = obj(d['enrollment']),
        rules = rows(d['rules']),
        selected = rules.any((r) => r['id'] == choice) ? choice : null;
    return [
      GCard(
        key: ValueKey('badge-summary:${e['id']}'),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            Text(
              '${e['player']} · ${e['sport']}',
              style: Theme.of(context).textTheme.titleLarge,
            ),
            const SizedBox(height: 12),
            const Icon(Icons.auto_awesome, color: Color(0xffe3b448), size: 32),
            Text(
              '${d['points']}',
              textAlign: TextAlign.center,
              style: Theme.of(context).textTheme.headlineLarge,
            ),
            Text(
              l(
                'نقاط الشارات السارية لهذه الرياضة فقط',
                'Active badge points for this sport only',
              ),
              textAlign: TextAlign.center,
            ),
            Text(
              l(
                'السحب يستبعد نقاط الشارة ويحفظ سجلها، وليس عقوبة مالية.',
                'Revocation excludes the badge points but preserves history; never a financial penalty.',
              ),
            ),
          ],
        ),
      ),
      if (widget.playerId == null && d['canGrant'] == true)
        GCard(
          key: ValueKey('badge-grant:${e['id']}'),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              DropdownButtonFormField<String>(
                initialValue: selected,
                isExpanded: true,
                decoration: InputDecoration(
                  labelText: l('قاعدة الشارة', 'Badge criterion'),
                ),
                items: rules
                    .map(
                      (r) => DropdownMenuItem(
                        value: textOf(r['id']),
                        child: Text(
                          '${r['title']} · ${r['points']}',
                          overflow: TextOverflow.ellipsis,
                        ),
                      ),
                    )
                    .toList(),
                onChanged: busy ? null : (v) => setState(() => choice = v),
              ),
              if (selected != null)
                Text(
                  textOf(
                    rules.singleWhere((r) => r['id'] == selected)['criterion'],
                  ),
                ),
              const SizedBox(height: 12),
              TextButton.icon(
                icon: const Icon(Icons.calendar_month_outlined),
                label: Text(
                  '${l('تاريخ الملاحظة', 'Observation date')}: ${observed ?? d['asOfDate']}',
                ),
                onPressed: busy
                    ? null
                    : () async {
                        final date = await showDatePicker(
                          context: context,
                          initialDate: DateTime.parse(
                            observed ?? textOf(d['asOfDate']),
                          ),
                          firstDate: DateTime(1900),
                          lastDate: DateTime.parse(textOf(d['asOfDate'])),
                        );
                        if (date != null && mounted) {
                          setState(
                            () => observed = date.toIso8601String().substring(
                              0,
                              10,
                            ),
                          );
                        }
                      },
              ),
              TextField(
                controller: note,
                maxLength: 500,
                decoration: InputDecoration(
                  labelText: l(
                    'ملاحظة للأسرة (اختياري)',
                    'Family note (optional)',
                  ),
                ),
              ),
              Text(
                l(
                  'كل قاعدة تمنح مرة واحدة للتسجيل، حتى بعد سحبها. لا ترقية أو حضور تلقائي.',
                  'One grant per rule and enrollment, even after revocation. No automatic promotion or attendance.',
                ),
              ),
              FilledButton.icon(
                icon: const Icon(Icons.workspace_premium_outlined),
                label: Text(
                  l(
                    'منح الشارة ونشرها للأسرة',
                    'Grant badge and publish to family',
                  ),
                ),
                onPressed: busy || selected == null
                    ? null
                    : () => save('$root/enrollments/${e['id']}/grant', {
                        'ruleId': selected,
                        'enrollmentVersion': d['enrollmentVersion'],
                        'observedOn': observed ?? d['asOfDate'],
                        'familyNote': note.text.trim().isEmpty
                            ? null
                            : note.text.trim(),
                      }),
              ),
            ],
          ),
        ),
      if (rows(d['items']).isEmpty)
        Text(
          l(
            'لم تمنح شارة بعد؛ كل طفل يتقدم بطريقته.',
            'No badges yet; every child progresses in their own way.',
          ),
        ),
      for (final b in rows(d['items']))
        GCard(
          key: ValueKey('badge:${b['id']}'),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              const Icon(
                Icons.workspace_premium_outlined,
                color: Color(0xffe3b448),
                size: 38,
              ),
              Text(
                textOf(b['title']),
                style: Theme.of(context).textTheme.titleLarge,
              ),
              Text(textOf(b['criterion'])),
              Text(
                '${b['points']} ${l('نقطة تشجيعية', 'encouragement points')}',
              ),
              Text(
                b['state'] == 'Granted'
                    ? l('شارة سارية', 'Active badge')
                    : l(
                        'شارة مسحوبة — مستبعدة من الملخص',
                        'Revoked — excluded from summary',
                      ),
              ),
              Text('${b['observedOn']} · ${b['actorName']}'),
              if (b['familyNote'] != null) Text(textOf(b['familyNote'])),
              if (b['revocation'] != null) ...[
                const Divider(),
                Text(
                  '${l('سحبها', 'Revoked by')} ${obj(b['revocation'])['actorName']} · ${textOf(obj(b['revocation'])['createdAtUtc']).split('T').first}',
                ),
                if (obj(b['revocation'])['familyNote'] != null)
                  Text(textOf(obj(b['revocation'])['familyNote'])),
              ],
              if (widget.playerId == null &&
                  d['canRevoke'] == true &&
                  b['state'] == 'Granted')
                _BadgeRevoke(
                  key: ValueKey('revoke:${b['id']}'),
                  busy: busy,
                  save: (n) => save('$root/grants/${b['id']}/revoke', {
                    'version': b['version'],
                    'note': n,
                  }),
                ),
            ],
          ),
        ),
      if (d['hasMore'] == true)
        Text(
          l(
            'تظهر أحدث 100 شارة؛ الملخص يشمل كل الشارات السارية لهذه الرياضة.',
            'Latest100 badges; summary includes all active badges for this sport.',
          ),
        ),
    ];
  }
}

class _BadgeRevoke extends StatefulWidget {
  final bool busy;
  final void Function(String?) save;
  const _BadgeRevoke({super.key, required this.busy, required this.save});
  @override
  State<_BadgeRevoke> createState() => _BadgeRevokeState();
}

class _BadgeRevokeState extends State<_BadgeRevoke> {
  final note = TextEditingController();
  @override
  void dispose() {
    note.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) => ExpansionTile(
    title: Text(
      localized(
        context,
        'سحب الشارة مع حفظ سجلها',
        'Revoke badge and preserve history',
      ),
    ),
    children: [
      TextField(
        controller: note,
        maxLength: 500,
        decoration: InputDecoration(
          labelText: localized(
            context,
            'ملاحظة السحب للأسرة (اختياري)',
            'Family revocation note (optional)',
          ),
        ),
      ),
      FilledButton(
        onPressed: widget.busy
            ? null
            : () => widget.save(
                note.text.trim().isEmpty ? null : note.text.trim(),
              ),
        child: Text(
          localized(context, 'تأكيد سحب الشارة', 'Confirm badge revocation'),
        ),
      ),
    ],
  );
}
