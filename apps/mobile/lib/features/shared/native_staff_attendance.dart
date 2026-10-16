import 'dart:async';
import 'dart:convert';

import 'package:flutter/material.dart';
import 'package:geolocator/geolocator.dart';

import '../../core/api/api_client.dart';
import '../../core/auth/auth_controller.dart';
import '../../core/auth/staff_passkey_provider.dart';
import '../../core/theme/ui_text.dart';
import '../guardian/guardian_ui.dart';
import 'staff_attendance_exceptions.dart';
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

// Own operations on the existing server path. No source writes or local biometric fallback.
class NativeStaffAttendanceFlow extends ChangeNotifier {
  static const root = '/api/v1/staff/passkeys/attendance';
  final AuthController auth;
  final StaffPasskeyProvider provider;
  final StaffLocationSource location;
  late final String? identity = staffContext(auth);
  Json? data;
  bool busy = false, disposed = false;
  String error = '', message = '';
  String? _pending;
  bool get hasPending => _pending != null;
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
  }) : provider = provider ?? IosStaffPasskeyProvider(),
       location = location ?? PlatformStaffLocation() {
    identity;
    auth.addListener(changedContext);
  }
  void changedContext() {
    if (!current) {
      data = null;
      _pending = null;
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

  Future<void> load() async {
    if (busy || !current) return;
    busy = true;
    error = '';
    emit();
    try {
      final value = obj(await auth.request('GET', root));
      check();
      data = value;
    } catch (_) {
      if (current) {
        data = null;
        error = 'تعذر قراءة حالة حضورك؛ أعد المحاولة.';
      }
    } finally {
      busy = false;
      emit();
    }
  }

  Future<void> submit(String branch, String operation) async {
    if (busy || !current) return;
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
            !rows(data?['branches'] ?? const [])
                .any((b) => b['id'] == branch && b['configured'] == true))) {
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
        _pending = jsonEncode({
          'challengeId': options['challengeId'],
          'intent': intent,
          'credential': credential,
        });
      }
      final body = jsonDecode(_pending!) as Json;
      final saved = obj(
        await auth.request('POST', '$root/complete', body: body),
      );
      check();
      if (saved['attendanceRecorded'] != true ||
          saved['operation'] != obj(body['intent'])['operation'] ||
          !validId(saved['attendanceId']) ||
          !validId(saved['workPeriodId']) ||
          saved['trustSource'] != 'ServerVerified' ||
          DateTime.tryParse(textOf(saved['confirmedAtUtc'])) == null) {
        throw const FormatException();
      }
      _pending = null;
      message = saved['operation'] == 'CheckIn'
          ? 'تم تسجيل حضورك بوقت الخادم المؤكد.'
          : 'تم تسجيل انصرافك للفترة نفسها بوقت الخادم المؤكد.';
      try {
        final latest = obj(await auth.request('GET', root));
        check();
        data = latest;
      } catch (_) {
        if (current) {
          data = null;
          error = 'العملية مؤكدة، لكن تعذر تحديث العرض؛ حدّث الحالة قبل عملية جديدة.';
        }
      }
    } on ApiFailure catch (f) {
      if (current) {
        if (f.status >= 400 && f.status < 500) {
          _pending = null;
          error = f.status == 409
              ? f.message
              : 'لم تقبل العملية؛ حدّث الحالة أو اطلب مراجعة الإدارة.';
        } else {
          error = hasPending
              ? 'لم نتأكد من نتيجة الإرسال؛ أعد إرسال الطلب نفسه أو حدّث الحالة، دون افتراض نجاحه.'
              : 'تعذر الاتصال؛ لم يعتمد حضور دون اتصال.';
        }
      }
    } on StaffPasskeyProblem catch (f) {
      if (current) error = f.message;
    } catch (_) {
      if (current) {
        error = hasPending
            ? 'لم نتأكد من رد الخادم؛ احتفظ بالطلب نفسه وأعد المحاولة.'
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
    unawaited(provider.cancel());
    super.dispose();
  }
}

class NativeStaffAttendancePage extends StatefulWidget {
  final AuthController auth;
  final StaffPasskeyProvider? provider;
  final StaffLocationSource? location;
  const NativeStaffAttendancePage({
    super.key,
    required this.auth,
    this.provider,
    this.location,
  });
  @override
  State<NativeStaffAttendancePage> createState() =>
      _NativeStaffAttendancePageState();
}

class _NativeStaffAttendancePageState extends State<NativeStaffAttendancePage> {
  late final flow = NativeStaffAttendanceFlow(
    widget.auth,
    provider: widget.provider,
    location: widget.location,
  )..load();
  String? branch;
  @override
  void dispose() {
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
        return gList([
          const GCard(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Heading('حضور وظيفي، بوقت مؤكد'),
                UiText(
                  'الموقع عند الضغط فقط، ثم توقيع يتحقق منه الخادم. لا نخزن بصمات أو صور وجوه؛ قد يستخدم الجهاز بصمة أو رمزًا. لا يضمن التوقيع سلامة GPS أو هاتفًا ماديًا وحيدًا.',
                ),
                UiText(
                  'لا يسجل حضور اللاعبين ولا ينشئ خصمًا أو راتبًا تلقائيًا.',
                ),
              ],
            ),
          ),
          if (!flow.provider.enabled ||
              d?['nativeIosOriginConfigured'] != true ||
              d?['rpId'] != flow.provider.rpId)
            const EmptyMessage(
              'يلزم إعداد نطاق HTTPS مرتبط بهذا التطبيق ووسيلة تحقق. الإعداد المحلي الحالي لا يكفي للتوقيع الأصلي؛ المراجعة الإدارية متاحة.',
            ),
          if (d?['profileMissing'] == true)
            const EmptyMessage('يلزم ربط ملف موظف فعال بحسابك.'),
          if (d?['configured'] == true && d?['hasCredential'] != true)
            const EmptyMessage(
              'أضف وسيلة تحقق من صفحة وسائل التحقق الخاصة بي بعد إكمال الإعداد.',
            ),
          if (sites.isNotEmpty)
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
                          enabled: s['configured'] == true,
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
                  if (selected != null)
                    UiText(
                      'النطاق ${textOf(selected['radiusMeters'])} مترًا · الدقة حتى ${textOf(selected['maxAccuracyMeters'])} مترًا · قراءة خلال ${textOf(selected['maxAgeSeconds'])} ثانية',
                    ),
                  if (d?['requiresSourceReview'] == true)
                    const UiText(
                      'الفترة المفتوحة تحتاج مراجعة المصدر؛ لا نسجل انصرافًا تخمينيًا.',
                    ),
                ],
              ),
            ),
          if (flow.busy) const Center(child: CircularProgressIndicator()),
          if (flow.message.isNotEmpty) UiText(flow.message),
          if (flow.error.isNotEmpty) UiText(flow.error),
          if (flow.hasPending)
            FilledButton(
              onPressed: flow.busy ? null : () => flow.submit('', ''),
              child: const UiText('إعادة إرسال الطلب نفسه'),
            )
          else if (flow.enabled)
            Wrap(
              spacing: 12,
              runSpacing: 12,
              children: [
                FilledButton.icon(
                  onPressed:
                      flow.busy ||
                          selected?['configured'] != true ||
                          d?['hasOpenAttendance'] == true
                      ? null
                      : () => flow.submit(branch!, 'CheckIn'),
                  icon: const Icon(Icons.fingerprint),
                  label: const UiText('تسجيل حضوري'),
                ),
                OutlinedButton.icon(
                  onPressed:
                      flow.busy ||
                          selected?['configured'] != true ||
                          d?['hasOpenAttendance'] != true ||
                          d?['requiresSourceReview'] == true
                      ? null
                      : () => flow.submit(branch!, 'CheckOut'),
                  icon: const Icon(Icons.logout),
                  label: const UiText('تسجيل انصرافي'),
                ),
              ],
            ),
          TextButton.icon(
            onPressed: flow.busy ? null : flow.load,
            icon: const Icon(Icons.refresh),
            label: const UiText('تحديث حالة الحضور'),
          ),
          GLink(
            'فترات عملي',
            Icons.history,
            () => openPage(context, StaffWorkPeriodsPage(auth: widget.auth)),
          ),
          GLink(
            'طلب إثبات حضور للمراجعة',
            Icons.fact_check_outlined,
            () => openPage(
              context,
              StaffAttendanceExceptionPage(auth: widget.auth),
            ),
          ),
        ]);
      },
    ),
  );
}
