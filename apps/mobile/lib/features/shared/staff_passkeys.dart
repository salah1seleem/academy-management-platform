import 'package:flutter/material.dart';

import '../../core/auth/auth_controller.dart';
import '../../core/theme/appearance.dart';
import '../../core/theme/ui_text.dart';
import '../guardian/guardian_ui.dart';
import 'staff_attendance_exceptions.dart';

class StaffPasskeyPage extends StatefulWidget {
  final AuthController auth;
  const StaffPasskeyPage({super.key, required this.auth});
  @override
  State<StaffPasskeyPage> createState() => _StaffPasskeyPageState();
}

class _StaffPasskeyPageState extends State<StaffPasskeyPage> {
  late final identity = staffContext(widget.auth);
  late Future<Object?> request = load();
  bool busy = false;
  String message = '';
  Future<Object?> load() =>
      widget.auth.request('GET', '/api/v1/staff/passkeys/');
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
                      'هذه الصفحة لإدارة الوسائل المحفوظة فقط؛ لا تسجل حضورًا أو تغير صلاحياتك. الإضافة والتجربة من الويب الآمن للحسابات المصرح لها بدخول اللوحة فقط؛ تكامل الجهاز الأصلي لم يكتمل بعد. يمكنك طلب إثبات حضور للمراجعة.',
                    ),
                  ],
                ),
              ),
              if (data['configured'] != true)
                const EmptyMessage(
                  'يلزم إعداد التحقق الآمن من المسؤول؛ طلب إثبات الحضور للمراجعة ما زال متاحًا.',
                ),
              if (message.isNotEmpty) UiText(message),
              StaffAttendanceBranchPolicies(auth: widget.auth),
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
