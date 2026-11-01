import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter/services.dart';

import '../../core/auth/auth_controller.dart';
import '../guardian/guardian_ui.dart';

class LauncherIconService {
  static const channel = MethodChannel('iacademy/launcher_icon');
  Future<Map<String, dynamic>> state() async {
    try {
      return Map<String, dynamic>.from(
        await channel.invokeMapMethod<String, dynamic>('state') ?? {},
      );
    } on MissingPluginException {
      return {'supported': false};
    }
  }

  Future<void> set(String? key, String? academyId) =>
      channel.invokeMethod<void>('set', {'key': key, 'academyId': academyId});
  Future<Map<String, dynamic>> choice(
    AuthController auth,
    Map<String, dynamic> native,
  ) async {
    final previous = native['academyId'] as String?;
    final query = previous == null
        ? ''
        : '?selectionAcademyId=${Uri.encodeQueryComponent(previous)}';
    return Map<String, dynamic>.from(
      await auth.request('GET', '/api/v1/academy/launcher-icon$query') as Map,
    );
  }

  // Validate the installed choice, not the currently selected academy. Role
  // switching therefore never silently replaces a still-authorized icon.
  Future<void> reconcile(AuthController auth) async {
    try {
      final native = await state();
      if (native['supported'] != true ||
          native['currentKey'] == null ||
          auth.session?.selected == null) {
        return;
      }
      if (native['academyId'] == null) {
        await set(null, null);
        return;
      }
      final allowed = await choice(auth, native);
      if (allowed['selectedAllowed'] == false) await set(null, null);
    } catch (_) {
      /* Offline: leave the OS choice intact until a trusted read succeeds. */
    }
  }
}

class LauncherIconGuard extends StatefulWidget {
  final AuthController auth;
  final Widget child;
  const LauncherIconGuard({super.key, required this.auth, required this.child});
  @override
  State<LauncherIconGuard> createState() => _LauncherIconGuardState();
}

class _LauncherIconGuardState extends State<LauncherIconGuard>
    with WidgetsBindingObserver {
  final service = LauncherIconService();
  bool running = false;
  String? identity;
  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addObserver(this);
    widget.auth.addListener(changed);
    WidgetsBinding.instance.addPostFrameCallback((_) => changed());
  }

  void changed() {
    final next = widget.auth.session?.membershipId;
    if (identity == next) return;
    identity = next;
    unawaited(check());
  }

  Future<void> check() async {
    if (running ||
        !mounted ||
        WidgetsBinding.instance.lifecycleState != AppLifecycleState.resumed) {
      return;
    }
    running = true;
    try {
      await service.reconcile(widget.auth);
    } finally {
      running = false;
    }
  }

  @override
  void didChangeAppLifecycleState(AppLifecycleState state) {
    if (state == AppLifecycleState.resumed) unawaited(check());
  }

  @override
  void dispose() {
    widget.auth.removeListener(changed);
    WidgetsBinding.instance.removeObserver(this);
    super.dispose();
  }

  @override
  Widget build(BuildContext context) => widget.child;
}

class LauncherIconPage extends StatefulWidget {
  final AuthController auth;
  final LauncherIconService? service;
  const LauncherIconPage({super.key, required this.auth, this.service});
  @override
  State<LauncherIconPage> createState() => _LauncherIconPageState();
}

class _LauncherIconPageState extends State<LauncherIconPage> {
  late final service = widget.service ?? LauncherIconService();
  Map<String, dynamic>? native, choice;
  String? error, message;
  bool busy = false;
  @override
  void initState() {
    super.initState();
    unawaited(load());
  }

  Future<void> load() async {
    setState(() {
      error = null;
    });
    try {
      final value = await service.state();
      final info = value['supported'] == true
          ? await service.choice(widget.auth, value)
          : <String, dynamic>{};
      if (mounted) {
        setState(() {
          native = value;
          choice = info;
        });
      }
    } catch (_) {
      if (mounted) {
        setState(
          () => error =
              'تعذر التحقق من إتاحة الأيقونة. أعد المحاولة عند الاتصال.',
        );
      }
    }
  }

  bool available(Map<String, dynamic> info, Map<String, dynamic> installed) =>
      info['enabled'] == true &&
      info['key'] != null &&
      (installed['keys'] as List? ?? []).contains(info['key']) &&
      (installed['build'] as num? ?? 0) >= (info['minimumBuild'] as num? ?? 1);
  Future<void> change(bool academy) async {
    if (busy) return;
    setState(() {
      busy = true;
      error = null;
      message = null;
    });
    try {
      final installed = await service.state();
      final info = academy
          ? await service.choice(widget.auth, installed)
          : <String, dynamic>{};
      if (academy && !available(info, installed)) {
        throw StateError('unavailable');
      }
      await service.set(
        academy ? info['key'] as String : null,
        academy ? info['academyId'] as String : null,
      );
      final updated = await service.state();
      if (mounted) {
        setState(() {
          native = updated;
          message = updated['pending'] == true
              ? 'حُفظ اختيارك. ارجع إلى شاشة الجهاز لتطبيقه؛ قد يحتاج المشغّل وقتًا للتحديث.'
              : 'تم تحديث أيقونة التطبيق على هذا الجهاز.';
        });
      }
    } catch (_) {
      if (mounted) {
        setState(
          () => error = 'تعذر تطبيق الأيقونة. حدّث الإتاحة وحاول مجددًا.',
        );
      }
    } finally {
      if (mounted) setState(() => busy = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    final info = choice ?? {}, installed = native ?? {};
    final supported = installed['supported'] == true;
    final canChoose = supported && available(info, installed);
    return GuardianPage(
      title: 'أيقونة التطبيق على الجهاز',
      child: gList([
        GCard(
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              const Text(
                'أيقونة واحدة لاختيارك',
                style: TextStyle(fontSize: 22, fontWeight: FontWeight.bold),
              ),
              const SizedBox(height: 12),
              const Text(
                'اختيار لهذا الجهاز فقط؛ يبقى اسم التطبيق iAcademy One.',
              ),
              const SizedBox(height: 16),
              Text(
                native == null
                    ? 'جارٍ التحقق من البناء والإتاحة…'
                    : !supported
                    ? 'هذا البناء أو الجهاز لا يدعم تغيير الأيقونة.'
                    : canChoose
                    ? 'شعار الأكاديمية متاح في هذا البناء.'
                    : info['enabled'] == true
                    ? 'رسم الأكاديمية ينتظر إضافته إلى تحديث التطبيق.'
                    : 'أيقونة iAcademy One القياسية متاحة.',
              ),
              if (canChoose) ...[
                const SizedBox(height: 16),
                FilledButton(
                  onPressed: busy ? null : () => change(true),
                  child: const Text('استخدام شعار أكاديميتي كأيقونة التطبيق'),
                ),
              ],
              if (supported)
                TextButton(
                  onPressed: busy ? null : () => change(false),
                  child: const Text('استخدام أيقونة iAcademy One القياسية'),
                ),
              const ExpansionTile(
                title: Text('عن تبديل الأيقونة'),
                children: [
                  Padding(
                    padding: EdgeInsets.all(12),
                    child: Text(
                      'تختار أيقونة واحدة لهذا التطبيق. لا تتبدل تلقائيًا عند تغيير الدور أو الأكاديمية. الرسم الجديد يحتاج تحديث التطبيق؛ وقد يعرض النظام تأكيدًا، وتختلف سرعة تحديث مشغّلات Android.',
                    ),
                  ),
                ],
              ),
              if (message != null)
                Padding(
                  padding: const EdgeInsets.only(top: 12),
                  child: Text(message!, semanticsLabel: message),
                ),
            ],
          ),
        ),
        if (error != null)
          GCard(
            child: Column(
              children: [
                Text(error!),
                TextButton(
                  onPressed: busy ? null : load,
                  child: const Text('إعادة المحاولة'),
                ),
              ],
            ),
          ),
      ]),
    );
  }
}
