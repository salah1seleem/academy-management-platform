import 'package:academy_mobile/core/auth/staff_passkey_provider.dart';
import 'package:flutter/foundation.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:passkeys/authenticator.dart';
import 'package:passkeys/types.dart';

class PlatformDouble extends PasskeyAuthenticator {
  RegisterRequestType? registration;
  AuthenticateRequestType? assertion;
  String handle = 'Aw';
  @override
  Future<RegisterResponseType> register(RegisterRequestType request) async {
    registration = request;
    return const RegisterResponseType(
      id: 'AQ',
      rawId: 'AQ',
      clientDataJSON: 'Ag',
      attestationObject: 'Aw',
      transports: ['internal'],
      clientExtensionResults: {
        'prf': {
          'results': {'first': 'must-not-leave-device'},
        },
      },
    );
  }

  @override
  Future<AuthenticateResponseType> authenticate(
    AuthenticateRequestType request,
  ) async {
    assertion = request;
    return AuthenticateResponseType(
      id: 'AQ',
      rawId: 'AQ',
      clientDataJSON: 'Ag',
      authenticatorData: 'Aw',
      signature: 'BA',
      userHandle: handle,
    );
  }
}

final registerOptions = <String, dynamic>{
  'challenge': 'AQ',
  'rp': {'id': 'academy.test', 'name': 'أكاديميتي'},
  'user': {'id': 'AQ', 'name': 'staff', 'displayName': 'موظف اصطناعي'},
  'authenticatorSelection': {'userVerification': 'required'},
  'pubKeyCredParams': [
    {'type': 'public-key', 'alg': -7},
  ],
};
final verifyOptions = <String, dynamic>{
  'challenge': 'AQ',
  'rpId': 'academy.test',
  'userVerification': 'required',
  'allowCredentials': [
    {'id': 'Ag', 'type': 'public-key'},
  ],
};
void main() {
  test('explicit iOS setup only; localhost, IP, URL, wildcard and Android never become native readiness', () {
    for (final domain in [
      '',
      'localhost',
      '127.0.0.1',
      'https://academy.test',
      '*.academy.test',
      'academy.test/path',
      'demo.localhost',
    ]) {
      expect(
        IosStaffPasskeyProvider(
          optedIn: true,
          rpId: domain,
          platform: TargetPlatform.iOS,
        ).enabled,
        isFalse,
      );
    }
    expect(
      IosStaffPasskeyProvider(
        rpId: 'academy.test',
        platform: TargetPlatform.iOS,
      ).enabled,
      isFalse,
    );
    expect(
      IosStaffPasskeyProvider(
        optedIn: true,
        rpId: 'academy.test',
        platform: TargetPlatform.android,
      ).enabled,
      isFalse,
    );
    final enabled = IosStaffPasskeyProvider(
      optedIn: true,
      rpId: 'academy.test',
      platform: TargetPlatform.iOS,
    );
    expect(enabled.enabled, isTrue);
    expect(enabled.authenticator.debugMode, isFalse);
  });
  test('adapter forwards UV and exact server options, returns standard proof and excludes PRF secret', () async {
    final os = PlatformDouble();
    final provider = IosStaffPasskeyProvider(
      authenticator: os,
      optedIn: true,
      rpId: 'academy.test',
      platform: TargetPlatform.iOS,
    );
    final registration = await provider.register(registerOptions);
    expect(os.registration!.authSelectionType!.userVerification, 'required');
    expect(registration['rawId'], 'AQ');
    expect(registration['extensions'], isEmpty);
    expect(registration.toString(), isNot(contains('must-not-leave-device')));
    expect(registration.containsKey('clientExtensionResults'), isFalse);
    final proof = await provider.assertCredential(verifyOptions);
    expect(os.assertion!.userVerification, 'required');
    expect(os.assertion!.relyingPartyId, 'academy.test');
    expect(os.assertion!.allowCredentials!.single.id, 'Ag');
    expect((proof['response'] as Map)['userHandle'], 'Aw');
    expect(proof.containsKey('biometricPassed'), isFalse);
    expect(os.assertion!.allowCredentials!.single.transports, isEmpty);
    expect(
      (verifyOptions['allowCredentials'] as List).single.containsKey(
        'transports',
      ),
      isFalse,
    );
  });
  test('RP mismatch, missing UV and unbound allow list fail before the OS; a missing user handle is not reconstructed', () async {
    final os = PlatformDouble();
    final provider = IosStaffPasskeyProvider(
      authenticator: os,
      optedIn: true,
      rpId: 'academy.test',
      platform: TargetPlatform.iOS,
    );
    for (final invalid in [
      {...verifyOptions, 'rpId': 'other.test'},
      {...verifyOptions, 'userVerification': 'preferred'},
      {...verifyOptions, 'allowCredentials': []},
      {...verifyOptions, 'challenge': null},
    ]) {
      await expectLater(
        provider.assertCredential(invalid),
        throwsA(isA<StaffPasskeyProblem>()),
      );
    }
    expect(os.assertion, isNull);
    os.handle = '';
    await expectLater(
      provider.assertCredential(verifyOptions),
      throwsA(isA<StaffPasskeyProblem>()),
    );
  });
}
