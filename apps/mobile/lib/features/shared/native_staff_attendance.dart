import 'dart:async';
import 'dart:convert';

import 'package:flutter/material.dart';
import 'package:geolocator/geolocator.dart';

import '../../core/api/api_client.dart';
import '../../core/auth/auth_controller.dart';
import '../../core/auth/staff_passkey_provider.dart';
import '../../core/storage/refresh_store.dart';
import '../../core/theme/ui_text.dart';
import '../guardian/guardian_ui.dart';
import 'staff_attendance_exceptions.dart';
import 'staff_passkeys.dart' show StaffPasskeyPage;
import 'staff_work_periods.dart';

class StaffLocationSample {
  final double latitude, longitude, accuracy;
  final DateTime capturedAt;
  const StaffLocationSample(
    this.latitude,
    this.longitude,
    this.accuracy,
    this.capturedAt,
  );
}

abstract class StaffLocationSource {
  Future<StaffLocationSample> capture();
}

class PlatformStaffLocation implements StaffLocationSource {
  @override
  Future<StaffLocationSample> capture() async {
    if (!await Geolocator.isLocationServiceEnabled()) {
      throw const StaffPasskeyProblem(
        'فعّل الموقع عند الاستخدام، أو اطلب إثبات حضور للمراجعة.',
      );
    }
    var permission = await Geolocator.checkPermission();
    if (permission == LocationPermission.denied) {
      permission = await Geolocator.requestPermission();
    }
    if (permission == LocationPermission.denied ||
        permission == LocationPermission.deniedForever) {
      throw const StaffPasskeyProblem(
        'تعذر إذن الموقع؛ المراجعة الإدارية متاحة.',
      );
    }
    final p = await Geolocator.getCurrentPosition(
      locationSettings: const LocationSettings(
        accuracy: LocationAccuracy.best,
        timeLimit: Duration(seconds: 20),
      ),
    );
    return StaffLocationSample(
      p.latitude,
      p.longitude,
      p.accuracy,
      p.timestamp.toUtc(),
    );
  }
}

typedef _AttendanceAttempt = ({String challengeId, String operation});

RefreshStore? _attendanceStore(AuthController auth) {
  final transport = auth.transport;
  final membership = auth.session?.selected;
  if (transport is! HttpApiTransport || membership == null) return null;
  final config = transport.config;
  return SecureRefreshStore(
    'academy.staff-clock.${config.environment}.${config.api.origin}.'
    '${membership.academyId}.${membership.id}',
  );
}

class NativeStaffAttendanceFlow extends ChangeNotifier {
  static const root = '/api/v1/staff/passkeys/attendance';
  final AuthController auth;
  final StaffPasskeyProvider provider;
  final StaffLocationSource location;
  final RefreshStore? _attemptStore;
  late final String? identity = staffContext(auth);
  Json? data;
  bool busy = false, disposed = false, waitingForResult = false;
  String error = '', message = '';
  String? _pending;
  _AttendanceAttempt? _restored;
  bool get canRetry => _pending != null;
  bool get hasPending => canRetry || _restored != null;
  String? get restoredOperation => _restored?.operation;
  bool get current =>
      !disposed &&
      identity != null &&
      identity == staffContext(auth) &&
      [
        'Coach',
        'AcademyAdmin',
        'AcademyOwner',
      ].contains(auth.session?.selected?.role);
  bool get enabled =>
      current &&
      provider.enabled &&
      data?['configured'] == true &&
      data?['nativeIosOriginConfigured'] == true &&
      data?['rpId'] == provider.rpId &&
      data?['profileMissing'] != true &&
      data?['hasCredential'] == true;
  NativeStaffAttendanceFlow(
    this.auth, {
    StaffPasskeyProvider? provider,
    StaffLocationSource? location,
    RefreshStore? attemptStore,
  }) : provider = provider ?? IosStaffPasskeyProvider(),
       location = location ?? PlatformStaffLocation(),
       _attemptStore = attemptStore ?? _attendanceStore(auth) {
    identity;
    auth.addListener(changedContext);
  }
  void changedContext() {
    if (!current) {
      data = null;
      _pending = null;
      _restored = null;
      waitingForResult = false;
      error = '';
      message = '';
      unawaited(provider.cancel());
      emit();
    }
  }

  void emit() {
    if (!disposed) notifyListeners();
  }

  void check() {
    if (!current) {
      throw const StaffPasskeyProblem(
        'تغير الحساب أو نطاق الأكاديمية؛ أعد فتح الشاشة.',
      );
    }
  }

  Future<void> _readState() async {
    try {
      final value = obj(await auth.request('GET', root));
      check();
      data = value;
    } catch (_) {
      if (current) {
        data = null;
        if (error.isEmpty) {
          error = message.isEmpty
              ? 'تعذر قراءة حالة حضورك؛ أعد المحاولة.'
              : 'العملية مؤكدة. تعذر تحديث الحالة؛ أعد القراءة قبل التسجيل.';
        }
      }
    }
  }

  Future<_AttendanceAttempt?> _readAttempt() async {
    try {
      final value = await _attemptStore?.read();
      if (value == null) return null;
      final reference = obj(jsonDecode(value));
      final challengeId = reference['challengeId'];
      final operation = reference['operation'];
      if (validId(challengeId) &&
          (operation == 'CheckIn' || operation == 'CheckOut')) {
        return (
          challengeId: challengeId as String,
          operation: operation as String,
        );
      }
    } catch (_) {
      // The device store can be unavailable while the session is still usable.
    }
    return null;
  }

  Future<void> _remember(_AttendanceAttempt attempt) async {
    if (!validId(attempt.challengeId)) return;
    try {
      await _attemptStore?.write(
        jsonEncode({
          'challengeId': attempt.challengeId,
          'operation': attempt.operation,
        }),
      );
    } catch (_) {
      // Keep the signed request in memory so the open screen can still retry.
    }
  }

  Future<void> _forget(_AttendanceAttempt attempt) async {
    if ((await _readAttempt())?.challengeId != attempt.challengeId ||
        !current) {
      return;
    }
    try {
      await _attemptStore?.clear();
    } catch (_) {
      // A later screen can re-read the same result from the server.
    }
  }

  _AttendanceAttempt _reference(Json body) => (
    challengeId: body['challengeId'] as String,
    operation: obj(body['intent'])['operation'] as String,
  );

  Future<void> _confirm(Json saved, _AttendanceAttempt attempt) async {
    if (saved['attendanceRecorded'] != true ||
        saved['operation'] != attempt.operation ||
        !validId(saved['attendanceId']) ||
        !validId(saved['workPeriodId']) ||
        saved['trustSource'] != 'ServerVerified' ||
        DateTime.tryParse(textOf(saved['confirmedAtUtc'])) == null) {
      throw const FormatException();
    }
    await _forget(attempt);
    check();
    _pending = null;
    _restored = null;
    message = attempt.operation == 'CheckIn'
        ? 'تم تسجيل حضورك بوقت الخادم المؤكد.'
        : 'تم تسجيل انصرافك للفترة نفسها بوقت الخادم المؤكد.';
  }

  Future<void> load() async {
    if (busy || !current) return;
    busy = true;
    error = '';
    waitingForResult = false;
    emit();
    try {
      if (!hasPending) {
        final reference = await _readAttempt();
        if (!current) return;
        _restored = reference;
      }
      final pending = _pending;
      final attempt = pending == null
          ? _restored
          : _reference(obj(jsonDecode(pending)));
      if (attempt != null &&
          await _verify(attempt, restored: pending == null)) {
        return;
      }
      await _readState();
    } finally {
      busy = false;
      emit();
    }
  }

  Future<bool> _verify(
    _AttendanceAttempt attempt, {
    required bool restored,
  }) async {
    final id = Uri.encodeComponent(attempt.challengeId);
    final path = '$root/attempts/$id${restored ? '?includeState=true' : ''}';
    try {
      final result = obj(await auth.request('GET', path));
      check();
      if (restored && result['attendanceRecorded'] == false) {
        if (result['attemptState'] == 'Pending') {
          waitingForResult = true;
          return true;
        }
        if (result['attemptState'] == 'Closed') {
          await _finishWithoutRecording(attempt, 'انتهت المحاولة دون تسجيل.');
          return true;
        }
      }
      await _confirm(result, attempt);
    } on ApiFailure catch (failure) {
      if (!current) return true;
      if (failure.status == 401 || failure.status == 403) {
        data = null;
        _pending = null;
        _restored = null;
        error = 'تعذر التحقق من المحاولة في الجلسة الحالية.';
        return true;
      }
      if (failure.status == 404 && restored) {
        await _finishWithoutRecording(attempt, 'لم تعد هذه المحاولة متاحة.');
        return true;
      }
      error = failure.status == 404
          ? 'لم تُحفظ نتيجة مؤكدة لهذه المحاولة بعد. يمكنك إعادة إرسال الطلب نفسه.'
          : 'تعذر التحقق من نتيجة التسجيل. أعد المحاولة.';
    } catch (_) {
      if (!current) return true;
      error = 'تعذر التحقق من نتيجة التسجيل. أعد المحاولة.';
    }
    return false;
  }

  Future<void> _finishWithoutRecording(
    _AttendanceAttempt attempt,
    String notice,
  ) async {
    await _readState();
    if (!current || data == null) return;
    await _forget(attempt);
    if (!current) return;
    _restored = null;
    message = notice;
  }

  Future<void> submit(String branch, String operation) async {
    if (busy || !current || _restored != null) return;
    if (_pending == null &&
        ((operation == 'CheckIn' && data?['hasOpenAttendance'] == true) ||
            (operation == 'CheckOut' &&
                (data?['hasOpenAttendance'] != true ||
                    data?['requiresSourceReview'] == true)))) {
      return;
    }
    if (_pending == null &&
        (!enabled ||
            !['CheckIn', 'CheckOut'].contains(operation) ||
            !rows(data?['branches'] ?? const []).any(
              (b) =>
                  b['id'] == branch &&
                  b['configured'] == true &&
                  b['enabled'] != false,
            ))) {
      return;
    }
    busy = true;
    message = '';
    error = '';
    emit();
    try {
      if (_pending == null) {
        final sample = await location.capture();
        check();
        final intent = <String, dynamic>{
          'branchId': branch,
          'operation': operation,
          'latitude': sample.latitude,
          'longitude': sample.longitude,
          'accuracyMeters': sample.accuracy,
          'capturedAtUtc': sample.capturedAt.toUtc().toIso8601String(),
        };
        final options = obj(
          await auth.request('POST', '$root/options', body: intent),
        );
        check();
        if (options['purpose'] != operation ||
            options['challengeId'] is! String ||
            options['options'] is! Map) {
          throw const FormatException();
        }
        final credential = await provider.assertCredential(
          obj(options['options']),
        );
        check();
        final body = <String, dynamic>{
          'challengeId': options['challengeId'],
          'intent': intent,
          'credential': credential,
        };
        _pending = jsonEncode(body);
        await _remember(_reference(body));
        check();
      }
      final body = jsonDecode(_pending!) as Json;
      final attempt = _reference(body);
      final saved = obj(
        await auth.request('POST', '$root/complete', body: body),
      );
      check();
      await _confirm(saved, attempt);
      await _readState();
    } on ApiFailure catch (f) {
      if (current) {
        if (f.status >= 400 &&
            f.status < 500 &&
            f.status != 429 &&
            f.status != 408) {
          final pending = _pending;
          if (pending != null && f.status != 401 && f.status != 403) {
            await _forget(_reference(obj(jsonDecode(pending))));
            if (!current) return;
          }
          _pending = null;
          error = f.status == 409
              ? f.message
              : 'لم تقبل العملية؛ حدّث الحالة أو اطلب مراجعة الإدارة.';
          await _readState();
        } else {
          error = hasPending
              ? 'تعذر تأكيد التسجيل. تحقّق من النتيجة قبل بدء عملية أخرى.'
              : 'تعذر الاتصال؛ لم يعتمد حضور دون اتصال.';
        }
      }
    } on StaffPasskeyProblem catch (f) {
      if (current) error = f.message;
    } catch (_) {
      if (current) {
        error = hasPending
            ? 'تعذر تأكيد التسجيل. تحقّق من النتيجة قبل بدء عملية أخرى.'
            : 'تعذر تأكيد الموقع أو التوقيع؛ اطلب مراجعة عند الحاجة.';
      }
    } finally {
      busy = false;
      emit();
    }
  }

  static bool validId(Object? id) =>
      id is String &&
      RegExp(r'^[0-9a-fA-F]{8}-(?:[0-9a-fA-F]{4}-){3}[0-9a-fA-F]{12}$')
          .hasMatch(id);

  @override
  void dispose() {
    auth.removeListener(changedContext);
    disposed = true;
    data = null;
    _pending = null;
    _restored = null;
    unawaited(provider.cancel());
    super.dispose();
  }
}

class NativeStaffAttendancePage extends StatefulWidget {
  final AuthController auth;
  final StaffPasskeyProvider? provider;
  final StaffLocationSource? location;
  final RefreshStore? attemptStore;
  const NativeStaffAttendancePage({
    super.key,
    required this.auth,
    this.provider,
    this.location,
    this.attemptStore,
  });
  @override
  State<NativeStaffAttendancePage> createState() =>
      _NativeStaffAttendancePageState();
}

class _NativeStaffAttendancePageState extends State<NativeStaffAttendancePage>
    with WidgetsBindingObserver {
  late final flow = NativeStaffAttendanceFlow(
    widget.auth,
    provider: widget.provider,
    location: widget.location,
    attemptStore: widget.attemptStore,
  );
  String? branch;

  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addObserver(this);
    flow.addListener(reconcileBranch);
    unawaited(flow.load());
  }

  void reconcileBranch() {
    final sites = rows(flow.data?['branches'] ?? const []);
    if (!sites.any((site) => site['id'] == branch && ready(site))) {
      branch = null;
    }
  }

  static bool ready(Json site) =>
      site['configured'] == true && site['enabled'] != false;

  @override
  void didChangeAppLifecycleState(AppLifecycleState state) {
    if (state == AppLifecycleState.resumed) {
      unawaited(flow.load());
    }
  }

  Future<void> openRelated(Widget page) async {
    await openPage(context, page);
    if (mounted) await flow.load();
  }

  @override
  void dispose() {
    WidgetsBinding.instance.removeObserver(this);
    flow.removeListener(reconcileBranch);
    flow.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) => GuardianPage(
    title: 'حضوري وانصرافي',
    child: ListenableBuilder(
      listenable: flow,
      builder: (context, _) {
        if (!flow.current) return const SizedBox.shrink();
        final d = flow.data,
            sites = rows(d?['branches'] ?? const []),
            selected = sites.where((s) => s['id'] == branch).firstOrNull;
        final open = d?['hasOpenAttendance'] == true;
        final branchReady = selected != null && ready(selected);
        final hasReadyBranch = sites.any(ready);
        return gList([
          if (flow.busy)
            const Center(
              child: CircularProgressIndicator(
                semanticsLabel: 'جارٍ التحقق من حالة الدوام',
              ),
            ),
          if (flow.message.isNotEmpty)
            Semantics(liveRegion: true, child: UiText(flow.message)),
          if (flow.error.isNotEmpty)
            Semantics(liveRegion: true, child: UiText(flow.error)),
          if (!flow.hasPending) ...[
            if (d?['profileMissing'] == true)
              const EmptyMessage('يلزم ربط ملف موظف فعال بحسابك.')
            else if (d != null && !hasReadyBranch)
              EmptyMessage(
                sites.any((site) => site['enabled'] == true)
                    ? 'يلزم إعداد موقع الفرع قبل التسجيل. يمكنك طلب المراجعة.'
                    : open
                    ? 'التسجيل الذاتي موقوف وفترة دوامك ما زالت مفتوحة. اطلب مراجعة الإدارة لإثبات الانصراف.'
                    : 'التسجيل الذاتي موقوف للفروع المتاحة. يمكنك طلب المراجعة.',
              )
            else if (d != null &&
                (!flow.provider.enabled ||
                    d['configured'] != true ||
                    d['nativeIosOriginConfigured'] != true ||
                    d['rpId'] != flow.provider.rpId))
              const EmptyMessage(
                'إعداد الحضور الآمن غير مكتمل. يمكنك طلب إثبات حضور للمراجعة.',
              )
            else if (d?['configured'] == true &&
                d?['hasCredential'] != true) ...[
              const UiText('أضف وسيلة تحقق قبل تسجيل الحضور أو الانصراف.'),
              GLink('وسائل التحقق', Icons.verified_user_outlined, () async {
                await openPage(
                  context,
                  StaffPasskeyPage(
                    auth: widget.auth,
                    provider: widget.provider,
                  ),
                );
                if (mounted) await flow.load();
              }),
            ],
          ],
          if (flow.enabled && hasReadyBranch && flow.restoredOperation == null)
            GCard(
              child: Column(
                children: [
                  DropdownButtonFormField<String>(
                    isExpanded: true,
                    itemHeight: null,
                    key: ValueKey(
                      '${branch ?? ''}:${sites.map((s) => s['id']).join(',')}',
                    ),
                    initialValue: selected == null ? null : branch,
                    decoration: const InputDecoration(labelText: 'فرع الحضور'),
                    items: [
                      for (final s in sites)
                        DropdownMenuItem(
                          value: textOf(s['id']),
                          enabled: ready(s),
                          child: UiText(
                            '${textOf(s['name'])}${s['enabled'] == false
                                ? ' — موقوف'
                                : s['configured'] != true
                                ? ' — يلزم إعداد الموقع'
                                : ''}',
                          ),
                        ),
                    ],
                    onChanged: flow.busy || flow.hasPending
                        ? null
                        : (v) => setState(() => branch = v),
                  ),
                  if (branchReady) ...[
                    UiText(
                      'النطاق ${textOf(selected['radiusMeters'])} مترًا · الدقة حتى ${textOf(selected['maxAccuracyMeters'])} مترًا · قراءة خلال ${textOf(selected['maxAgeSeconds'])} ثانية',
                    ),
                    if (selected['attendanceRequired'] == true)
                      const UiText('التسجيل مطلوب لفئتك في هذا الفرع.'),
                    if (selected['attendanceRequired'] == false)
                      const UiText('التسجيل اختياري لفئتك في هذا الفرع.'),
                  ],
                  if (d?['requiresSourceReview'] == true)
                    const UiText(
                      'راجع الإدارة لتأكيد فترة الدوام المفتوحة قبل الانصراف.',
                    ),
                ],
              ),
            ),
          if (flow.hasPending) ...[
            if (flow.restoredOperation case final String operation)
              GCard(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    UiText(
                      operation == 'CheckIn'
                          ? 'محاولة الحضور السابقة'
                          : 'محاولة الانصراف السابقة',
                    ),
                    if (flow.waitingForResult)
                      const UiText(
                        'لم يظهر تسجيل مؤكد بعد. تحقّق مرة أخرى بعد انتهاء المحاولة.',
                      ),
                  ],
                ),
              ),
            FilledButton.icon(
              onPressed: flow.busy ? null : flow.load,
              icon: const Icon(Icons.fact_check_outlined),
              label: const UiText('التحقق من نتيجة التسجيل'),
            ),
            if (flow.canRetry)
              TextButton(
                onPressed: flow.busy ? null : () => flow.submit('', ''),
                child: const UiText('إعادة إرسال الطلب نفسه'),
              ),
          ] else ...[
            if (flow.enabled && hasReadyBranch)
              FilledButton.icon(
                onPressed:
                    flow.busy ||
                        !branchReady ||
                        (open && d?['requiresSourceReview'] == true)
                    ? null
                    : () => flow.submit(branch!, open ? 'CheckOut' : 'CheckIn'),
                icon: Icon(open ? Icons.logout : Icons.verified_user_outlined),
                label: UiText(open ? 'تسجيل انصرافي' : 'تسجيل حضوري'),
              ),
            TextButton.icon(
              onPressed: flow.busy ? null : flow.load,
              icon: const Icon(Icons.refresh),
              label: const UiText('تحديث حالة الحضور'),
            ),
          ],
          GLink(
            'سجل دوامي',
            Icons.history,
            () => openRelated(StaffWorkPeriodsPage(auth: widget.auth)),
          ),
          GLink(
            'طلب إثبات حضور للمراجعة',
            Icons.fact_check_outlined,
            () => openRelated(StaffAttendanceExceptionPage(auth: widget.auth)),
          ),
          const ExpansionTile(
            title: UiText('طريقة التسجيل والخصوصية'),
            childrenPadding: EdgeInsets.fromLTRB(16, 0, 16, 16),
            children: [
              UiText(
                'يُطلب الموقع عند التسجيل، ثم يتحقق الخادم من توقيع الجهاز. قد يستخدم الجهاز بصمة أو رمزًا. التوقيع وحده لا يضمن سلامة الموقع أو أن الوسيلة محصورة في هاتف واحد.',
              ),
              UiText('الدوام الوظيفي مستقل عن حضور اللاعبين والرواتب.'),
            ],
          ),
        ]);
      },
    ),
  );
}
