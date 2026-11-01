import 'audience_communication.dart';

import 'package:flutter/material.dart';

import '../../core/auth/auth_controller.dart';
import '../../core/api/api_client.dart';
import '../../core/theme/ui_text.dart';
import '../admin/native_operation_form.dart';
import '../guardian/guardian_ui.dart';
import 'experience_ui.dart' show isoDay;
import 'match_experience.dart';
import '../../core/theme/academy_brand.dart';

import 'dart:convert';

import '../owner/owner_followups.dart' show followUpRequestId;

class NativeMatchManagement extends StatefulWidget {
  const NativeMatchManagement({super.key, required this.auth});
  final AuthController auth;
  @override
  State<NativeMatchManagement> createState() => _NativeMatchManagementState();
}

class _NativeMatchManagementState
    extends NativeOperationState<NativeMatchManagement> {
  @override
  AuthController get auth => widget.auth;
  Json? options;
  List<Json> matches = [];
  bool canAdd = false, canEdit = false;
  @override
  void initState() {
    super.initState();
    load();
  }

  Future<void> load() => run(() async {
    final owner = auth.session?.selected?.role == 'AcademyOwner';
    final permissions = owner
        ? <String>[]
        : (await auth.request('GET', '/api/v1/my-permissions') as List)
              .cast<String>();
    final access = owner
        ? <String, dynamic>{}
        : obj(await auth.request('GET', '/api/v1/my-access'));
    final grant = rows(access['actions'] ?? <Json>[])
        .where((g) => g['module'] == 'matches')
        .firstOrNull;
    bool allows(String operation) =>
        owner ||
        permissions.contains('structure.manage') &&
            (access['useActionGrants'] != true ||
                grant?['read'] == true && grant?[operation] == true);
    final configuration = obj(
      await auth.request('GET', '/api/v1/manage/matches/options'),
    );
    final list = rows(await auth.request('GET', '/api/v1/manage/matches'));
    if (current) {
      setState(() {
        options = configuration;
        matches = list;
        canAdd = configuration['canAdd'] as bool? ?? allows('add');
        canEdit = configuration['canEdit'] as bool? ?? allows('edit');
      });
    }
  });
  Future<void> edit([Json? match]) async {
    await openPage(
      context,
      NativeMatchEditor(auth: auth, options: options!, match: match),
    );
    if (current) await load();
  }

  @override
  Widget build(BuildContext context) => page('إدارة المباريات والنتائج', [
    if (options == null && !busy)
      OutlinedButton(onPressed: load, child: const UiText('إعادة المحاولة')),
    if (options != null && canAdd)
      FilledButton.icon(
        onPressed: () => edit(),
        icon: const Icon(Icons.add),
        label: const UiText('إضافة مباراة'),
      ),
    if (options != null && matches.isEmpty)
      const EmptyMessage('لا توجد مباريات مسجلة.'),
    if (options != null && rows(options!['teams']).isNotEmpty)
      PopupMenuButton<String>(
        tooltip: 'اختيار الفريق للتواصل',
        onSelected: (id) => openPage(
          context,
          AudienceCommunicationPage(auth: auth, id: id, team: true),
        ),
        itemBuilder: (_) => [
          for (final team in rows(options!['teams']))
            PopupMenuItem(
              value: textOf(team['id']),
              child: UiText(textOf(team['name'])),
            ),
        ],
        child: const ListTile(
          leading: Icon(Icons.forum_outlined),
          title: UiText('تواصل فريق'),
          subtitle: UiText(
            'اختر الفريق لمعاينة جمهوره وإرسال إشعار أو ربط دعوته',
          ),
        ),
      ),
    for (final match in matches) ...[
      MatchResultCard(data: match),
      if (canEdit)
        OutlinedButton(
          key: ValueKey('match-edit-${match['id']}'),
          onPressed: () => edit(match),
          child: const UiText('تفاصيل المباراة والنتيجة'),
        ),
    ],
  ]);
}

class NativeMatchEditor extends StatefulWidget {
  const NativeMatchEditor({
    super.key,
    required this.auth,
    required this.options,
    this.match,
  });
  final AuthController auth;
  final Json options;
  final Json? match;
  @override
  State<NativeMatchEditor> createState() => _NativeMatchEditorState();
}

class _NativeMatchEditorState extends NativeOperationState<NativeMatchEditor> {
  @override
  AuthController get auth => widget.auth;
  late final opponent = TextEditingController(text: widget.match?['opponent']),
      venue = TextEditingController(text: widget.match?['venue']),
      notes = TextEditingController(text: widget.match?['notes']),
      date = TextEditingController(
        text: widget.match?['matchDate'] == null
            ? isoDay(DateTime.now())
            : day(widget.match!['matchDate']),
      ),
      time = TextEditingController(
        text: widget.match?['startTime'] == null
            ? '17:00'
            : textOf(widget.match!['startTime']).substring(0, 5),
      ),
      ourScore = TextEditingController(
        text: widget.match?['ourScore']?.toString(),
      ),
      opponentScore = TextEditingController(
        text: widget.match?['opponentScore']?.toString(),
      );
  String? teamId, tournamentId, coachId, quoteHash, savedId;
  List<Json> roster = [];
  List<Json> goals = [];
  final creationRequestId = followUpRequestId();
  bool resultFinalized = false;
  bool get football =>
      rows(widget.options['teams'])
          .any((t) => t['id'] == teamId && t['sportKind'] == 'Football');
  void syncGoals() {
    final n = int.tryParse(ourScore.text) ?? 0;
    if (n < 0 || n > 100) return;
    setState(() {
      goals = List.generate(
        n,
        (i) => i < goals.length
            ? goals[i]
            : {'ordinal': i + 1, 'kind': 'Unknown', 'playerId': null},
      );
    });
  }

  final bench = <String>{};
  bool publish = false, active = true, uncertainCreate = false;
  bool verificationPending = false;
  Json? frozen;
  List<Json> expectedPlayers = [];
  Future<void> verifyStored() async {
    final body = frozen!;
    final saved = obj(
      await auth.request('GET', '/api/v1/manage/matches/$savedId'),
    );
    final expectedPublished = body['publish'] == true || published;
    final savedPlayers = rows(saved['players'] ?? <Json>[]);
    if (saved['id'] != savedId ||
        saved['teamId'] != body['teamId'] ||
        saved['tournamentId'] != body['tournamentId'] ||
        saved['opponent'] != body['opponent'] ||
        day(saved['matchDate']) != body['matchDate'] ||
        saved['startTime'] != body['startTime'] ||
        saved['venue'] != body['venue'] ||
        saved['notes'] != body['notes'] ||
        saved['ourScore'] != body['ourScore'] ||
        saved['opponentScore'] != body['opponentScore'] ||
        body.containsKey('resultFinalized') &&
            saved['resultFinalized'] != body['resultFinalized'] ||
        body['goalSlots'] != null &&
            jsonEncode(saved['goalSlots']) != jsonEncode(body['goalSlots']) ||
        saved['isActive'] != body['isActive'] ||
        (saved['publishedAtUtc'] != null) != expectedPublished ||
        coachId != null && saved['assignedCoachMembershipId'] != coachId ||
        expectedPublished &&
            (savedPlayers.length != expectedPlayers.length ||
                expectedPlayers.any(
                  (p) => !savedPlayers.any(
                    (s) =>
                        s['playerId'] == p['playerId'] &&
                        s['isBench'] == p['isBench'],
                  ),
                ))) {
      throw const ApiFailure(
        0,
        'لم تتأكد مطابقة المباراة المحفوظة. راجع نفس المباراة من القائمة دون إضافة أخرى.',
      );
    }
    if (mounted && current) Navigator.of(context).pop(true);
  }

  bool get published => widget.match?['publishedAtUtc'] != null;
  @override
  void initState() {
    super.initState();
    final m = widget.match;
    teamId = m?['teamId'];
    tournamentId = m?['tournamentId'];
    coachId = m?['assignedCoachMembershipId'];
    publish = published;
    active = m?['isActive'] != false;
    resultFinalized = m?['resultFinalized'] == true;
    goals = rows(m?['goalSlots'] ?? <Json>[])
        .map((g) => Map<String, dynamic>.from(g))
        .toList();
    goals = List.generate(
      (int.tryParse(ourScore.text) ?? 0).clamp(0, 100),
      (i) => i < goals.length
          ? goals[i]
          : {'ordinal': i + 1, 'kind': 'Unknown', 'playerId': null},
    );
    ourScore.addListener(syncGoals);
    bench.addAll(
      rows(m?['players'] ?? <Json>[])
          .where((p) => p['isBench'] == true)
          .map((p) => textOf(p['playerId'])),
    );
  }

  @override
  void dispose() {
    ourScore.removeListener(syncGoals);
    for (final c in [
      opponent,
      venue,
      notes,
      date,
      time,
      ourScore,
      opponentScore,
    ]) {
      c.dispose();
    }
    super.dispose();
  }

  Future<void> reviewRoster() => run(() async {
    if (teamId == null || tournamentId == null) {
      setState(() => error = 'اختر الفريق والبطولة أولًا.');
      return;
    }
    final result = obj(
      await auth.request(
        'GET',
        '/api/v1/manage/matches/teams/$teamId/roster?tournamentId=$tournamentId',
      ),
    );
    if (current) {
      setState(() {
        roster = rows(result['items']);
        quoteHash = textOf(result['quoteHash']);
        bench.removeWhere((id) => !roster.any((p) => p['playerId'] == id));
      });
    }
  });
  Future<void> save() async {
    if (verificationPending) {
      await run(verifyStored);
      return;
    }
    if (!form.currentState!.validate()) return;
    final hasResult = ourScore.text.isNotEmpty || opponentScore.text.isNotEmpty;
    final ours = int.tryParse(ourScore.text),
        theirs = int.tryParse(opponentScore.text);
    if (DateTime.tryParse(date.text) == null ||
        !RegExp(r'^\d{2}:\d{2}$').hasMatch(time.text) ||
        hasResult &&
            (ours == null ||
                theirs == null ||
                ours < 0 ||
                theirs < 0 ||
                ours > 100 ||
                theirs > 100)) {
      setState(
        () => error = 'راجع التاريخ والوقت ونتيجة الفريقين من صفر إلى100.',
      );
      return;
    }
    if (publish && !published && quoteHash == null) {
      setState(() => error = 'راجع قائمة المشاركين قبل النشر.');
      return;
    }
    await run(() async {
      final id = savedId ?? widget.match?['id'];
      frozen ??= {
        'requestId': widget.match == null ? creationRequestId : null,
        'teamId': teamId,
        'tournamentId': tournamentId,
        'opponent': opponent.text.trim(),
        'matchDate': date.text,
        'startTime': '${time.text}:00',
        'venue': venue.text.trim(),
        'notes': notes.text.trim().isEmpty ? null : notes.text.trim(),
        'ourScore': ours,
        'opponentScore': theirs,
        'resultFinalized':
            hasResult && resultFinalized && active && (published || publish),
        'resultVersion': widget.match?['resultRevision'],
        'goalSlots': football ? goals : <Json>[],
        'benchPlayerIds': bench.toList(),
        'publish': publish,
        'isActive': active,
        'rosterQuoteHash': quoteHash,
        'coachMembershipId':
            coachId != widget.match?['assignedCoachMembershipId']
            ? coachId
            : null,
        'coachAssignmentVersion': widget.match?['coachAssignmentVersion'] ?? 0,
      };
      expectedPlayers = published
          ? rows(widget.match?['players'] ?? <Json>[])
                .map((p) => Map<String, dynamic>.from(p))
                .toList()
          : roster
                .map(
                  (p) => <String, dynamic>{
                    'playerId': p['playerId'],
                    'isBench': bench.contains(p['playerId']),
                  },
                )
                .toList();
      Object? response;
      try {
        response = await auth.request(
          id == null ? 'POST' : 'PUT',
          '/api/v1/manage/matches${id == null ? '' : '/$id'}',
          body: frozen,
        );
      } catch (e) {
        if (id == null &&
            (e is! ApiFailure || e.status == 0 || e.status >= 500)) {
          uncertainCreate = true;
          throw const ApiFailure(
            0,
            'لم يتأكد إنشاء المباراة. ارجع إلى القائمة وحدّثها لمراجعتها قبل إضافة مباراة أخرى.',
          );
        }
        if (e is! ApiFailure || e.status == 0 || e.status >= 500) {
          savedId = textOf(id);
          verificationPending = true;
        } else {
          frozen = null;
        }
        rethrow;
      }
      final result = obj(response);
      if (result['id'] == null) {
        uncertainCreate = id == null;
        throw const ApiFailure(
          0,
          'لم تتأكد هوية المباراة المحفوظة. راجع القائمة.',
        );
      }
      savedId = textOf(result['id']);
      verificationPending = true;
      await verifyStored();
    });
  }

  @override
  Widget build(BuildContext context) {
    final teams = rows(widget.options['teams'])
            .where((t) => t['isActive'] != false)
            .toList(),
        team = teams.where((t) => t['id'] == teamId).firstOrNull;
    final tournaments = rows(widget.options['tournaments'])
        .where(
          (t) =>
              t['isActive'] != false &&
              (team == null || t['sportId'] == team['sportId']),
        )
        .toList();
    final coaches = rows(widget.options['coaches'])
        .where(
          (c) =>
              team == null ||
              (c['allBranches'] == true ||
                      (c['branchIds'] as List? ?? []).contains(
                        team['branchId'],
                      )) &&
                  (c['allSports'] == true ||
                      (c['sportIds'] as List? ?? []).contains(team['sportId'])),
        )
        .toList();
    return page(
      widget.match == null ? 'إضافة مباراة' : 'تفاصيل المباراة والنتيجة',
      [
        AbsorbPointer(
          absorbing: verificationPending || uncertainCreate,
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              if (published)
                GCard(
                  child: UiText(
                    '${widget.match!['team']} · ${widget.match!['tournament']}',
                  ),
                )
              else ...[
                choice(
                  'الفريق',
                  teamId,
                  teams,
                  (id) => setState(() {
                    teamId = id;
                    tournamentId = null;
                    coachId = null;
                    quoteHash = null;
                    roster = [];
                    bench.clear();
                    goals = List.generate(
                      (int.tryParse(ourScore.text) ?? 0).clamp(0, 100),
                      (i) => {
                        'ordinal': i + 1,
                        'kind': 'Unknown',
                        'playerId': null,
                      },
                    );
                  }),
                  name: 'name',
                ),
                choice(
                  'البطولة',
                  tournamentId,
                  tournaments,
                  (id) => setState(() {
                    tournamentId = id;
                    quoteHash = null;
                    roster = [];
                    bench.clear();
                  }),
                  name: 'name',
                ),
              ],
              field('المنافس', opponent),
              dateField('تاريخ المباراة', date),
              Padding(
                padding: const EdgeInsets.only(bottom: 14),
                child: TextFormField(
                  controller: time,
                  readOnly: true,
                  decoration: const InputDecoration(
                    labelText: 'الوقت',
                    suffixIcon: Icon(Icons.schedule),
                  ),
                  onTap: () async {
                    final parts = time.text.split(':');
                    final picked = await showTimePicker(
                      context: context,
                      initialTime: TimeOfDay(
                        hour: int.tryParse(parts.first) ?? 17,
                        minute: parts.length > 1
                            ? int.tryParse(parts[1]) ?? 0
                            : 0,
                      ),
                    );
                    if (current && picked != null) {
                      setState(
                        () => time.text =
                            '${picked.hour.toString().padLeft(2, '0')}:${picked.minute.toString().padLeft(2, '0')}',
                      );
                    }
                  },
                ),
              ),
              field('الملعب', venue),
              choice(
                'مدرب المباراة',
                coachId,
                coaches,
                (id) => setState(() => coachId = id),
                name: 'name',
                required: false,
              ),
              field(
                'نتيجة فريقنا',
                ourScore,
                required: false,
                keyboard: TextInputType.number,
              ),
              field(
                'نتيجة المنافس',
                opponentScore,
                required: false,
                keyboard: TextInputType.number,
              ),
              if (football)
                for (var i = 0; i < goals.length; i++)
                  Padding(
                    padding: const EdgeInsets.only(bottom: 14),
                    child: DropdownButtonFormField<String>(
                      key: ValueKey(
                        'goal-${i + 1}-$teamId-${goals[i]['kind']}-${goals[i]['playerId']}',
                      ),
                      initialValue: goals[i]['kind'] == 'Player'
                          ? textOf(goals[i]['playerId'])
                          : textOf(goals[i]['kind']),
                      isExpanded: true,
                      decoration: InputDecoration(labelText: 'الهدف ${i + 1}'),
                      items: [
                        const DropdownMenuItem(
                          value: 'Unknown',
                          child: UiText('غير معروف'),
                        ),
                        const DropdownMenuItem(
                          value: 'OwnGoal',
                          child: UiText('هدف عكسي للمنافس'),
                        ),
                        for (final player
                            in published
                                ? rows(widget.match?['players'] ?? <Json>[])
                                : roster)
                          DropdownMenuItem(
                            value: textOf(player['playerId']),
                            child: UiText(textOf(player['name'])),
                          ),
                      ],
                      onChanged: (v) => setState(
                        () => goals[i] = {
                          'ordinal': i + 1,
                          'kind': v == 'Unknown' || v == 'OwnGoal'
                              ? v
                              : 'Player',
                          'playerId': v == 'Unknown' || v == 'OwnGoal'
                              ? null
                              : v,
                        },
                      ),
                    ),
                  ),
              SwitchListTile(
                title: const UiText('اعتماد النتيجة وإرسال ملخص الأهداف'),
                subtitle: const UiText(
                  'الحفظ دون اعتماد يبقي النتيجة قيد المراجعة.',
                ),
                value: resultFinalized,
                onChanged: (v) => setState(() => resultFinalized = v),
              ),
              field('ملاحظات المباراة', notes, required: false, maxLines: 3),
              if (!published) ...[
                OutlinedButton(
                  onPressed: reviewRoster,
                  child: const UiText('مراجعة المشاركين'),
                ),
                for (final player in roster)
                  CheckboxListTile(
                    title: UiText(textOf(player['name'])),
                    subtitle: const UiText('تحديد لاعب بديل'),
                    value: bench.contains(player['playerId']),
                    onChanged: (checked) => setState(() {
                      checked == true
                          ? bench.add(textOf(player['playerId']))
                          : bench.remove(player['playerId']);
                    }),
                  ),
              ],
              if (!published)
                SwitchListTile(
                  title: const UiText('نشر المباراة للمشاركين'),
                  value: publish,
                  onChanged: (v) => setState(() => publish = v),
                ),
              if (widget.match != null)
                SwitchListTile(
                  title: const UiText('المباراة فعالة'),
                  value: active,
                  onChanged: (v) => setState(() => active = v),
                ),
            ],
          ),
        ),
        FilledButton(
          onPressed: uncertainCreate ? null : save,
          child: UiText(
            verificationPending
                ? 'إعادة التحقق من المباراة نفسها'
                : 'حفظ المباراة',
          ),
        ),
      ],
    );
  }
}

class NativeMatchEntry extends StatefulWidget {
  const NativeMatchEntry({super.key, required this.auth, this.asLink = false});
  final AuthController auth;
  final bool asLink;
  @override
  State<NativeMatchEntry> createState() => _NativeMatchEntryState();
}

class _NativeMatchEntryState extends State<NativeMatchEntry> {
  String? get contextId => widget.auth.session?.selected == null
      ? null
      : '${widget.auth.session!.selected!.id}:${widget.auth.session!.selected!.contextRevision}';
  late final identity = contextId;
  late final access = load();
  Future<bool> load() async {
    final role = widget.auth.session?.selected?.role;
    if (role == 'AcademyOwner') return true;
    if (role == 'Coach') {
      final access = obj(
        await widget.auth.request('GET', '/api/v1/my-match-access'),
      );
      return access['canRead'] == true;
    }
    if (role != 'AcademyAdmin') return false;
    final permissions =
        await widget.auth.request('GET', '/api/v1/my-permissions') as List;
    final scope = obj(await widget.auth.request('GET', '/api/v1/my-access'));
    final grant = rows(scope['actions'] ?? <Json>[])
        .where((g) => g['module'] == 'matches')
        .firstOrNull;
    return permissions.contains('structure.manage') &&
        (scope['useActionGrants'] != true || grant?['read'] == true);
  }

  @override
  Widget build(BuildContext context) => ListenableBuilder(
    listenable: widget.auth,
    builder: (context, _) {
      final brand = BrandScope.maybeOf(context);
      if (brand != null && !brand.features.contains('matches')) {
        return const SizedBox.shrink();
      }
      if (identity == null || identity != contextId) {
        return const SizedBox.shrink();
      }
      return FutureBuilder<bool>(
        future: access,
        builder: (context, s) {
          if (s.data != true) return const SizedBox.shrink();
          void open() =>
              openPage(context, NativeMatchManagement(auth: widget.auth));
          return widget.asLink
              ? GLink(
                  'إدارة المباريات والنتائج',
                  Icons.scoreboard_outlined,
                  open,
                )
              : IconButton(
                  onPressed: open,
                  tooltip: 'إدارة المباريات والنتائج',
                  icon: const Icon(Icons.edit_calendar_outlined),
                );
        },
      );
    },
  );
}
