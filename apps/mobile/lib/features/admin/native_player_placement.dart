import 'dart:convert';

import 'package:flutter/material.dart';
import 'package:intl/intl.dart';

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
  String cause = 'ApprovedAbsence', unit = 'Session';
  final effective = TextEditingController(), validTo = TextEditingController();
  Json? preview, frozen, recorded;
  bool ready = false, uncertain = false;
  DateTime? academyToday;
  String? writeSignature;
  String? writeKey;
  bool sessionOptionsReady = false;
  bool get sessionTransfer => action == PlayerPlacementAction.sessions;
  bool get transfer => groupTransfer || sessionTransfer;
  bool get groupTransfer => action == PlayerPlacementAction.group;
  DateTime? get lastTransferDate {
    final today = academyToday;
    if (today == null) return null;
    final lastDay = DateUtils.getDaysInMonth(today.year + 1, today.month);
    return DateTime(today.year + 1, today.month, today.day.clamp(1, lastDay));
  }

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
                (!groupTransfer || g['id'] != e['trainingGroupId']) &&
                (e['registrationFormat'] == null ||
                    g['registrationFormat'] == e['registrationFormat']),
          )
          .toList();
      if (groups.any((g) => g['id'] == widget.initialTargetGroupId)) {
        group = widget.initialTargetGroupId;
      }
      if (groupTransfer) {
        final calendar = obj(
          await auth.request('GET', '/api/v1/academy/calendar'),
        );
        effective.text = textOf(calendar['today']);
        academyToday = DateTime.tryParse(effective.text);
        if (academyToday == null) {
          throw const ApiFailure(0, 'تعذر تحميل تاريخ الأكاديمية.');
        }
      } else {
        await loadSessionOptions();
      }
    } else {
      final options = obj(
        await auth.request('GET', '$enrollmentRoot/compensation/options'),
      );
      sources = rows(options['sources']);
      final validFrom = day(widget.grant?['validFrom'] ?? '');
      final validThrough = day(widget.grant?['validTo'] ?? '');
      targets = rows(options['targets']).where((slot) {
        final date = day(slot['date']);
        return (validFrom.isEmpty || date.compareTo(validFrom) >= 0) &&
            (validThrough.isEmpty || date.compareTo(validThrough) <= 0);
      }).toList();
      effective.text = textOf(options['today']);
      validTo.text = effective.text;
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
    if (sessionTransfer) await loadSessionOptions();
  });

  Future<void> loadSessionOptions() async {
    if (!current) return;
    setState(() {
      sessionOptionsReady = false;
      targets = [];
    });
    final suffix = group == null
        ? ''
        : '?targetGroup=${Uri.encodeComponent(group!)}';
    final data = obj(await auth.request('GET', '$endpoint/options$suffix'));
    if (current) {
      setState(() {
        sources = rows(data['original']);
        targets = rows(data['alternative']);
        sessionOptionsReady = true;
      });
    }
  }

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
        sessionTransfer && (pairs.isEmpty || !sessionOptionsReady)) {
      setState(() => error = 'اختر البيانات والحصص المطلوب مراجعتها أولًا.');
      return;
    }
    if (groupTransfer) {
      final day = DateTime.tryParse(effective.text);
      if (day == null ||
          day.isBefore(academyToday!) ||
          day.isAfter(lastTransferDate!)) {
        setState(
          () => error = 'اختر تاريخًا من اليوم وحتى سنة بتوقيت الأكاديمية.',
        );
        return;
      }
    }
    setState(() => preview = null);
    final result = obj(
      await auth.request('POST', '$endpoint/preview', body: body()),
    );
    if (groupTransfer &&
        (result['effectiveDate'] != effective.text ||
            (result['targetGroupId'] != null &&
                result['targetGroupId'] != group))) {
      throw const ApiFailure(409, 'تغيّرت بيانات النقل. أعد المعاينة.');
    }
    if (sessionTransfer &&
        !matchesPairs(
          rows(body()['pairs']),
          rows(result['changes'])
              .map(
                (change) => {
                  'sourceSessionId': obj(change['source'])['id'],
                  'targetSessionId': obj(change['target'])['id'],
                },
              )
              .toList(),
        )) {
      throw const ApiFailure(409, 'تغيّرت الحصص. أعد المعاينة.');
    }
    if (action == PlayerPlacementAction.booking &&
        result['target'] is Map &&
        obj(result['target'])['id'] != target) {
      throw const ApiFailure(409, 'تغيّر موعد الحجز. أعد المعاينة.');
    }
    if (current) {
      setState(() {
        preview = result;
        frozen = null;
      });
    }
  });
  Future<void> save() => run(() async {
    if (preview == null ||
        uncertain ||
        rowsOrStrings(preview!['conflicts']).isNotEmpty ||
        preview!['fingerprint'] == null) {
      return;
    }
    frozen ??= {...body(), 'fingerprint': preview!['fingerprint']};
    if (transfer) {
      await saveTransfer();
      return;
    }
    final signature = jsonEncode(body());
    if (writeSignature != signature) {
      writeSignature = signature;
      writeKey = commandKey();
    }
    try {
      recorded = obj(
        await auth.request(
          'POST',
          endpoint,
          body: frozen,
          idempotencyKey: writeKey,
        ),
      );
      await readCompensation();
    } catch (failure) {
      if (!current) return;
      final status = failure is ApiFailure ? failure.status : 0;
      final needsCheck = recorded != null || status == 0 || status >= 500;
      setState(() {
        uncertain = needsCheck;
        if (!needsCheck) invalidate();
      });
      if (status == 401 || status == 403) rethrow;
      if (needsCheck) {
        throw const ApiFailure(0, 'تعذر تأكيد النتيجة. تحقّق من سجل التعويض.');
      }
      rethrow;
    }
  });

  Future<void> readCompensation() async {
    final command = frozen;
    final reviewed = preview;
    if (command == null || reviewed == null) return;
    final grants = rows(
      await auth.request('GET', '$enrollmentRoot/compensations'),
    );
    final receiptId = recorded?['id'];
    final found = action == PlayerPlacementAction.compensation
        ? grants.any(
            (grant) =>
                (receiptId == null || grant['id'] == receiptId) &&
                grant['sportEnrollmentId'] == widget.enrollment['id'] &&
                grant['sourceSessionId'] == command['sourceSessionId'] &&
                grant['cause'] == command['cause'] &&
                grant['unit'] == command['unit'] &&
                grant['units'] == reviewed['units'] &&
                day(grant['validFrom']) == command['validFrom'] &&
                day(grant['validTo']) == command['validTo'],
          )
        : grants.any(
            (grant) =>
                grant['id'] == widget.grant!['id'] &&
                rows(grant['bookings']).any(
                  (booking) =>
                      (receiptId == null || booking['id'] == receiptId) &&
                      booking['trainingSessionId'] ==
                          command['trainingSessionId'] &&
                      booking['units'] == reviewed['units'] &&
                      booking['state'] == 'Reserved',
                ),
          );
    if (!current) return;
    if (!found && recorded != null) {
      throw const ApiFailure(
        0,
        'النتيجة لم تظهر في السجل بعد. تحقّق مرة أخرى.',
      );
    }
    setState(() {
      uncertain = false;
      invalidate();
      if (found) {
        ready = false;
        message = action == PlayerPlacementAction.booking
            ? 'تم حجز الحصة التعويضية.'
            : 'تم اعتماد التعويض.';
      } else {
        message = 'لم تظهر نتيجة مطابقة في السجل. أعد المعاينة قبل التأكيد.';
      }
    });
  }

  Future<void> saveTransfer() async {
    final command = jsonEncode(frozen);
    if (writeSignature != command) {
      writeSignature = command;
      writeKey = commandKey();
    }
    try {
      recorded = obj(
        await auth.request(
          'POST',
          endpoint,
          body: frozen,
          idempotencyKey: writeKey,
        ),
      );
      await readTransfer();
    } catch (failure) {
      if (!current) return;
      final status = failure is ApiFailure ? failure.status : 0;
      final needsCheck =
          recorded != null || status == 0 || status >= 500 || status == 409;
      setState(() {
        uncertain = needsCheck;
        if (!needsCheck) invalidate();
      });
      if (status == 401 || status == 403) rethrow;
      if (needsCheck && (status == 0 || status >= 500)) {
        throw const ApiFailure(
          0,
          'تعذر تأكيد النقل. تحقّق من سجل النقل قبل إعادة المحاولة.',
        );
      }
      rethrow;
    }
  }

  Future<void> readTransfer() async {
    final expected = frozen;
    final review = preview;
    if (expected == null || review == null) return;
    final saved = groupTransfer
        ? await groupTransferRecorded(expected, review)
        : await sessionTransferRecorded(expected);
    if (!current) return;
    if (saved) {
      setState(() {
        message = groupTransfer
            ? 'تم تسجيل نقل المجموعة من ${displayTransferDate(expected['effectiveDate'])}.'
            : 'تم تسجيل نقل الحصص المختارة.';
        uncertain = false;
        ready = false;
        invalidate();
      });
    } else if (recorded != null) {
      throw const ApiFailure(
        0,
        'لم يظهر سجل النقل بعد. تحقّق من السجل مرة أخرى.',
      );
    } else {
      if (sessionTransfer) await loadSessionOptions();
      if (!current) return;
      setState(() {
        uncertain = false;
        invalidate();
        message = 'لم يظهر نقل مطابق في السجل. أعد المعاينة قبل التأكيد.';
      });
    }
  }

  Future<bool> groupTransferRecorded(Json expected, Json review) async {
    final history = rows(
      await auth.request('GET', '$enrollmentRoot/group-transfers'),
    );
    return history.any(
      (row) =>
          (recorded?['id'] == null || row['id'] == recorded?['id']) &&
          row['sourceGroupId'] == review['sourceGroupId'] &&
          row['targetGroupId'] == expected['groupId'] &&
          day(row['effectiveDate']) == expected['effectiveDate'],
    );
  }

  bool matchesPairs(List<Json> expected, List<Json> actual) =>
      expected.length == actual.length &&
      expected.every(
        (pair) => actual.any(
          (move) =>
              move['sourceSessionId'] == pair['sourceSessionId'] &&
              move['targetSessionId'] == pair['targetSessionId'],
        ),
      );

  Future<bool> sessionTransferRecorded(Json expected) async {
    final player = obj(
      await auth.request(
        'GET',
        '/api/v1/people/players/${widget.enrollment['playerId']}',
      ),
    );
    final enrollment = rows(player['enrollments'])
        .where((item) => item['id'] == widget.enrollment['id'])
        .firstOrNull;
    if (enrollment == null || enrollment['sessionMoves'] is! List) {
      throw const ApiFailure(0, 'تعذر قراءة سجل النقل. أعد التحقق.');
    }
    final batches = <Object, List<Json>>{};
    for (final row in rows(enrollment['sessionMoves'])) {
      final batchId = row['batchId'];
      if (batchId == null ||
          (recorded?['id'] != null && recorded?['id'] != batchId)) {
        continue;
      }
      (batches[batchId] ??= []).add(row);
    }
    return batches.values.any(
      (batch) => matchesPairs(rows(expected['pairs']), batch),
    );
  }

  String displayTransferDate(Object? value) {
    final parsed = DateTime.tryParse(day(value));
    return parsed == null
        ? ''
        : DateFormat.yMMMd(Localizations.localeOf(context).toString())
              .format(parsed);
  }

  Widget transferSchedule(String title, List<Json> sessions) => Column(
    crossAxisAlignment: CrossAxisAlignment.start,
    children: [
      const SizedBox(height: 12),
      UiText(title, style: Theme.of(context).textTheme.titleSmall),
      UiText(
        sessions.isEmpty
            ? 'لا توجد مواعيد مجدولة.'
            : 'العدد: ${sessions.length}',
      ),
      for (final item
          in sessions.where((row) => row['sessionDate'] != null).take(3))
        UiText(
          '${displayTransferDate(item['sessionDate'])} · ${trainingTimeRange(item['startTime'], item['endTime'])}',
        ),
      if (sessions.length > 3) const UiText('تظهر أقرب 3 مواعيد.'),
    ],
  );

  Widget groupTransferPreview(Json value) => GCard(
    child: Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        const Heading('أثر النقل على الجدول'),
        UiText('المجموعة الحالية · ${value['sourceGroup']}'),
        UiText('المجموعة الجديدة · ${value['targetGroup']}'),
        UiText('بداية النقل · ${displayTransferDate(value['effectiveDate'])}'),
        transferSchedule(
          'مواعيد تخرج من جدول اللاعب',
          rows(value['removedSessions']),
        ),
        transferSchedule(
          'مواعيد تضاف إلى جدول اللاعب',
          rows(value['addedSessions']),
        ),
        const SizedBox(height: 12),
        const UiText(
          'يتغيّر جدول اللاعب من تاريخ النقل، مع حفظ الاشتراك والتحصيل وسجل الحضور.',
        ),
      ],
    ),
  );

  List<String> rowsOrStrings(Object? value) =>
      value is List ? value.map((v) => '$v').toList() : [];
  void addPair() {
    if (source == null || target == null) return;
    if (source == target ||
        pairs.any(
          (p) =>
              p['sourceSessionId'] == source ||
              p['targetSessionId'] == source ||
              p['sourceSessionId'] == target ||
              p['targetSessionId'] == target,
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

  Widget sessionFact(String title, Json slot) => Column(
    crossAxisAlignment: CrossAxisAlignment.start,
    children: [
      UiText(title, style: Theme.of(context).textTheme.titleSmall),
      UiText(textOf(slot['group'])),
      UiText(
        '${displayTransferDate(slot['date'])} · ${trainingTimeRange(slot['start'], slot['end'])}',
      ),
    ],
  );

  Widget compensationPreview(Json result) {
    final booking = action == PlayerPlacementAction.booking;
    final slot = booking
        ? (result['target'] is Map
              ? obj(result['target'])
              : targets.firstWhere((item) => item['id'] == target))
        : sources.firstWhere((item) => item['id'] == source);
    final unitLabel = (booking ? widget.grant!['unit'] : unit) == 'Minute'
        ? 'دقيقة'
        : 'حصة';
    return GCard(
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Heading(booking ? 'الحصة التعويضية' : 'الحصة الأصلية'),
          UiText(textOf(slot['group'])),
          UiText(displayTransferDate(slot['date'])),
          if (booking) UiText(trainingTimeRange(slot['start'], slot['end'])),
          const SizedBox(height: 8),
          UiText(
            booking
                ? 'يُحجز من التعويض: ${result['units']} $unitLabel'
                : 'التعويض: ${result['units']} $unitLabel',
          ),
          if (booking && result['available'] != null)
            UiText('المتاح قبل الحجز: ${result['available']} $unitLabel'),
          if (booking && result['expectedPlayers'] != null)
            UiText(
              'العدد بعد الحجز: ${result['expectedPlayers']}'
              '${result['capacity'] == null ? '' : ' من ${result['capacity']}'}',
            ),
          if (!booking) ...[
            UiText(
              'الصلاحية من ${displayTransferDate(effective.text)} إلى ${displayTransferDate(validTo.text)}',
            ),
            const UiText('يُضاف التعويض دون تحصيل أو تمديد الاشتراك.'),
          ],
        ],
      ),
    );
  }

  @override
  Widget build(BuildContext context) => page(title, [
    UiText(
      '${widget.enrollment['sportName']} · ${widget.enrollment['branchName']}',
    ),
    if (ready) ...[
      AbsorbPointer(
        absorbing: uncertain,
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            if (action == PlayerPlacementAction.group ||
                action == PlayerPlacementAction.sessions)
              if (groups.isNotEmpty)
                choice('المجموعة البديلة', group, groups, selectGroup)
              else
                const EmptyMessage('لا توجد مجموعة مطابقة في هذا الفرع.'),
            if (groupTransfer && groups.isNotEmpty)
              dateField(
                'تاريخ بدء النقل',
                effective,
                changed: invalidate,
                firstDate: academyToday,
                lastDate: lastTransferDate,
              ),
            if (sessionTransfer && sessionOptionsReady && sources.isEmpty)
              const EmptyMessage('لا توجد حصص قابلة للنقل لهذا التسجيل.'),
            if (sessionTransfer && group != null && !sessionOptionsReady)
              OutlinedButton(
                onPressed: () => run(loadSessionOptions),
                child: const UiText('إعادة تحميل الحصص'),
              ),
            if (sessionTransfer &&
                group != null &&
                sessionOptionsReady &&
                targets.isEmpty)
              const EmptyMessage('لا توجد حصص بديلة في المجموعة المختارة.'),
            if (action == PlayerPlacementAction.compensation && sources.isEmpty)
              const EmptyMessage('لا توجد حصة أصلية متاحة للاعتماد.'),
            if (action == PlayerPlacementAction.booking && targets.isEmpty)
              const EmptyMessage('لا توجد حصة متاحة خلال صلاحية هذا التعويض.'),
            if ((sessionTransfer && targets.isNotEmpty && sources.isNotEmpty) ||
                action == PlayerPlacementAction.compensation &&
                    sources.isNotEmpty)
              choice(
                'الحصة الأصلية',
                source,
                slots(
                  sessionTransfer
                      ? sources
                            .where(
                              (item) => !pairs.any(
                                (pair) =>
                                    pair['sourceSessionId'] == item['id'] ||
                                    pair['targetSessionId'] == item['id'],
                              ),
                            )
                            .toList()
                      : sources,
                ),
                (v) => setState(() {
                  source = v;
                  invalidate();
                }),
                required: action != PlayerPlacementAction.sessions,
              ),
            if ((sessionTransfer && targets.isNotEmpty && sources.isNotEmpty) ||
                action == PlayerPlacementAction.booking && targets.isNotEmpty)
              choice(
                'الحصة البديلة',
                target,
                slots(
                  sessionTransfer
                      ? targets
                            .where(
                              (item) => !pairs.any(
                                (pair) =>
                                    pair['targetSessionId'] == item['id'] ||
                                    pair['sourceSessionId'] == item['id'],
                              ),
                            )
                            .toList()
                      : targets,
                ),
                (v) => setState(() {
                  target = v;
                  invalidate();
                }),
                required: action != PlayerPlacementAction.sessions,
              ),
            if (action == PlayerPlacementAction.sessions) ...[
              if (targets.isNotEmpty && sources.isNotEmpty)
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
                          'الأصلية · ${pair['sourceLabel']}\nالبديلة · ${pair['targetLabel']}',
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
      if (!uncertain &&
          (!groupTransfer || groups.isNotEmpty) &&
          (action != PlayerPlacementAction.compensation ||
              sources.isNotEmpty) &&
          (action != PlayerPlacementAction.booking || targets.isNotEmpty))
        OutlinedButton(
          onPressed:
              (groupTransfer && group == null) ||
                  (sessionTransfer && (pairs.isEmpty || !sessionOptionsReady))
              ? null
              : review,
          child: UiText(
            groupTransfer
                ? 'معاينة نقل المجموعة'
                : sessionTransfer
                ? 'معاينة نقل الحصص'
                : action == PlayerPlacementAction.booking
                ? 'معاينة حجز التعويض'
                : 'معاينة استحقاق التعويض',
          ),
        ),
      if (preview != null) ...[
        if (groupTransfer) groupTransferPreview(preview!),
        if (sessionTransfer) ...[
          const Heading('أثر النقل على الجدول'),
          for (final change in rows(preview!['changes']))
            GCard(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  sessionFact('الحصة الأصلية', obj(change['source'])),
                  const SizedBox(height: 8),
                  sessionFact('الحصة البديلة', obj(change['target'])),
                  const SizedBox(height: 8),
                  UiText(
                    'العدد بعد النقل: ${change['expectedPlayers']}${change['capacity'] == null ? '' : ' · السعة ${change['capacity']}'}',
                  ),
                ],
              ),
            ),
          const UiText(
            'يتغيّر موعد الحصص المختارة، مع حفظ المجموعة الأساسية والاشتراك وسجل الحضور.',
          ),
        ],
        if (!transfer) compensationPreview(preview!),
        for (final conflict in rowsOrStrings(preview!['conflicts']))
          GCard(child: UiText(conflict)),
        if (transfer && uncertain)
          OutlinedButton(
            onPressed: () => run(readTransfer),
            child: const UiText('تحقّق من سجل النقل'),
          ),
        if (transfer &&
            !uncertain &&
            rowsOrStrings(preview!['conflicts']).isEmpty)
          FilledButton(
            onPressed: save,
            child: UiText(
              groupTransfer ? 'تأكيد نقل المجموعة' : 'تأكيد نقل الحصص',
            ),
          ),
        if (!transfer && uncertain)
          OutlinedButton(
            onPressed: () => run(readCompensation),
            child: const UiText('تحقّق من سجل التعويض'),
          ),
        if (!transfer &&
            !uncertain &&
            rowsOrStrings(preview!['conflicts']).isEmpty)
          FilledButton(
            onPressed: save,
            child: UiText(
              action == PlayerPlacementAction.booking
                  ? 'تأكيد حجز التعويض'
                  : 'تأكيد استحقاق التعويض',
            ),
          ),
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
  Json? releaseToVerify;
  bool releaseAcknowledged = false;
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
      try {
        await auth.request(
          'POST',
          '/api/v1/people/compensation-bookings/${booking['id']}/cancel',
          body: {'version': booking['version']},
        );
      } catch (failure) {
        if (!current) return;
        if (failure is ApiFailure &&
            failure.status > 0 &&
            failure.status < 500) {
          rethrow;
        }
        setState(() {
          releaseToVerify = booking;
          releaseAcknowledged = false;
        });
        throw const ApiFailure(
          0,
          'تعذر تأكيد تحرير الحجز. تحقّق من سجل التعويض.',
        );
      }
      if (!current) return;
      setState(() {
        releaseToVerify = booking;
        releaseAcknowledged = true;
      });
      await readRelease();
    });
  }

  Future<void> readRelease() async {
    final booking = releaseToVerify;
    if (booking == null) return;
    final data = rows(await auth.request('GET', root));
    if (!current) return;
    final released = data.any(
      (grant) => rows(
        grant['bookings'],
      ).any((row) => row['id'] == booking['id'] && row['state'] == 'Cancelled'),
    );
    setState(() => grants = data);
    if (!released && releaseAcknowledged) {
      throw const ApiFailure(
        0,
        'التحرير لم يظهر في السجل بعد. تحقّق مرة أخرى.',
      );
    }
    setState(() {
      releaseToVerify = null;
      releaseAcknowledged = false;
      message = released
          ? 'حُرر الحجز وأُعيدت قراءة السجل.'
          : 'لم يظهر تحرير الحجز في السجل. راجع الموعد قبل المحاولة مجددًا.';
    });
  }

  @override
  Widget build(BuildContext context) => page('التعويض والحصص البديلة', [
    if (releaseToVerify != null)
      OutlinedButton(
        onPressed: () => run(readRelease),
        child: const UiText('تحقّق من سجل التعويض'),
      ),
    if (widget.canManage && releaseToVerify == null)
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
            if (widget.canManage &&
                (g['units'] as num) > (g['allocated'] as num))
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
                        releaseToVerify == null &&
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
    if (releaseToVerify == null)
      OutlinedButton(onPressed: load, child: const UiText('تحديث سجل التعويض')),
  ], screen: 'players');
}
