import 'dart:async';

import 'package:flutter/foundation.dart';

import '../api/api_client.dart';
import '../storage/refresh_store.dart';
import 'session.dart';

class AuthController extends ChangeNotifier {
  final ApiTransport transport;
  final RefreshStore store;
  Session? session;
  bool busy = false, restoring = true, restoreFailed = false;
  String? error;
  int _epoch = 0;
  Future<void>? _refreshing;
  Future<void> _storageTail = Future.value();
  AuthController(this.transport, this.store);

  // Serialize storage writes/deletion so logout cannot race a late refresh write.
  Future<void> _storage(Future<void> Function() action) {
    final result = _storageTail.then((_) => action());
    _storageTail = result.catchError((Object _) {});
    return result;
  }

  Future<void> _accept(Object? json, int epoch) async {
    if (epoch != _epoch) return;
    final next = Session.fromJson(json);
    await _storage(() async {
      if (epoch == _epoch) await store.write(next.refreshToken);
    });
    if (epoch != _epoch) return;
    session = next;
    error = null;
    notifyListeners();
  }

  Future<void> restore() async {
    restoring = true;
    restoreFailed = false;
    error = null;
    notifyListeners();
    try {
      await refresh();
    } on ApiFailure catch (failure) {
      restoreFailed = failure.status != 401;
      error = failure.status == 401 ? null : failure.message;
    } catch (_) {
      restoreFailed = true;
      error = 'تعذر فتح التخزين الآمن. أعد المحاولة بعد فتح قفل الجهاز.';
    } finally {
      restoring = false;
      notifyListeners();
    }
  }

  Future<String> requestOtp(String phone) async {
    final result = await transport.send(
      'POST',
      '/api/v1/mobile/auth/otp/request',
      body: {'phoneNumber': phone},
    ) as Map<String, dynamic>;
    return result['challengeId'] as String;
  }

  Future<void> verify(String phone, String challenge, String code) async {
    final epoch = ++_epoch;
    await _accept(
      await transport.send(
        'POST',
        '/api/v1/mobile/auth/otp/verify',
        body: {
          'phoneNumber': phone,
          'challengeId': challenge,
          'code': code,
          'deviceName': 'Academy Native',
        },
      ),
      epoch,
    );
  }

  Future<void> refresh() =>
      _refreshing ??= _doRefresh().whenComplete(() => _refreshing = null);
  Future<void> _doRefresh() async {
    final epoch = _epoch;
    final token = session?.refreshToken ?? await store.read();
    if (token == null || epoch != _epoch) return;
    try {
      await _accept(
        await transport.send(
          'POST',
          '/api/v1/mobile/auth/refresh',
          body: {'refreshToken': token},
        ),
        epoch,
      );
    } on ApiFailure catch (failure) {
      if (failure.status == 401 && epoch == _epoch) {
        session = null;
        await _storage(store.clear);
        notifyListeners();
      }
      rethrow;
    }
  }

  Future<Object?> request(String method, String path, {Object? body}) async {
    if (busy) throw const ApiFailure(409, 'انتظر اكتمال تحديث الجلسة.');
    final epoch = _epoch;
    if (session == null) throw const ApiFailure(401, 'سجل الدخول أولًا.');
    if (session!.accessExpires.isBefore(
      DateTime.now().toUtc().add(const Duration(seconds: 30)),
    )) {
      await refresh();
    }
    if (epoch != _epoch || session == null) {
      throw const ApiFailure(401, 'تغيرت الجلسة. أعد فتح الصفحة.');
    }
    final token = session!.accessToken;
    try {
      final result = await transport.send(
        method,
        path,
        body: body,
        token: token,
      );
      if (epoch != _epoch || session == null) {
        throw const ApiFailure(401, 'تغيرت الجلسة.');
      }
      return result;
    } on ApiFailure catch (failure) {
      if (failure.status != 401 || epoch != _epoch) rethrow;
      // Retry only a read, once. Writes are never replayed automatically.
      if (session?.accessToken == token) await refresh();
      if (method != 'GET' || epoch != _epoch || session == null) rethrow;
      final result = await transport.send(
        method,
        path,
        token: session!.accessToken,
      );
      if (epoch != _epoch || session == null) {
        throw const ApiFailure(401, 'تغيرت الجلسة.');
      }
      return result;
    }
  }

  Future<void> selectRole(String membershipId) async {
    if (busy || session == null) return;
    busy = true;
    notifyListeners();
    try {
      await refresh(); // Serialize with any running refresh before rotating for selection.
      final epoch = ++_epoch;
      if (session == null) throw const ApiFailure(401, 'سجل الدخول مجددًا.');
      await _accept(
        await transport.send(
          'POST',
          '/api/v1/mobile/auth/select-role',
          token: session!.accessToken,
          body: {'membershipId': membershipId},
        ),
        epoch,
      );
    } finally {
      busy = false;
      notifyListeners();
    }
  }

  Future<void> logout() async {
    final token = session?.accessToken;
    ++_epoch;
    busy = true;
    error = null;
    notifyListeners();
    try {
      await _storage(store.clear);
    } catch (_) {
      error = 'تعذر مسح التخزين الآمن. افتح قفل الجهاز وحاول الخروج مجددًا.';
      rethrow;
    } finally {
      busy = false;
      notifyListeners();
    }
    session = null;
    restoreFailed = false;
    notifyListeners();
    if (token != null) {
      try {
        await transport.send(
          'POST',
          '/api/v1/mobile/auth/logout',
          token: token,
        );
      } catch (_) {
        error = 'تم الخروج محليًا، لكن تعذر تأكيد إلغاء الجلسة على الخادم.';
        notifyListeners();
      }
    }
  }
}
