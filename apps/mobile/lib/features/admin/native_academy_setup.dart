import 'package:flutter/material.dart';

import '../../core/api/api_client.dart';
import '../../core/auth/auth_controller.dart';
import '../../core/theme/ui_text.dart';
import '../guardian/guardian_ui.dart';
import '../shared/experience_ui.dart';
import '../shared/operation_design.dart';
import '../shared/staff_capabilities.dart';
import '../owner/owner_operations.dart' show OwnerGroupOperations;
import 'native_operation_form.dart';
import 'native_structure_operations.dart' show NativeCoachCreate;

class NativeNamedSetup extends StatefulWidget {
  const NativeNamedSetup({
    super.key,
    required this.auth,
    required this.kind,
    this.sportId,
  });
  final AuthController auth;
  final String kind;
  final String? sportId;
  @override
  State<NativeNamedSetup> createState() => _NativeNamedSetupState();
}

class _NativeNamedSetupState extends NativeOperationState<NativeNamedSetup> {
  @override
  AuthController get auth => widget.auth;
  final name = TextEditingController(),
      description = TextEditingController(),
      minimum = TextEditingController(),
      maximum = TextEditingController();
  bool done = false, uncertain = false;
  String get title => switch (widget.kind) {
    'branches' => 'إضافة فرع',
    'sports' => 'إضافة رياضة',
    'age-categories' => 'إضافة فئة عمرية',
    _ => 'إضافة مستوى تدريب',
  };
  @override
  void dispose() {
    name.dispose();
    description.dispose();
    minimum.dispose();
    maximum.dispose();
    super.dispose();
  }

  Future<void> save() => run(() async {
    if (!form.currentState!.validate()) return;
    if (uncertain) return;
    final body = <String, dynamic>{
      'arabicName': name.text.trim(),
      if (widget.kind == 'sports')
        'kind': RegExp(r'سباح|swim', caseSensitive: false).hasMatch(name.text)
            ? 'Swimming'
            : RegExp(
                r'كرة القدم|قدم|football',
                caseSensitive: false,
              ).hasMatch(name.text)
            ? 'Football'
            : 'General',
      if (widget.kind == 'branches') 'description': description.text.trim(),
      if (widget.kind == 'age-categories') ...{
        'minimumBirthYear': int.tryParse(minimum.text),
        'maximumBirthYear': int.tryParse(maximum.text),
      },
      if (widget.kind == 'training-levels') ...{
        'sportId': widget.sportId,
        'rank': int.tryParse(minimum.text) ?? 1,
      },
    };
    try {
      await auth.request('POST', '/api/v1/manage/${widget.kind}', body: body);
      if (current) {
        setState(() {
          done = true;
          message = 'تم الحفظ بنجاح.';
        });
      }
    } on ApiFailure catch (e) {
      if (e.status == 0 || e.status >= 500) {
        uncertain = true;
        throw const ApiFailure(
          0,
          'لم تتأكد النتيجة. راجع القائمة قبل إنشاء سجل آخر.',
        );
      }
      rethrow;
    }
  });
  @override
  Widget build(BuildContext context) => page(title, [
    OperationSection(
      title: widget.kind == 'branches'
          ? 'بيانات الفرع'
          : widget.kind == 'sports'
          ? 'بيانات الرياضة'
          : title,
      icon: widget.kind == 'branches'
          ? Icons.location_on_outlined
          : Icons.sports_outlined,
      children: [
        field('الاسم', name, markRequired: true),
        if (widget.kind == 'branches')
          field('العنوان — اختياري', description, required: false, maxLines: 2),
        if (widget.kind == 'age-categories') ...[
          field(
            'أقل سنة ميلاد — اختياري',
            minimum,
            required: false,
            keyboard: TextInputType.number,
          ),
          field(
            'أكبر سنة ميلاد — اختياري',
            maximum,
            required: false,
            keyboard: TextInputType.number,
          ),
        ],
        if (widget.kind == 'training-levels')
          field('ترتيب المستوى', minimum, keyboard: TextInputType.number),
        if (!done)
          FilledButton.icon(
            onPressed: uncertain ? null : save,
            icon: const Icon(Icons.check),
            label: UiText(
              'حفظ ${widget.kind == 'branches'
                  ? 'الفرع'
                  : widget.kind == 'sports'
                  ? 'الرياضة'
                  : 'البيانات'}',
            ),
          ),
        if (done)
          OutlinedButton(
            onPressed: () => Navigator.pop(context),
            child: const UiText('تم · العودة'),
          ),
      ],
    ),
  ]);
}

class NativePlanCreate extends StatefulWidget {
  const NativePlanCreate({super.key, required this.auth});
  final AuthController auth;
  @override
  State<NativePlanCreate> createState() => _NativePlanCreateState();
}

class _NativePlanCreateState extends NativeOperationState<NativePlanCreate> {
  @override
  AuthController get auth => widget.auth;
  final name = TextEditingController(),
      price = TextEditingController(),
      months = TextEditingController(text: '1'),
      sessions = TextEditingController(text: '12');
  Json options = {};
  String? sport;
  String type = 'Combined', format = 'Group';
  bool allBranches = true,
      allowAll = true,
      review = false,
      done = false,
      uncertain = false;
  final branches = <String>{};
  @override
  void initState() {
    super.initState();
    load();
  }

  Future<void> load() => run(() async {
    final o = obj(
      await auth.request('GET', '/api/v1/manage/structure/options'),
    );
    final access = await StaffCapabilities.load(auth);
    if (!current) return;
    setState(() {
      options = o;
      final s = rows(o['sports']);
      if (s.length == 1) sport = textOf(s.first['id']);
      allowAll = access.owner || access.scope['allBranches'] == true;
      allBranches = allowAll;
      if (!allowAll && rows(o['branches']).length == 1) {
        branches.add(textOf(rows(o['branches']).first['id']));
      }
    });
  });
  @override
  void dispose() {
    for (final c in [name, price, months, sessions]) {
      c.dispose();
    }
    super.dispose();
  }

  Future<void> save() => run(() async {
    if (uncertain) return;
    final body = {
      'arabicName': name.text.trim(),
      'sportId': sport,
      'planType': type,
      'price': price.text.trim(),
      'durationMonths': type == 'Sessions' ? null : int.tryParse(months.text),
      'durationDays': null,
      'sessionCount': type == 'Duration' ? null : int.tryParse(sessions.text),
      'allBranches': allBranches,
      'branchIds': allBranches ? <String>[] : branches.toList(),
      'trainingFormat': format,
      'serviceKind': 'Training',
    };
    try {
      await auth.request('POST', '/api/v1/subscriptions/plans', body: body);
      if (current) {
        setState(() {
          done = true;
          message = 'تم حفظ الباقة وأصبحت متاحة للاشتراكات.';
        });
      }
    } on ApiFailure catch (e) {
      if (e.status == 0 || e.status >= 500) {
        uncertain = true;
        throw const ApiFailure(
          0,
          'لم تتأكد نتيجة إنشاء الباقة. راجع الباقات قبل إعادة الإنشاء.',
        );
      }
      rethrow;
    }
  });
  void reviewPlan() {
    final valid = form.currentState!.validate();
    setState(() {
      if (valid && (allBranches || branches.isNotEmpty)) {
        review = true;
        error = null;
      } else {
        error = 'أكمل الحقول واختر فرعًا واحدًا على الأقل.';
      }
    });
  }

  @override
  Widget build(BuildContext context) => page(
    'إضافة باقة',
    [
      OperationSteps(
        labels: const ['الباقة', 'المراجعة'],
        current: review ? 1 : 0,
      ),
      if (options.isNotEmpty && !done) ...[
        if (!review) ...[
          OperationSection(
            title: 'تعريف الباقة',
            icon: Icons.inventory_2_outlined,
            children: [
              field('اسم الباقة', name, markRequired: true),
              choice(
                'الرياضة',
                sport,
                rows(options['sports']),
                (v) => setState(() => sport = v),
                markRequired: true,
              ),
              choice('نوع الباقة', type, const [
                {'id': 'Combined', 'arabicName': 'مدة وحصص'},
                {'id': 'Duration', 'arabicName': 'مدة فقط'},
                {'id': 'Sessions', 'arabicName': 'حصص فقط'},
              ], (v) => setState(() => type = v!)),
              choice('نمط التدريب', format, const [
                {'id': 'Group', 'arabicName': 'جماعي'},
                {'id': 'Private', 'arabicName': 'خاص'},
                {'id': 'SemiPrivate', 'arabicName': 'شبه خاص'},
                {'id': 'Season', 'arabicName': 'موسمي'},
              ], (v) => setState(() => format = v!)),
            ],
          ),
          OperationSection(
            title: 'الاستحقاق والسعر',
            icon: Icons.payments_outlined,
            children: [
              if (type != 'Sessions')
                field(
                  'عدد الأشهر',
                  months,
                  keyboard: TextInputType.number,
                  markRequired: true,
                ),
              if (type != 'Duration')
                field(
                  'عدد الحصص',
                  sessions,
                  keyboard: TextInputType.number,
                  markRequired: true,
                ),
              field(
                'سعر الباقة',
                price,
                keyboard: const TextInputType.numberWithOptions(decimal: true),
                markRequired: true,
              ),
            ],
          ),
          OperationSection(
            title: 'إتاحة الباقة',
            icon: Icons.location_on_outlined,
            children: [
              if (allowAll)
                SwitchListTile.adaptive(
                  contentPadding: EdgeInsets.zero,
                  title: const UiText('كل الفروع'),
                  value: allBranches,
                  onChanged: (v) => setState(() => allBranches = v),
                ),
              if (!allBranches)
                for (final b in rows(options['branches']))
                  CheckboxListTile(
                    contentPadding: EdgeInsets.zero,
                    title: UiText(textOf(b['arabicName'])),
                    value: branches.contains(b['id']),
                    onChanged: (v) => setState(
                      () => v == true
                          ? branches.add(textOf(b['id']))
                          : branches.remove(b['id']),
                    ),
                  ),
            ],
          ),
        ] else
          OperationSection(
            title: 'تأكيد الباقة',
            icon: Icons.task_alt,
            children: [
              Heading(name.text.trim()),
              UiText(
                'السعر: ${price.text} · ${type == 'Sessions'
                    ? '${sessions.text} حصة'
                    : type == 'Duration'
                    ? '${months.text} شهر'
                    : '${months.text} شهر · ${sessions.text} حصة'}',
              ),
              const SizedBox(height: 12),
              const UiText(
                'حفظ الباقة يتيح اختيارها للاشتراكات؛ لا يُحصّل مبلغًا.',
              ),
            ],
          ),
      ],
      if (done)
        OperationSection(
          title: 'الباقة جاهزة',
          icon: Icons.check_circle_outline,
          children: [
            const UiText('تقدر تختارها الآن عند تسجيل اشتراك جديد.'),
            TextButton(
              onPressed: () => Navigator.pop(context),
              child: const UiText('العودة للعمليات'),
            ),
          ],
        ),
    ],
    bottom: done || options.isEmpty
        ? null
        : Column(
            mainAxisSize: MainAxisSize.min,
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              FilledButton.icon(
                onPressed: uncertain
                    ? null
                    : review
                    ? save
                    : reviewPlan,
                icon: Icon(
                  review ? Icons.check_rounded : Icons.arrow_back_rounded,
                ),
                label: UiText(review ? 'تأكيد حفظ الباقة' : 'مراجعة الباقة'),
              ),
              if (review)
                TextButton(
                  onPressed: uncertain
                      ? null
                      : () => setState(() => review = false),
                  child: const UiText('تعديل البيانات'),
                ),
            ],
          ),
  );
}

class NativeGroupCreate extends StatefulWidget {
  const NativeGroupCreate({super.key, required this.auth});
  final AuthController auth;
  @override
  State<NativeGroupCreate> createState() => _NativeGroupCreateState();
}

class _NativeGroupCreateState extends NativeOperationState<NativeGroupCreate> {
  @override
  AuthController get auth => widget.auth;
  final name = TextEditingController(),
      capacity = TextEditingController(text: '20'),
      from = TextEditingController(text: isoDay(DateTime.now())),
      to = TextEditingController(
        text: isoDay(DateTime.now().add(const Duration(days: 30))),
      );
  Json options = {}, facilities = {};
  List<Json> levels = [];
  String? branch, sport, age, level, coach, resource;
  String format = 'Group';
  bool capacityReviewed = false, exclusive = false, uncertain = false;
  int step = 0;
  TimeOfDay start = const TimeOfDay(hour: 17, minute: 0),
      end = const TimeOfDay(hour: 18, minute: 0);
  final days = <int>{};
  Json? preview, frozen;
  String key = commandKey(), createdId = '';
  @override
  void initState() {
    super.initState();
    load();
  }

  Future<void> load() => run(() async {
    final values = await Future.wait([
      auth.request('GET', '/api/v1/manage/structure/options'),
      auth.request('GET', '/api/v1/manage/training-levels'),
    ]);
    Json f = {};
    final a = await StaffCapabilities.load(auth);
    if (a.allows('facilities.read', 'facilities', 'Read')) {
      try {
        f = obj(await auth.request('GET', '/api/v1/facilities'));
      } on ApiFailure catch (_) {}
    }
    if (current) {
      setState(() {
        options = obj(values[0]);
        levels = rows(values[1]);
        facilities = f;
        final bs = rows(options['branches']), ss = rows(options['sports']);
        if (bs.length == 1) branch = textOf(bs.first['id']);
        if (ss.length == 1) sport = textOf(ss.first['id']);
      });
    }
  });
  @override
  void dispose() {
    for (final c in [name, capacity, from, to]) {
      c.dispose();
    }
    super.dispose();
  }

  String time(TimeOfDay t) =>
      '${t.hour.toString().padLeft(2, '0')}:${t.minute.toString().padLeft(2, '0')}:00';
  Json get draft => {
    'arabicName': name.text.trim(),
    'branchId': branch,
    'sportId': sport,
    'ageCategoryId': age,
    'levelId': level,
    'capacity': int.tryParse(capacity.text),
    'registrationFormat': format,
    'capacityReviewed': capacityReviewed,
    'coachMembershipId': coach,
    'enrollmentIds': <String>[],
    'timetable': {
      'from': from.text,
      'to': to.text,
      'days': days.toList()..sort(),
      'startTime': time(start),
      'endTime': time(end),
      if (resource != null)
        'resource': {
          'resourceId': resource,
          'coachMembershipId': coach,
          'seats': int.tryParse(capacity.text),
          'exclusive': exclusive,
        },
    },
  };
  Future<void> next() => run(() async {
    if (!form.currentState!.validate()) {
      error = 'أكمل الحقول المطلوبة للمتابعة.';
      return;
    }
    if (step == 0) {
      if ((int.tryParse(capacity.text) ?? 0) < 1) {
        throw const ApiFailure(422, 'أدخل سعة صحيحة للمجموعة.');
      }
      setState(() => step = 1);
      if (operationScroll.hasClients) operationScroll.jumpTo(0);
      return;
    }
    if (days.isEmpty) throw const ApiFailure(422, 'اختر أيام التدريب.');
    final p = obj(
      await auth.request(
        'POST',
        '/api/v1/manage/group-creation/preview',
        body: draft,
      ),
    );
    if (current) {
      setState(() {
        preview = p;
        key = commandKey();
        step = 2;
      });
    }
  });
  Future<void> save() => run(() async {
    frozen ??= {...draft, 'fingerprint': preview!['fingerprint']};
    try {
      final r = obj(
        await auth.request(
          'POST',
          '/api/v1/manage/group-creation',
          body: frozen,
          idempotencyKey: key,
        ),
      );
      if (current) {
        setState(() {
          createdId = textOf(r['id']);
          message = 'تم إنشاء المجموعة وحصصها وإسناد المدرب.';
        });
      }
    } on ApiFailure catch (e) {
      if (e.status == 0 || e.status >= 500) uncertain = true;
      rethrow;
    }
  });
  Future<void> add(String kind) => openPage(
    context,
    NativeNamedSetup(auth: auth, kind: kind, sportId: sport),
  ).then((_) => load());
  @override
  Widget build(BuildContext context) {
    final available = levels.where((x) => x['sportId'] == sport).toList();
    final availableCoaches = rows(options['coaches'] ?? [])
        .where(
          (c) =>
              (!c.containsKey('allBranches') ||
                  c['allBranches'] == true ||
                  (c['branchIds'] as List? ?? []).contains(branch)) &&
              (!c.containsKey('allSports') ||
                  c['allSports'] == true ||
                  (c['sportIds'] as List? ?? []).contains(sport)),
        )
        .toList();
    final resources = rows(facilities['resources'] ?? [])
        .where(
          (r) =>
              r['branchId'] == branch &&
              r['approvedAtUtc'] != null &&
              rows(facilities['spaces'] ?? []).any(
                (s) => s['id'] == r['facilitySpaceId'] && s['sportId'] == sport,
              ),
        )
        .map((r) => {...r, 'arabicName': r['name']})
        .toList();
    return page(
      'إنشاء مجموعة',
      [
        OperationSteps(
          labels: const ['المجموعة', 'الحصص والمدرب', 'المراجعة'],
          current: step,
        ),
        if (createdId.isNotEmpty)
          OperationSection(
            title: 'المجموعة جاهزة',
            icon: Icons.check_circle_outline,
            children: [
              const UiText(
                'الحصص والمدرب محفوظان مع المجموعة. تقدر تضيف اللاعبين من تسكين لاعب.',
              ),
              FilledButton(
                onPressed: () => openPage(
                  context,
                  OwnerGroupOperations(auth: auth, id: createdId),
                ),
                child: const UiText('فتح المجموعة'),
              ),
            ],
          )
        else if (options.isNotEmpty) ...[
          if (step == 0)
            OperationSection(
              title: 'هوية المجموعة',
              icon: Icons.groups_outlined,
              children: [
                field('اسم المجموعة', name, markRequired: true),
                if (rows(options['branches']).length == 1)
                  InputDecorator(
                    decoration: const InputDecoration(labelText: 'الفرع'),
                    child: UiText(
                      textOf(rows(options['branches']).single['arabicName']),
                    ),
                  )
                else
                  choice(
                    'الفرع',
                    branch,
                    rows(options['branches']),
                    (v) => setState(() {
                      branch = v;
                      coach = null;
                      resource = null;
                    }),
                    markRequired: true,
                  ),
                if (rows(options['sports']).length == 1)
                  InputDecorator(
                    decoration: const InputDecoration(labelText: 'الرياضة'),
                    child: UiText(
                      textOf(rows(options['sports']).single['arabicName']),
                    ),
                  )
                else
                  choice(
                    'الرياضة',
                    sport,
                    rows(options['sports']),
                    (v) => setState(() {
                      sport = v;
                      coach = null;
                      level = null;
                      resource = null;
                    }),
                    markRequired: true,
                  ),
                choice(
                  'الفئة العمرية',
                  age,
                  rows(options['categories']),
                  (v) => setState(() => age = v),
                  markRequired: true,
                ),
                TextButton.icon(
                  onPressed: () => add('age-categories'),
                  icon: const Icon(Icons.add, size: 16),
                  label: const UiText('إضافة فئة عمرية'),
                ),
                choice(
                  'المستوى — اختياري',
                  level,
                  available,
                  (v) => setState(() => level = v),
                  required: false,
                ),
                if (sport != null)
                  TextButton.icon(
                    onPressed: () => add('training-levels'),
                    icon: const Icon(Icons.add, size: 16),
                    label: const UiText('إضافة مستوى للرياضة'),
                  ),
                field(
                  'سعة المجموعة',
                  capacity,
                  keyboard: TextInputType.number,
                  markRequired: true,
                ),
                choice('نمط التدريب', format, const [
                  {'id': 'Group', 'arabicName': 'جماعي'},
                  {'id': 'Private', 'arabicName': 'خاص'},
                  {'id': 'SemiPrivate', 'arabicName': 'شبه خاص'},
                ], (v) => setState(() => format = v!)),
                if (format != 'Group')
                  CheckboxListTile(
                    contentPadding: EdgeInsets.zero,
                    title: const UiText('راجعت سعة التدريب الخاص'),
                    value: capacityReviewed,
                    onChanged: (v) => setState(() => capacityReviewed = v!),
                  ),
              ],
            ),
          if (step == 1)
            OperationSection(
              title: 'الحصص والمدرب',
              icon: Icons.calendar_month_outlined,
              children: [
                choice(
                  'المدرب',
                  coach,
                  availableCoaches,
                  (v) => setState(() => coach = v),
                  markRequired: true,
                ),
                if (availableCoaches.isEmpty)
                  const UiText(
                    'لا يوجد مدرب مخوّل لهذا الفرع والرياضة. أضف مدربًا أو حدّث نطاقه.',
                  ),
                TextButton.icon(
                  onPressed: () async {
                    final result = await Navigator.of(context).push<Json>(
                      MaterialPageRoute(
                        builder: (_) =>
                            NativeCoachCreate(auth: auth, inline: true),
                      ),
                    );
                    if (current) {
                      await load();
                      if (result?['membershipId'] != null && current) {
                        setState(() => coach = textOf(result!['membershipId']));
                      }
                    }
                  },
                  icon: const Icon(Icons.person_add_alt_1_outlined, size: 17),
                  label: const UiText('إضافة مدرب'),
                ),
                const Heading('أيام التدريب'),
                Wrap(
                  spacing: 7,
                  children: [
                    for (final d in const [
                      (6, 'السبت'),
                      (0, 'الأحد'),
                      (1, 'الاثنين'),
                      (2, 'الثلاثاء'),
                      (3, 'الأربعاء'),
                      (4, 'الخميس'),
                      (5, 'الجمعة'),
                    ])
                      FilterChip(
                        label: UiText(d.$2),
                        selected: days.contains(d.$1),
                        onSelected: (v) => setState(
                          () => v ? days.add(d.$1) : days.remove(d.$1),
                        ),
                      ),
                  ],
                ),
                const SizedBox(height: 16),
                dateField('بداية الجدول', from),
                dateField('نهاية الجدول', to),
                Row(
                  children: [
                    Expanded(
                      child: OutlinedButton.icon(
                        onPressed: () async {
                          final t = await showTimePicker(
                            context: context,
                            initialTime: start,
                          );
                          if (t != null && current) setState(() => start = t);
                        },
                        icon: const Icon(Icons.schedule, size: 17),
                        label: UiText('من ${start.format(context)}'),
                      ),
                    ),
                    const SizedBox(width: 10),
                    Expanded(
                      child: OutlinedButton.icon(
                        onPressed: () async {
                          final t = await showTimePicker(
                            context: context,
                            initialTime: end,
                          );
                          if (t != null && current) setState(() => end = t);
                        },
                        icon: const Icon(Icons.schedule, size: 17),
                        label: UiText('إلى ${end.format(context)}'),
                      ),
                    ),
                  ],
                ),
                const SizedBox(height: 12),
                choice(
                  'مورد التدريب ${format == 'Group' ? '— اختياري' : ''}',
                  resource,
                  resources,
                  (v) => setState(() => resource = v),
                  required: format != 'Group',
                ),
                if (resource != null)
                  SwitchListTile.adaptive(
                    title: const UiText('حجز حصري'),
                    value: exclusive,
                    onChanged: (v) => setState(() => exclusive = v),
                  ),
                if (format != 'Group' && resources.isEmpty)
                  const UiText(
                    'التدريب الخاص يحتاج موردًا معتمدًا متاحًا لهذا الفرع والرياضة.',
                  ),
              ],
            ),
          if (step == 2 && preview != null)
            OperationSection(
              title: name.text.trim(),
              icon: Icons.task_alt,
              children: [
                UiText(
                  '${preview!['sessions']} حصة · سعة ${capacity.text} لاعب',
                  style: const TextStyle(
                    fontSize: 18,
                    fontWeight: FontWeight.w700,
                  ),
                ),
                const SizedBox(height: 12),
                const UiText(
                  'سيُحفظ الجدول وإسناد المدرب مع المجموعة. يمكنك تسكين اللاعبين بعدها.',
                ),
                for (final c in (preview!['conflicts'] as List? ?? []))
                  Padding(
                    padding: const EdgeInsets.only(top: 8),
                    child: UiText(
                      '$c',
                      style: TextStyle(
                        color: Theme.of(context).colorScheme.error,
                      ),
                    ),
                  ),
              ],
            ),
        ],
      ],
      bottom: createdId.isNotEmpty || options.isEmpty
          ? null
          : Column(
              mainAxisSize: MainAxisSize.min,
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: [
                if (step < 2)
                  FilledButton(
                    onPressed: next,
                    child: UiText(
                      step == 0 ? 'متابعة للحصص والمدرب' : 'معاينة المجموعة',
                    ),
                  )
                else
                  FilledButton(
                    onPressed: (preview!['conflicts'] as List? ?? []).isNotEmpty
                        ? null
                        : save,
                    child: UiText(
                      uncertain
                          ? 'إعادة التحقق من نفس الطلب'
                          : 'تأكيد إنشاء المجموعة',
                    ),
                  ),
                if (step > 0)
                  TextButton(
                    onPressed: uncertain
                        ? null
                        : () => setState(() {
                            step--;
                            preview = null;
                            frozen = null;
                          }),
                    child: const UiText('رجوع للخطوة السابقة'),
                  ),
              ],
            ),
    );
  }
}
