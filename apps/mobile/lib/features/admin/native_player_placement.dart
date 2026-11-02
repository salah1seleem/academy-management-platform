import 'package:flutter/material.dart';

import '../../core/api/api_client.dart';
import '../../core/auth/auth_controller.dart';
import '../../core/theme/ui_text.dart';
import '../guardian/guardian_ui.dart';
import 'native_operation_form.dart';

enum PlayerPlacementAction { group, sessions, compensation, booking }

/// Uses the existing placement preview, fingerprint and idempotent command.
/// The backend remains responsible for eligibility, capacity and balance.
class NativePlayerPlacement extends StatefulWidget {
  const NativePlayerPlacement({
    super.key,
    required this.auth,
    required this.enrollment,
    required this.action,
    this.grant,
    this.initialTargetGroupId,
  });
  final AuthController auth;
  final Json enrollment;
  final PlayerPlacementAction action;
  final Json? grant;
  final String? initialTargetGroupId;
  @override
  State<NativePlayerPlacement> createState() => _NativePlayerPlacementState();
}

class _NativePlayerPlacementState
    extends NativeOperationState<NativePlayerPlacement> {
  @override
  AuthController get auth => widget.auth;
  PlayerPlacementAction get action => widget.action;
  String get enrollmentRoot =>
      '/api/v1/people/enrollments/${widget.enrollment['id']}';
  String get endpoint => switch (action) {
    PlayerPlacementAction.group => '$enrollmentRoot/group-transfer',
    PlayerPlacementAction.sessions => '$enrollmentRoot/session-transfer',
    PlayerPlacementAction.compensation => '$enrollmentRoot/compensation',
    PlayerPlacementAction.booking =>
      '/api/v1/people/compensations/${widget.grant!['id']}/booking',
  };
  String get title => switch (action) {
    PlayerPlacementAction.group => 'نقل مجموعة اللاعب',
    PlayerPlacementAction.sessions => 'نقل حصص مختارة',
    PlayerPlacementAction.compensation => 'اعتماد تعويض',
    PlayerPlacementAction.booking => 'حجز حصة بديلة',
  };
  List<Json> groups = [], sources = [], targets = [];
  final pairs = <Json>[];
  String? group, source, target;
  String cause = 'ApprovedAbsence', unit = 'Session', intent = commandKey();
  final effective = TextEditingController(), validTo = TextEditingController();
  Json? preview, frozen, recorded;
  bool ready = false, confirmed = false, uncertain = false;
  @override
  void initState() {
    super.initState();
    load();
  }

  @override
  void dispose() {
    effective.dispose();
    validTo.dispose();
    super.dispose();
  }

  void invalidate() {
    preview = null;
    frozen = null;
    recorded = null;
    confirmed = false;
    intent = commandKey();
  }

  Future<void> load() => run(() async {
    final e = widget.enrollment;
    if (action == PlayerPlacementAction.group ||
        action == PlayerPlacementAction.sessions) {
      final options = obj(
        await auth.request('GET', '/api/v1/manage/structure/options'),
      );
      groups = rows(options['groups'])
          .where(
            (g) =>
                g['branchId'] == e['branchId'] &&
                g['sportId'] == e['sportId'] &&
                g['id'] != e['trainingGroupId'] &&
                (e['registrationFormat'] == null ||
                    g['registrationFormat'] == e['registrationFormat']),
          )
          .toList();
      if (groups.any((g) => g['id'] == widget.initialTargetGroupId)) {
        group = widget.initialTargetGroupId;
      }
      final calendar = obj(
        await auth.request('GET', '/api/v1/academy/calendar'),
      );
      effective.text = textOf(calendar['today']);
      if (action == PlayerPlacementAction.sessions) {
        final options = obj(
          await auth.request('GET', '$enrollmentRoot/session-transfer/options'),
        );
        sources = rows(options['original']);
      }
    } else {
      final options = obj(
        await auth.request('GET', '$enrollmentRoot/compensation/options'),
      );
      sources = rows(options['sources']);
      targets = rows(options['targets']);
      effective.text = textOf(options['today']);
      validTo.text = textOf(options['today']);
    }
    if (current) setState(() => ready = true);
  });
  Future<void> selectGroup(String? id) => run(() async {
    setState(() {
      group = id;
      source = null;
      target = null;
      targets = [];
      invalidate();
    });
    if (action == PlayerPlacementAction.sessions && id != null) {
      final options = obj(
        await auth.request(
          'GET',
          '$enrollmentRoot/session-transfer/options?targetGroup=${Uri.encodeComponent(id)}',
        ),
      );
      if (current) {
        setState(() {
          sources = rows(options['original']);
          targets = rows(options['alternative']);
        });
      }
    }
  });
  List<Json> slots(List<Json> data) => [
    for (final s in data)
      {
        ...s,
        'arabicName':
            '${s['group']} · ${day(s['date'])}${s['start'] == null ? '' : ' · ${s['start']} — ${s['end']}'}',
      },
  ];
  Json body() => switch (action) {
    PlayerPlacementAction.group => {
      'groupId': group,
      'effectiveDate': effective.text,
    },
    PlayerPlacementAction.sessions => {
      'pairs': pairs
          .map(
            (p) => {
              'sourceSessionId': p['sourceSessionId'],
              'targetSessionId': p['targetSessionId'],
            },
          )
          .toList(),
    },
    PlayerPlacementAction.compensation => {
      'sourceSessionId': source,
      'cause': cause,
      'unit': unit,
      'validFrom': effective.text,
      'validTo': validTo.text,
    },
    PlayerPlacementAction.booking => {'trainingSessionId': target},
  };
  Future<void> review() => run(() async {
    if (!form.currentState!.validate() ||
        action == PlayerPlacementAction.sessions && pairs.isEmpty) {
      setState(() => error = 'اختر البيانات والحصص المطلوب مراجعتها أولًا.');
      return;
    }
    final result = obj(
      await auth.request('POST', '$endpoint/preview', body: body()),
    );
    if (current) {
      setState(() {
        preview = result;
        confirmed = false;
        frozen = null;
        intent = commandKey();
      });
    }
  });
  Future<void> save() => run(() async {
    if (preview == null ||
        !confirmed ||
        rowsOrStrings(preview!['conflicts']).isNotEmpty ||
        preview!['fingerprint'] == null) {
      return;
    }
    frozen ??= {...body(), 'fingerprint': preview!['fingerprint']};
    var written = recorded != null;
    try {
      recorded ??= obj(
        await auth.request(
          'POST',
          endpoint,
          body: frozen,
          idempotencyKey: intent,
        ),
      );
      written = true;
      final Object? read = await auth.request('GET', switch (action) {
        PlayerPlacementAction.group => '$enrollmentRoot/group-transfers',
        PlayerPlacementAction.sessions =>
          '/api/v1/people/players/${widget.enrollment['playerId']}',
        _ => '$enrollmentRoot/compensations',
      });
      final found = switch (action) {
        PlayerPlacementAction.group ||
        PlayerPlacementAction.compensation => rows(read).any(
          (r) =>
              r['id'] == recorded!['id'] &&
              (action == PlayerPlacementAction.group
                  ? r['targetGroupId'] == frozen!['groupId'] &&
                        r['sourceGroupId'] == preview!['sourceGroupId'] &&
                        day(r['effectiveDate']) == frozen!['effectiveDate']
                  : r['sportEnrollmentId'] == widget.enrollment['id'] &&
                        r['sourceSessionId'] == frozen!['sourceSessionId'] &&
                        r['cause'] == frozen!['cause'] &&
                        r['unit'] == frozen!['unit'] &&
                        r['units'] == preview!['units'] &&
                        day(r['validFrom']) == frozen!['validFrom'] &&
                        day(r['validTo']) == frozen!['validTo']),
        ),
        PlayerPlacementAction.booking => rows(read).any(
          (g) =>
              g['id'] == widget.grant!['id'] &&
              rows(g['bookings']).any(
                (b) =>
                    b['id'] == recorded!['id'] &&
                    b['trainingSessionId'] == frozen!['trainingSessionId'] &&
                    b['units'] == preview!['units'] &&
                    b['state'] == 'Reserved',
              ),
        ),
        PlayerPlacementAction.sessions => rows(obj(read)['enrollments']).any(
          (e) =>
              e['id'] == widget.enrollment['id'] &&
              rows(e['sessionMoves'])
                      .where((m) => m['batchId'] == recorded!['id'])
                      .length ==
                  rows(frozen!['pairs']).length &&
              rows(frozen!['pairs']).every(
                (p) => rows(e['sessionMoves']).any(
                  (m) =>
                      m['batchId'] == recorded!['id'] &&
                      m['sourceSessionId'] == p['sourceSessionId'] &&
                      m['targetSessionId'] == p['targetSessionId'],
                ),
              ),
        ),
      };
      if (!found) {
        throw const ApiFailure(
          0,
          'لم تتأكد قراءة النتيجة المحفوظة. أعد تأكيد الطلب نفسه دون إنشاء طلب آخر.',
        );
      }
      if (current) {
        setState(() {
          message = 'حُفظ الإجراء وأُعيدت قراءة سجله الفعلي.';
          uncertain = false;
          ready = false;
          preview = null;
          frozen = null;
          recorded = null;
          confirmed = false;
        });
      }
    } catch (e) {
      if (current) {
        setState(
          () => uncertain =
              written || e is! ApiFailure || e.status == 0 || e.status >= 500,
        );
      }
      rethrow;
    }
  });
  List<String> rowsOrStrings(Object? value) =>
      value is List ? value.map((v) => '$v').toList() : [];
  void addPair() {
    if (source == null || target == null) return;
    if (pairs.any(
          (p) =>
              p['sourceSessionId'] == source || p['targetSessionId'] == target,
        ) ||
        pairs.length >= 100) {
      setState(() => error = 'اختر حصة وبديلًا غير مكررين، بحد أقصى 100 حصة.');
      return;
    }
    setState(() {
      pairs.add({
        'sourceSessionId': source,
        'targetSessionId': target,
        'sourceLabel': slots(sources)
            .firstWhere((s) => s['id'] == source)['arabicName'],
        'targetLabel': slots(targets)
            .firstWhere((s) => s['id'] == target)['arabicName'],
      });
      source = null;
      target = null;
      invalidate();
    });
  }

  @override
  Widget build(BuildContext context) => page(title, [
    UiText(
      '${widget.enrollment['sportName']} · ${widget.enrollment['branchName']}',
    ),
    const UiText(
      'لا يغيّر هذا الإجراء التحصيل أو الحضور السابق. راجع الأثر والتعارضات قبل الاعتماد.',
    ),
    if (ready) ...[
      AbsorbPointer(
        absorbing: uncertain,
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            if (action == PlayerPlacementAction.group ||
                action == PlayerPlacementAction.sessions)
              choice('المجموعة البديلة', group, groups, selectGroup),
            if (action == PlayerPlacementAction.group)
              dateField('تاريخ بدء النقل', effective, changed: invalidate),
            if (action == PlayerPlacementAction.sessions ||
                action == PlayerPlacementAction.compensation)
              choice(
                'الحصة الأصلية',
                source,
                slots(sources),
                (v) => setState(() {
                  source = v;
                  invalidate();
                }),
                required: action != PlayerPlacementAction.sessions,
              ),
            if (action == PlayerPlacementAction.sessions ||
                action == PlayerPlacementAction.booking)
              choice(
                'الحصة البديلة',
                target,
                slots(targets),
                (v) => setState(() {
                  target = v;
                  invalidate();
                }),
                required: action != PlayerPlacementAction.sessions,
              ),
            if (action == PlayerPlacementAction.sessions) ...[
              OutlinedButton(
                onPressed: source == null || target == null ? null : addPair,
                child: const UiText('إضافة إلى الحصص المختارة'),
              ),
              for (final pair in pairs)
                GCard(
                  child: Row(
                    children: [
                      Expanded(
                        child: UiText(
                          '${pair['sourceLabel']}\n← ${pair['targetLabel']}',
                        ),
                      ),
                      IconButton(
                        tooltip: 'إزالة من المراجعة',
                        onPressed: () => setState(() {
                          pairs.remove(pair);
                          invalidate();
                        }),
                        icon: const Icon(Icons.close),
                      ),
                    ],
                  ),
                ),
            ],
            if (action == PlayerPlacementAction.compensation) ...[
              choice(
                'سبب التعويض',
                cause,
                const [
                  {
                    'id': 'ApprovedAbsence',
                    'arabicName': 'غياب أو اعتذار معتمد للاعب',
                  },
                  {'id': 'CancelledSession', 'arabicName': 'إلغاء حصة فعلية'},
                  {'id': 'CoachAbsent', 'arabicName': 'غياب المدربين المسجل'},
                ],
                (v) => setState(() {
                  cause = v!;
                  invalidate();
                }),
              ),
              choice(
                'وحدة التعويض',
                unit,
                const [
                  {'id': 'Session', 'arabicName': 'حصة'},
                  {'id': 'Minute', 'arabicName': 'دقيقة'},
                ],
                (v) => setState(() {
                  unit = v!;
                  invalidate();
                }),
              ),
              dateField('بداية صلاحية التعويض', effective, changed: invalidate),
              dateField('نهاية صلاحية التعويض', validTo, changed: invalidate),
            ],
          ],
        ),
      ),
      if (!uncertain)
        OutlinedButton(
          onPressed: review,
          child: const UiText('معاينة الأثر والتعارضات'),
        ),
      if (preview != null) ...[
        if (preview!['effect'] != null)
          GCard(child: UiText('${preview!['effect']}')),
        if (action == PlayerPlacementAction.group)
          GCard(
            child: UiText(
              '${preview!['sourceGroup']} ← ${preview!['targetGroup']}\n${day(preview!['effectiveDate'])}\nحصص مغادرة: ${rows(preview!['removedSessions']).length} · حصص بديلة: ${rows(preview!['addedSessions']).length}',
            ),
          ),
        if (action == PlayerPlacementAction.sessions)
          for (final c in rows(preview!['changes']))
            GCard(
              child: UiText(
                '${slots([obj(c['source'])]).first['arabicName']}\n← ${slots([obj(c['target'])]).first['arabicName']}\nالعدد المتوقع ${c['expectedPlayers']} · السعة ${c['capacity'] ?? 'غير محددة'}',
              ),
            ),
        if (preview!['units'] != null)
          GCard(
            child: UiText(
              'الوحدات ${preview!['units']}${preview!['available'] == null ? '' : ' · المتاح ${preview!['available']}'}',
            ),
          ),
        if (action == PlayerPlacementAction.booking &&
            preview!['target'] is Map)
          GCard(
            child: UiText(
              '${slots([obj(preview!['target'])]).first['arabicName']}\nالعدد المتوقع ${preview!['expectedPlayers']} · السعة ${preview!['capacity'] ?? 'غير محددة'}',
            ),
          ),
        for (final conflict in rowsOrStrings(preview!['conflicts']))
          GCard(child: UiText(conflict)),
        if (rowsOrStrings(preview!['conflicts']).isEmpty) ...[
          CheckboxListTile(
            value: confirmed,
            onChanged: uncertain
                ? null
                : (v) => setState(() => confirmed = v == true),
            title: const UiText('راجعت الأثر والحصص وأؤكد اعتماد الإجراء.'),
          ),
          if (uncertain)
            const UiText(
              'نتيجة الطلب غير مؤكدة. أعد تأكيد نفس الطلب؛ لا تغيّر الاختيارات.',
            ),
          FilledButton(
            onPressed: confirmed ? save : null,
            child: UiText(
              uncertain ? 'إعادة تأكيد الإجراء نفسه' : 'تأكيد الإجراء',
            ),
          ),
        ],
      ],
    ],
    if (!ready && message == null && !busy)
      OutlinedButton(
        onPressed: load,
        child: const UiText('إعادة تحميل الخيارات'),
      ),
  ], screen: 'players');
}

class NativePlayerCompensations extends StatefulWidget {
  const NativePlayerCompensations({
    super.key,
    required this.auth,
    required this.enrollment,
    required this.canManage,
  });
  final AuthController auth;
  final Json enrollment;
  final bool canManage;
  @override
  State<NativePlayerCompensations> createState() =>
      _NativePlayerCompensationsState();
}

class _NativePlayerCompensationsState
    extends NativeOperationState<NativePlayerCompensations> {
  @override
  AuthController get auth => widget.auth;
  List<Json> grants = [];
  bool ready = false;
  String get root =>
      '/api/v1/people/enrollments/${widget.enrollment['id']}/compensations';
  @override
  void initState() {
    super.initState();
    load();
  }

  Future<void> load() => run(() async {
    final data = rows(await auth.request('GET', root));
    if (current) {
      setState(() {
        grants = data;
        ready = true;
      });
    }
  });
  Future<void> open(PlayerPlacementAction action, [Json? grant]) async {
    await openPage(
      context,
      NativePlayerPlacement(
        auth: auth,
        enrollment: widget.enrollment,
        action: action,
        grant: grant,
      ),
    );
    if (current) await load();
  }

  Future<void> cancel(Json booking) async {
    final yes = await showDialog<bool>(
      context: context,
      builder: (context) => AlertDialog(
        title: const UiText('تحرير حجز التعويض'),
        content: UiText(
          '${booking['group']} · ${day(booking['date'])}\nيحرر الحجز دون حذف سجل التعويض أو تغيير حضور قائم.',
        ),
        actions: [
          TextButton(
            onPressed: () => Navigator.pop(context, false),
            child: const UiText('رجوع'),
          ),
          FilledButton(
            onPressed: () => Navigator.pop(context, true),
            child: const UiText('تأكيد تحرير الحجز'),
          ),
        ],
      ),
    );
    if (yes != true || !current) return;
    await run(() async {
      await auth.request(
        'POST',
        '/api/v1/people/compensation-bookings/${booking['id']}/cancel',
        body: {'version': booking['version']},
      );
      final data = rows(await auth.request('GET', root));
      if (!data.any(
        (g) => rows(g['bookings'])
            .any((b) => b['id'] == booking['id'] && b['state'] == 'Cancelled'),
      )) {
        throw const ApiFailure(
          0,
          'لم تتأكد قراءة تحرير الحجز. حدّث السجل قبل أي إجراء آخر.',
        );
      }
      if (current) {
        setState(() {
          grants = data;
          message = 'حُرر الحجز وأُعيدت قراءة السجل.';
        });
      }
    });
  }

  @override
  Widget build(BuildContext context) => page('التعويض والحصص البديلة', [
    if (widget.canManage)
      GLink(
        'اعتماد تعويض',
        Icons.add,
        () => open(PlayerPlacementAction.compensation),
      ),
    if (ready && grants.isEmpty)
      const EmptyMessage('لا توجد تعويضات مسجلة لهذا التسجيل.'),
    for (final g in grants)
      GCard(
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            Heading(
              '${g['sport']} · ${g['unit'] == 'Minute' ? 'دقائق' : 'حصص'}',
            ),
            UiText(
              'الحصة الأصلية ${day(g['sourceDate'])} · ${const {'ApprovedAbsence': 'غياب أو اعتذار معتمد', 'CancelledSession': 'حصة ملغاة', 'CoachAbsent': 'غياب المدربين المسجل'}[g['cause']] ?? 'سبب تعويض مسجل'}',
            ),
            UiText(
              'الوحدات ${g['units']} · المحجوز ${g['allocated']} · المستهلك ${g['consumed']}\n${day(g['validFrom'])} — ${day(g['validTo'])}',
            ),
            if (widget.canManage)
              OutlinedButton(
                onPressed: () => open(PlayerPlacementAction.booking, g),
                child: const UiText('حجز حصة بديلة'),
              ),
            for (final b in rows(g['bookings']))
              Padding(
                padding: const EdgeInsets.symmetric(vertical: 8),
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.stretch,
                  children: [
                    UiText(
                      '${b['group']} · ${day(b['date'])} · ${b['units']} ${g['unit'] == 'Minute' ? 'دقيقة' : 'حصة'}',
                    ),
                    UiText(b['state'] == 'Cancelled' ? 'حجز محرر' : 'حجز قائم'),
                    if (widget.canManage &&
                        b['state'] == 'Reserved' &&
                        (b['consumed'] as num) == 0)
                      OutlinedButton(
                        onPressed: () => cancel(b),
                        child: const UiText('تحرير حجز التعويض'),
                      ),
                  ],
                ),
              ),
          ],
        ),
      ),
    OutlinedButton(onPressed: load, child: const UiText('تحديث سجل التعويض')),
  ], screen: 'players');
}
