import 'payroll_requests.dart';

import 'package:flutter/foundation.dart';

import '../shared/information_help.dart';

import 'package:flutter/material.dart';

import '../../core/auth/auth_controller.dart';
import '../../core/api/api_client.dart';
import '../../core/theme/ui_text.dart';
import '../../core/widgets/arabic_number_input.dart';
import '../admin/native_operation_form.dart';
import '../guardian/guardian_ui.dart';
import '../shared/experience_ui.dart';

const staffModuleLabels = <String, String>{
  'academy': 'المجموعات والحصص',
  'players': 'اللاعبون وأولياء الأمور',
  'subscriptions': 'الباقات والاشتراكات',
  'payments': 'الدفع والتحصيل',
  'attendance': 'الحضور',
  'evaluations': 'التقييمات',
  'catalog': 'المنتجات والطلبات',
  'nutrition': 'التغذية',
  'medical': 'السجلات الطبية',
  'media': 'الصور والفيديوهات',
  'finance': 'الماليات',
  'reports': 'التقارير والتصدير',
  'payroll': 'الموظفون والرواتب',
  'settings': 'إعدادات الأكاديمية',
  'staff': 'حسابات الموظفين',
  'news': 'الأخبار والفعاليات',
  'matches': 'المباريات والنتائج',
  'facilities': 'المرافق والحجوزات',
  'followups': 'المتابعات المسندة',
};
const payrollPermissionLabels = <String, String>{
  'payroll.addition.review': 'اعتماد المبالغ الإضافية',
  'payroll.pay': 'تسجيل صرف الرواتب',
  'payroll.agreement.manage': 'اتفاقيات أجر الخاص',
  'payroll.salary.manage': 'اعتماد الراتب وتغييراته',
  'payroll.statement.approve': 'اعتماد كشف المستحقات',
  'payroll.advance.manage': 'السلف وتخصيصها',
};

final _egyptianStaffNumber = RegExp(r'^(?:(?:\+|00)?200?|0)(1[0-9]{9})$');

String? _staffPhone(String value) {
  final number = canonicalNumericInput(value)
      .replaceAll(RegExp(r'[\s()\-]'), '');
  final match = _egyptianStaffNumber.firstMatch(number);
  return match == null ? null : '+20${match[1]}';
}

class NativeStaffManagement extends StatefulWidget {
  const NativeStaffManagement({super.key, required this.auth});
  final AuthController auth;
  @override
  State<NativeStaffManagement> createState() => _NativeStaffManagementState();
}

class _NativeStaffManagementState
    extends NativeOperationState<NativeStaffManagement> {
  @override
  AuthController get auth => widget.auth;
  Json? data;
  List<Json> members = [];
  bool payrollAvailable = false;
  final search = TextEditingController();
  @override
  void initState() {
    super.initState();
    load();
  }

  @override
  void dispose() {
    search.dispose();
    super.dispose();
  }

  Future<void> load() => run(() async {
    if (auth.session?.selected?.role != 'AcademyOwner') {
      throw const ApiFailure(403, 'الموظفون والصلاحيات متاحون للمالك فقط.');
    }
    final staff = obj(await auth.request('GET', '/api/v1/manage/staff'));
    Json employees = {'items': <Json>[]};
    var payroll = false;
    try {
      employees = obj(await auth.request('GET', '/api/v1/manage/employees'));
      payroll = true;
    } on ApiFailure catch (e) {
      if (e.status != 403) rethrow;
    }
    if (current) {
      setState(() {
        data = employees;
        payrollAvailable = payroll;
        members = rows(staff['members']);
      });
    }
  });
  Future<void> open(Widget page) async {
    await openPage(context, page);
    if (current) await load();
  }

  Future<void> edit(Json employee) async {
    Json? detail;
    await run(() async {
      detail = obj(
        await auth.request('GET', '/api/v1/manage/employees/${employee['id']}'),
      );
    });
    if (mounted && current && detail != null) {
      await open(
        NativeEmployeeEditor(auth: auth, options: data!, employee: detail),
      );
    }
  }

  @override
  Widget build(BuildContext context) {
    final term = search.text.trim();
    final phone = _staffPhone(term);
    final employees = rows(data?['items'] ?? const <Json>[])
        .where(
          (e) =>
              '${e['name']} ${e['phone']} ${e['jobTitle']}'.contains(term) ||
              phone != null && _staffPhone(textOf(e['phone'])) == phone,
        )
        .toList();
    final accounts = members
        .where(
          (m) =>
              '${m['name']} ${m['phoneNumber']} ${m['jobTitle']}'.contains(
                term,
              ) ||
              phone != null && _staffPhone(textOf(m['phoneNumber'])) == phone,
        )
        .toList();
    return page('الموظفون والصلاحيات', [
      if (data == null && !busy)
        OutlinedButton(onPressed: load, child: const UiText('إعادة المحاولة')),
      if (data != null) ...[
        Wrap(
          spacing: 8,
          runSpacing: 8,
          children: [
            FilledButton.icon(
              onPressed: () => open(
                payrollAvailable
                    ? NativeEmployeeEditor(auth: auth, options: data!)
                    : NativeStaffAccountCreate(auth: auth),
              ),
              icon: const Icon(Icons.person_add_alt),
              label: UiText(
                payrollAvailable ? 'إضافة موظف' : 'إضافة حساب دخول',
              ),
            ),
            if (payrollAvailable)
              OutlinedButton(
                onPressed: () => open(NativeStaffAccountCreate(auth: auth)),
                child: const UiText('إضافة حساب دخول'),
              ),
          ],
        ),
        const SizedBox(height: 12),
        GLink(
          'أدوار الموظفين',
          Icons.work_outline,
          () => open(NativeStaffRoles(auth: auth)),
        ),
        if (payrollAvailable) ...[
          const SizedBox(height: 12),
          GLink(
            'السلف والمبالغ الإضافية',
            Icons.account_balance_wallet_outlined,
            () => open(OwnerPayrollRequests(auth: auth)),
          ),
        ],
        const SizedBox(height: 14),
        TextField(
          controller: search,
          decoration: const InputDecoration(
            labelText: 'بحث بالاسم أو الهاتف أو الوظيفة',
          ),
          onChanged: (_) => setState(() {}),
        ),
        if (employees.isNotEmpty) const Heading('بيانات التوظيف والرواتب'),
        for (final employee in employees)
          GCard(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: [
                UiText(
                  textOf(employee['name']),
                  style: const TextStyle(
                    fontSize: 16,
                    fontWeight: FontWeight.w700,
                  ),
                ),
                UiText(
                  '${employee['jobTitle']} · \u2066${employee['phone']}\u2069',
                  style: const TextStyle(fontSize: 12),
                ),
                MoneyText(
                  employee['monthlySalary'],
                  employee['currency'],
                  size: 20,
                ),
                OutlinedButton(
                  onPressed: () => edit(employee),
                  child: const UiText('بيانات الموظف'),
                ),
                if (members.any(
                  (m) =>
                      m['id'] == employee['membershipId'] &&
                      m['role'] == 'AcademyAdmin',
                ))
                  TextButton(
                    onPressed: () => open(
                      NativeStaffAccessEditor(
                        auth: auth,
                        membershipId: textOf(employee['membershipId']),
                        name: textOf(employee['name']),
                      ),
                    ),
                    child: const UiText('الأفعال ونطاق الصلاحيات'),
                  ),
              ],
            ),
          ),
        if (accounts.isNotEmpty) const Heading('المدربون والموظفون'),
        if (employees.isEmpty && accounts.isEmpty)
          UiText(
            term.isEmpty
                ? 'لا توجد حسابات أو سجلات موظفين.'
                : 'لا توجد نتائج مطابقة.',
          ),
        for (final member in accounts)
          GCard(
            padding: const EdgeInsets.symmetric(horizontal: 14, vertical: 4),
            child: ListTile(
              minTileHeight: 64,
              contentPadding: EdgeInsets.zero,
              title: UiText(textOf(member['name'])),
              subtitle: UiText(
                '${member['jobTitle'] ?? (member['role'] == 'Coach' ? 'مدرب' : 'إداري')} · ${member['isActive'] == true ? 'فعال' : 'موقوف'}',
              ),
              trailing: member['role'] == 'AcademyAdmin'
                  ? const Icon(Icons.tune)
                  : const Icon(Icons.groups_outlined),
              onTap: member['role'] == 'AcademyAdmin'
                  ? () => open(
                      NativeStaffAccessEditor(
                        auth: auth,
                        membershipId: textOf(member['id']),
                        name: textOf(member['name']),
                      ),
                    )
                  : () => open(
                      NativeCoachAccessEditor(
                        auth: auth,
                        membershipId: textOf(member['id']),
                        name: textOf(member['name']),
                      ),
                    ),
            ),
          ),
      ],
    ], screen: 'staff');
  }
}

class NativeStaffAccountCreate extends StatefulWidget {
  const NativeStaffAccountCreate({super.key, required this.auth});
  final AuthController auth;
  @override
  State<NativeStaffAccountCreate> createState() =>
      _NativeStaffAccountCreateState();
}

class _NativeStaffAccountCreateState
    extends NativeOperationState<NativeStaffAccountCreate> {
  @override
  AuthController get auth => widget.auth;
  final name = TextEditingController(),
      phone = TextEditingController(),
      email = TextEditingController(),
      password = TextEditingController(),
      job = TextEditingController();
  String role = 'AcademyAdmin';
  bool uncertain = false;
  List<Json> roles = [];
  @override
  void initState() {
    super.initState();
    loadRoles();
  }

  Future<void> loadRoles() async {
    try {
      final result = obj(
        await auth.request('GET', '/api/v1/manage/staff/role-templates/'),
      );
      if (current) {
        setState(
          () =>
              roles = rows(result['items'])
                  .where((r) => r['isActive'] == true)
                  .toList(),
        );
      }
    } catch (_) {
      /* A title can still be entered without a role preset. */
    }
  }

  @override
  void dispose() {
    for (final c in [name, phone, email, password, job]) {
      c.dispose();
    }
    super.dispose();
  }

  Future<void> save() async {
    if (uncertain || !form.currentState!.validate()) return;
    final displayName = name.text.trim();
    final accountRole = role;
    await run(() async {
      String id;
      try {
        final result = obj(
          await auth.request(
            'POST',
            '/api/v1/manage/staff',
            body: {
              'name': displayName,
              'email': email.text.trim(),
              'phone': _staffPhone(phone.text),
              'temporaryPassword': accountRole == 'AcademyAdmin'
                  ? password.text
                  : null,
              'jobTitle': job.text.trim(),
              'role': accountRole,
            },
          ),
        );
        final savedId = result['id'];
        if (savedId is! String || savedId.isEmpty) {
          throw StateError('Unconfirmed account');
        }
        id = savedId;
      } catch (e) {
        if (e is! ApiFailure || e.status == 0 || e.status >= 500) {
          uncertain = true;
          throw const ApiFailure(
            0,
            'تعذر تأكيد إنشاء الحساب. اعرض حسابات الفريق للتحقق من وجوده.',
          );
        }
        if (e.status == 409) {
          throw const ApiFailure(
            409,
            'رقم الهاتف أو البريد الإلكتروني مستخدم بالفعل.',
          );
        }
        rethrow;
      }
      if (!mounted || !current) return;
      password.clear();
      if (accountRole == 'AcademyAdmin') {
        await openPage(
          context,
          NativeStaffAccessEditor(
            auth: auth,
            membershipId: id,
            name: displayName,
          ),
        );
      }
      if (mounted && current) Navigator.of(context).pop(true);
    });
  }

  @override
  Widget build(BuildContext context) {
    if (uncertain) {
      return page('حساب دخول للموظف', [
        FilledButton(
          onPressed: () => Navigator.of(context).pop(),
          child: const UiText('عرض حسابات الفريق'),
        ),
      ], screen: 'staff');
    }
    return page('حساب دخول للموظف', [
      choice('الدور', role, [
        {'id': 'AcademyAdmin', 'arabicName': 'إداري'},
        {'id': 'Coach', 'arabicName': 'مدرب'},
      ], (v) => setState(() => role = v!)),
      field('الاسم', name),
      Padding(
        padding: const EdgeInsets.only(bottom: 14),
        child: TextFormField(
          controller: phone,
          textDirection: TextDirection.ltr,
          textAlign: TextAlign.left,
          keyboardType: TextInputType.phone,
          textInputAction: TextInputAction.next,
          autofillHints: const [AutofillHints.telephoneNumber],
          inputFormatters: const [ArabicNumberInputFormatter()],
          decoration: const InputDecoration(
            label: RequiredFieldLabel('رقم الهاتف المصري'),
            helperText:
                'مثل \u206601012345678\u2069 أو \u2066+201012345678\u2069',
            errorMaxLines: 2,
          ),
          validator: (value) {
            if (value == null || value.trim().isEmpty) {
              return 'أدخل رقم الهاتف المصري.';
            }
            final number = _staffPhone(value);
            return number != null && '0125'.contains(number[4])
                ? null
                : 'اكتب رقم موبايل مصري صحيح.';
          },
        ),
      ),
      field(
        'البريد الإلكتروني — اختياري',
        email,
        required: false,
        keyboard: TextInputType.emailAddress,
        validate: (value) =>
            value.isEmpty ||
                value.length <= 200 &&
                    RegExp(r'^[^\s@]+@[^\s@]+$').hasMatch(value)
            ? null
            : 'اكتب بريدًا إلكترونيًا صالحًا.',
      ),
      if (roles.isEmpty)
        field('المسمى الوظيفي', job, required: false)
      else
        choice(
          'المسمى الوظيفي',
          job.text.isEmpty ? null : job.text,
          [
            for (final r in roles)
              {
                'id': obj(r['template'])['name'],
                'arabicName': obj(r['template'])['name'],
              },
          ],
          (v) => setState(() => job.text = v ?? ''),
          required: false,
        ),
      if (role == 'AcademyAdmin')
        TextFormField(
          controller: password,
          obscureText: true,
          enableSuggestions: false,
          autocorrect: false,
          decoration: const InputDecoration(
            labelText: 'كلمة مرور الويب — اختياري',
          ),
          validator: (v) {
            if (v == null || v.isEmpty) return null;
            if (email.text.trim().isEmpty) {
              return 'أدخل البريد الإلكتروني لدخول الويب.';
            }
            return v.length < 12
                ? 'استخدم كلمة مرور من 12 خانة أو أكثر.'
                : null;
          },
        ),
      const SizedBox(height: 14),
      FilledButton(onPressed: save, child: const UiText('إنشاء الحساب')),
    ], screen: 'staff');
  }
}

class NativeEmployeeEditor extends StatefulWidget {
  const NativeEmployeeEditor({
    super.key,
    required this.auth,
    required this.options,
    this.employee,
  });
  final AuthController auth;
  final Json options;
  final Json? employee;
  @override
  State<NativeEmployeeEditor> createState() => _NativeEmployeeEditorState();
}

class _NativeEmployeeEditorState
    extends NativeOperationState<NativeEmployeeEditor> {
  @override
  AuthController get auth => widget.auth;
  late final name = TextEditingController(text: widget.employee?['name']),
      phone = TextEditingController(text: widget.employee?['phone']),
      job = TextEditingController(text: widget.employee?['jobTitle']),
      salary = TextEditingController(
        text: '${widget.employee?['monthlySalary'] ?? 0}',
      ),
      start = TextEditingController(
        text: widget.employee?['contractStart'] == null
            ? isoDay(DateTime.now())
            : day(widget.employee!['contractStart']),
      ),
      end = TextEditingController(
        text: widget.employee?['contractEnd'] == null
            ? ''
            : day(widget.employee!['contractEnd']),
      );
  String? branch, membership, savedId;
  bool active = true, uncertain = false;
  @override
  void initState() {
    super.initState();
    branch = widget.employee?['branchId'];
    membership = widget.employee?['membershipId'];
    active = widget.employee?['isActive'] != false;
  }

  @override
  void dispose() {
    for (final c in [name, phone, job, salary, start, end]) {
      c.dispose();
    }
    super.dispose();
  }

  Future<void> save() async {
    if (!form.currentState!.validate()) return;
    final amount = num.tryParse(salary.text);
    if (amount == null ||
        !amount.isFinite ||
        amount < 0 ||
        amount > 100000000 ||
        DateTime.tryParse(start.text) == null ||
        end.text.isNotEmpty &&
            (DateTime.tryParse(end.text) == null ||
                end.text.compareTo(start.text) < 0)) {
      setState(() => error = 'راجع الراتب وتاريخ بداية ونهاية العقد.');
      return;
    }
    await run(() async {
      final id = savedId ?? widget.employee?['id'];
      Object? result;
      try {
        result = await auth.request(
          id == null ? 'POST' : 'PUT',
          '/api/v1/manage/employees${id == null ? '' : '/$id'}',
          body: {
            'name': name.text.trim(),
            'phone': phone.text.trim(),
            'jobTitle': job.text.trim(),
            'monthlySalary': amount,
            'branchId': branch,
            'membershipId': membership,
            'contractStart': start.text,
            'contractEnd': end.text.isEmpty ? null : end.text,
            'photoAssetId': widget.employee?['photoAssetId'],
            'isActive': active,
          },
        );
      } catch (e) {
        if (id == null &&
            (e is! ApiFailure || e.status == 0 || e.status >= 500)) {
          uncertain = true;
        }
        rethrow;
      }
      savedId = id == null ? textOf(obj(result)['id']) : textOf(id);
      final read = obj(
        await auth.request('GET', '/api/v1/manage/employees/$savedId'),
      );
      if (read['name'] != name.text.trim() || read['branchId'] != branch) {
        throw StateError('Unconfirmed employee');
      }
      if (mounted && current) Navigator.of(context).pop(true);
    });
  }

  @override
  Widget build(BuildContext context) {
    final accounts = rows(widget.options['accounts'])
        .map((m) => Map<String, dynamic>.from(m))
        .toList();
    if (membership != null && !accounts.any((m) => m['id'] == membership)) {
      accounts.add({'id': membership, 'name': 'حساب الموظف المرتبط'});
    }
    return page(widget.employee == null ? 'إضافة موظف' : 'بيانات الموظف', [
      field('الاسم', name),
      field('الهاتف', phone, keyboard: TextInputType.phone),
      field('المسمى الوظيفي', job),
      choice(
        'الفرع',
        branch,
        rows(widget.options['branches']),
        (v) => setState(() => branch = v),
        name: 'name',
      ),
      choice(
        'حساب الدخول المرتبط',
        membership,
        [
          {'id': '', 'name': 'بدون حساب دخول'},
          ...accounts,
        ],
        (v) => setState(() => membership = v == '' ? null : v),
        name: 'name',
        required: false,
      ),
      field(
        'الراتب الشهري',
        salary,
        keyboard: const TextInputType.numberWithOptions(decimal: true),
        readOnly: widget.employee != null,
      ),
      if (widget.employee != null)
        const Padding(
          padding: EdgeInsets.only(bottom: 14),
          child: UiText(
            'تغييرات الراتب والعقد تحفظ في سجلهما المؤرخ.',
            style: TextStyle(fontSize: 12),
          ),
        ),
      field(
        'بداية العقد',
        start,
        keyboard: TextInputType.datetime,
        readOnly: widget.employee?['hasContractHistory'] == true,
      ),
      field(
        'نهاية العقد',
        end,
        required: false,
        keyboard: TextInputType.datetime,
        readOnly: widget.employee?['hasContractHistory'] == true,
      ),
      SwitchListTile(
        title: const UiText('الموظف فعال'),
        value: active,
        onChanged: (v) => setState(() => active = v),
      ),
      if (uncertain)
        const UiText(
          'راجع قائمة الموظفين قبل إعادة الإضافة؛ لم نؤكد نتيجة الطلب.',
        ),
      FilledButton(
        onPressed: uncertain ? null : save,
        child: const UiText('حفظ بيانات الموظف'),
      ),
    ], screen: 'staff');
  }
}

class NativeStaffAccessEditor extends StatefulWidget {
  const NativeStaffAccessEditor({
    super.key,
    required this.auth,
    required this.membershipId,
    required this.name,
  });
  final AuthController auth;
  final String membershipId, name;
  @override
  State<NativeStaffAccessEditor> createState() =>
      _NativeStaffAccessEditorState();
}

class _NativeStaffAccessEditorState
    extends NativeOperationState<NativeStaffAccessEditor> {
  @override
  AuthController get auth => widget.auth;
  Json? data;
  final actions = <String, Json>{};
  final branches = <String>{},
      sports = <String>{},
      workflows = <String>{},
      sensitive = <String>{};
  bool allBranches = true, allSports = true;
  String get endpoint => '/api/v1/manage/staff/${widget.membershipId}/access';
  @override
  void initState() {
    super.initState();
    load();
  }

  Future<void> load() => run(() async {
    final read = obj(await auth.request('GET', endpoint));
    if (!current) return;
    setState(() {
      data = read;
      allBranches = read['allBranches'] == true;
      allSports = read['allSports'] != false;
      branches
        ..clear()
        ..addAll((read['selectedBranches'] as List).cast<String>());
      sports
        ..clear()
        ..addAll((read['selectedSports'] as List? ?? []).cast<String>());
      workflows
        ..clear()
        ..addAll(
          rows(read['workflowActions'] ?? <Json>[])
              .where((a) => a['allowed'] == true)
              .map((a) => textOf(a['key'])),
        );
      sensitive
        ..clear()
        ..addAll(
          (read['selectedSensitivePermissions'] as List? ?? []).cast<String>(),
        );
      actions.clear();
      for (final module in (read['modules'] as List).cast<String>()) {
        final existing = rows(read['actions'])
            .where((a) => a['module'] == module)
            .firstOrNull;
        actions[module] = {
          'module': module,
          'read': existing?['read'] == true,
          'add': existing?['add'] == true,
          'edit': existing?['edit'] == true,
          'delete': existing?['delete'] == true,
        };
      }
    });
  });
  bool eligible(Json action) {
    final grant = actions[action['module']], operation = action['operation'];
    return grant?['read'] == true &&
        (operation == 'Read' ||
            operation == 'Add' && grant?['add'] == true ||
            operation == 'Edit' && grant?['edit'] == true ||
            operation == 'Write' &&
                grant?['add'] == true &&
                grant?['edit'] == true);
  }

  void toggleModule(String module, String operation, bool enabled) =>
      setState(() {
        final grant = actions[module]!;
        grant[operation] = enabled;
        if (operation == 'read' && !enabled) {
          for (final op in ['add', 'edit', 'delete']) {
            grant[op] = false;
          }
        } else if (enabled) {
          grant['read'] = true;
        }
        workflows.removeWhere(
          (key) =>
              !rows(data!['workflowActions'])
                  .any((a) => a['key'] == key && eligible(a)),
        );
      });
  void toggleWorkflow(Json action, bool enabled) => setState(() {
    final key = textOf(action['key']);
    if (!enabled) {
      workflows.remove(key);
      return;
    }
    final grant = actions[action['module']]!;
    grant['read'] = true;
    if (action['operation'] == 'Add' || action['operation'] == 'Write') {
      grant['add'] = true;
    }
    if (action['operation'] == 'Edit' || action['operation'] == 'Write') {
      grant['edit'] = true;
    }
    if (key == 'workflow.subscriptions.refund') {
      actions['players']!['read'] = true;
      actions['finance']!['read'] = true;
      actions['finance']!['add'] = true;
    }
    workflows.add(key);
  });
  Future<void> save() async {
    if (!allBranches && branches.isEmpty || !allSports && sports.isEmpty) {
      setState(() => error = 'اختر فرعًا ورياضة ضمن النطاق المحدد قبل الحفظ.');
      return;
    }
    final approved = await showDialog<bool>(
      context: context,
      builder: (context) => AlertDialog(
        title: const UiText('اعتماد صلاحيات الموظف؟'),
        content: UiText(
          '${widget.name}\n${allBranches ? 'كل الفروع' : '${branches.length} فروع محددة'} · ${allSports ? 'كل الرياضات' : '${sports.length} رياضات محددة'}\n${workflows.length} أفعال مخصصة.\nالوحدات المتاحة: ${actions.entries.where((e) => e.value['read'] == true).map((e) => staffModuleLabels[e.key] ?? 'صلاحية إضافية').join('، ')}',
        ),
        actions: [
          TextButton(
            onPressed: () => Navigator.pop(context, false),
            child: const UiText('رجوع'),
          ),
          FilledButton(
            onPressed: () => Navigator.pop(context, true),
            child: const UiText('تأكيد الاعتماد'),
          ),
        ],
      ),
    );
    if (approved != true || !current) return;
    await run(() async {
      final body = {
        'allBranches': allBranches,
        'branches': allBranches ? <String>[] : branches.toList(),
        'allSports': allSports,
        'sports': allSports ? <String>[] : sports.toList(),
        'actions': actions.values.toList(),
        'workflowActions': workflows.toList(),
        'sensitivePermissions': sensitive.toList(),
      };
      await auth.request('PUT', endpoint, body: body);
      final read = obj(await auth.request('GET', endpoint));
      final persisted = rows(read['actions']);
      if (read['allBranches'] != allBranches ||
          read['allSports'] != allSports ||
          actions.values.any(
            (a) => !persisted.any(
              (g) =>
                  g['module'] == a['module'] &&
                  [
                    'read',
                    'add',
                    'edit',
                    'delete',
                  ].every((op) => g[op] == a[op]),
            ),
          ) ||
          !_same(
            workflows,
            rows(read['workflowActions'])
                .where((a) => a['allowed'] == true)
                .map((a) => textOf(a['key']))
                .toSet(),
          ) ||
          !_same(
            sensitive,
            (read['selectedSensitivePermissions'] as List)
                .cast<String>()
                .toSet(),
          ) ||
          !allBranches &&
              !_same(
                branches,
                (read['selectedBranches'] as List).cast<String>().toSet(),
              ) ||
          !allSports &&
              !_same(
                sports,
                (read['selectedSports'] as List).cast<String>().toSet(),
              )) {
        throw StateError('Unconfirmed access');
      }
      if (current) {
        setState(() => message = 'حُفظت الصلاحيات وأُعيد التحقق منها.');
      }
    });
  }

  bool _same(Set<String> a, Set<String> b) =>
      a.length == b.length && a.containsAll(b);
  @override
  Widget build(BuildContext context) => page('صلاحيات ${widget.name}', [
    if (data == null && !busy)
      OutlinedButton(onPressed: load, child: const UiText('إعادة المحاولة')),
    if (data != null) ...[
      const InformationHelp(
        title: 'الأدوار والصلاحيات',
        message: 'اختر الأفعال المطلوبة ونطاق الفروع والرياضات. اسم الدور الوظيفي لا يمنح صلاحية بمفرده.',
      ),
      SwitchListTile(
        title: const UiText('كل الفروع'),
        value: allBranches,
        onChanged: (v) => setState(() => allBranches = v),
      ),
      if (!allBranches)
        for (final b in rows(data!['branches']))
          CheckboxListTile(
            title: UiText(textOf(b['name'])),
            value: branches.contains(b['id']),
            onChanged: (v) => setState(() {
              v == true
                  ? branches.add(textOf(b['id']))
                  : branches.remove(b['id']);
            }),
          ),
      SwitchListTile(
        title: const UiText('كل الرياضات'),
        value: allSports,
        onChanged: (v) => setState(() => allSports = v),
      ),
      if (!allSports)
        for (final s in rows(data!['sports']))
          CheckboxListTile(
            title: UiText(textOf(s['name'])),
            value: sports.contains(s['id']),
            onChanged: (v) => setState(() {
              v == true ? sports.add(textOf(s['id'])) : sports.remove(s['id']);
            }),
          ),
      const Heading('الأفعال المخولة'),
      for (final module in actions.keys)
        ExpansionTile(
          key: PageStorageKey('staff-access-$module'),
          title: UiText(staffModuleLabels[module] ?? 'صلاحية إضافية'),
          subtitle: UiText(
            actions[module]!['read'] == true ? 'متاحة' : 'غير متاحة',
            style: const TextStyle(fontSize: 11),
          ),
          children: [
            for (final action in rows(
              data!['workflowActions'],
            ).where((a) => a['module'] == module))
              CheckboxListTile(
                title: UiText(textOf(action['label'])),
                value: workflows.contains(action['key']),
                onChanged: (v) => toggleWorkflow(action, v == true),
              ),
            ExpansionTile(
              title: const UiText('صلاحيات الوحدة المتقدمة'),
              children: [
                for (final operation in [
                  ('read', 'عرض'),
                  ('add', 'إضافة'),
                  ('edit', 'تعديل'),
                  ('delete', 'حذف'),
                ])
                  SwitchListTile(
                    title: UiText(
                      '${operation.$2} ${staffModuleLabels[module] ?? 'الوحدة'}',
                    ),
                    value: actions[module]![operation.$1] == true,
                    onChanged:
                        module == 'followups' &&
                            ['add', 'delete'].contains(operation.$1)
                        ? null
                        : (v) => toggleModule(module, operation.$1, v),
                  ),
              ],
            ),
          ],
        ),
      ExpansionTile(
        title: const UiText('تفويضات الرواتب الحساسة'),
        children: [
          for (final permission
              in (data!['sensitivePermissions'] as List? ?? []).cast<String>())
            CheckboxListTile(
              title: UiText(
                payrollPermissionLabels[permission] ?? 'تفويض رواتب إضافي',
              ),
              value: sensitive.contains(permission),
              onChanged: (v) => setState(() {
                v == true
                    ? sensitive.add(permission)
                    : sensitive.remove(permission);
              }),
            ),
        ],
      ),
      const SizedBox(height: 14),
      FilledButton(onPressed: save, child: const UiText('اعتماد الصلاحيات')),
    ],
  ], screen: 'staff');
}

class NativeCoachAccessEditor extends StatefulWidget {
  const NativeCoachAccessEditor({
    super.key,
    required this.auth,
    required this.membershipId,
    required this.name,
  });
  final AuthController auth;
  final String membershipId, name;
  @override
  State<NativeCoachAccessEditor> createState() =>
      _NativeCoachAccessEditorState();
}

class _NativeCoachAccessEditorState
    extends NativeOperationState<NativeCoachAccessEditor> {
  @override
  AuthController get auth => widget.auth;
  Json? data;
  final selected = <String>{};
  String get endpoint =>
      '/api/v1/manage/staff/${widget.membershipId}/coach-access';
  @override
  void initState() {
    super.initState();
    load();
  }

  Future<void> load() => run(() async {
    final result = obj(await auth.request('GET', endpoint));
    if (current) {
      setState(() {
        data = result;
        selected.clear();
        selected.addAll(
          rows(result['actions'])
              .where((a) => a['allowed'] == true)
              .map((a) => textOf(a['key'])),
        );
      });
    }
  });
  Future<void> save() => run(() async {
    final expected = {...selected};
    await auth.request(
      'PUT',
      endpoint,
      body: {'revision': data!['revision'], 'actions': expected.toList()},
    );
    final result = obj(await auth.request('GET', endpoint));
    final stored = rows(result['actions'])
        .where((a) => a['allowed'] == true)
        .map((a) => textOf(a['key']))
        .toSet();
    if (!setEquals(expected, stored)) {
      throw const ApiFailure(0, 'لم تتأكد الصلاحيات المحفوظة. حدّث الصفحة.');
    }
    if (current) {
      setState(() {
        data = result;
        message = 'حُفظت صلاحيات المدرب لمجموعاته فقط.';
      });
    }
  });
  @override
  Widget build(BuildContext context) => page('صلاحيات ${widget.name}', [
    if (data != null) ...[
      for (final action in rows(data!['actions']))
        SwitchListTile.adaptive(
          contentPadding: EdgeInsets.zero,
          title: UiText(textOf(action['label'])),
          value: selected.contains(action['key']),
          onChanged: busy
              ? null
              : (v) => setState(() {
                  if (v) {
                    selected.add(textOf(action['key']));
                  } else {
                    selected.remove(action['key']);
                  }
                }),
        ),
      const InformationHelp(
        title: 'نطاق المدرب',
        message: 'هذه الخيارات تحدد أفعال المدرب داخل المجموعات المسندة إليه. لا تمنحه وصولًا لبيانات مجموعات أخرى أو الماليات أو إدارة الموظفين.',
      ),
      FilledButton(
        onPressed: busy ? null : save,
        child: const UiText('حفظ الصلاحيات'),
      ),
    ],
  ], screen: 'staff');
}

class NativeStaffRoles extends StatefulWidget {
  const NativeStaffRoles({super.key, required this.auth});
  final AuthController auth;
  @override
  State<NativeStaffRoles> createState() => _NativeStaffRolesState();
}

class _NativeStaffRolesState extends NativeOperationState<NativeStaffRoles> {
  @override
  AuthController get auth => widget.auth;
  Json? data;
  final name = TextEditingController();
  @override
  void initState() {
    super.initState();
    load();
  }

  @override
  void dispose() {
    name.dispose();
    super.dispose();
  }

  Future<void> load() => run(() async {
    final result = obj(
      await auth.request('GET', '/api/v1/manage/staff/role-templates/'),
    );
    if (current) setState(() => data = result);
  });
  Future<void> create() => run(() async {
    if (name.text.trim().isEmpty) return;
    await auth.request(
      'POST',
      '/api/v1/manage/staff/role-templates/',
      body: {
        'name': name.text.trim(),
        'description': null,
        'isActive': true,
        'actions': [
          for (final module in data!['modules'] as List)
            {
              'module': module,
              'read': false,
              'add': false,
              'edit': false,
              'delete': false,
            },
        ],
      },
    );
    final result = obj(
      await auth.request('GET', '/api/v1/manage/staff/role-templates/'),
    );
    if (current) {
      setState(() {
        data = result;
        name.clear();
        message = 'أُضيف الدور. حدّد صلاحيات الموظف عند إسناده.';
      });
    }
  });
  @override
  Widget build(BuildContext context) => page('أدوار الموظفين', [
    if (data != null) ...[
      field('اسم الدور', name),
      FilledButton.icon(
        onPressed: busy ? null : create,
        icon: const Icon(Icons.add),
        label: const UiText('إضافة دور'),
      ),
      const SizedBox(height: 16),
      for (final role in rows(data!['items']))
        GCard(
          child: ListTile(
            contentPadding: EdgeInsets.zero,
            leading: const Icon(Icons.work_outline),
            title: UiText(textOf(obj(role['template'])['name'])),
            subtitle: UiText(role['isActive'] == true ? 'فعال' : 'موقوف'),
          ),
        ),
    ],
  ], screen: 'staff');
}
