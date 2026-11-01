import '../admin/native_academy_setup.dart';
import '../shared/operation_design.dart';
import '../shared/directory_cards.dart';

import 'package:flutter/material.dart';

import '../../core/auth/auth_controller.dart';
import 'owner_action_rail.dart';
import 'native_staff_management.dart';
import '../../core/theme/ui_text.dart';
import '../admin/admin_home.dart';
import '../shared/staff_capabilities.dart';
import '../shared/native_media_groups.dart';
import '../admin/native_enrollment.dart';
import '../admin/native_renewal.dart';
import '../admin/native_structure_operations.dart';
import '../coach/coach_evaluation.dart';
import '../guardian/guardian_ui.dart';

class OwnerQuickOperations extends StatelessWidget {
  const OwnerQuickOperations({
    super.key,
    required this.auth,
    this.environment = 'Production',
    this.webBaseUrl = '',
    this.openDashboard,
  });
  final AuthController auth;
  // Retained constructor compatibility; core actions always use the native session.
  final String environment, webBaseUrl;
  final Future<bool> Function(Uri)? openDashboard;
  @override
  Widget build(BuildContext context) => GuardianPage(
    title: 'العمليات',
    child: NativeOperationActions(auth: auth),
  );
}

class NativeOperationActions extends StatefulWidget {
  const NativeOperationActions({
    super.key,
    required this.auth,
    this.compact = false,
    this.carousel = false,
  });
  final AuthController auth;
  final bool compact, carousel;
  @override
  State<NativeOperationActions> createState() => _NativeOperationActionsState();
}

class _NativeOperationActionsState extends State<NativeOperationActions> {
  String? get contextId => widget.auth.session?.selected == null
      ? null
      : '${widget.auth.session!.selected!.id}:${widget.auth.session!.selected!.contextRevision}';
  late final opened = contextId;
  String query = '', category = 'الكل';
  Future<List<Object?>> loadAccess() =>
      widget.auth.session?.selected?.role == 'AcademyOwner'
      ? Future.value([<String>[], <String, dynamic>{}])
      : Future.wait([
          widget.auth.request('GET', '/api/v1/my-permissions'),
          widget.auth.request('GET', '/api/v1/my-access'),
        ]);
  late Future<List<Object?>> access = loadAccess();
  @override
  Widget build(BuildContext context) => ListenableBuilder(
    listenable: widget.auth,
    builder: (context, _) {
      if (opened == null ||
          opened != contextId ||
          ![
            'AcademyOwner',
            'AcademyAdmin',
          ].contains(widget.auth.session?.selected?.role)) {
        return const SizedBox.shrink();
      }
      return FutureBuilder(
        future: access,
        builder: (context, s) {
          Widget retry() => Column(
            mainAxisSize: MainAxisSize.min,
            children: [
              const EmptyMessage('تعذر تحميل العمليات المتاحة بصلاحياتك.'),
              TextButton(
                onPressed: () => setState(() => access = loadAccess()),
                child: const UiText('إعادة تحميل الإجراءات'),
              ),
            ],
          );
          if (s.hasError) return retry();
          if (!s.hasData) {
            return SizedBox(
              height: widget.carousel
                  ? (MediaQuery.textScalerOf(context).scale(1) > 1.4 ? 124 : 92)
                  : 80,
              child: const Center(child: CircularProgressIndicator()),
            );
          }
          if (s.data![0] is! List ||
              s.data![1] is! Map<String, dynamic> ||
              (s.data![0] as List).any((v) => v is! String)) {
            return retry();
          }
          final owner = widget.auth.session!.selected!.role == 'AcademyOwner',
              permissions = (s.data![0] as List).cast<String>(),
              scope = obj(s.data![1]);
          final capabilities = StaffCapabilities(
            owner: owner,
            permissions: permissions,
            scope: scope,
          );
          bool allows(
            String permission,
            String module,
            String operation,
            List<String> leaves,
          ) => capabilities.allows(permission, module, operation, leaves);

          final registrationCollection = capabilities.explicitAction(
            'finance',
            'Add',
          );
          final actions = <({String label, IconData icon, Widget page})>[
            if (registrationCollection &&
                allows('people.manage', 'players', 'Add', [
                  'workflow.players.create',
                ]) &&
                allows(
                  'subscription.renew.manage',
                  'subscriptions',
                  'Add',
                  [],
                ) &&
                allows('structure.manage', 'academy', 'Read', []) &&
                allows('payment.read', 'payments', 'Read', [
                  'workflow.payments.collect',
                ]))
              (
                label: 'اشتراك جديد',
                icon: Icons.person_add_alt_1_outlined,
                page: NativeNewEnrollment(auth: widget.auth),
              ),
            if (allows('subscription.renew.manage', 'subscriptions', 'Write', [
              'workflow.subscriptions.renew',
            ]))
              (
                label: 'تجديد اشتراك',
                icon: Icons.autorenew_rounded,
                page: NativeAdminRenewal(auth: widget.auth),
              ),
            if (allows('structure.manage', 'academy', 'Add', [])) ...[
              if (owner || scope['allBranches'] == true)
                (
                  label: 'إضافة فرع',
                  icon: Icons.add_location_alt_outlined,
                  page: NativeNamedSetup(auth: widget.auth, kind: 'branches'),
                ),
              if (owner || scope['allSports'] == true)
                (
                  label: 'إضافة رياضة',
                  icon: Icons.sports_outlined,
                  page: NativeNamedSetup(auth: widget.auth, kind: 'sports'),
                ),
            ],
            if (allows('structure.manage', 'academy', 'Add', [
                  'workflow.groups.manage',
                ]) &&
                allows('people.manage', 'players', 'Edit', [
                  'workflow.players.place',
                ]))
              (
                label: 'إنشاء مجموعة',
                icon: Icons.group_add_outlined,
                page: NativeGroupCreate(auth: widget.auth),
              ),
            if (allows('subscription.plan.manage', 'subscriptions', 'Add', []))
              (
                label: 'إضافة باقة',
                icon: Icons.inventory_2_outlined,
                page: NativePlanCreate(auth: widget.auth),
              ),
            if (allows('staff.provision', 'staff', 'Add', []))
              (
                label: 'إضافة مدرب',
                icon: Icons.sports_outlined,
                page: NativeCoachCreate(auth: widget.auth),
              ),
            if (allows('evaluation.manage', 'evaluations', 'Add', []))
              (
                label: 'إنشاء تقييم',
                icon: Icons.assignment_outlined,
                page: NativeEvaluationPicker(auth: widget.auth),
              ),
            if (allows('attendance.manage', 'attendance', 'Edit', [
              'workflow.attendance.players',
            ]))
              (
                label: 'تسجيل الحضور',
                icon: Icons.fact_check_outlined,
                page: NativeSessions(auth: widget.auth),
              ),
            if (!allows('attendance.manage', 'attendance', 'Edit', [
                  'workflow.attendance.players',
                ]) &&
                allows('attendance.read', 'attendance', 'Read', []))
              (
                label: 'الحصص',
                icon: Icons.calendar_month_outlined,
                page: NativeSessions(auth: widget.auth),
              ),
            if (allows('structure.manage', 'academy', 'Write', [
              'workflow.groups.manage',
            ]))
              (
                label: 'إسناد مدرب لمجموعة',
                icon: Icons.group_add_outlined,
                page: NativeCoachAssignment(auth: widget.auth),
              ),
            if (allows('structure.manage', 'academy', 'Edit', []) &&
                allows('people.manage', 'players', 'Edit', [
                  'workflow.players.place',
                ]))
              (
                label: 'تسكين لاعب',
                icon: Icons.person_pin_circle_outlined,
                page: NativeGroupPicker(
                  auth: widget.auth,
                  title: 'تسكين لاعب',
                  open: (id) =>
                      NativeGroupPlacement(auth: widget.auth, groupId: id),
                ),
              ),
            if (allows('structure.manage', 'academy', 'Write', [
              'workflow.groups.manage',
            ]))
              (
                label: 'تعديل مجموعة',
                icon: Icons.edit_outlined,
                page: NativeGroupPicker(
                  auth: widget.auth,
                  title: 'تعديل مجموعة',
                  open: (id) => NativeGroupEdit(auth: widget.auth, id: id),
                ),
              ),
          ];
          if (allows('content.media.manage', 'media', 'Add', [])) {
            actions.add((
              label: 'رفع صور وفيديو',
              icon: Icons.add_photo_alternate_outlined,
              page: NativeMediaGroups(auth: widget.auth),
            ));
          }
          if (owner && !widget.carousel) {
            actions.add((
              label: 'الموظفون والصلاحيات',
              icon: Icons.admin_panel_settings_outlined,
              page: NativeStaffManagement(auth: widget.auth),
            ));
          }
          if (actions.isEmpty) return const SizedBox.shrink();
          if (widget.carousel) {
            const colors = {
              'اشتراك جديد': Color(0xFFCA2449),
              'تجديد اشتراك': Color(0xFF159B82),
              'إضافة مدرب': Color(0xFFE99A23),
              'إسناد مدرب لمجموعة': Color(0xFF6755C7),
              'الموظفون والصلاحيات': Color(0xFF377CC4),
              'تسجيل الحضور': Color(0xFF248EBA),
              'إنشاء تقييم': Color(0xFFCE6485),
              'تسكين لاعب': Color(0xFF6755C7),
              'الحصص': Color(0xFF248EBA),
              'تعديل مجموعة': Color(0xFF6755C7),
              'رفع صور وفيديو': Color(0xFF377CC4),
              'إضافة فرع': Color(0xFF377CC4),
              'إضافة رياضة': Color(0xFF159B82),
              'إنشاء مجموعة': Color(0xFF6755C7),
              'إضافة باقة': Color(0xFFE99A23),
            };
            const priority = [
              'اشتراك جديد',
              'تجديد اشتراك',
              'إنشاء مجموعة',
              'إضافة باقة',
              'إضافة فرع',
              'إضافة رياضة',
              'إضافة مدرب',
              'إسناد مدرب لمجموعة',
              'الموظفون والصلاحيات',
              'تسجيل الحضور',
              'الحصص',
              'تسكين لاعب',
              'إنشاء تقييم',
              'تعديل مجموعة',
              'رفع صور وفيديو',
            ];
            return OwnerActionRail(
              automatic: true,
              onAll: () =>
                  openPage(context, OwnerQuickOperations(auth: widget.auth)),
              actions: [
                for (final label in priority)
                  for (final a in actions.where((a) => a.label == label))
                    OwnerRailAction(
                      a.label,
                      a.icon,
                      colors[label]!,
                      () => openPage(context, a.page),
                    ),
              ],
            );
          }
          const sections = {
            'الاشتراكات واللاعبون': [
              'اشتراك جديد',
              'تجديد اشتراك',
              'تسكين لاعب',
            ],
            'تجهيز الأكاديمية': [
              'إضافة فرع',
              'إضافة رياضة',
              'إنشاء مجموعة',
              'إضافة باقة',
              'إضافة مدرب',
              'إسناد مدرب لمجموعة',
              'تعديل مجموعة',
              'الموظفون والصلاحيات',
            ],
            'المتابعة اليومية': [
              'تسجيل الحضور',
              'الحصص',
              'إنشاء تقييم',
              'رفع صور وفيديو',
            ],
          };
          const descriptions = {
            'اشتراك جديد': 'بيانات اللاعب والأسرة، ثم الباقة والتحصيل',
            'تجديد اشتراك': 'اختيار اللاعب ومراجعة الباقة والفترة القادمة',
            'تسكين لاعب': 'اختر المجموعة وراجع أهلية اللاعبين',
            'إنشاء مجموعة': 'الفرع والرياضة، ثم الحصص والمدرب',
            'إضافة باقة': 'المدة والحصص والسعر والفروع المتاحة',
            'إضافة فرع': 'اسم الفرع وموقعه',
            'إضافة رياضة': 'اسم الرياضة',
            'إضافة مدرب': 'حساب المدرب ونطاق عمله',
            'إسناد مدرب لمجموعة': 'ربط المدرب بمجموعة محددة',
            'تعديل مجموعة': 'تحديث بيانات المجموعة',
            'الموظفون والصلاحيات': 'الحسابات ونطاق وصول كل موظف',
            'تسجيل الحضور': 'افتح الحصة وسجّل حضور اللاعبين',
            'الحصص': 'مواعيد التدريب وتفاصيل الحصص',
            'إنشاء تقييم': 'اختر اللاعب وقيّم معايير رياضته',
            'رفع صور وفيديو': 'وسائط اللاعبين ضمن صلاحياتك',
          };
          final visible = actions
              .where(
                (a) =>
                    (category == 'الكل' ||
                        (sections[category]?.contains(a.label) ?? false)) &&
                    (query.trim().isEmpty ||
                        '${a.label} ${descriptions[a.label] ?? ''}'.contains(
                          query.trim(),
                        )),
              )
              .toList();
          final tiles = <Widget>[
            if (!widget.compact && actions.length > 5) ...[
              TextField(
                key: const ValueKey('operation-search'),
                onChanged: (value) => setState(() => query = value),
                decoration: const InputDecoration(
                  labelText: 'ابحث عن إجراء',
                  prefixIcon: Icon(Icons.search_rounded),
                ),
              ),
              const SizedBox(height: 14),
              SingleChildScrollView(
                scrollDirection: Axis.horizontal,
                child: Row(
                  children: [
                    for (final name in [
                      'الكل',
                      ...sections.entries
                          .where(
                            (s) =>
                                actions.any((a) => s.value.contains(a.label)),
                          )
                          .map((s) => s.key),
                    ])
                      Padding(
                        padding: const EdgeInsetsDirectional.only(end: 8),
                        child: ChoiceChip(
                          label: UiText(name),
                          selected: name == category,
                          showCheckmark: false,
                          onSelected: (_) => setState(() => category = name),
                        ),
                      ),
                  ],
                ),
              ),
              const SizedBox(height: 6),
              if (visible.isEmpty)
                const EmptyMessage(
                  'لا يوجد إجراء مطابق. جرّب كلمة أخرى أو اختر الكل.',
                ),
            ],
            for (final section in sections.entries)
              if (visible.any((a) => section.value.contains(a.label))) ...[
                Padding(
                  padding: const EdgeInsets.only(top: 10, bottom: 12),
                  child: UiText(
                    section.key,
                    style: const TextStyle(
                      fontSize: 18,
                      fontWeight: FontWeight.w700,
                    ),
                  ),
                ),
                for (final label in section.value)
                  for (final a in visible.where((a) => a.label == label))
                    OperationActionTile(
                      title: a.label,
                      subtitle: descriptions[a.label] ?? '',
                      icon: a.icon,
                      onTap: () => openPage(context, a.page),
                    ),
              ],
          ];
          return Theme(
            data: operationTheme(context),
            child: widget.compact
                ? Column(
                    crossAxisAlignment: CrossAxisAlignment.stretch,
                    children: tiles,
                  )
                : gList(tiles),
          );
        },
      );
    },
  );
}

class NativeEvaluationPicker extends StatelessWidget {
  const NativeEvaluationPicker({super.key, required this.auth});
  final AuthController auth;
  @override
  Widget build(BuildContext context) => GuardianPage(
    title: 'إنشاء تقييم',
    child: RemoteBody(
      auth: auth,
      path: '/api/v1/evaluations/options',
      builder: (context, v) => SearchableDirectory(
        items: rows(obj(v)['enrollments']),
        label: 'بحث عن لاعب للتقييم',
        itemBuilder: (context, e) => PlayerDirectoryCard(
          auth: auth,
          player: {...e, 'arabicName': e['player']},
          enrollments: [e],
          openLabel: 'بدء التقييم',
          onOpen: () =>
              openPage(context, CoachEvaluation(auth: auth, enrollment: e)),
        ),
      ),
    ),
  );
}
