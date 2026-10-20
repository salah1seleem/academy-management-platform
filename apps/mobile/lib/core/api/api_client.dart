import 'dart:async';
import 'dart:convert';
import 'dart:io';
import 'dart:typed_data';

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

abstract interface class AssetTransport {
  Future<Uint8List> asset(String path, String token);
  Future<Object?> upload(
    Uint8List bytes,
    String contentType,
    Map<String, String> fields,
    String token,
  );
}

class HttpApiTransport implements ApiTransport, AssetTransport {
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

  @override
  Future<Uint8List> asset(String path, String token) async {
    final reportPath = Uri.parse(path).path;
    final reportExport =
        path.startsWith('/api/v1/reports/') &&
        const [
          '/api/v1/reports/financial/export',
          '/api/v1/reports/attendance/export',
        ].contains(reportPath);
    if (!reportExport &&
        path != '/api/v1/academy/branding/logo' &&
        !RegExp(
          r'^/api/v1/help/lessons/[a-z0-9-]+/media/[a-fA-F0-9-]{36}/(video|thumbnail)$',
        ).hasMatch(path) &&
        !RegExp(r'^/api/v1/assets/[a-fA-F0-9-]{36}$').hasMatch(path)) {
      throw const ApiFailure(400, 'مرجع الوسائط غير صالح.');
    }
    try {
      final request = await _client
          .getUrl(config.api.resolve(path))
          .timeout(const Duration(seconds: 20));
      request.followRedirects = false;
      request.headers.set(HttpHeaders.authorizationHeader, 'Bearer $token');
      var response = await request.close().timeout(const Duration(seconds: 20));
      if (response.statusCode == 302 &&
          RegExp(r'^/api/v1/assets/[a-fA-F0-9-]{36}$').hasMatch(path)) {
        final location = response.headers.value(HttpHeaders.locationHeader);
        await response.timeout(const Duration(seconds: 20)).drain<void>();
        final target = location == null ? null : Uri.tryParse(location);
        final resolved = target == null ? null : config.api.resolveUri(target);
        final values = resolved?.queryParametersAll['access'];
        if (resolved == null ||
            resolved.scheme != config.api.scheme ||
            resolved.host != config.api.host ||
            resolved.port != config.api.port ||
            resolved.userInfo.isNotEmpty ||
            resolved.path != path ||
            resolved.fragment.isNotEmpty ||
            resolved.queryParametersAll.length != 1 ||
            values == null ||
            values.length != 1 ||
            values.single.length > 4096 ||
            !RegExp(r'^ML1\.[A-Za-z0-9_-]+$').hasMatch(values.single)) {
          throw const ApiFailure(400, 'رابط الوسائط غير صالح؛ أعد فتح المعرض.');
        }
        // One same-origin redirect, same asset only; never an arbitrary credential redirect.
        final download = await _client
            .getUrl(resolved)
            .timeout(const Duration(seconds: 20));
        download.followRedirects = false;
        download.headers.set(HttpHeaders.authorizationHeader, 'Bearer $token');
        response = await download.close().timeout(const Duration(seconds: 20));
      }
      if (response.statusCode != 200) {
        throw ApiFailure(
          response.statusCode,
          response.statusCode == 429
              ? 'بلغت حد تنزيل الوسائط اليومي. يعود الحد مع بداية يوم UTC التالي.'
              : 'تعذر عرض الوسائط.',
        );
      }
      final bytes = BytesBuilder();
      await for (final chunk in response.timeout(const Duration(seconds: 20))) {
        bytes.add(chunk);
        if (bytes.length > 50 * 1024 * 1024) {
          throw const ApiFailure(413, 'حجم الوسائط أكبر من المسموح.');
        }
      }
      return bytes.takeBytes();
    } on SocketException {
      throw const ApiFailure(0, 'تعذر الاتصال بالخادم.');
    } on TimeoutException {
      throw const ApiFailure(0, 'انتهت مهلة تحميل الوسائط.');
    }
  }

  @override
  Future<Object?> upload(
    Uint8List bytes,
    String contentType,
    Map<String, String> fields,
    String token,
  ) async {
    final uploadFields = {
      'purpose',
      'playerId',
      'groupId',
      'caption',
      if (fields['purpose'] == 'Media') ...{
        'eventOn',
        'subjectPlayerIds',
        'noRecognizableChildren',
        'privacyConfirmed',
        'sessionId',
      },
    };
    if (bytes.length > 50 * 1024 * 1024 ||
        !{
          'image/jpeg',
          'image/png',
          'image/webp',
          'video/mp4',
          'video/quicktime',
        }.contains(contentType) ||
        fields.keys.any((k) => !uploadFields.contains(k))) {
      throw const ApiFailure(400, 'ملف غير مدعوم أو أكبر من المسموح.');
    }
    final boundary = 'academy-${DateTime.now().microsecondsSinceEpoch}';
    try {
      final request = await _client
          .postUrl(config.api.resolve('/api/v1/assets/upload'))
          .timeout(const Duration(seconds: 20));
      request.followRedirects = false;
      request.headers.set(HttpHeaders.authorizationHeader, 'Bearer $token');
      request.headers.set(
        HttpHeaders.contentTypeHeader,
        'multipart/form-data; boundary=$boundary',
      );
      for (final field in fields.entries) {
        request.add(
          utf8.encode(
            '--$boundary\r\nContent-Disposition: form-data; name="${field.key}"\r\n\r\n${field.value}\r\n',
          ),
        );
      }
      request.add(
        utf8.encode(
          '--$boundary\r\nContent-Disposition: form-data; name="file"; filename="upload"\r\nContent-Type: $contentType\r\n\r\n',
        ),
      );
      request.add(bytes);
      request.add(utf8.encode('\r\n--$boundary--\r\n'));
      final response = await request.close().timeout(
        const Duration(seconds: 60),
      );
      final raw = await utf8.decoder
          .bind(response)
          .join()
          .timeout(const Duration(seconds: 20));
      if (response.statusCode != 201) {
        throw ApiFailure(
          response.statusCode,
          response.statusCode == 422 && fields['purpose'] == 'Media'
              ? 'لم ينشر الملف. راجع موافقات الأطفال الظاهرين، تاريخ الواقعة وإقرار المراجعة.'
              : response.statusCode == 503
              ? 'فحص الملفات غير متاح أو مشغول؛ لم يُنشر الملف. حاول لاحقًا أو تواصل مع الإدارة.'
              : response.statusCode == 400 &&
                    {'Media', 'Photo'}.contains(fields['purpose'])
              ? 'لم يُحفظ الملف. راجع سلامته وحدوده: صورة 8 ميجابايت و20 مليون بكسل؛ فيديو 50 ميجابايت ودقيقتان و1080p.'
              : 'تعذر رفع الملف. راجع النوع والحجم والصلاحية.',
        );
      }
      return jsonDecode(raw);
    } on SocketException {
      throw const ApiFailure(0, 'تعذر الاتصال. لم يُعَد رفع الملف تلقائيًا.');
    } on TimeoutException {
      throw const ApiFailure(
        0,
        'انتهت مهلة رفع الملف. حدّث الطلب قبل المحاولة.',
      );
    }
  }
}
