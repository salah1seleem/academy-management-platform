import 'dart:async';

import '../../core/widgets/arabic_number_input.dart';
import '../shared/group_schedule.dart';
import '../shared/directory_cards.dart';
import '../coach/coach_evaluation.dart';

import 'package:flutter/material.dart';

import '../../core/auth/auth_controller.dart';
import '../../core/api/api_client.dart';
import '../../core/theme/ui_text.dart';
import '../guardian/guardian_ui.dart';
import '../shared/staff_capabilities.dart';
import '../shared/guardian_contact.dart';
import '../owner/member_values.dart' show MemberGuardianDetail;
import '../shared/private_media.dart';
import '../shared/matches.dart';
import '../shared/receivable_balances.dart';
import '../guardian/guardian_payments.dart';
import '../coach/coach_player.dart';
import '../coach/coach_attendance.dart';
import 'native_operation_form.dart';
import 'native_enrollment.dart';
import 'native_renewal.dart';
import 'native_refund.dart';
import 'native_player_placement.dart';

class NativePlayerWorkspace extends StatefulWidget {
  const NativePlayerWorkspace({
    super.key,
    required this.auth,
    required this.id,
  });
  final AuthController auth;
  final String id;
  @override
  State<NativePlayerWorkspace> createState() => _NativePlayerWorkspaceState();
}

class _NativePlayerWorkspaceState
    extends NativeOperationState<NativePlayerWorkspace> {
  @override
  AuthController get auth => widget.auth;
  StaffCapabilities? access;
  Json? player;
  String tab = 'overview', enrollmentId = '';
  List<Json> get enrollments => rows(player?['enrollments'] ?? []);
  Json? get enrollment =>
      enrollments.where((e) => e['id'] == enrollmentId).firstOrNull;
  bool allows(
    String permission,
    String module,
    String operation, [
    List<String> leaves = const [],
  ]) => access?.allows(permission, module, operation, leaves) == true;
  @override
  void initState() {
    super.initState();
    load();
  }

  Future<void> load() => run(() async {
    final rights = await StaffCapabilities.load(auth);
    if (!rights.allows('people.manage', 'players', 'Read', [
      'workflow.players.read',
    ])) {
      throw const ApiFailure(
        403,
        'ليست لديك صلاحية عرض ملف اللاعب في هذا النطاق.',
      );
    }
    final p = obj(
      await auth.request('GET', '/api/v1/people/players/${widget.id}'),
    );
    if (current) {
      setState(() {
        access = rights;
        player = p;
        if (!enrollments.any((e) => e['id'] == enrollmentId)) {
          enrollmentId = enrollments.isEmpty
              ? ''
              : textOf(enrollments.first['id']);
        }
      });
    }
  });
  Future<void> open(Widget child) async {
    await openPage(context, child);
    if (current) await load();
  }

  Widget actionLink(String title, IconData icon, VoidCallback onTap) =>
      GLink(title, icon, onTap, compact: true);
  @override
  Widget build(BuildContext context) {
    final p = player, e = enrollment;
    final tabs = <String, String>{
      'overview': 'نظرة عامة',
      if (allows('subscription.read', 'subscriptions', 'Read'))
        'subscriptions': 'الاشتراكات والماليات',
      if (allows('attendance.read', 'attendance', 'Read'))
        'training': 'التدريب والحضور',
      if (allows('structure.manage', 'matches', 'Read')) 'matches': 'المباريات',
      if (allows('evaluation.read', 'evaluations', 'Read'))
        'progress': 'التطور',
      'more': 'المزيد',
    };
    if (!tabs.containsKey(tab)) tab = 'overview';
    return page('ملف اللاعب', [
      if (p == null && !busy)
        OutlinedButton(
          onPressed: load,
          child: const UiText('إعادة تحميل ملف اللاعب'),
        ),
      if (p != null) ...[
        Row(
          children: [
            SizedBox(
              width: 72,
              height: 88,
              child: isPrivateAsset(p['photoReference'])
                  ? PrivateImage(
                      auth: auth,
                      path: textOf(p['photoReference']),
                      height: 88,
                    )
                  : ColoredBox(
                      color: Theme.of(context)
                          .colorScheme
                          .surfaceContainerHighest,
                      child: const Icon(Icons.person_outline, size: 40),
                    ),
            ),
            const SizedBox(width: 12),
            Expanded(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  UiText(
                    textOf(p['arabicName']),
                    style: const TextStyle(
                      fontSize: 18,
                      fontWeight: FontWeight.w700,
                    ),
                  ),
                  UiText(
                    textOf(p['playerCode']),
                    style: const TextStyle(fontSize: 12),
                  ),
                  UiText(
                    'العمر ${ageOn(p['dateOfBirth'], p['asOfDate'] ?? DateTime.now()) ?? 'غير محدد'} سنة',
                    style: const TextStyle(fontSize: 12),
                  ),
                ],
              ),
            ),
          ],
        ),
        GuardianContactAction(auth: auth, playerId: widget.id),
        if (enrollments.length > 1)
          choice('الرياضة والتسجيل', enrollmentId, [
            for (final row in enrollments)
              {
                ...row,
                'arabicName': '${row['sportName']} · ${row['branchName']}',
              },
          ], (v) => setState(() => enrollmentId = v ?? '')),
        SingleChildScrollView(
          scrollDirection: Axis.horizontal,
          child: Row(
            children: [
              for (final item in tabs.entries)
                TextButton(
                  onPressed: () => setState(() => tab = item.key),
                  child: UiText(
                    item.value,
                    style: TextStyle(
                      fontSize: 11,
                      fontWeight: tab == item.key
                          ? FontWeight.bold
                          : FontWeight.normal,
                      color: tab == item.key
                          ? Theme.of(context).colorScheme.primary
                          : null,
                    ),
                  ),
                ),
            ],
          ),
        ),
        if (tab == 'overview') ...[
          for (final g in rows(p['guardians'] ?? []))
            GCard(
              child: ListTile(
                title: UiText(textOf(g['displayName'])),
                subtitle: UiText(
                  textOf(
                    g['relationshipType'] == 'ولي أمر — اعتماد استيراد'
                        ? 'ولي الأمر'
                        : g['relationshipType'] ?? 'ولي الأمر',
                  ),
                ),
                trailing: const Icon(Icons.chevron_left),
                onTap: () =>
                    open(MemberGuardianDetail(auth: auth, id: textOf(g['id']))),
              ),
            ),
          if (e != null)
            GCard(
              child: UiText(
                '${e['sportName']} · ${e['branchName']}\n${e['groupName']}\n${e['levelName'] ?? ''}${e['shirtNumber'] == null ? '' : ' · رقم القميص ${e['shirtNumber']}'}',
              ),
            ),
          if (allows('people.manage', 'players', 'Edit', [
            'workflow.players.edit',
          ]))
            actionLink(
              'تعديل بيانات اللاعب',
              Icons.edit_outlined,
              () => open(NativePlayerEdit(auth: auth, player: p)),
            ),
          if (allows('people.manage', 'players', 'Edit', [
            'workflow.players.edit',
          ]))
            actionLink(
              'تغيير صورة اللاعب',
              Icons.add_a_photo_outlined,
              () => run(() async {
                final asset = await pickAndUpload(auth, {
                  'purpose': 'Photo',
                  'playerId': widget.id,
                }, imageOnly: true);
                if (asset == null || !current) return;
                final read = obj(
                  await auth.request(
                    'GET',
                    '/api/v1/people/players/${widget.id}',
                  ),
                );
                if (read['photoReference'] != asset['url']) {
                  throw const ApiFailure(
                    0,
                    'رُفعت الصورة ولم تتأكد قراءة ارتباطها. حدّث ملف اللاعب قبل إعادة الرفع.',
                  );
                }
                if (current) {
                  setState(() {
                    player = read;
                    message = 'حُفظت صورة اللاعب وأُعيدت قراءتها.';
                  });
                }
              }),
            ),
          if (access?.explicitAction('finance', 'Add') == true &&
              allows('people.manage', 'players', 'Add', [
                'workflow.players.create',
              ]) &&
              allows('subscription.renew.manage', 'subscriptions', 'Add') &&
              allows('structure.manage', 'academy', 'Read') &&
              allows('payment.read', 'payments', 'Read', [
                'workflow.payments.collect',
              ]))
            actionLink(
              'اشتراك جديد',
              Icons.person_add_alt_outlined,
              () => open(NativeNewEnrollment(auth: auth, player: p)),
            ),
        ],
        if (tab == 'subscriptions') ...[
          if (e == null)
            const EmptyMessage('لا توجد تسجيلات رياضية.')
          else ...[
            GCard(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Heading('${e['sportName']} · ${e['branchName']}'),
                  UiText(textOf(e['groupName'])),
                  if (e['subscription'] is Map) ...[
                    StatusPill(e['subscription']['status']),
                    const SizedBox(height: 12),
                    Row(
                      children: [
                        Expanded(
                          child: Column(
                            crossAxisAlignment: CrossAxisAlignment.start,
                            children: [
                              const UiText(
                                'بداية الاشتراك',
                                style: TextStyle(fontSize: 12),
                              ),
                              UiText(
                                day(e['subscription']['startDate']),
                                style: const TextStyle(
                                  fontWeight: FontWeight.w700,
                                ),
                              ),
                            ],
                          ),
                        ),
                        const SizedBox(width: 16),
                        Expanded(
                          child: Column(
                            crossAxisAlignment: CrossAxisAlignment.start,
                            children: [
                              const UiText(
                                'نهاية الاشتراك',
                                style: TextStyle(fontSize: 12),
                              ),
                              UiText(
                                day(e['subscription']['endDate']),
                                style: const TextStyle(
                                  fontWeight: FontWeight.w700,
                                ),
                              ),
                            ],
                          ),
                        ),
                      ],
                    ),
                    const SizedBox(height: 12),
                    UiText(textOf(e['subscription']['plan'])),
                  ],
                ],
              ),
            ),
            if (allows('subscription.renew.manage', 'subscriptions', 'Write', [
              'workflow.subscriptions.renew',
            ]))
              actionLink(
                'تجديد اشتراك',
                Icons.refresh,
                () => open(
                  NativeAdminRenewal(auth: auth, enrollmentId: enrollmentId),
                ),
              ),
            if (e['subscription'] is Map)
              actionLink(
                'تفاصيل الاشتراك وسجل التعديلات',
                Icons.history,
                () => open(
                  NativeSubscriptionPeriod(
                    auth: auth,
                    id: textOf(e['subscription']['id']),
                    access: access!,
                  ),
                ),
              ),
            if (allows('reports.financial.read', 'finance', 'Read', [
              'workflow.reports.financial',
            ]))
              actionLink(
                'المستحقات والأرصدة',
                Icons.account_balance_wallet_outlined,
                () => open(
                  GuardianPage(
                    title: 'مستحقات اللاعب',
                    child: ReceivableBalances(
                      auth: auth,
                      staff: true,
                      filters: {'playerId': widget.id},
                    ),
                  ),
                ),
              ),
          ],
        ],
        if (tab == 'subscriptions' &&
            allows('subscription.adjustment.manage', 'subscriptions', 'Edit', [
              'workflow.subscriptions.refund',
            ]) &&
            allows('people.manage', 'players', 'Read') &&
            access!.explicitAction('finance', 'Add'))
          actionLink(
            'تسجيل استرداد',
            Icons.undo_outlined,
            () => open(NativePlayerRefund(auth: auth, playerId: widget.id)),
          ),
        if (tab == 'training' && e != null) ...[
          WeeklyGroupSchedule(schedules: rows(e['recurring'] ?? <Json>[])),
          if (e['trainingGroupId'] != null)
            actionLink(
              'الحصص والحضور',
              Icons.fact_check_outlined,
              () => open(
                NativePlayerSessions(
                  auth: auth,
                  playerId: widget.id,
                  groupId: textOf(e['trainingGroupId']),
                  sportId: textOf(e['sportId']),
                  branchId: textOf(e['branchId']),
                ),
              ),
            ),
          for (final h in rows(e['transfers'] ?? []))
            GCard(
              child: UiText(
                'نقل مجموعة · ${h['groupName']} · ${day(h['effectiveDate'])}',
              ),
            ),
          for (final h in rows(e['sessionMoves'] ?? []))
            GCard(
              child: UiText('حصة منقولة · ${h['group']} · ${day(h['date'])}'),
            ),
        ],
        if (tab == 'matches')
          actionLink(
            'مباريات اللاعب',
            Icons.sports_soccer_outlined,
            () => open(MatchSchedule(auth: auth, playerId: widget.id)),
          ),
        if (tab == 'progress' && e != null)
          actionLink(
            'التقييمات والتطور',
            Icons.insights_outlined,
            () => open(
              CoachPlayerPage(
                auth: auth,
                enrollmentId: enrollmentId,
                groupId: e['trainingGroupId'] as String?,
              ),
            ),
          ),
        if (tab == 'more') ...[
          if (e != null && allows('structure.manage', 'academy', 'Edit')) ...[
            if (allows('people.manage', 'players', 'Edit', [
              'workflow.players.place',
            ]))
              actionLink(
                'نقل مجموعة اللاعب',
                Icons.move_up_outlined,
                () => open(
                  NativePlayerPlacement(
                    auth: auth,
                    enrollment: {...e, 'playerId': widget.id},
                    action: PlayerPlacementAction.group,
                  ),
                ),
              ),
            if (allows('people.manage', 'players', 'Edit', [
              'workflow.players.session-transfer',
            ]))
              actionLink(
                'نقل حصص مختارة',
                Icons.event_repeat_outlined,
                () => open(
                  NativePlayerPlacement(
                    auth: auth,
                    enrollment: {...e, 'playerId': widget.id},
                    action: PlayerPlacementAction.sessions,
                  ),
                ),
              ),
            actionLink(
              'التعويض والحصص البديلة',
              Icons.event_available_outlined,
              () => open(
                NativePlayerCompensations(
                  auth: auth,
                  enrollment: {...e, 'playerId': widget.id},
                  canManage: allows('people.manage', 'players', 'Edit', [
                    'workflow.players.compensation',
                  ]),
                ),
              ),
            ),
          ],
          if (e != null &&
              allows('people.manage', 'players', 'Edit', [
                'workflow.players.edit',
              ]))
            actionLink(
              'رقم القميص لهذا التسجيل',
              Icons.confirmation_number_outlined,
              () => open(NativeShirtNumberEdit(auth: auth, enrollment: e)),
            ),
        ],
      ],
    ], screen: 'players');
  }
}

class NativePlayerEdit extends StatefulWidget {
  const NativePlayerEdit({super.key, required this.auth, required this.player});
  final AuthController auth;
  final Json player;
  @override
  State<NativePlayerEdit> createState() => _NativePlayerEditState();
}

class _NativePlayerEditState extends NativeOperationState<NativePlayerEdit> {
  @override
  AuthController get auth => widget.auth;
  late final name = TextEditingController(
        text: textOf(widget.player['arabicName']),
      ),
      english = TextEditingController(
        text: textOf(widget.player['englishName'] ?? ''),
      ),
      birth = TextEditingController(text: textOf(widget.player['dateOfBirth'])),
      address = TextEditingController(
        text: textOf(widget.player['address'] ?? ''),
      );
  @override
  void dispose() {
    name.dispose();
    english.dispose();
    birth.dispose();
    address.dispose();
    super.dispose();
  }

  Future<void> save() => run(() async {
    if (!form.currentState!.validate()) return;
    final p = widget.player;
    await auth.request(
      'PUT',
      '/api/v1/people/players/${p['id']}',
      body: {
        'arabicName': name.text.trim(),
        'englishName': english.text.trim(),
        'dateOfBirth': birth.text,
        'address': address.text.trim(),
        'gender': p['gender'],
        'heightCm': p['heightCm'],
        'weightKg': p['weightKg'],
        'preferredFoot': p['preferredFoot'],
        'footballPosition': p['footballPosition'],
      },
    );
    final read = obj(
      await auth.request('GET', '/api/v1/people/players/${p['id']}'),
    );
    if (read['arabicName'] != name.text.trim() ||
        read['dateOfBirth'] != birth.text ||
        textOf(read['englishName'] ?? '').trim() != english.text.trim() ||
        textOf(read['address'] ?? '').trim() != address.text.trim()) {
      throw const FormatException();
    }
    if (current) {
      setState(() => message = 'حُفظت بيانات اللاعب وأُعيدت قراءتها.');
    }
  });
  @override
  Widget build(BuildContext context) => page('تعديل بيانات اللاعب', [
    field('اسم اللاعب', name),
    field('الاسم بالإنجليزية', english, required: false),
    dateField('تاريخ الميلاد', birth),
    field('العنوان', address, required: false),
    FilledButton(onPressed: save, child: const UiText('حفظ بيانات اللاعب')),
  ], screen: 'players');
}

class NativeShirtNumberEdit extends StatefulWidget {
  const NativeShirtNumberEdit({
    super.key,
    required this.auth,
    required this.enrollment,
  });
  final AuthController auth;
  final Json enrollment;
  @override
  State<NativeShirtNumberEdit> createState() => _NativeShirtNumberEditState();
}

class _NativeShirtNumberEditState
    extends NativeOperationState<NativeShirtNumberEdit> {
  @override
  AuthController get auth => widget.auth;
  late final number = TextEditingController(
    text: widget.enrollment['shirtNumber']?.toString() ?? '',
  );
  @override
  void dispose() {
    number.dispose();
    super.dispose();
  }

  Future<void> save() => run(() async {
    final n = number.text.trim().isEmpty ? null : int.tryParse(number.text);
    if (number.text.trim().isNotEmpty && (n == null || n < 0 || n > 999)) {
      setState(() => error = 'أدخل رقمًا من 0 إلى 999، أو اتركه فارغًا.');
      return;
    }
    final value = obj(
      await auth.request(
        'PUT',
        '/api/v1/people/enrollments/${widget.enrollment['id']}/shirt-number',
        body: {'shirtNumber': n},
      ),
    );
    if (value['shirtNumber'] != n) throw const FormatException();
    if (current) {
      setState(() => message = 'حُفظ رقم القميص لهذا التسجيل الرياضي.');
    }
  });
  @override
  Widget build(BuildContext context) => page('رقم القميص لهذا التسجيل', [
    UiText(
      '${widget.enrollment['sportName']} · ${widget.enrollment['branchName']}',
    ),
    const UiText(
      'رقم اختياري لهذا التسجيل. توافره في الزي يُراجع عند اختيار المقاس.',
    ),
    field(
      'رقم القميص',
      number,
      required: false,
      keyboard: TextInputType.number,
    ),
    FilledButton(onPressed: save, child: const UiText('حفظ رقم القميص')),
  ], screen: 'players');
}

class NativeSubscriptionPeriod extends StatefulWidget {
  const NativeSubscriptionPeriod({
    super.key,
    required this.auth,
    required this.id,
    required this.access,
  });
  final AuthController auth;
  final String id;
  final StaffCapabilities access;
  @override
  State<NativeSubscriptionPeriod> createState() =>
      _NativeSubscriptionPeriodState();
}

class _NativeSubscriptionPeriodState
    extends NativeOperationState<NativeSubscriptionPeriod> {
  @override
  AuthController get auth => widget.auth;
  Json? period;
  DateTime? accountingDay;
  final when = TextEditingController(),
      days = TextEditingController(),
      end = TextEditingController(),
      reason = TextEditingController();
  String? operation;
  bool confirmed = false, uncertain = false;
  Json? frozen;
  String? savedAdjustmentId;
  String intent = commandKey();
  bool allowed(String leaf, [String? capability]) {
    final effective = capability == null ? null : period?[capability];
    return effective is bool
        ? effective
        : widget.access.allows(
            'subscription.adjustment.manage',
            'subscriptions',
            'Edit',
            [leaf],
          );
  }

  bool get financial =>
      widget.access.owner ||
      widget.access.allows('payment.read', 'payments', 'Read', [
        'workflow.payments.read',
      ]) ||
      widget.access.allows('reports.financial.read', 'finance', 'Read', [
        'workflow.reports.financial',
      ]);
  @override
  void initState() {
    super.initState();
    load();
  }

  @override
  void dispose() {
    when.dispose();
    days.dispose();
    end.dispose();
    reason.dispose();
    super.dispose();
  }

  DateTime? periodDay(Object? value) =>
      DateTime.tryParse('${day(value)}T00:00:00Z');

  String dateLabel(BuildContext context, DateTime date) =>
      MaterialLocalizations.of(context).formatShortDate(date);

  Future<void> load() => run(readPeriod);

  Future<void> readPeriod() async {
    try {
      final p = obj(
        await auth.request('GET', '/api/v1/subscriptions/periods/${widget.id}'),
      );
      if (p['id'] != widget.id) {
        throw const ApiFailure(404, 'الفترة المطلوبة غير متاحة.');
      }
      final today =
          p['asOfDate'] ??
          obj(await auth.request('GET', '/api/v1/academy/calendar'))['today'];
      if (current) {
        setState(() {
          period = p;
          accountingDay = periodDay(today);
          when.text = textOf(today);
          end.text = day(p['endDate'] ?? today);
          operation = null;
          invalidate();
        });
      }
    } catch (_) {
      if (current) setState(() => period = null);
      rethrow;
    }
  }

  void invalidate() {
    frozen = null;
    savedAdjustmentId = null;
    confirmed = false;
    intent = commandKey();
  }

  Future<void> save() => run(() async {
    if (period == null ||
        operation == null ||
        !confirmed ||
        !form.currentState!.validate()) {
      return;
    }
    final count = int.tryParse(days.text);
    if (operation == 'Add' ||
        operation == 'Deduct' ||
        operation == 'sessions') {
      if (count == null || count <= 0) {
        setState(
          () => error = operation == 'sessions'
              ? 'أدخل عدد حصص صحيحًا أكبر من صفر.'
              : 'أدخل عدد أيام صحيحًا أكبر من صفر.',
        );
        return;
      }
    }
    if (operation == 'sessions' &&
        (count! > 10000 ||
            reason.text.trim().isEmpty ||
            DateTime.tryParse(end.text) == null)) {
      setState(() => error = 'راجع عدد الحصص وتاريخ النهاية وسبب الإضافة.');
      return;
    }
    final action = operation == 'Add' || operation == 'Deduct'
        ? 'days'
        : operation!;
    frozen ??= {
      'expectedVersion': period!['version'],
      'reason': reason.text.trim().isEmpty ? null : reason.text.trim(),
      if (action == 'sessions') ...{
        'sessions': count,
        'endDate': end.text,
      } else if (action == 'days') ...{
        'direction': operation,
        'days': count,
      } else
        'effectiveDate': when.text,
    };
    var written = false;
    try {
      if (savedAdjustmentId == null) {
        final saved = obj(
          await auth.request(
            'POST',
            '/api/v1/subscriptions/periods/${widget.id}/$action',
            body: frozen,
            idempotencyKey: intent,
          ),
        );
        savedAdjustmentId = saved['adjustmentId'] as String?;
        if (savedAdjustmentId == null) {
          throw const ApiFailure(
            0,
            'لم يصل مرجع التعديل؛ أعد التحقق من الطلب نفسه.',
          );
        }
      }
      written = true;
      final read = obj(
        await auth.request('GET', '/api/v1/subscriptions/periods/${widget.id}'),
      );
      if (read['id'] != widget.id ||
          !rows(read['adjustments'] ?? []).any(
            (h) =>
                h['id'] == savedAdjustmentId &&
                (action != 'sessions' ||
                    h['type'] == 'SessionsAdded' &&
                        h['sessionsDelta'] == frozen!['sessions'] &&
                        day(h['newEndDate']) == frozen!['endDate'] &&
                        h['newRemainingSessions'] is int &&
                        h['oldRemainingSessions'] is int &&
                        (h['newRemainingSessions'] as int) -
                                (h['oldRemainingSessions'] as int) ==
                            frozen!['sessions']),
          ) ||
          action == 'sessions' &&
              (day(read['endDate']) != frozen!['endDate'] ||
                  read['initialSessions'] != period!['initialSessions'])) {
        throw const ApiFailure(
          0,
          'لم تتأكد قراءة حركة التعديل. أعد تأكيد الطلب نفسه دون تغيير التفاصيل.',
        );
      }
      if (current) {
        setState(() {
          period = read;
          accountingDay = periodDay(read['asOfDate']) ?? accountingDay;
          message = 'حُفظ التعديل وأُعيدت قراءة سجل الاشتراك.';
          uncertain = false;
          frozen = null;
          savedAdjustmentId = null;
          confirmed = false;
          operation = null;
          intent = commandKey();
        });
      }
    } catch (e) {
      if (!written && e is ApiFailure && [403, 404, 409].contains(e.status)) {
        await readPeriod();
        if (current) setState(() => error = e.message);
        return;
      }
      if (current) {
        setState(
          () => uncertain =
              written || e is! ApiFailure || e.status == 0 || e.status >= 500,
        );
      }
      rethrow;
    }
  });
  @override
  Widget build(BuildContext context) {
    final p = period;
    final startDate = periodDay(p?['startDate']);
    final endDate = periodDay(p?['endDate']);
    final frozenFrom = periodDay(p?['frozenFromDate']);
    final resumeFrom = frozenFrom?.add(const Duration(days: 1));
    final canPause = allowed('workflow.subscriptions.pause', 'canPause');
    final waitForResume =
        canPause &&
        p?['status'] == 'Frozen' &&
        resumeFrom != null &&
        accountingDay != null &&
        resumeFrom.isAfter(accountingDay!);
    final deductionLimit = startDate != null && endDate != null
        ? endDate.difference(startDate).inDays.clamp(0, 365)
        : 0;
    final dayCount = int.tryParse(days.text);
    final dated =
        p != null &&
        ['Duration', 'Combined'].contains(p['planType']) &&
        p['endDate'] != null &&
        p['status'] != 'Cancelled';
    final choices = <Json>[
      if (dated &&
          deductionLimit > 0 &&
          allowed('workflow.subscriptions.days', 'canDeductDays'))
        {'id': 'Deduct', 'arabicName': 'خصم أيام'},
      if (p != null &&
          p['status'] != 'Cancelled' &&
          p['remainingSessions'] != null &&
          ['Sessions', 'Combined'].contains(p['planType']) &&
          p['canGrantSessions'] == true &&
          allowed('workflow.subscriptions.sessions'))
        {'id': 'sessions', 'arabicName': 'إضافة حصص وتحديد نهاية الاشتراك'},
      if (dated && canPause) ...[
        if (p['status'] == 'Active')
          {'id': 'freeze', 'arabicName': 'تجميد الاشتراك'},
        if (p['status'] == 'Frozen' && resumeFrom != null && !waitForResume)
          {'id': 'resume', 'arabicName': 'استئناف الاشتراك'},
      ],
      if (p != null &&
          p['status'] != 'Cancelled' &&
          allowed('workflow.subscriptions.cancel', 'canCancel'))
        {'id': 'cancel', 'arabicName': 'إلغاء الاشتراك'},
    ];
    return page(
      'الاشتراك وسجل التعديلات',
      [
        if (p == null && error != null)
          OutlinedButton(
            onPressed: load,
            child: const UiText('إعادة المحاولة'),
          ),
        if (p != null) ...[
          Heading(textOf(p['plan'])),
          UiText(
            [p['player'], p['sport'], p['branch']]
                .whereType<String>()
                .where((value) => value.isNotEmpty)
                .join(' · '),
          ),
          StatusPill(p['status']),
          if (startDate != null)
            UiText(
              endDate == null
                  ? 'تاريخ البداية ${dateLabel(context, startDate)}'
                  : 'من ${dateLabel(context, startDate)} إلى ${dateLabel(context, endDate)}',
            ),
          if (waitForResume)
            UiText(
              'يمكن الاستئناف ابتداءً من ${dateLabel(context, resumeFrom)}.',
            ),
          if (p['remainingSessions'] != null)
            UiText('${p['remainingSessions']} حصص متبقية'),
          if ((p['financialValuesVisible'] as bool? ?? financial) &&
              p['priceSnapshot'] != null)
            UiText(
              'القيمة المسجلة ${money(p['priceSnapshot'], p['currencySnapshot'])}',
            ),
          if (p['receiptId'] != null)
            GLink(
              'إيصال التحصيل',
              Icons.receipt_long_outlined,
              () => openPage(
                context,
                ReceiptPage(
                  auth: auth,
                  id: textOf(p['receiptId']),
                  staff: true,
                ),
              ),
            ),
          if (choices.isNotEmpty) ...[
            choice(
              'الإجراء',
              operation,
              choices,
              uncertain
                  ? null
                  : (v) => setState(() {
                      operation = v;
                      invalidate();
                    }),
              required: false,
            ),
            if (operation != null)
              AbsorbPointer(
                absorbing: uncertain,
                child: Column(
                  children: [
                    if (operation == 'sessions') ...[
                      field(
                        'عدد الحصص الإضافية',
                        days,
                        keyboard: TextInputType.number,
                        changed: () => setState(invalidate),
                      ),
                      dateField(
                        'نهاية الاشتراك الجديدة',
                        end,
                        changed: invalidate,
                      ),
                      const UiText(
                        'تضاف الحصص إلى رصيد هذه الفترة وتتغير نهايتها كلها. السعر والتحصيلات كما هي.',
                        style: TextStyle(fontSize: 12),
                      ),
                    ] else if (operation == 'Deduct') ...[
                      field(
                        'عدد الأيام',
                        days,
                        keyboard: TextInputType.number,
                        changed: () => setState(invalidate),
                        validate: (value) {
                          final count = int.tryParse(value);
                          return count != null &&
                                  count >= 1 &&
                                  count <= deductionLimit
                              ? null
                              : 'أدخل عددًا صحيحًا من 1 إلى $deductionLimit.';
                        },
                      ),
                      if (endDate != null &&
                          dayCount != null &&
                          dayCount >= 1 &&
                          dayCount <= deductionLimit)
                        UiText(
                          'النهاية المتوقعة ${dateLabel(context, endDate.subtract(Duration(days: dayCount)))}.',
                        ),
                    ] else
                      dateField(
                        'تاريخ السريان',
                        when,
                        changed: invalidate,
                        firstDate: operation == 'resume'
                            ? resumeFrom
                            : operation == 'freeze'
                            ? startDate
                            : null,
                        lastDate: operation == 'resume' || operation == 'freeze'
                            ? accountingDay
                            : null,
                      ),
                    field(
                      'سبب التعديل',
                      reason,
                      required: operation == 'sessions',
                      maxLines: 3,
                      changed: () => setState(invalidate),
                    ),
                    if (operation == 'cancel')
                      const UiText(
                        'إلغاء الاشتراك لا يصرف مبلغًا تلقائيًا. الاسترداد له مراجعة وصلاحية منفصلة.',
                      ),
                    CheckboxListTile(
                      value: confirmed,
                      title: const UiText(
                        'راجعت أثر التعديل على الاشتراك وأؤكد تنفيذه.',
                      ),
                      onChanged: (v) => setState(() => confirmed = v == true),
                    ),
                  ],
                ),
              ),
            if (uncertain)
              const UiText(
                'لم تتأكد نتيجة الطلب؛ احتفظ بنفس التفاصيل وأعد التأكيد أو راجع السجل.',
              ),
            if (operation != null)
              FilledButton(
                onPressed: confirmed ? save : null,
                child: UiText(
                  uncertain
                      ? 'إعادة تأكيد التعديل نفسه'
                      : 'تأكيد تعديل الاشتراك',
                ),
              ),
          ],
          if (!uncertain)
            OutlinedButton(
              onPressed: load,
              child: const UiText('تحديث سجل الاشتراك'),
            ),
          const Heading('سجل التعديلات'),
          if (rows(p['adjustments'] ?? []).isEmpty)
            const EmptyMessage('لا توجد تعديلات مسجلة.'),
          for (final h in rows(p['adjustments'] ?? []))
            adjustmentCard(context, h),
        ],
      ],
      screen: 'players',
      onRetry: load,
    );
  }

  Widget adjustmentCard(BuildContext context, Json h) {
    final type = h['type'];
    final date = periodDay(h['effectiveDate']);
    final endDate = periodDay(h['newEndDate']);
    final delta = h['daysDelta'];
    return GCard(
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          UiText(
            const {
                  'FreezeStarted': 'بدء التجميد',
                  'FreezeEnded': 'استئناف الاشتراك',
                  'DaysAdded': 'إضافة أيام',
                  'DaysDeducted': 'خصم أيام',
                  'SessionsAdded': 'إضافة حصص',
                  'Cancelled': 'إلغاء الاشتراك',
                }[type] ??
                'تعديل مسجل',
            style: const TextStyle(fontWeight: FontWeight.w700),
          ),
          if (date != null) UiText(dateLabel(context, date)),
          if (delta is int && type == 'DaysDeducted')
            UiText('الأيام المخصومة ${delta.abs()}'),
          if (delta is int && type == 'DaysAdded')
            UiText('الأيام المضافة $delta'),
          if (delta is int && type == 'FreezeEnded')
            UiText('أيام التجميد $delta'),
          if (type == 'SessionsAdded')
            UiText(
              'أضيفت ${h['sessionsDelta']} حصة · الرصيد من ${h['oldRemainingSessions']} إلى ${h['newRemainingSessions']}',
            ),
          if (endDate != null)
            UiText('نهاية الفترة ${dateLabel(context, endDate)}'),
          if (h['reason'] is String &&
              (h['reason'] as String).trim().isNotEmpty)
            UiText(h['reason'] as String),
        ],
      ),
    );
  }
}

class NativePlayerSessions extends StatelessWidget {
  const NativePlayerSessions({
    super.key,
    required this.auth,
    required this.playerId,
    required this.groupId,
    required this.sportId,
    required this.branchId,
  });
  final AuthController auth;
  final String playerId, groupId, sportId, branchId;
  @override
  Widget build(BuildContext context) => GuardianPage(
    title: 'تدريب وحضور اللاعب',
    child: RemoteBody(
      auth: auth,
      path:
          '/api/v1/attendance/history?${Uri(queryParameters: {'playerId': playerId, 'sportId': sportId, 'branchId': branchId}).query}',
      builder: (context, value) => gList([
        GLink(
          'حصص المجموعة اليوم',
          Icons.fact_check_outlined,
          () => openPage(
            context,
            GuardianPage(
              title: 'حصص المجموعة',
              child: RemoteBody(
                auth: auth,
                path:
                    '/api/v1/attendance/sessions?${Uri(queryParameters: {'groupId': groupId}).query}',
                builder: (context, value) => gList([
                  for (final s in rows(value))
                    GLink(
                      '${sessionDateLabel(s['sessionDate'])} · ${s['group']}',
                      Icons.calendar_today_outlined,
                      () => openPage(
                        context,
                        CoachAttendance(auth: auth, sessionId: textOf(s['id'])),
                      ),
                    ),
                ]),
              ),
            ),
          ),
        ),
        const Heading('سجل حضور اللاعب'),
        if (rows(value).isEmpty)
          const EmptyMessage('لا يوجد حضور مسجل لهذا التسجيل الرياضي.'),
        for (final row in rows(value))
          GCard(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                UiText(
                  '${sessionDateLabel(row['sessionDate'])} · ${row['group']}',
                ),
                StatusPill(row['status']),
                UiText(
                  row['sessionConsumed'] == true
                      ? 'احتُسبت الحصة من الرصيد'
                      : 'لم تُخصم حصة من الرصيد',
                ),
                if (row['trainingSessionId'] != null)
                  TextButton.icon(
                    onPressed: () => openPage(
                      context,
                      CoachAttendance(
                        auth: auth,
                        sessionId: textOf(row['trainingSessionId']),
                      ),
                    ),
                    icon: const Icon(Icons.fact_check_outlined),
                    label: const UiText('فتح كشف هذه الحصة'),
                  ),
              ],
            ),
          ),
      ]),
    ),
  );
}

class NativePlayerDirectory extends StatefulWidget {
  const NativePlayerDirectory({super.key, required this.auth});
  final AuthController auth;
  @override
  State<NativePlayerDirectory> createState() => _NativePlayerDirectoryState();
}

class _NativePlayerDirectoryState
    extends NativeOperationState<NativePlayerDirectory> {
  @override
  AuthController get auth => widget.auth;
  final search = TextEditingController();
  List<Json> players = [];
  int number = 1, total = 0, queryRevision = 0;
  Timer? searchDebounce;
  bool queuedSearch = false, exhausted = false;
  StaffCapabilities? access;
  @override
  void initState() {
    super.initState();
    autoPage(
      hasMore: () => !exhausted && players.length < total,
      next: () => load(more: true),
    );
    load();
  }

  @override
  void dispose() {
    searchDebounce?.cancel();
    search.dispose();
    super.dispose();
  }

  void searchChanged() {
    searchDebounce?.cancel();
    queuedSearch = false;
    setState(() {
      queryRevision++;
      players = [];
      total = 0;
      number = 1;
      exhausted = false;
    });
    searchDebounce = Timer(const Duration(milliseconds: 280), () {
      if (current) load();
    });
  }

  void submitSearch() {
    searchDebounce?.cancel();
    queuedSearch = false;
    FocusManager.instance.primaryFocus?.unfocus();
    setState(() {
      queryRevision++;
      players = [];
      total = 0;
      number = 1;
      exhausted = false;
    });
    load();
  }

  Future<void> load({bool more = false}) async {
    if (!current) return;
    if (busy) {
      if (!more) queuedSearch = true;
      return;
    }
    final revision = queryRevision, target = more ? number + 1 : 1;
    final query = canonicalNumericInput(search.text.trim());
    await run(() async {
      try {
        final rights = access ?? await StaffCapabilities.load(auth);
        final data = obj(
          await auth.request(
            'GET',
            '/api/v1/people/players?${Uri(queryParameters: {'search': query, 'page': '$target', 'pageSize': '25', 'sort': 'name'}).query}',
          ),
        );
        if (current &&
            revision == queryRevision &&
            query == canonicalNumericInput(search.text.trim())) {
          final incoming = rows(data['items']);
          final existing = more ? players : <Json>[];
          final ids = existing.map((r) => r['id']).toSet();
          final fresh = incoming.where((r) => ids.add(r['id'])).toList();
          setState(() {
            access = rights;
            players = [...existing, ...fresh];
            number = target;
            total = (data['total'] as num).toInt();
            exhausted = incoming.isEmpty || more && fresh.isEmpty;
          });
        }
      } catch (e) {
        if (revision == queryRevision ||
            e is ApiFailure && [401, 403].contains(e.status)) {
          rethrow;
        }
      }
    }, blocking: false);
    if (queuedSearch && current) {
      queuedSearch = false;
      await load();
    }
  }

  Future<void> evaluatePlayer(Json p) async {
    final enrollments = rows(p['enrollmentSummaries'] ?? [])
        .where((e) => e['status'] != 'Cancelled' && e['status'] != 'Inactive')
        .toList();
    Json? chosen;
    if (enrollments.length == 1) {
      chosen = enrollments.first;
    } else {
      chosen = await showModalBottomSheet<Json>(
        context: context,
        showDragHandle: true,
        builder: (c) => SafeArea(
          child: ListView(
            shrinkWrap: true,
            children: [
              const Padding(
                padding: EdgeInsets.all(16),
                child: Heading('اختر رياضة التقييم'),
              ),
              for (final e in enrollments)
                ListTile(
                  title: UiText(textOf(e['sportName'])),
                  subtitle: UiText('${e['branchName']} · ${e['groupName']}'),
                  onTap: () => Navigator.pop(c, e),
                ),
            ],
          ),
        ),
      );
    }
    if (chosen != null && mounted && current) {
      await openPage(
        context,
        CoachEvaluation(
          auth: auth,
          enrollment: {
            ...chosen,
            'playerId': p['id'],
            'player': p['arabicName'],
            'group': chosen['groupName'],
          },
        ),
      );
    }
  }

  @override
  Widget build(BuildContext context) => page(
    'اللاعبون',
    [
      TextFormField(
        controller: search,
        textInputAction: TextInputAction.search,
        onChanged: (_) => searchChanged(),
        onFieldSubmitted: (_) => submitSearch(),
        decoration: InputDecoration(
          labelText: 'بحث بالاسم أو الكود أو هاتف ولي الأمر',
          prefixIcon: const Icon(Icons.search_rounded),
          suffixIcon: search.text.isEmpty
              ? null
              : IconButton(
                  tooltip: 'مسح البحث',
                  icon: const Icon(Icons.close_rounded),
                  onPressed: () {
                    search.clear();
                    submitSearch();
                  },
                ),
        ),
      ),
      const SizedBox(height: 12),
      OutlinedButton(
        onPressed: submitSearch,
        child: const UiText('بحث اللاعبين'),
      ),
      Semantics(
        liveRegion: true,
        child: UiText(
          busy
              ? 'جارٍ تحميل اللاعبين…'
              : 'عرض ${players.length} من $total لاعب',
        ),
      ),
      if (!busy && players.isEmpty)
        const EmptyMessage('لا توجد نتائج في نطاقك.'),
      for (final p in players)
        PlayerDirectoryCard(
          auth: auth,
          player: p,
          enrollments: rows(p['enrollmentSummaries'] ?? []),
          onOpen: () async {
            await openPage(
              context,
              NativePlayerWorkspace(auth: auth, id: textOf(p['id'])),
            );
            if (current) await load();
          },
          onEvaluate:
              access?.allows('evaluation.manage', 'evaluations', 'Add') == true
              ? () => evaluatePlayer(p)
              : null,
        ),
      if (error != null && players.isNotEmpty)
        TextButton.icon(
          onPressed: () => load(more: true),
          icon: const Icon(Icons.refresh),
          label: const UiText('إعادة تحميل المزيد'),
        ),
    ],
    screen: 'players',
    onRetry: load,
  );
}
