import 'package:flutter/material.dart';
import 'package:flutter/foundation.dart';
import 'package:flutter/services.dart';

import 'app/app.dart';
import 'core/api/api_client.dart';
import 'core/auth/auth_controller.dart';
import 'core/storage/refresh_store.dart';
import 'core/theme/appearance.dart';

import 'package:flutter_secure_storage/flutter_secure_storage.dart';

Future<void> main() async {
  WidgetsFlutterBinding.ensureInitialized();
  LicenseRegistry.addLicense(() async* {
    yield LicenseEntryWithLineBreaks([
      'Academy Arabic',
    ], await rootBundle.loadString('assets/fonts/AcademyArabic-OFL.txt'));
  });
  try {
    final config = AppConfig(
      const String.fromEnvironment('APP_ENV', defaultValue: 'Production'),
      const String.fromEnvironment('API_BASE_URL'),
    );
    final auth = AuthController(
      HttpApiTransport(config),
      SecureRefreshStore(config.storageKey),
    );
    final appearance = Appearance(storage: const FlutterSecureStorage());
    await appearance.load();
    runApp(
      AcademyApp(
        auth: auth,
        environment: config.environment,
        appearance: appearance,
      ),
    );
    // Mount the entry animation before a fast local session restore can finish.
    WidgetsBinding.instance.addPostFrameCallback((_) => auth.restore());
  } catch (_) {
    runApp(
      const MaterialApp(
        home: Directionality(
          textDirection: TextDirection.rtl,
          child: Scaffold(
            body: SafeArea(
              child: Center(
                child: Padding(
                  padding: EdgeInsets.all(24),
                  child: Text(
                    'تعذر تشغيل التطبيق. راجع إعدادات الاتصال أو تواصل مع الدعم.',
                  ),
                ),
              ),
            ),
          ),
        ),
      ),
    );
  }
}
