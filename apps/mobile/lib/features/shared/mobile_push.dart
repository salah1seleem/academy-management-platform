import 'package:flutter_secure_storage/flutter_secure_storage.dart';

import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter/services.dart';

import '../../core/auth/auth_controller.dart';
import '../../core/theme/ui_text.dart';
import '../guardian/guardian_ui.dart';
import 'matches.dart';
import 'audience_communication.dart';

final _intents = Expando<ValueNotifier<Map<String, String>?>>('push intent');
ValueNotifier<Map<String, String>?> pushIntent(AuthController auth) =>
    _intents[auth] ??= ValueNotifier(null);

class MobilePushService {
  static const preferences = FlutterSecureStorage();
  static String preferenceKey(AuthController auth) =>
      'academy.push.disabled.${auth.session?.userId ?? 'signed-out'}';
  Future<bool> disabled(AuthController auth) async {
    try {
      return await preferences.read(key: preferenceKey(auth)) == 'yes';
    } catch (_) {
      return false;
    }
  }

  Future<void> setDisabled(AuthController auth, bool value) async {
    await preferences.write(
      key: preferenceKey(auth),
      value: value ? 'yes' : 'no',
    );
  }

  static const channel = MethodChannel('iacademy/mobile_push');
  Future<Map<String, dynamic>> state({bool request = false}) async {
    try {
      return Map<String, dynamic>.from(
        await channel.invokeMapMethod(request ? 'request' : 'state') ?? {},
      );
    } on MissingPluginException {
      return {'configured': false};
    }
  }

  Future<Map<String, dynamic>> register(
    AuthController auth, {
    bool request = false,
  }) async {
    final account = auth.session?.userId;
    if (await disabled(auth)) return {'permission': 'disabled'};
    final native = await state(request: request);
    if (account == null || auth.session?.userId != account) {
      return {'permission': 'changed'};
    }
    if (native['permission'] == 'granted' && native['token'] is String) {
      await auth.request(
        'PUT',
        '/api/v1/mobile-push/device',
        body: {'provider': native['provider'], 'token': native['token']},
      );
    } else if (native['permission'] == 'denied') {
      await auth.request('DELETE', '/api/v1/mobile-push/device');
    }
    return native;
  }
}

class MobilePushBridge extends StatefulWidget {
  const MobilePushBridge({super.key, required this.auth, required this.child});
  final AuthController auth;
  final Widget child;
  @override
  State<MobilePushBridge> createState() => _MobilePushBridgeState();
}

class _MobilePushBridgeState extends State<MobilePushBridge>
    with WidgetsBindingObserver {
  final service = MobilePushService();
  bool busy = false;
  String? account;
  Map<String, String>? pending;
  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addObserver(this);
    widget.auth.addListener(sync);
    MobilePushService.channel.setMethodCallHandler((call) async {
      if (call.method == 'intent' && call.arguments is Map) {
        accept(call.arguments as Map);
      }
      if (call.method == 'tokenChanged') unawaited(sync());
    });
    unawaited(takeIntent());
  }

  void accept(Map payload) {
    final notice = payload['noticeId'], academy = payload['academyId'];
    final uuid = RegExp(r'^[0-9a-fA-F-]{36}$');
    if (notice is String &&
        academy is String &&
        uuid.hasMatch(notice) &&
        uuid.hasMatch(academy)) {
      pending = {'noticeId': notice, 'academyId': academy};
      unawaited(sync());
    }
  }

  Future<void> takeIntent() async {
    try {
      final payload = await MobilePushService.channel.invokeMapMethod(
        'takeIntent',
      );
      if (payload != null) accept(payload);
    } catch (_) {
      /* No remote capability in this build. */
    }
    await sync();
  }

  Future<void> sync() async {
    if (!mounted || busy || widget.auth.busy) return;
    final auth = widget.auth;
    if (auth.session == null) {
      if (account != null) {
        pushIntent(auth).value = null;
        pending = null;
      }
      account = null;
      return;
    }
    busy = true;
    try {
      account = auth.session?.userId;
      if (pending != null) {
        final target = auth.session!.memberships
            .where(
              (m) =>
                  m.academyId == pending!['academyId'] && m.role == 'Guardian',
            )
            .firstOrNull;
        if (target != null) {
          if (auth.session!.membershipId != target.id) {
            await auth.selectRole(target.id);
          }
          if (auth.session?.membershipId == target.id) {
            pushIntent(auth).value = pending;
            pending = null;
          }
        } else {
          pending = null;
        }
      }
      if (auth.session?.selected != null) await service.register(auth);
    } catch (_) {
      /* Keep inbox accessible; retry on foreground/token change. */
    } finally {
      busy = false;
    }
  }

  @override
  void didChangeAppLifecycleState(AppLifecycleState state) {
    if (state == AppLifecycleState.resumed) unawaited(takeIntent());
  }

  @override
  void dispose() {
    widget.auth.removeListener(sync);
    WidgetsBinding.instance.removeObserver(this);
    MobilePushService.channel.setMethodCallHandler(null);
    super.dispose();
  }

  @override
  Widget build(BuildContext context) => widget.child;
}

class PushIntentGate extends StatelessWidget {
  const PushIntentGate({super.key, required this.auth, required this.child});
  final AuthController auth;
  final Widget child;
  @override
  Widget build(BuildContext context) => ValueListenableBuilder(
    valueListenable: pushIntent(auth),
    builder: (context, intent, _) =>
        intent == null ||
            intent['academyId'] != auth.session?.selected?.academyId
        ? child
        : GuardianPage(
            title: 'التنبيهات',
            child: Column(
              children: [
                TextButton(
                  onPressed: () => pushIntent(auth).value = null,
                  child: const UiText('العودة إلى الرئيسية'),
                ),
                Expanded(
                  child: RemoteBody(
                    auth: auth,
                    path: '/api/v1/mobile-push/notices/${intent['noticeId']}',
                    builder: (context, value) {
                      final notice = obj(value);
                      return gList([
                        GCard(child: UiText(textOf(notice['message']))),
                        if (notice['targetKind'] == 'GroupAnnouncement' &&
                            notice['targetId'] is String)
                          FilledButton(
                            onPressed: () => openPage(
                              context,
                              AudienceAnnouncementPage(
                                auth: auth,
                                id: notice['targetId'] as String,
                              ),
                            ),
                            child: const UiText('عرض تفاصيل الإشعار'),
                          ),
                        if (notice['matchId'] is String)
                          FilledButton(
                            onPressed: () => openPage(
                              context,
                              MatchSchedule(
                                auth: auth,
                                matchId: notice['matchId'] as String,
                              ),
                            ),
                            child: const UiText('عرض المباراة'),
                          ),
                      ]);
                    },
                  ),
                ),
              ],
            ),
          ),
  );
}

class MobilePushSettings extends StatefulWidget {
  const MobilePushSettings({super.key, required this.auth});
  final AuthController auth;
  @override
  State<MobilePushSettings> createState() => _MobilePushSettingsState();
}

class _MobilePushSettingsState extends State<MobilePushSettings> {
  String message = 'التنبيهات داخل التطبيق متاحة. يُطلب إذن الجهاز باختيارك.';
  bool busy = false;
  @override
  Widget build(BuildContext context) => GuardianPage(
    title: 'إشعارات الجهاز',
    child: gList([
      GCard(child: UiText(message)),
      FilledButton(
        onPressed: busy
            ? null
            : () async {
                setState(() => busy = true);
                try {
                  final status = obj(
                    await widget.auth.request(
                      'GET',
                      '/api/v1/mobile-push/status',
                    ),
                  );
                  final native = await MobilePushService().register(
                    widget.auth,
                    request: status['remoteConfigured'] == true,
                  );
                  if (mounted) {
                    setState(
                      () => message =
                          status['remoteConfigured'] != true ||
                              native['configured'] != true
                          ? 'إشعارات الجهاز غير مهيأة في هذه النسخة. تابع التنبيهات داخل التطبيق.'
                          : native['permission'] == 'denied'
                          ? 'إذن الإشعارات مغلق من إعدادات الجهاز.'
                          : native['token'] == null
                          ? 'جارٍ تسجيل الجهاز؛ أعد التحقق بعد اتصال الشبكة.'
                          : 'سُجل الجهاز لاستقبال التنبيهات. الوصول يعتمد على اتصال الجهاز وخدمة الإرسال.',
                    );
                  }
                } catch (_) {
                  if (mounted) {
                    setState(
                      () => message = 'تعذر تسجيل الجهاز؛ أعد المحاولة. التنبيهات داخل التطبيق متاحة.',
                    );
                  }
                } finally {
                  if (mounted) setState(() => busy = false);
                }
              },
        child: const UiText('تفعيل إشعارات الجهاز / إعادة التحقق'),
      ),
    ]),
  );
}

class MobilePushToggle extends StatefulWidget {
  const MobilePushToggle({super.key, required this.auth});
  final AuthController auth;
  @override
  State<MobilePushToggle> createState() => _MobilePushToggleState();
}

class _MobilePushToggleState extends State<MobilePushToggle> {
  bool enabled = false, busy = false;
  @override
  void initState() {
    super.initState();
    refresh();
  }

  Future<void> refresh() async {
    final service = MobilePushService();
    final off = await service.disabled(widget.auth);
    final state = await service.state();
    if (mounted) {
      setState(
        () => enabled =
            !off && state['permission'] == 'granted' && state['token'] != null,
      );
    }
  }

  Future<void> change(bool value) async {
    setState(() => busy = true);
    try {
      if (!value) {
        await MobilePushService().setDisabled(widget.auth, true);
        await widget.auth.request('DELETE', '/api/v1/mobile-push/device');
        if (mounted) setState(() => enabled = false);
      } else {
        await MobilePushService().setDisabled(widget.auth, false);
        final status = obj(
          await widget.auth.request('GET', '/api/v1/mobile-push/status'),
        );
        final state = await MobilePushService().register(
          widget.auth,
          request: status['remoteConfigured'] == true,
        );
        final ready =
            status['remoteConfigured'] == true &&
            state['permission'] == 'granted' &&
            state['token'] != null;
        if (mounted) {
          setState(() => enabled = ready);
          if (!ready) {
            ScaffoldMessenger.of(context).showSnackBar(
              const SnackBar(
                content: UiText(
                  'إشعارات الجهاز غير متاحة حاليًا. تنبيهات التطبيق متاحة من علامة الجرس.',
                ),
              ),
            );
          }
        }
      }
    } catch (_) {
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          const SnackBar(
            content: UiText('تعذر تحديث إشعارات الجهاز. أعد المحاولة.'),
          ),
        );
      }
    } finally {
      if (mounted) setState(() => busy = false);
    }
  }

  @override
  Widget build(BuildContext context) => SwitchListTile.adaptive(
    contentPadding: EdgeInsets.zero,
    secondary: const Icon(Icons.notifications_active_outlined),
    title: const UiText('إشعارات الجهاز'),
    value: enabled,
    onChanged: busy ? null : change,
  );
}
