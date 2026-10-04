import 'dart:async';

import 'package:academy_mobile/core/api/api_client.dart';
import 'package:academy_mobile/core/auth/auth_controller.dart';
import 'package:academy_mobile/core/storage/refresh_store.dart';
import 'package:flutter_test/flutter_test.dart';

class MemoryStore implements RefreshStore {
  String? token;
  bool failWrite = false, failClear = false;
  @override
  Future<String?> read() async => token;
  @override
  Future<void> write(String value) async {
    if (failWrite) throw Exception('storage');
    token = value;
  }

  @override
  Future<void> clear() async {
    if (failClear) throw Exception('storage');
    token = null;
  }
}

typedef Handler = Future<Object?> Function(String, String, Object?, String?);

class FakeApi implements ApiTransport {
  final Handler handler;
  final List<String> paths = [];
  final List<String?> keys = [];
  FakeApi(this.handler);
  @override
  Future<Object?> send(
    String method,
    String path, {
    Object? body,
    String? token,
    String? idempotencyKey,
  }) {
    paths.add(path);
    keys.add(idempotencyKey);
    return handler(method, path, body, token);
  }
}

Map<String, dynamic> credentials({String suffix = 'one', bool multi = false}) =>
    {
      'accessToken': 'access-$suffix',
      'refreshToken': 'refresh-$suffix',
      'displayName': 'حساب تجريبي',
      'accessExpiresAtUtc': '2100-01-01T00:00:00Z',
      'membershipId': multi ? null : 'guardian',
      'memberships': [
        {
          'id': 'guardian',
          'academyId': 'academy',
          'academyName': 'أكاديمية العرض',
          'role': 'Guardian',
        },
        if (multi)
          {
            'id': 'owner',
            'academyId': 'another',
            'academyName': 'أكاديمية ثانية',
            'role': 'AcademyOwner',
          },
      ],
    };

void main() {
  test('A late data response cannot leak across logout', () async {
    final response = Completer<Object?>();
    final auth = AuthController(
      FakeApi((m, p, b, t) async {
        if (p == '/api/v1/me') return response.future;
        return credentials();
      }),
      MemoryStore(),
    );
    await auth.verify('phone', 'challenge', 'code');
    final request = auth.request('GET', '/api/v1/me');
    final rejected = expectLater(request, throwsA(isA<ApiFailure>()));
    await auth.logout();
    response.complete({'private': 'previous account'});
    await rejected;
  });
  test(
    'Failed secure deletion does not falsely report logout success',
    () async {
      final store = MemoryStore();
      final auth = AuthController(
        FakeApi((a, b, c, d) async => credentials()),
        store,
      );
      await auth.verify('phone', 'challenge', 'code');
      store.failClear = true;
      await expectLater(auth.logout(), throwsException);
      expect(auth.session, isNotNull);
      expect(auth.error, contains('التخزين الآمن'));
      store.failClear = false;
      await auth.logout();
      expect(auth.session, isNull);
      expect(store.token, isNull);
    },
  );
  test('Only refresh token is stored and restore rotates it', () async {
    final store = MemoryStore();
    final api = FakeApi(
      (m, p, b, t) async =>
          credentials(suffix: p.endsWith('/refresh') ? 'two' : 'one'),
    );
    final auth = AuthController(api, store);
    await auth.verify('phone', 'challenge', 'code');
    expect(store.token, 'refresh-one');
    expect(auth.session!.accessToken, 'access-one');
    final restored = AuthController(api, store);
    await restored.restore();
    expect(store.token, 'refresh-two');
    expect(restored.session!.selected!.role, 'Guardian');
  });
  test('Concurrent refresh is single flight', () async {
    final store = MemoryStore()..token = 'saved';
    final pending = Completer<Object?>();
    final api = FakeApi((a, b, c, d) => pending.future);
    final auth = AuthController(api, store);
    final first = auth.refresh();
    final second = auth.refresh();
    await Future<void>.delayed(Duration.zero);
    expect(api.paths.length, 1);
    pending.complete(credentials());
    await Future.wait([first, second]);
    expect(store.token, 'refresh-one');
  });
  test('Late refresh cannot resurrect a logged out session', () async {
    final store = MemoryStore()..token = 'saved';
    final pending = Completer<Object?>();
    final auth = AuthController(FakeApi((a, b, c, d) => pending.future), store);
    final refresh = auth.refresh();
    await Future<void>.delayed(Duration.zero);
    await auth.logout();
    pending.complete(credentials());
    await refresh;
    expect(auth.session, isNull);
    expect(store.token, isNull);
  });
  test(
    'Revoked refresh is deleted; transient network failure is preserved',
    () async {
      for (final status in [0, 401, 429]) {
        final store = MemoryStore()..token = 'saved';
        final auth = AuthController(
          FakeApi((a, b, c, d) async => throw ApiFailure(status, 'فشل')),
          store,
        );
        await auth.restore();
        expect(store.token, status == 401 ? isNull : 'saved');
        expect(auth.restoreFailed, status != 401);
      }
    },
  );
  test('A write is never automatically repeated after 401', () async {
    var writes = 0;
    final auth = AuthController(
      FakeApi((m, p, b, t) async {
        if (p.contains('/mobile/auth/')) return credentials();
        writes++;
        throw const ApiFailure(401, 'فشل');
      }),
      MemoryStore(),
    );
    await auth.verify('phone', 'challenge', 'code');
    await expectLater(
      auth.request('POST', '/api/v1/renewals', body: {}),
      throwsA(isA<ApiFailure>()),
    );
    expect(writes, 1);
  });
  test(
    'A read retries once after refreshing an expired access token',
    () async {
      var reads = 0;
      final auth = AuthController(
        FakeApi((m, p, b, t) async {
          if (p.contains('/mobile/auth/')) return credentials();
          if (++reads == 1) throw const ApiFailure(401, 'فشل');
          return {'value': 'fresh'};
        }),
        MemoryStore(),
      );
      await auth.verify('phone', 'challenge', 'code');
      expect(await auth.request('GET', '/api/v1/me'), {'value': 'fresh'});
      expect(reads, 2);
    },
  );
  test(
    'Logout clears local secret even when remote revocation fails',
    () async {
      final store = MemoryStore();
      final auth = AuthController(
        FakeApi((m, p, b, t) async {
          if (p.endsWith('/logout')) throw const ApiFailure(0, 'offline');
          return credentials();
        }),
        store,
      );
      await auth.verify('phone', 'challenge', 'code');
      await auth.logout();
      expect(store.token, isNull);
      expect(auth.session, isNull);
      expect(auth.error, contains('محليًا'));
    },
  );
  test('Secure write failure never accepts a session', () async {
    final store = MemoryStore()..failWrite = true;
    final auth = AuthController(
      FakeApi((a, b, c, d) async => credentials()),
      store,
    );
    await expectLater(
      auth.verify('phone', 'challenge', 'code'),
      throwsException,
    );
    expect(auth.session, isNull);
  });
  test(
    'Role selection uses membership ID explicitly and rotates storage',
    () async {
      final store = MemoryStore();
      final auth = AuthController(
        FakeApi((m, p, b, t) async {
          if (p.endsWith('/select-role')) {
            expect(b, {'membershipId': 'owner'});
            return {
              ...credentials(suffix: 'owner', multi: true),
              'membershipId': 'owner',
            };
          }
          return credentials(multi: true);
        }),
        store,
      );
      await auth.verify('phone', 'challenge', 'code');
      expect(auth.session!.selected, isNull);
      await auth.selectRole('owner');
      expect(auth.session!.selected!.role, 'AcademyOwner');
      expect(store.token, 'refresh-owner');
    },
  );
  test('Environment requires HTTPS in production and rejects deceptive local hostnames', () {
    expect(
      () => AppConfig('Production', 'http://127.0.0.1:5080'),
      throwsA(isA<ApiFailure>()),
    );
    expect(
      () => AppConfig('Demo', 'http://10.evil.example'),
      throwsA(isA<ApiFailure>()),
    );
    expect(
      () => AppConfig('Demo', 'https://user:secret@example.test'),
      throwsA(isA<ApiFailure>()),
    );
    expect(AppConfig('Demo', 'http://10.0.2.2:5080').api.port, 5080);
    expect(
      AppConfig('Production', 'https://api.example.test').api.scheme,
      'https',
    );
  });
}
