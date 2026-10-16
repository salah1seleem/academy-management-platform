import 'package:flutter/material.dart';
import 'package:geolocator/geolocator.dart';
import 'package:local_auth/local_auth.dart';

import '../../core/api/api_client.dart';
import '../../core/auth/auth_controller.dart';
import '../../core/theme/ui_text.dart';
import '../guardian/guardian_ui.dart';
import '../shared/staff_attendance_exceptions.dart';

class CheckInFix {
  final double latitude, longitude, accuracyMeters;
  const CheckInFix(this.latitude, this.longitude, this.accuracyMeters);
}

abstract class CheckInDevice {
  Future<bool> confirmBiometric();
  Future<CheckInFix> currentLocation();
}

class NativeCheckInDevice implements CheckInDevice {
  @override
  Future<bool> confirmBiometric() async {
    final auth = LocalAuthentication();
    if ((await auth.getAvailableBiometrics()).isEmpty) {
      throw StateError(
        'لا توجد بصمة مفعّلة على هذا الجهاز. اطلب تسجيل الحضور من الإدارة.',
      );
    }
    return auth.authenticate(
      localizedReason: 'أكد هويتك لتسجيل حضورك في فرع الأكاديمية',
      biometricOnly: true,
    );
  }

  @override
  Future<CheckInFix> currentLocation() async {
    if (!await Geolocator.isLocationServiceEnabled()) {
      throw StateError('فعّل خدمات الموقع لتسجيل الحضور.');
    }
    var permission = await Geolocator.checkPermission();
    if (permission == LocationPermission.denied) {
      permission = await Geolocator.requestPermission();
    }
    if (permission == LocationPermission.denied ||
        permission == LocationPermission.deniedForever) {
      throw StateError(
        'امنح التطبيق إذن الموقع أثناء الاستخدام، أو اطلب تسجيل الحضور من الإدارة.',
      );
    }
    final position = await Geolocator.getCurrentPosition(
      locationSettings: const LocationSettings(
        accuracy: LocationAccuracy.best,
        timeLimit: Duration(seconds: 20),
      ),
    );
    return CheckInFix(position.latitude, position.longitude, position.accuracy);
  }
}

class DeviceCheckIn extends StatefulWidget {
  final AuthController auth;
  final CheckInDevice? device;
  const DeviceCheckIn({super.key, required this.auth, this.device});
  @override
  State<DeviceCheckIn> createState() => _DeviceCheckInState();
}

class _DeviceCheckInState extends State<DeviceCheckIn> {
  String? branchId;
  bool busy = false;
  String message = '';

  Future<void> submit() async {
    if (busy || branchId == null) return;
    setState(() {
      busy = true;
      message = '';
    });
    try {
      final device = widget.device ?? NativeCheckInDevice();
      if (!await device.confirmBiometric()) {
        throw StateError('لم تُؤكد البصمة؛ لم يُسجل الحضور.');
      }
      final fix = await device.currentLocation();
      final result = obj(
        await widget.auth.request(
          'POST',
          '/api/v1/staff/check-in/',
          body: {
            'branchId': branchId,
            'latitude': fix.latitude,
            'longitude': fix.longitude,
            'accuracyMeters': fix.accuracyMeters,
            'clientBiometricAcknowledged': true,
          },
        ),
      );
      if (mounted) {
        setState(
          () => message = result['replay'] == true
              ? 'حضورك مسجل بالفعل لهذا اليوم.'
              : 'تم تسجيل حضورك اليوم في نطاق الفرع.',
        );
      }
    } catch (error) {
      if (mounted) {
        setState(
          () => message = error is ApiFailure
              ? error.message
              : error is StateError
              ? error.message
              : 'تعذر التحقق من البصمة والموقع. حاول مرة أخرى أو راجع الإدارة.',
        );
      }
    } finally {
      if (mounted) setState(() => busy = false);
    }
  }

  @override
  Widget build(BuildContext context) => GuardianPage(
    title: 'حضور المدرب',
    child: Column(
      children: [
        Expanded(
          child: RemoteBody(
            auth: widget.auth,
            path: '/api/v1/staff/check-in/sites',
            builder: (context, value) {
              final sites = rows(value);
              return gList([
                const UiText(
                  'ميزة عرض تجريبية: تأكيد الجهاز المحلي ليس إثباتًا يتحقق منه الخادم. يمكن طلب مراجعة إدارية بدلًا منه.',
                ),
                const UiText(
                  'اختر فرعك ثم راجع نطاقه المحفوظ. التأكيد المحلي تجريبي فقط؛ قد يلزم توقيع خادمي أو مراجعة إدارية.',
                ),
                if (sites.isEmpty)
                  const UiText(
                    'لم يُضبط موقع حضور لفرع مجموعاتك. تواصل مع الإدارة.',
                  ),
                if (sites.isNotEmpty)
                  GCard(
                    child: Column(
                      children: [
                        DropdownButtonFormField<String>(
                          initialValue: branchId,
                          decoration: const InputDecoration(labelText: 'الفرع'),
                          items: [
                            for (final site in sites)
                              DropdownMenuItem(
                                value: textOf(site['id']),
                                child: UiText(
                                  '${textOf(site['name'])} — ${textOf(site['radiusMeters'])} مترًا',
                                ),
                              ),
                          ],
                          onChanged: busy
                              ? null
                              : (value) => setState(() => branchId = value),
                        ),
                        const SizedBox(height: 14),
                        FilledButton.icon(
                          onPressed: busy || branchId == null ? null : submit,
                          icon: const Icon(Icons.fingerprint),
                          label: UiText(
                            busy ? 'جارٍ التحقق…' : 'تأكيد البصمة والموقع',
                          ),
                        ),
                      ],
                    ),
                  ),
                if (message.isNotEmpty) UiText(message),
              ]);
            },
          ),
        ),
        SafeArea(
          top: false,
          child: Padding(
            padding: const EdgeInsets.all(12),
            child: OutlinedButton.icon(
              onPressed: busy
                  ? null
                  : () => openPage(
                      context,
                      StaffAttendanceExceptionPage(auth: widget.auth),
                    ),
              icon: const Icon(Icons.fact_check_outlined),
              label: const UiText('تعذر التحقق؟ اطلب إثبات حضور للمراجعة'),
            ),
          ),
        ),
      ],
    ),
  );
}
