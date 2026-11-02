import 'package:flutter_secure_storage/flutter_secure_storage.dart';

abstract interface class RefreshStore {
  Future<String?> read();
  Future<void> write(String token);
  Future<void> clear();
}

class SecureRefreshStore implements RefreshStore {
  final String key;
  final FlutterSecureStorage storage;
  SecureRefreshStore(this.key, {FlutterSecureStorage? storage})
    : storage =
          storage ??
          const FlutterSecureStorage(
            iOptions: IOSOptions(
              accessibility: KeychainAccessibility.unlocked_this_device,
            ),
          );
  @override
  Future<String?> read() => storage.read(key: key);
  @override
  Future<void> write(String token) => storage.write(key: key, value: token);
  @override
  Future<void> clear() => storage.delete(key: key);
}
