import 'dart:async';
import 'dart:convert';
import 'dart:io';

class ApiFailure implements Exception {
  final int status;
  final String message;
  const ApiFailure(this.status, this.message);
  @override
  String toString() => message; // Never include response bodies, credentials or URLs.
}

class AppConfig {
  final String environment;
  final Uri api;
  AppConfig(this.environment, String url) : api = Uri.parse(url) {
    final ip = InternetAddress.tryParse(api.host);
    final octets = ip?.rawAddress;
    final privateHost =
        api.host == 'localhost' ||
        (ip != null &&
            (ip.isLoopback ||
                (octets!.length == 4 &&
                    (octets[0] == 10 ||
                        (octets[0] == 192 && octets[1] == 168) ||
                        (octets[0] == 172 &&
                            octets[1] >= 16 &&
                            octets[1] <= 31)))));
    if (!['Demo', 'Development', 'Production'].contains(environment) ||
        api.host.isEmpty ||
        api.userInfo.isNotEmpty ||
        api.hasQuery ||
        api.hasFragment ||
        (api.path.isNotEmpty && api.path != '/') ||
        (api.scheme != 'https' &&
            !(environment != 'Production' &&
                api.scheme == 'http' &&
                privateHost))) {
      throw const ApiFailure(
        0,
        'إعداد عنوان الخادم غير صالح. الإنتاج يحتاج HTTPS.',
      );
    }
  }
  String get storageKey => 'academy.refresh.$environment.${api.origin}';
}

abstract interface class ApiTransport {
  Future<Object?> send(
    String method,
    String path, {
    Object? body,
    String? token,
    String? idempotencyKey,
  });
}

class HttpApiTransport implements ApiTransport {
  final AppConfig config;
  final HttpClient _client = HttpClient()
    ..connectionTimeout = const Duration(seconds: 12);
  HttpApiTransport(this.config);
  @override
  Future<Object?> send(
    String method,
    String path, {
    Object? body,
    String? token,
    String? idempotencyKey,
  }) async {
    if (!path.startsWith('/api/v1/') || path.contains('..')) {
      throw const ApiFailure(0, 'مسار غير صالح.');
    }
    try {
      final request = await _client
          .openUrl(method, config.api.resolve(path))
          .timeout(const Duration(seconds: 20));
      request.followRedirects =
          false; // Do not forward bearer credentials to redirects.
      if (idempotencyKey != null) {
        request.headers.set('Idempotency-Key', idempotencyKey);
      }
      request.headers.set(HttpHeaders.acceptHeader, 'application/json');
      if (token != null) {
        request.headers.set(HttpHeaders.authorizationHeader, 'Bearer $token');
      }
      if (body != null) {
        request.headers.contentType = ContentType.json;
        request.write(jsonEncode(body));
      }
      final response = await request.close().timeout(
        const Duration(seconds: 20),
      );
      final raw = await utf8.decoder
          .bind(response)
          .join()
          .timeout(const Duration(seconds: 20));
      if (response.statusCode < 200 || response.statusCode >= 300) {
        // Fixed localized messages avoid leaking HTML, stack traces or arbitrary server content.
        throw ApiFailure(response.statusCode, switch (response.statusCode) {
          400 => 'راجع البيانات المدخلة وحاول مجددًا.',
          401 => 'انتهت الجلسة أو تعذر التحقق من بيانات الدخول.',
          403 => 'ليس لديك صلاحية لهذا الإجراء.',
          409 => 'تغيرت البيانات. حدّث الصفحة قبل المحاولة.',
          429 => 'محاولات كثيرة. انتظر قليلًا قبل المحاولة.',
          501 => 'خدمة رسائل التحقق الحقيقية غير مفعلة بعد.',
          _ => 'تعذر تنفيذ الطلب الآن. حاول لاحقًا.',
        });
      }
      return raw.isEmpty ? null : jsonDecode(raw);
    } on SocketException {
      throw const ApiFailure(0, 'تعذر الاتصال. تحقق من الشبكة وعنوان الخادم.');
    } on TimeoutException {
      throw const ApiFailure(
        0,
        'انتهت مهلة الاتصال. لم يُعَد إرسال الطلب تلقائيًا.',
      );
    } on FormatException {
      throw const ApiFailure(0, 'استجابة الخادم غير صالحة.');
    } on HttpException {
      throw const ApiFailure(0, 'تعذر إكمال الاتصال بالخادم.');
    }
  }

  void close() => _client.close(force: true);
}
