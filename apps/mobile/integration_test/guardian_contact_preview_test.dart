import 'package:academy_mobile/core/auth/auth_controller.dart';
import 'package:academy_mobile/core/auth/session.dart';
import 'package:academy_mobile/core/theme/app_theme.dart';
import 'package:academy_mobile/features/shared/guardian_contact.dart';
import 'package:flutter/material.dart';
import 'package:flutter_localizations/flutter_localizations.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:integration_test/integration_test.dart';

import '../test/auth_controller_test.dart'
    show FakeApi, MemoryStore, credentials;

// Synthetic UI preview: transport and launcher are test adapters. No external
// WhatsApp app is opened, no recipient is contacted, and no message is sent.
void main() {
  final binding = IntegrationTestWidgetsFlutterBinding.ensureInitialized();
  testWidgets('contact chooser native preview, large text and no external send', (
    t,
  ) async {
    final calls = <Uri>[];
    final api = FakeApi(
      (m, p, b, token) async => {
        'contacts': [
          {
            'id': 'guardian-a',
            'name': 'الأم الخيالية',
            'relationship': 'والدة',
            'phone': '+201000000001',
            'url':
                'https://wa.me/201000000001?text=${Uri.encodeComponent('مرحبًا من أكاديمية العرض.')}',
          },
          {
            'id': 'guardian-b',
            'name': 'الأب الخيالي',
            'relationship': 'والد',
            'phone': '+201000000002',
            'url':
                'https://wa.me/201000000002?text=${Uri.encodeComponent('مرحبًا من أكاديمية العرض.')}',
          },
        ],
      },
    );
    final auth = AuthController(api, MemoryStore());
    final session = credentials();
    (session['memberships'] as List).first['role'] = 'Coach';
    auth.session = Session.fromJson(session);
    Future<void> tick(int ms) async {
      for (var elapsed = 0; elapsed < ms; elapsed += 50) {
        await t.pump(const Duration(milliseconds: 50));
      }
    }

    Future<void> mount({bool large = false}) async {
      await t.pumpWidget(
        MaterialApp(
          locale: const Locale('ar'),
          supportedLocales: const [Locale('ar')],
          localizationsDelegates: GlobalMaterialLocalizations.delegates,
          theme: AcademyTheme.dark(),
          builder: (context, child) => MediaQuery(
            data: MediaQuery.of(context)
                .copyWith(textScaler: TextScaler.linear(large ? 2 : 1)),
            child: child!,
          ),
          home: Scaffold(
            appBar: AppBar(title: const Text('ملف اللاعب · معاينة')),
            body: Padding(
              padding: const EdgeInsets.all(20),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.stretch,
                children: [
                  const Text(
                    'معاينة اصطناعية بلا إرسال',
                    style: TextStyle(fontSize: 13),
                  ),
                  const SizedBox(height: 20),
                  const Icon(Icons.person_outline, size: 72),
                  const Text(
                    'لاعب خيالي',
                    style: TextStyle(fontSize: 24, fontWeight: FontWeight.bold),
                  ),
                  const Text('مجموعة التدريب · أكاديمية العرض'),
                  const SizedBox(height: 20),
                  GuardianContactAction(
                    auth: auth,
                    playerId: 'fictional-player',
                    openUrl: (u) async {
                      calls.add(u);
                      return false;
                    },
                  ),
                ],
              ),
            ),
          ),
        ),
      );
      await tick(600);
    }

    await mount();
    await binding.takeScreenshot('phase-contact-player-action');
    await t.tap(find.text('التواصل مع ولي الأمر عبر واتساب'));
    await tick(600);
    expect(calls, isEmpty);
    await binding.takeScreenshot('phase-contact-guardian-chooser');
    await t.tap(find.text('الأب الخيالي'));
    await tick(600);
    expect(calls.single.path, '/201000000002');
    expect(find.textContaining('تعذر فتح واتساب'), findsOneWidget);
    await mount(large: true);
    await t.tap(find.text('التواصل مع ولي الأمر عبر واتساب'));
    await tick(600);
    await binding.takeScreenshot('phase-contact-large-text');
    expect(t.takeException(), isNull);
  });
}
