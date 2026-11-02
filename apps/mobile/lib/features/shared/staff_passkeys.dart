import 'package:flutter/material.dart';

import '../../core/auth/auth_controller.dart';
import '../../core/auth/staff_passkey_provider.dart';
import '../../core/theme/appearance.dart';
import '../../core/theme/ui_text.dart';
import '../guardian/guardian_ui.dart';
import 'staff_attendance_exceptions.dart';
import 'native_staff_attendance.dart';

class StaffPasskeyPage extends StatefulWidget {
  final AuthController auth;
  final StaffPasskeyProvider? provider;
  const StaffPasskeyPage({super.key, required this.auth, this.provider});
  @override
  State<StaffPasskeyPage> createState() => _StaffPasskeyPageState();
}

class _StaffPasskeyPageState extends State<StaffPasskeyPage> {
  late final identity = staffContext(widget.auth);
  late Future<Object?> request = load();
  late final provider = widget.provider ?? IosStaffPasskeyProvider();
  final name = TextEditingController(text: 'وسيلة هذا الجهاز');
  bool busy = false;
  String message = '';
  Future<Object?> load() =>
      widget.auth.request('GET', '/api/v1/staff/passkeys/');
  bool get current =>
      mounted && identity != null && identity == staffContext(widget.auth);
  Future<void> ceremony(bool register) async {
    if (busy || !current || !provider.enabled) return;
    setState(() {
      busy = true;
      message = '';
    });
    final kind = register ? 'register' : 'verify',
        purpose = register ? 'Register' : 'Verify';
    try {
      final options = obj(
        await widget.auth.request(
          'POST',
          '/api/v1/staff/passkeys/$kind/options',
          body: register ? {'name': name.text.trim()} : {},
        ),
      );
      if (!current) return;
      if (options['purpose'] != purpose ||
          options['challengeId'] is! String ||
          options['options'] is! Map) {
        throw const FormatException();
      }
      final proof = register
          ? await provider.register(obj(options['options']))
          : await provider.assertCredential(obj(options['options']));
      if (!current) return;
      final result = obj(
        await widget.auth.request(
          'POST',
          '/api/v1/staff/passkeys/$kind/complete',
          body: {'challengeId': options['challengeId'], 'credential': proof},
        ),
      );
      if (!current) return;
      if (result['verified'] != true ||
          result['purpose'] != purpose ||
          result['attendanceRecorded'] != false ||
          obj(result['credential'])['id'] is! String) {
        throw const FormatException();
      }
      setState(() {
        message = register
            ? 'تم تسجيل وسيلة التحقق على الخادم؛ لم يسجل حضور.'
            : 'تم التحقق من الوسيلة؛ هذا التحقق ليس تسجيل حضور.';
        request = load();
      });
    } on StaffPasskeyProblem catch (f) {
      if (current) setState(() => message = f.message);
    } catch (_) {
      if (current) {
        setState(
          () => message = 'تعذر تأكيد نتيجة وسيلة التحقق؛ حدّث القائمة قبل إعادة المحاولة. لم نفترض تسجيل حضور.',
        );
      }
    } finally {
      if (mounted) setState(() => busy = false);
    }
  }

  @override
  void dispose() {
    name.dispose();
    provider.cancel();
    super.dispose();
  }

  Future<void> stop(Json key) async {
    if (busy) return;
    final yes = await showDialog<bool>(
      context: context,
      builder: (c) => AlertDialog(
        title: const UiText('إيقاف وسيلة التحقق؟'),
        content: UiText('${key['name']} — لا يمكن إعادة تفعيل المفتاح نفسه.'),
        actions: [
          TextButton(
            onPressed: () => Navigator.pop(c, false),
            child: const UiText('رجوع'),
          ),
          FilledButton(
            onPressed: () => Navigator.pop(c, true),
            child: const UiText('تأكيد الإيقاف'),
          ),
        ],
      ),
    );
    if (yes != true || !mounted || identity != staffContext(widget.auth)) {
      return;
    }
    setState(() {
      busy = true;
      message = '';
    });
    try {
      final saved = obj(
        await widget.auth.request(
          'POST',
          '/api/v1/staff/passkeys/credentials/${key['id']}/revoke',
          body: {},
        ),
      );
      if (saved['isActive'] != false) throw StateError('Unconfirmed');
      if (mounted && identity == staffContext(widget.auth)) {
        setState(() {
          message = 'تم إيقاف وسيلة التحقق.';
          request = load();
        });
      }
    } catch (_) {
      if (mounted && identity == staffContext(widget.auth)) {
        setState(
          () => message =
              'تعذر تأكيد الإيقاف؛ حدّث القائمة لمعرفة الحالة المحفوظة.',
        );
      }
    } finally {
      if (mounted) {
        setState(() => busy = false);
      }
    }
  }

  @override
  Widget build(BuildContext context) => GuardianPage(
    title: localized(
      context,
      'وسائل التحقق الخاصة بي',
      'My verification methods',
    ),
    child: ListenableBuilder(
      listenable: widget.auth,
      builder: (c, _) {
        if (identity == null || identity != staffContext(widget.auth)) {
          return const SizedBox.shrink();
        }
        return FutureBuilder<Object?>(
          future: request,
          builder: (c, s) {
            if (s.connectionState != ConnectionState.done) {
              return const Center(child: CircularProgressIndicator());
            }
            if (s.hasError) {
              return gList([
                const EmptyMessage('تعذر عرض وسائل التحقق الخاصة بحسابك.'),
                TextButton(
                  onPressed: () => setState(() {
                    request = load();
                  }),
                  child: const UiText('إعادة المحاولة'),
                ),
              ]);
            }
            final data = obj(s.data), keys = rows(data['items']);
            return gList([
              const GCard(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Heading('أمان وسيلة التحقق'),
                    UiText(
                      'لا نخزن بصمات أو صور وجوه. قد تستخدم الوسيلة بصمة أو رمز الجهاز، وقد تتزامن بين أجهزة.',
                    ),
                    UiText(
                      'تسجيل الوسيلة أو تجربتها لا يسجل حضورًا ولا يغير صلاحياتك. التوقيع الأصلي يحتاج إعداد نطاق مرتبط بالتطبيق؛ لا يمنح المدرب أو ولي الأمر دخول الداشبورد.',
                    ),
                  ],
                ),
              ),
              if (data['configured'] != true)
                const EmptyMessage(
                  'يلزم إعداد التحقق الآمن من المسؤول؛ طلب إثبات الحضور للمراجعة ما زال متاحًا.',
                ),
              if (message.isNotEmpty) UiText(message),
              if (provider.enabled &&
                  data['nativeIosOriginConfigured'] == true &&
                  data['rpId'] == provider.rpId)
                GCard(
                  child: Column(
                    children: [
                      TextField(
                        controller: name,
                        maxLength: 80,
                        enabled: !busy,
                        decoration: const InputDecoration(
                          labelText: 'اسم وسيلة التحقق',
                        ),
                      ),
                      FilledButton.icon(
                        onPressed: busy ? null : () => ceremony(true),
                        icon: const Icon(Icons.add_moderator_outlined),
                        label: const UiText('إضافة وسيلة تحقق'),
                      ),
                      if (keys.any((k) => k['isActive'] == true))
                        TextButton(
                          onPressed: busy ? null : () => ceremony(false),
                          child: const UiText('تجربة وسيلة التحقق'),
                        ),
                    ],
                  ),
                )
              else
                const EmptyMessage(
                  'إعداد التوقيع الأصلي لم يكتمل في هذا الاتصال. يلزم نطاق HTTPS مرتبط بالتطبيق وإعداد توقيع مخول؛ يمكنك طلب إثبات حضور للمراجعة.',
                ),
              StaffAttendanceBranchPolicies(auth: widget.auth),
              GLink(
                'حضوري وانصرافي',
                Icons.fingerprint,
                () => openPage(
                  context,
                  NativeStaffAttendancePage(
                    auth: widget.auth,
                    provider: widget.provider,
                  ),
                ),
              ),
              TextButton.icon(
                onPressed: busy
                    ? null
                    : () => setState(() {
                        request = load();
                      }),
                icon: const Icon(Icons.refresh),
                label: const UiText('تحديث الوسائل'),
              ),
              if (keys.isEmpty)
                const EmptyMessage('لا توجد وسائل تحقق محفوظة لهذا الحساب.'),
              for (final key in keys)
                GCard(
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      Heading(textOf(key['name'])),
                      UiText(key['isActive'] == true ? 'فعالة' : 'متوقفة'),
                      if (key['lastVerifiedAtUtc'] != null)
                        UiText(
                          'آخر تحقق — بتوقيت جهازك: ${claimTime(key['lastVerifiedAtUtc'])}',
                        ),
                      if (key['mayBeSynced'] == true)
                        const UiText('قد تتزامن بين أجهزة.'),
                      if (key['isActive'] == true)
                        OutlinedButton.icon(
                          onPressed: busy ? null : () => stop(key),
                          icon: const Icon(Icons.block),
                          label: const UiText('إيقاف الوسيلة'),
                        ),
                    ],
                  ),
                ),
              OutlinedButton.icon(
                onPressed: busy
                    ? null
                    : () => openPage(
                        context,
                        StaffAttendanceExceptionPage(auth: widget.auth),
                      ),
                icon: const Icon(Icons.fact_check_outlined),
                label: const UiText('طلب إثبات حضور للمراجعة'),
              ),
            ]);
          },
        );
      },
    ),
  );
}

// Read the same authorized branch policy as Web, without granting native signing or attendance CRUD.
class StaffAttendanceBranchPolicies extends StatelessWidget {
  final AuthController auth;
  const StaffAttendanceBranchPolicies({super.key, required this.auth});
  @override
  Widget build(BuildContext context) => RemoteBody(
    auth: auth,
    path: '/api/v1/staff/passkeys/attendance',
    builder: (context, value) {
      final branches = rows(obj(value)['branches']);
      if (branches.isEmpty) return const SizedBox.shrink();
      return GCard(
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            const Heading('سياسة حضوري حسب الفرع'),
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
        ),
      );
    },
  );
}
