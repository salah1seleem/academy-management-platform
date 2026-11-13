import 'dart:async';

import 'package:flutter/material.dart';

import '../../core/api/api_client.dart';
import '../../core/auth/auth_controller.dart';
import '../../core/auth/staff_passkey_provider.dart';
import '../../core/theme/appearance.dart';
import '../../core/theme/ui_text.dart';
import '../../core/widgets/app_widgets.dart';
import '../guardian/guardian_ui.dart';
import 'native_staff_attendance.dart';
import 'staff_attendance_exceptions.dart';

const _passkeys = '/api/v1/staff/passkeys';

enum _PasskeyAction { read, register, verify, confirmStop, revoke }

typedef _Recovery = ({_PasskeyAction action, String? id, String? name});

class StaffPasskeyPage extends StatefulWidget {
  final AuthController auth;
  final StaffPasskeyProvider? provider;
  const StaffPasskeyPage({super.key, required this.auth, this.provider});
  @override
  State<StaffPasskeyPage> createState() => _StaffPasskeyPageState();
}

class _StaffPasskeyPageState extends State<StaffPasskeyPage> {
  late final identity = staffContext(widget.auth);
  late final provider = widget.provider ?? IosStaffPasskeyProvider();
  final name = TextEditingController();
  final nameFocus = FocusNode();
  final scroll = ScrollController();

  Json? data;
  _PasskeyAction? activity = _PasskeyAction.read;
  _Recovery? recovery;
  bool readFailed = false;
  String? error, message, nameError;

  bool get current =>
      mounted &&
      identity != null &&
      identity == staffContext(widget.auth) &&
      !widget.auth.roleSessionUnconfirmed;
  bool get locked =>
      activity != null || recovery != null || readFailed || widget.auth.busy;
  bool get configured => data?['configured'] == true;
  bool get deviceAvailable =>
      configured &&
      provider.enabled &&
      data?['nativeIosOriginConfigured'] == true &&
      data?['rpId'] == provider.rpId;
  List<Json> get keys => rows(data?['items']);
  int get activeCount => keys.where((item) => item['isActive'] == true).length;

  @override
  void initState() {
    super.initState();
    if (current) unawaited(readMethods().whenComplete(finish));
  }

  void finish() {
    if (!current) return;
    setState(() => activity = null);
    if (error == null && message == null && !readFailed) return;
    WidgetsBinding.instance.addPostFrameCallback((_) {
      if (!current || !scroll.hasClients || scroll.offset <= 0) return;
      if (MediaQuery.disableAnimationsOf(context)) {
        scroll.jumpTo(0);
      } else {
        unawaited(
          scroll.animateTo(
            0,
            duration: const Duration(milliseconds: 180),
            curve: Curves.easeOut,
          ),
        );
      }
    });
  }

  void begin(_PasskeyAction action) {
    setState(() {
      activity = action;
      error = null;
      message = null;
    });
  }

  Future<void> readMethods([_Recovery? target]) async {
    try {
      final fresh = obj(await widget.auth.request('GET', '$_passkeys/'));
      if (!current) return;
      if (fresh['configured'] is! bool ||
          fresh['items'] is! List ||
          (fresh['items'] as List).any(
            (item) =>
                item is! Map ||
                item['id'] is! String ||
                item['name'] is! String ||
                item['isActive'] is! bool,
          )) {
        throw const FormatException();
      }
      final found = rows(fresh['items'])
          .where((item) => item['id'] == target?.id)
          .firstOrNull;
      setState(() {
        data = fresh;
        readFailed = false;
        if (target == null) return;
        if (target.action == _PasskeyAction.revoke && found == null) {
          recovery = target;
          error = 'الوسيلة غير ظاهرة في القائمة. أعد التحقق من حالتها.';
          return;
        }
        recovery = null;
        message = switch (target.action) {
          _PasskeyAction.revoke =>
            found?['isActive'] == false
                ? 'تم إيقاف وسيلة التحقق.'
                : 'وسيلة التحقق ما زالت فعّالة.',
          _PasskeyAction.register when found?['isActive'] == true =>
            'تمت إضافة وسيلة التحقق.',
          _PasskeyAction.verify when found?['isActive'] == true =>
            'تم التحقق من الوسيلة.',
          _ => 'تم تحديث القائمة. راجع الوسائل المحفوظة قبل المحاولة مرة أخرى.',
        };
        if (target.action == _PasskeyAction.register && target.id != null) {
          name.clear();
        }
      });
    } catch (_) {
      if (current) {
        setState(() {
          readFailed = true;
          if (target != null) {
            recovery = target;
            error = 'تعذر التحقق من الحالة. أعد قراءة القائمة.';
          }
        });
      }
    }
  }

  Future<void> reload() async {
    if (!current || activity != null || widget.auth.busy) return;
    begin(_PasskeyAction.read);
    try {
      await readMethods(recovery);
    } finally {
      finish();
    }
  }

  Future<void> ceremony(bool register) async {
    if (!current ||
        locked ||
        !deviceAvailable ||
        (register ? activeCount >= 5 : activeCount == 0)) {
      return;
    }
    final trimmed = name.text.trim();
    if (register && (trimmed.length < 2 || trimmed.length > 80)) {
      setState(() => nameError = 'اكتب اسمًا من حرفين إلى ٨٠ حرفًا.');
      nameFocus.requestFocus();
      return;
    }
    FocusScope.of(context).unfocus();
    final action = register ? _PasskeyAction.register : _PasskeyAction.verify;
    begin(action);
    final kind = register ? 'register' : 'verify';
    final purpose = register ? 'Register' : 'Verify';
    var completing = false;
    try {
      final challenge = obj(
        await widget.auth.request(
          'POST',
          '$_passkeys/$kind/options',
          body: register ? {'name': trimmed} : {},
        ),
      );
      if (!current) return;
      if (challenge['purpose'] != purpose ||
          challenge['challengeId'] is! String ||
          challenge['options'] is! Map) {
        throw const FormatException();
      }
      final options = obj(challenge['options']);
      final proof = register
          ? await provider.register(options)
          : await provider.assertCredential(options);
      if (!current) return;
      completing = true;
      final saved = obj(
        await widget.auth.request(
          'POST',
          '$_passkeys/$kind/complete',
          body: {'challengeId': challenge['challengeId'], 'credential': proof},
        ),
      );
      if (!current) return;
      final id = obj(saved['credential'])['id'];
      if (saved['verified'] != true ||
          saved['purpose'] != purpose ||
          saved['attendanceRecorded'] != false ||
          id is! String) {
        throw const FormatException();
      }
      await readMethods((action: action, id: id, name: null));
    } catch (cause) {
      if (!current) return;
      setState(() {
        if (completing) {
          recovery = (action: action, id: null, name: null);
          error = 'تعذر تأكيد نتيجة المحاولة. تحقّق من الوسائل المحفوظة.';
        } else if (cause is StaffPasskeyProblem) {
          error = cause.message;
        } else if (cause is ApiFailure && cause.status == 429) {
          error = 'محاولات كثيرة. انتظر قليلًا ثم أعد المحاولة.';
        } else {
          error = 'تعذر بدء التحقق. حاول مرة أخرى.';
        }
      });
    } finally {
      finish();
    }
  }

  Future<void> stop(Json item) async {
    if (!current || locked) return;
    begin(_PasskeyAction.confirmStop);
    final yes = await showDialog<bool>(
      context: context,
      builder: (c) => AlertDialog(
        scrollable: true,
        title: UiText(
          localized(
            c,
            'إيقاف وسيلة «${item['name']}»؟',
            'Stop “${item['name']}”?',
          ),
        ),
        content: const UiText(
          'ستبقى الوسيلة في السجل. لا يمكن إعادة تفعيلها بعد الإيقاف.',
        ),
        actions: [
          TextButton(
            autofocus: true,
            onPressed: () => Navigator.pop(c, false),
            child: const UiText('إلغاء'),
          ),
          FilledButton(
            onPressed: () => Navigator.pop(c, true),
            child: const UiText('تأكيد الإيقاف'),
          ),
        ],
      ),
    );
    if (!current) return;
    if (yes != true || widget.auth.busy) {
      finish();
      return;
    }
    final target = (
      action: _PasskeyAction.revoke,
      id: item['id'] as String,
      name: item['name'] as String,
    );
    begin(_PasskeyAction.revoke);
    try {
      final saved = obj(
        await widget.auth.request(
          'POST',
          '$_passkeys/credentials/${target.id}/revoke',
          body: {},
        ),
      );
      if (!current) return;
      if (saved['id'] != target.id || saved['isActive'] != false) {
        throw const FormatException();
      }
      await readMethods(target);
    } catch (_) {
      if (current) {
        setState(() {
          recovery = target;
          error = 'تعذر تأكيد الإيقاف. تحقّق من حالة الوسيلة.';
        });
      }
    } finally {
      finish();
    }
  }

  @override
  void dispose() {
    name.dispose();
    nameFocus.dispose();
    scroll.dispose();
    unawaited(provider.cancel());
    super.dispose();
  }

  String? get progress => switch (activity) {
    _PasskeyAction.read =>
      recovery == null ? 'جارٍ تحميل وسائل التحقق…' : 'جارٍ التحقق من الحالة…',
    _PasskeyAction.register => 'جارٍ إضافة الوسيلة…',
    _PasskeyAction.verify => 'جارٍ التحقق من الوسيلة…',
    _PasskeyAction.revoke => 'جارٍ إيقاف الوسيلة…',
    _ => null,
  };

  @override
  Widget build(BuildContext context) => GuardianPage(
    title: localized(
      context,
      'وسائل التحقق الخاصة بي',
      'My verification methods',
    ),
    child: ListenableBuilder(
      listenable: widget.auth,
      builder: (c, _) => current
          ? gList(controller: scroll, [
              if (progress case final text?)
                Semantics(liveRegion: true, child: UiText(text)),
              if (error != null) ErrorNotice(error!),
              if (message != null)
                Semantics(liveRegion: true, child: UiText(message!)),
              if (recovery case final pending?)
                OutlinedButton(
                  onPressed: activity == null && !widget.auth.busy
                      ? reload
                      : null,
                  child: UiText(
                    pending.name == null
                        ? 'تحقّق من الوسائل المحفوظة'
                        : localized(
                            c,
                            'تحقّق من حالة «${pending.name}»',
                            'Check “${pending.name}”',
                          ),
                  ),
                ),
              if (readFailed && recovery == null) ...[
                const ErrorNotice('تعذر عرض وسائل التحقق الخاصة بحسابك.'),
                OutlinedButton(
                  onPressed: activity == null && !widget.auth.busy
                      ? reload
                      : null,
                  child: const UiText('إعادة المحاولة'),
                ),
              ],
              if (data != null) ...[
                if (!deviceAvailable)
                  GCard(
                    child: UiText(
                      configured
                          ? 'إضافة وسيلة تحقق غير متاحة على هذا الجهاز. يمكنك طلب إثبات حضور للمراجعة.'
                          : 'إضافة وسيلة تحقق غير متاحة حاليًا. تواصل مع المسؤول عن إعدادها.',
                    ),
                  ),
                if (deviceAvailable)
                  GCard(
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.stretch,
                      children: [
                        const UiText(
                          'إضافة الوسيلة أو تجربتها لا تسجّل حضورًا.',
                        ),
                        const SizedBox(height: 8),
                        if (activeCount < 5) ...[
                          TextField(
                            controller: name,
                            focusNode: nameFocus,
                            maxLength: 80,
                            enabled: !locked,
                            decoration: InputDecoration(
                              labelText: ui(c, 'اسم الوسيلة'),
                              hintText: ui(c, 'هاتف العمل'),
                              errorText: nameError == null
                                  ? null
                                  : ui(c, nameError!),
                              counterText: '',
                              errorMaxLines: 3,
                            ),
                            onChanged: (_) {
                              if (nameError != null) {
                                setState(() => nameError = null);
                              }
                            },
                          ),
                          const SizedBox(height: 8),
                          FilledButton(
                            onPressed: locked ? null : () => ceremony(true),
                            child: const UiText('إضافة وسيلة تحقق'),
                          ),
                        ] else
                          const UiText(
                            'لديك خمس وسائل فعّالة. أوقف إحدى الوسائل قبل إضافة وسيلة أخرى.',
                          ),
                        if (activeCount > 0)
                          TextButton(
                            onPressed: locked ? null : () => ceremony(false),
                            child: const UiText('تجربة وسيلة التحقق'),
                          ),
                      ],
                    ),
                  ),
                const Heading('الوسائل المحفوظة'),
                if (keys.isEmpty && !readFailed)
                  const UiText('لا توجد وسائل تحقق محفوظة لهذا الحساب.'),
                for (final item in keys)
                  _PasskeyCard(
                    item: item,
                    onStop: locked ? null : () => stop(item),
                  ),
                if (recovery == null && !readFailed)
                  TextButton(
                    onPressed: activity == null && !widget.auth.busy
                        ? reload
                        : null,
                    child: const UiText('تحديث القائمة'),
                  ),
              ],
              OutlinedButton.icon(
                onPressed: activity != null || widget.auth.busy
                    ? null
                    : () => openPage(
                        c,
                        StaffAttendanceExceptionPage(auth: widget.auth),
                      ),
                icon: const Icon(Icons.fact_check_outlined),
                label: const UiText('طلب إثبات حضور للمراجعة'),
              ),
              TextButton(
                onPressed: activity != null || widget.auth.busy
                    ? null
                    : () => openPage(
                        c,
                        NativeStaffAttendancePage(
                          auth: widget.auth,
                          provider: widget.provider,
                        ),
                      ),
                child: const UiText('حضوري وانصرافي'),
              ),
              GCard(
                child: ExpansionTile(
                  tilePadding: EdgeInsets.zero,
                  title: const UiText('التحقق والخصوصية'),
                  children: const [
                    UiText(
                      'قد تستخدم بصمة الإصبع أو الوجه أو رمز الجهاز. يمكن أن تتزامن الوسيلة بين أجهزتك. لا نخزّن بصمات أو صور وجوه.',
                    ),
                  ],
                ),
              ),
              if (data != null)
                GCard(
                  child: ExpansionTile(
                    tilePadding: EdgeInsets.zero,
                    title: const UiText('سياسة الحضور حسب الفرع'),
                    children: [
                      StaffAttendanceBranchPolicies(
                        auth: widget.auth,
                        embedded: true,
                      ),
                    ],
                  ),
                ),
            ])
          : const SizedBox.shrink(),
    ),
  );
}

class _PasskeyCard extends StatelessWidget {
  final Json item;
  final VoidCallback? onStop;
  const _PasskeyCard({required this.item, this.onStop});

  @override
  Widget build(BuildContext context) {
    final active = item['isActive'] == true;
    final dates = [
      (label: 'تاريخ الإضافة', value: claimTime(item['createdAtUtc'])),
      (label: 'آخر تحقق', value: claimTime(item['lastVerifiedAtUtc'])),
      (label: 'تاريخ الإيقاف', value: claimTime(item['revokedAtUtc'])),
    ];
    return GCard(
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          UiText(
            textOf(item['name']),
            style: const TextStyle(fontSize: 17, fontWeight: FontWeight.w800),
          ),
          UiText(
            active ? 'فعّالة' : 'متوقفة',
            style: const TextStyle(fontWeight: FontWeight.w700),
          ),
          const SizedBox(height: 6),
          for (final date in dates)
            if (date.value != '—')
              Wrap(
                spacing: 8,
                children: [
                  UiText(date.label),
                  Text(date.value, textDirection: TextDirection.ltr),
                ],
              ),
          if (item['mayBeSynced'] == true)
            const UiText('قد تتزامن بين أجهزتك.'),
          if (active) ...[
            const SizedBox(height: 8),
            OutlinedButton(
              onPressed: onStop,
              child: const UiText('إيقاف الوسيلة'),
            ),
          ],
        ],
      ),
    );
  }
}

// Read the same authorized branch policy as Web, without granting native signing or attendance CRUD.
class StaffAttendanceBranchPolicies extends StatelessWidget {
  final AuthController auth;
  final bool embedded;
  const StaffAttendanceBranchPolicies({
    super.key,
    required this.auth,
    this.embedded = false,
  });
  @override
  Widget build(BuildContext context) => RemoteBody(
    auth: auth,
    path: '/api/v1/staff/passkeys/attendance',
    builder: (context, value) {
      final branches = rows(obj(value)['branches']);
      if (branches.isEmpty) return const SizedBox.shrink();
      final policy = Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          if (!embedded) const Heading('سياسة حضوري حسب الفرع'),
          for (final branch in branches) ...[
            UiText(textOf(branch['name'])),
            if (branch['enabled'] != true)
              const UiText('التسجيل الذاتي موقوف — المراجعة الإدارية متاحة.')
            else if (branch['configured'] != true)
              const UiText('يلزم إعداد موقع الفرع؛ لا نستخدم نقطة تجريبية.')
            else ...[
              UiText(
                'النطاق ${textOf(branch['radiusMeters'])} مترًا · الدقة حتى ${textOf(branch['maxAccuracyMeters'])} مترًا',
              ),
              UiText(
                'حداثة الموقع ${textOf(branch['maxAgeSeconds'])} ثانية · مهلة التوقيع ${textOf(branch['challengeSeconds'])} ثانية',
              ),
              UiText(
                branch['attendanceRequired'] == true
                    ? 'تسجيل الحضور مطلوب لفئتك في هذا الفرع.'
                    : 'الإلزام غير مفعل لفئتك؛ لا خصم تلقائي.',
              ),
            ],
            const SizedBox(height: 10),
          ],
          const UiText(
            'هذه قراءة للسياسة المحفوظة، وليست إثبات حضور أو وسيلة توقيع جديدة.',
          ),
        ],
      );
      return embedded ? policy : GCard(child: policy);
    },
  );
}
