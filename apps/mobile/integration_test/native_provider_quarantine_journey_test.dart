import 'package:integration_test/integration_test.dart';

import '../test/native_provider_quarantine_live_test.dart';

void main() {
  final binding = IntegrationTestWidgetsFlutterBinding.ensureInitialized();
  runNativeProviderQuarantineAcceptance(
    capture: (name) async {
      await binding.takeScreenshot('phase-provider-$name');
    },
  );
}
