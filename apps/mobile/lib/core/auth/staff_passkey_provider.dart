import 'package:flutter/foundation.dart';
import 'package:passkeys/authenticator.dart';
import 'package:passkeys/types.dart';

typedef PasskeyJson = Map<String, dynamic>;

class StaffPasskeyProblem implements Exception {
  final String message;
  const StaffPasskeyProblem(this.message);
}

abstract class StaffPasskeyProvider {
  bool get enabled;
  String get rpId;
  Future<PasskeyJson> register(PasskeyJson options);
  Future<PasskeyJson> assertCredential(PasskeyJson options);
  Future<void> cancel();
}

// Build setup merely enables the OS prompt. Only the existing API verifies its proof.
class IosStaffPasskeyProvider implements StaffPasskeyProvider {
  final PasskeyAuthenticator authenticator;
  final bool optedIn;
  @override
  final String rpId;
  final TargetPlatform platform;
  IosStaffPasskeyProvider({
    PasskeyAuthenticator? authenticator,
    this.optedIn = const bool.fromEnvironment('STAFF_PASSKEYS_ENABLED'),
    this.rpId = const String.fromEnvironment('STAFF_PASSKEY_RP_ID'),
    TargetPlatform? platform,
  }) : authenticator = authenticator ?? PasskeyAuthenticator(debugMode: false),
       platform = platform ?? defaultTargetPlatform;
  @override
  bool get enabled =>
      optedIn && !kIsWeb && platform == TargetPlatform.iOS && validDomain(rpId);
  static bool validDomain(String domain) =>
      domain == domain.toLowerCase() &&
      !domain.endsWith('.localhost') &&
      RegExp(
        r'^(?=.{1,253}$)(?:[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?\.)+[a-z]{2,63}$',
      ).hasMatch(domain);
  void validate(PasskeyJson options, bool registration) {
    final party = options['rp'], selection = options['authenticatorSelection'];
    final rp = registration
        ? (party is Map ? party['id'] : null)
        : options['rpId'];
    final uv = registration
        ? (selection is Map ? selection['userVerification'] : null)
        : options['userVerification'];
    if (!enabled ||
        rp != rpId ||
        uv != 'required' ||
        options['challenge'] is! String) {
      throw const StaffPasskeyProblem(
        'إعداد التوقيع الآمن لا يطابق هذه المحاولة؛ اطلب مراجعة الإدارة.',
      );
    }
    // Do not ask a platform for an unbound/discoverable assertion on this own-only route.
    if (!registration &&
        (options['allowCredentials'] is! List ||
            (options['allowCredentials'] as List).isEmpty)) {
      throw const StaffPasskeyProblem(
        'لا توجد وسيلة محددة لحسابك؛ أضف وسيلة أو اطلب مراجعة.',
      );
    }
  }

  PasskeyJson standard(PasskeyJson value) {
    if (value['id'] is! String ||
        (value['id'] as String).isEmpty ||
        value['rawId'] is! String ||
        (value['rawId'] as String).isEmpty ||
        value['type'] != 'public-key' ||
        value['response'] is! Map) {
      throw const StaffPasskeyProblem('تعذر قراءة إثبات الجهاز.');
    }
    // No PRF output, local biometric boolean, or reconstructed userHandle is sent.
    return {
      'id': value['id'],
      'rawId': value['rawId'],
      'type': 'public-key',
      'response': Map<String, dynamic>.from(value['response'] as Map),
      'extensions': <String, dynamic>{},
    };
  }

  Future<PasskeyJson> perform(PasskeyJson options, bool registration) async {
    validate(options, registration);
    final nativeOptions = <String, dynamic>{...options};
    // Optional WebAuthn transports are required by this SDK. An absent list
    // means unknown, never a fabricated "internal" hardware guarantee.
    for (final key in ['allowCredentials', 'excludeCredentials']) {
      if (options[key] is List) {
        nativeOptions[key] = [
          for (final item in options[key] as List)
            {
              ...Map<String, dynamic>.from(item as Map),
              'transports': item['transports'] ?? <String>[],
            },
        ];
      }
    }
    try {
      if (registration) {
        return standard(
          (await authenticator.register(
            RegisterRequestType.fromJson(nativeOptions),
          )).toJson(),
        );
      }
      final result = standard(
        (await authenticator.authenticate(
          AuthenticateRequestType.fromJson(
            nativeOptions,
            preferImmediatelyAvailableCredentials: false,
          ),
        )).toJson(),
      );
      if ((result['response'] as Map)['userHandle'] is! String ||
          ((result['response'] as Map)['userHandle'] as String).isEmpty) {
        throw const StaffPasskeyProblem(
          'لم يقدم الجهاز هوية الوسيلة؛ اطلب مراجعة بدل اختلاق إثبات.',
        );
      }
      return result;
    } on PasskeyAuthCancelledException {
      throw const StaffPasskeyProblem('تم إلغاء التحقق؛ لم يسجل حضور.');
    } on DomainNotAssociatedException {
      throw const StaffPasskeyProblem(
        'نطاق الخدمة غير مرتبط بهذا التطبيق؛ راجع المسؤول أو اطلب إثبات حضور.',
      );
    } on StaffPasskeyProblem {
      rethrow;
    } catch (_) {
      throw const StaffPasskeyProblem(
        'تعذر استخدام وسيلة التحقق على الجهاز؛ أعد المحاولة أو اطلب مراجعة.',
      );
    }
  }

  @override
  Future<PasskeyJson> register(PasskeyJson options) => perform(options, true);
  @override
  Future<PasskeyJson> assertCredential(PasskeyJson options) =>
      perform(options, false);
  @override
  Future<void> cancel() async {
    if (enabled) {
      try {
        await authenticator.cancelCurrentAuthenticatorOperation();
      } catch (_) {
        /* No success claim or proof on cancellation. */
      }
    }
  }
}
