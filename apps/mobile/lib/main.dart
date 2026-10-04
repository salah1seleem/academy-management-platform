import 'package:flutter/material.dart';

import 'app/app.dart';
import 'core/api/api_client.dart';
import 'core/auth/auth_controller.dart';
import 'core/storage/refresh_store.dart';

void main() {
  WidgetsFlutterBinding.ensureInitialized();
  try {
    final config = AppConfig(
      const String.fromEnvironment('APP_ENV', defaultValue: 'Production'),
      const String.fromEnvironment('API_BASE_URL'),
    );
    final auth = AuthController(
      HttpApiTransport(config),
      SecureRefreshStore(config.storageKey),
    );
    runApp(AcademyApp(auth: auth, environment: config.environment));
    auth.restore();
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
                    'يلزم إعداد APP_ENV وAPI_BASE_URL صالحين لتشغيل التطبيق.',
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
