import 'dart:async';
import 'dart:typed_data';

import 'package:academy_mobile/core/api/api_client.dart';
import 'package:academy_mobile/core/auth/auth_controller.dart';
import 'package:academy_mobile/features/shared/private_media.dart';
import 'package:file_picker/file_picker.dart';
import 'package:flutter_test/flutter_test.dart';

import 'auth_controller_test.dart' show FakeApi, MemoryStore;
import 'owner_test.dart' show ownerCredentials;

final class SyntheticPickedFile extends PlatformFile {
  final read = Completer<Uint8List>();
  @override
  String get name => 'synthetic.png';
  @override
  Future<int?> length() async => 3;
  @override
  Future<Uint8List> readAsBytes() => read.future;
  @override
  dynamic noSuchMethod(Invocation invocation) => super.noSuchMethod(invocation);
}

class DeferredMediaPicker extends FilePickerPlatform {
  final result = Completer<PlatformFile?>();
  @override
  Future<PlatformFile?> pickFile({
    String? dialogTitle,
    String? initialDirectory,
    FileType type = FileType.any,
    List<String>? allowedExtensions,
    Function(FilePickerStatus)? onFileLoading,
    int compressionQuality = 0,
    AndroidOptions androidOptions = const AndroidOptions(),
    DarwinOptions darwinOptions = const DarwinOptions(),
    WindowsOptions windowsOptions = const WindowsOptions(),
    LinuxOptions linuxOptions = const LinuxOptions(),
    WebOptions webOptions = const WebOptions(),
  }) => result.future;
}

class UploadApi extends FakeApi implements AssetTransport {
  UploadApi() : super((m, p, b, k) async => ownerCredentials());
  final writes = <Map<String, String>>[];
  @override
  Future<Uint8List> asset(String path, String token) async => Uint8List(0);
  @override
  Future<Object?> upload(
    Uint8List bytes,
    String type,
    Map<String, String> fields,
    String token,
  ) async {
    writes.add(Map.of(fields));
    return {'id': 'saved'};
  }
}

void main() {
  TestWidgetsFlutterBinding.ensureInitialized();
  for (final changedDuringRead in [false, true]) {
    test(
      'a picker intent cannot publish after logout and login of the same membership, change during ${changedDuringRead ? 'file read' : 'picker'}',
      () async {
        final original = FilePickerPlatform.instance;
        final picker = DeferredMediaPicker();
        FilePickerPlatform.instance = picker;
        addTearDown(() => FilePickerPlatform.instance = original);
        final api = UploadApi();
        final auth = AuthController(api, MemoryStore())..restoring = false;
        await auth.verify('p', 'c', 'o');
        final file = SyntheticPickedFile();
        final result = pickAndUpload(auth, {
          'purpose': 'Media',
          'groupId': 'original',
        });
        final rejected = expectLater(
          result,
          throwsA(isA<ApiFailure>().having((e) => e.status, 'status', 409)),
        );
        if (changedDuringRead) {
          picker.result.complete(file);
          await Future<void>.delayed(Duration.zero);
        }
        await auth.logout();
        await auth.verify('p', 'c', 'o');
        if (!changedDuringRead) picker.result.complete(file);
        file.read.complete(Uint8List.fromList([1, 2, 3]));
        await rejected;
        expect(api.writes, isEmpty);
        auth.dispose();
      },
    );
  }
  test('unchanged authority publishes the reviewed fields once even when caller mutates its map while picker is open', () async {
    final original = FilePickerPlatform.instance;
    final picker = DeferredMediaPicker();
    FilePickerPlatform.instance = picker;
    addTearDown(() => FilePickerPlatform.instance = original);
    final api = UploadApi();
    final auth = AuthController(api, MemoryStore())..restoring = false;
    await auth.verify('p', 'c', 'o');
    final fields = {'purpose': 'Media', 'groupId': 'reviewed'};
    final result = pickAndUpload(auth, fields);
    fields['groupId'] = 'changed';
    final file = SyntheticPickedFile();
    file.read.complete(Uint8List.fromList([1, 2, 3]));
    picker.result.complete(file);
    expect((await result)!['id'], 'saved');
    expect(api.writes.single['groupId'], 'reviewed');
    auth.dispose();
  });
}
