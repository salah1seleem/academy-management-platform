import 'dart:convert';

import 'package:flutter/material.dart';

import '../../core/api/api_client.dart';
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
  bool foreground = true, busy = false, conflict = false, showAllSports = false;
  bool get locked => busy || conflict;
  bool get scoped =>
      widget.playerId != null && widget.enrollmentId != null && !showAllSports;
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
      message = '';
    });
  }

  Future<void> save(String path, Json body) async {
    if (locked ||
        !foreground ||
        identity == null ||
        identity != original ||
        widget.playerId != null) {
      return;
    }
    final next = jsonEncode({'path': path, 'body': body});
    if (signature != next) {
      signature = next;
      request = goalCommandId();
    }
    setState(() {
      busy = true;
      error = '';
      message = '';
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
        message = path.endsWith('/revoke')
            ? l(
                'سُحبت الشارة وحُدّث إجمالي النقاط.',
                'Badge revoked and points updated.',
              )
            : l(
                'مُنحت الشارة وأصبحت متاحة للأسرة.',
                'Badge granted and visible to the family.',
              );
      });
    } catch (failure) {
      if (mounted && identity == original) {
        setState(() {
          conflict = failure is ApiFailure && failure.status == 409;
          error = failure is ApiFailure
              ? failure.message
              : l(
                  'تعذر تأكيد الحفظ. حدّث السجل للتحقق.',
                  'Could not confirm the save. Refresh the record to check.',
                );
        });
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
      final records = RemoteBody(
        key: ValueKey('$identity:$revision'),
        auth: widget.auth,
        path: widget.playerId != null
            ? '/api/v1/guardian/children/${widget.playerId}/badges${scoped ? '?${Uri(queryParameters: {'enrollmentId': widget.enrollmentId}).query}' : ''}'
            : '$root/enrollments/${widget.enrollmentId}',
        builder: (context, v) {
          final d = obj(v);
          final journeys = rows(d['enrollments'])
              .where(
                (journey) =>
                    !scoped ||
                    obj(journey['enrollment'])['id'] == widget.enrollmentId,
              )
              .toList();
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
                child: Text(l('تحديث السجل', 'Refresh badge record')),
              ),
            ],
            if (journeys.isEmpty)
              EmptyMessage(
                scoped
                    ? l(
                        'سجل هذا التسجيل الرياضي غير متاح.',
                        'The badge record for this enrollment is unavailable.',
                      )
                    : l(
                        'لا يوجد تسجيل رياضي لعرض شاراته.',
                        'There are no sport enrollments to show badges for.',
                      ),
              ),
            for (final journey in journeys) ...journeyWidgets(journey),
            if (!scoped && d['hasMore'] == true)
              Text(
                l(
                  'تظهر أول 20 تسجيلًا رياضيًا.',
                  'Showing the first 20 sport enrollments.',
                ),
              ),
            if (widget.playerId != null && journeys.isNotEmpty)
              ExpansionTile(
                title: Text(l('عن نقاط الشارات', 'About badge points')),
                children: [
                  Padding(
                    padding: const EdgeInsets.all(12),
                    child: Text(
                      l(
                        'النقاط للتشجيع. تُستبعد الشارات المسحوبة من الإجمالي، ويظل السحب ظاهرًا في السجل.',
                        'Points are for encouragement. Revoked badges stay in the record and their points are excluded.',
                      ),
                    ),
                  ),
                ],
              ),
          ]);
        },
      );
      return GuardianPage(
        title: l('شارات المجهود والتحسن', 'Effort and improvement badges'),
        onBack: () => Navigator.of(context).maybePop(),
        child: scoped
            ? Column(
                crossAxisAlignment: CrossAxisAlignment.stretch,
                children: [
                  Padding(
                    padding: const EdgeInsets.symmetric(horizontal: 20),
                    child: TextButton(
                      onPressed: () => setState(() => showAllSports = true),
                      child: Text(
                        l(
                          'عرض شارات كل الرياضات',
                          'Show badges for all sports',
                        ),
                      ),
                    ),
                  ),
                  Expanded(child: records),
                ],
              )
            : records,
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
            const SizedBox(height: 8),
            Wrap(
              crossAxisAlignment: WrapCrossAlignment.center,
              spacing: 8,
              runSpacing: 4,
              children: [
                Text(
                  '${d['points']}',
                  style: Theme.of(context).textTheme.titleLarge,
                ),
                Text(
                  l(
                    'نقاط الشارات السارية في هذه الرياضة',
                    'Active badge points for this sport',
                  ),
                ),
              ],
            ),
          ],
        ),
      ),
      if (widget.playerId == null && d['canGrant'] == true && rules.isNotEmpty)
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
                onChanged: locked ? null : (v) => setState(() => choice = v),
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
                onPressed: locked
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
                enabled: !locked,
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
                  'تُمنح كل شارة مرة واحدة لهذا التسجيل الرياضي. الملاحظة تظهر للأسرة.',
                  'Each badge can be granted once per enrollment. The family sees the note.',
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
                onPressed: locked || selected == null
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
      if (widget.playerId == null && d['canGrant'] == true && rules.isEmpty)
        Text(
          l(
            'لا توجد شارات متاحة للمنح لهذا التسجيل.',
            'No badges are available to grant for this enrollment.',
          ),
        ),
      if (rows(d['items']).isEmpty)
        EmptyMessage(
          l(
            'لا توجد شارات مسجلة لهذه الرياضة.',
            'There are no badges for this sport yet.',
          ),
        ),
      for (final badge in rows(d['items']))
        badgeCard(badge, canRevoke: d['canRevoke'] == true),
      if (d['hasMore'] == true)
        Text(
          l(
            'تظهر أحدث 100 شارة؛ الملخص يشمل كل الشارات السارية لهذه الرياضة.',
            'Latest100 badges; summary includes all active badges for this sport.',
          ),
        ),
    ];
  }

  String badgeDate(Object? value) {
    final date = DateTime.tryParse(textOf(value));
    return date == null
        ? textOf(value)
        : MaterialLocalizations.of(context).formatMediumDate(date);
  }

  Widget badgeCard(Json badge, {required bool canRevoke}) {
    final active = badge['state'] == 'Granted';
    final revocation = badge['revocation'] == null
        ? null
        : obj(badge['revocation']);
    final colors = Theme.of(context).colorScheme;
    return GCard(
      key: ValueKey('badge:${badge['id']}'),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(
            textOf(badge['title']),
            style: Theme.of(context).textTheme.titleMedium,
          ),
          const SizedBox(height: 6),
          Wrap(
            crossAxisAlignment: WrapCrossAlignment.center,
            spacing: 8,
            runSpacing: 5,
            children: [
              DecoratedBox(
                decoration: BoxDecoration(
                  color: active
                      ? colors.secondaryContainer
                      : colors.errorContainer,
                  borderRadius: BorderRadius.circular(8),
                ),
                child: Padding(
                  padding: const EdgeInsets.symmetric(
                    horizontal: 8,
                    vertical: 4,
                  ),
                  child: Text(
                    active
                        ? l('شارة سارية', 'Active badge')
                        : l('شارة مسحوبة', 'Revoked badge'),
                    style: Theme.of(context).textTheme.labelLarge?.copyWith(
                      color: active
                          ? colors.onSecondaryContainer
                          : colors.onErrorContainer,
                    ),
                  ),
                ),
              ),
              Text(
                active
                    ? l("${badge['points']} نقطة", "${badge['points']} points")
                    : l(
                        "${badge['points']} نقطة عند المنح",
                        "${badge['points']} points when granted",
                      ),
              ),
            ],
          ),
          const SizedBox(height: 8),
          Text(textOf(badge['criterion'])),
          Text(
            l(
              "تاريخ الملاحظة ${badgeDate(badge['observedOn'])} · منحها ${badge['actorName']}",
              "Observed ${badgeDate(badge['observedOn'])} · Granted by ${badge['actorName']}",
            ),
          ),
          if (badge['familyNote'] is String &&
              textOf(badge['familyNote']).isNotEmpty)
            Text(textOf(badge['familyNote'])),
          if (revocation != null) ...[
            const Divider(),
            Text(
              l(
                "سحبها ${revocation['actorName']} · ${badgeDate(textOf(revocation['createdAtUtc']).split('T').first)}",
                "Revoked by ${revocation['actorName']} · ${badgeDate(textOf(revocation['createdAtUtc']).split('T').first)}",
              ),
            ),
            if (revocation['familyNote'] is String &&
                textOf(revocation['familyNote']).isNotEmpty)
              Text(textOf(revocation['familyNote'])),
          ],
          if (widget.playerId == null && canRevoke && active)
            _BadgeRevoke(
              key: ValueKey('revoke:${badge['id']}'),
              busy: locked,
              save: (note) => save("$root/grants/${badge['id']}/revoke", {
                'version': badge['version'],
                'note': note,
              }),
            ),
        ],
      ),
    );
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
      Text(
        localized(
          context,
          'تُستبعد نقاط الشارة من الإجمالي، ويظل السحب ظاهرًا في سجل الأسرة.',
          'The points are removed from the total and the family can still see the revocation.',
        ),
      ),
      const SizedBox(height: 8),
      TextField(
        controller: note,
        enabled: !widget.busy,
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
