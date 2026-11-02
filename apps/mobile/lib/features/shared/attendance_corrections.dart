import 'package:flutter/material.dart';

import '../../core/api/api_client.dart';
import '../../core/auth/auth_controller.dart';
import '../../core/theme/ui_text.dart';
import '../admin/native_operation_form.dart';
import '../coach/coach_attendance.dart';
import '../guardian/guardian_ui.dart';
import 'staff_capabilities.dart';
import 'experience_ui.dart';

class _CorrectionMetadata extends StatelessWidget {
  const _CorrectionMetadata(this.data);
  final Json data;
  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final values = [
      ('الحالة', correctionState(data['state'])),
      ('الحصة', sessionDateLabel(data['sessionDate'])),
      ('المجموعة', textOf(data['group'])),
      ('الكشف', data['kind'] == 'Staff' ? 'الجهاز الفني' : 'اللاعبون'),
      ('صاحب الطلب', textOf(data['requester'])),
      (
        'المراجع',
        data['reviewer'] == null
            ? 'بانتظار المراجعة'
            : textOf(data['reviewer']),
      ),
    ];
    final statusColor = data['state'] == 'Approved'
        ? ExperienceColors.positive(context)
        : data['state'] == 'Rejected'
        ? ExperienceColors.negative(context)
        : ExperienceColors.warning(context);
    return GCard(
      child: LayoutBuilder(
        builder: (context, bounds) {
          final columns = MediaQuery.textScalerOf(context).scale(14) > 20
              ? 1
              : 2;
          final width = (bounds.maxWidth - (columns - 1) * 12) / columns;
          return Wrap(
            spacing: 12,
            runSpacing: 14,
            children: [
              for (final value in values)
                SizedBox(
                  width: width,
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      UiText(
                        value.$1,
                        style: TextStyle(
                          fontSize: 12,
                          color: theme.colorScheme.onSurfaceVariant,
                        ),
                      ),
                      const SizedBox(height: 3),
                      UiText(
                        value.$2,
                        textDirection: value.$1 == 'الحصة'
                            ? TextDirection.ltr
                            : null,
                        style: TextStyle(
                          fontSize: 14,
                          fontWeight: FontWeight.w600,
                          color: value.$1 == 'الحالة'
                              ? statusColor
                              : theme.colorScheme.onSurface,
                        ),
                      ),
                    ],
                  ),
                ),
            ],
          );
        },
      ),
    );
  }
}

String correctionState(Object? value) => switch (value) {
  'Pending' => 'بانتظار الاعتماد',
  'Applying' => 'قيد التطبيق',
  'Approved' => 'اعتمد وطُبق',
  'Rejected' => 'لم يُعتمد',
  _ => 'حالة غير معروفة',
};

Future<StaffCapabilities> correctionAccess(AuthController auth) async {
  final role = auth.session?.selected?.role;
  if (role != 'AcademyOwner' && role != 'AcademyAdmin') {
    throw const ApiFailure(403, 'ليست لديك صلاحية عرض طلبات تصحيح الحضور.');
  }
  final access = await StaffCapabilities.load(auth);
  if (!access.allows('attendance.read', 'attendance', 'Read')) {
    throw const ApiFailure(403, 'ليست لديك صلاحية عرض طلبات تصحيح الحضور.');
  }
  return access;
}

class AttendanceCorrectionsEntry extends StatefulWidget {
  const AttendanceCorrectionsEntry({super.key, required this.auth});
  final AuthController auth;
  @override
  State<AttendanceCorrectionsEntry> createState() => _CorrectionsEntryState();
}

class _CorrectionsEntryState extends State<AttendanceCorrectionsEntry> {
  late final access = correctionAccess(widget.auth);
  String? get contextKey => widget.auth.session?.selected == null
      ? null
      : '${widget.auth.session!.selected!.academyId}:${widget.auth.session!.selected!.id}:${widget.auth.session!.selected!.contextRevision}';
  late final identity = contextKey;
  @override
  Widget build(BuildContext context) => ListenableBuilder(
    listenable: widget.auth,
    builder: (context, _) => identity == null || contextKey != identity
        ? const SizedBox.shrink()
        : FutureBuilder<StaffCapabilities>(
            future: access,
            builder: (context, value) => !value.hasData
                ? const SizedBox.shrink()
                : GLink(
                    'طلبات تصحيح الحضور',
                    Icons.fact_check_outlined,
                    () => openPage(
                      context,
                      AttendanceCorrectionsPage(auth: widget.auth),
                    ),
                  ),
          ),
  );
}

class AttendanceCorrectionsPage extends StatefulWidget {
  const AttendanceCorrectionsPage({super.key, required this.auth});
  final AuthController auth;
  @override
  State<AttendanceCorrectionsPage> createState() => _CorrectionsPageState();
}

class _CorrectionsPageState
    extends NativeOperationState<AttendanceCorrectionsPage> {
  @override
  AuthController get auth => widget.auth;
  List<Json>? items;
  int pageNumber = 1, total = 0;
  static const pageSize = 25;
  @override
  void initState() {
    super.initState();
    autoPage(
      hasMore: () => (items?.length ?? 0) < total,
      next: () => load(pageNumber + 1),
    );
    load();
  }

  Future<void> load([int? requestedPage]) => run(() async {
    await correctionAccess(auth);
    final target = requestedPage ?? 1;
    final result = obj(
      await auth.request('GET', '/api/v1/attendance/corrections?page=$target'),
    );
    if (result['page'] != target ||
        result['pageSize'] != pageSize ||
        result['total'] is! int ||
        result['items'] is! List) {
      throw const ApiFailure(
        502,
        'تعذر التحقق من صفحة طلبات التصحيح. أعد المحاولة.',
      );
    }
    if (current) {
      items = [
        if (requestedPage != null && target > 1) ...?items,
        ...rows(result['items']),
      ];
      pageNumber = target;
      total = result['total'] as int;
    }
  });
  @override
  Widget build(BuildContext context) => page('طلبات تصحيح الحضور', [
    const UiText('طلب التصحيح لا يغيّر الحضور قبل اعتماد المالك.'),
    OutlinedButton.icon(
      onPressed: busy ? null : () => load(),
      icon: const Icon(Icons.refresh),
      label: const UiText('تحديث الطلبات'),
    ),
    if (items != null) ...[
      UiText('إجمالي الطلبات $total'),
      const SizedBox.shrink(),
    ],
    if (items != null && items!.isEmpty)
      const EmptyMessage('لا توجد طلبات تصحيح في هذه الصفحة.'),
    for (final item in items ?? <Json>[])
      GLink(
        textOf(item['group']),
        Icons.history,
        () async {
          await openPage(
            context,
            AttendanceCorrectionDetail(auth: auth, id: textOf(item['id'])),
          );
          if (current) await load();
        },
        key: ValueKey('attendance-correction-${item['id']}'),
        subtitle:
            '${sessionDateLabel(item['sessionDate'])} · ${item['kind'] == 'Staff' ? 'الجهاز الفني' : 'اللاعبون'} · ${correctionState(item['state'])}',
      ),
  ], screen: 'attendance');
}

class AttendanceCorrectionDetail extends StatefulWidget {
  const AttendanceCorrectionDetail({
    super.key,
    required this.auth,
    required this.id,
  });
  final AuthController auth;
  final String id;
  @override
  State<AttendanceCorrectionDetail> createState() => _CorrectionDetailState();
}

class _CorrectionDetailState
    extends NativeOperationState<AttendanceCorrectionDetail> {
  @override
  AuthController get auth => widget.auth;
  Json? data;
  StaffCapabilities? access;
  final reason = TextEditingController();
  ({bool approve, Json body})? intent;
  bool accepted = false;
  @override
  void initState() {
    super.initState();
    load();
  }

  @override
  void dispose() {
    reason.dispose();
    super.dispose();
  }

  Future<Json> read() async {
    final result = obj(
      await auth.request('GET', '/api/v1/attendance/corrections/${widget.id}'),
    );
    if (result['id'] != widget.id) {
      throw const ApiFailure(
        409,
        'لم تتأكد هوية طلب التصحيح. أعد فتحه من القائمة.',
      );
    }
    return result;
  }

  Future<void> load() => run(() async {
    data = null;
    final permissions = await correctionAccess(auth);
    final result = await read();
    if (current) {
      access = permissions;
      data = result;
      intent = null;
      accepted = false;
    }
  });
  Future<void> applyIntent() => run(() async {
    final command = intent;
    if (command == null) return;
    await correctionAccess(auth);
    if (!current) return;
    if (!accepted) {
      await auth.request(
        'POST',
        '/api/v1/attendance/corrections/${widget.id}/${command.approve ? 'approve' : 'reject'}',
        body: command.body,
      );
      accepted = true;
    }
    final result = await read();
    final matches =
        result['state'] == (command.approve ? 'Approved' : 'Rejected') &&
        result['decisionReason'] == command.body['reason'] &&
        (!command.approve || result['appliedAtUtc'] != null);
    if (!matches) {
      throw const ApiFailure(
        409,
        'لم يتأكد القرار من السجل المحفوظ. حدّث الطلب وراجع حالته.',
      );
    }
    if (current) {
      data = result;
      intent = null;
      accepted = false;
      message = command.approve
          ? 'اعتمد التصحيح وتأكد تطبيقه على السجل المحفوظ.'
          : 'لم يُعتمد الطلب؛ تأكدت حالته دون تغيير الحضور.';
    }
  });
  Future<void> decide(bool approve) async {
    if (!current ||
        busy ||
        intent != null ||
        data?['canApprove'] != true ||
        auth.session?.selected?.role != 'AcademyOwner') {
      return;
    }
    final confirmed = await showDialog<bool>(
      context: context,
      builder: (context) => AlertDialog(
        title: UiText(approve ? 'اعتماد وتطبيق التصحيح' : 'عدم الاعتماد'),
        content: UiText(
          approve
              ? 'سيطبق الطلب المعروض على الحضور ورصيد الحصص المؤهل وفق القواعد الحالية، دون تحصيل جديد.'
              : 'يبقى سجل الحضور كما هو، مع حفظ قرار عدم الاعتماد.',
        ),
        actions: [
          TextButton(
            onPressed: () => Navigator.pop(context, false),
            child: const UiText('رجوع'),
          ),
          FilledButton(
            onPressed: () => Navigator.pop(context, true),
            child: const UiText('تأكيد القرار'),
          ),
        ],
      ),
    );
    if (confirmed != true || !current || busy) return;
    intent = (
      approve: approve,
      body: {
        'expectedVersion': data!['version'],
        'reason': reason.text.trim().isEmpty ? null : reason.text.trim(),
      },
    );
    await applyIntent();
  }

  @override
  Widget build(BuildContext context) {
    final item = data;
    final canDecide =
        item?['canApprove'] == true &&
        auth.session?.selected?.role == 'AcademyOwner' &&
        access?.allows('attendance.manage', 'attendance', 'Edit') == true;
    return page('تفاصيل تصحيح الحضور', [
      OutlinedButton.icon(
        onPressed: busy ? null : load,
        icon: const Icon(Icons.refresh),
        label: const UiText('تحديث الطلب'),
      ),
      if (item != null) ...[
        _CorrectionMetadata(item),
        const Heading('سبب التصحيح'),
        GCard(child: UiText(textOf(item['reason']))),
        const Heading('التغييرات المطلوبة'),
        for (final row in rows(item['items']))
          GCard(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                UiText(
                  textOf(row['name']),
                  style: const TextStyle(fontWeight: FontWeight.w600),
                ),
                UiText('المسجل: ${label(row['before'])}'),
                UiText('المطلوب: ${label(row['after'])}'),
              ],
            ),
          ),
        if (item['decisionReason'] != null) ...[
          const Heading('سبب القرار'),
          GCard(child: UiText(textOf(item['decisionReason']))),
        ],
        if (item['kind'] == 'Players')
          OutlinedButton.icon(
            onPressed: busy
                ? null
                : () => openPage(
                    context,
                    CoachAttendance(
                      auth: auth,
                      sessionId: textOf(item['trainingSessionId']),
                    ),
                  ),
            icon: const Icon(Icons.fact_check_outlined),
            label: const UiText('كشف الحصة'),
          ),
        if (canDecide && intent == null) ...[
          TextFormField(
            controller: reason,
            maxLength: 500,
            maxLines: 3,
            decoration: const InputDecoration(
              labelText: 'ملاحظة القرار (اختياري)',
            ),
          ),
          FilledButton(
            onPressed: busy ? null : () => decide(true),
            child: const UiText('اعتماد وتطبيق التصحيح'),
          ),
          OutlinedButton(
            onPressed: busy ? null : () => decide(false),
            child: const UiText('عدم الاعتماد'),
          ),
        ],
      ],
      if (intent != null)
        FilledButton(
          onPressed: busy ? null : applyIntent,
          child: const UiText('إعادة التحقق من القرار'),
        ),
    ], screen: 'attendance');
  }
}
