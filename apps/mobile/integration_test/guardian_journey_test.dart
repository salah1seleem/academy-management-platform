import 'package:academy_mobile/app/app.dart';
import 'package:academy_mobile/core/api/api_client.dart';
import 'package:academy_mobile/core/auth/auth_controller.dart';
import 'package:academy_mobile/core/storage/refresh_store.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:integration_test/integration_test.dart';

void main() {
  final binding = IntegrationTestWidgetsFlutterBinding.ensureInitialized();
  testWidgets(
    'Guardian real Demo native profile, records, renewal, receipt and opaque beneficiary',
    (tester) async {
      final config = AppConfig(
        'Demo',
        const String.fromEnvironment('API_BASE_URL'),
      );
      final transport = HttpApiTransport(config),
          store = SecureRefreshStore(
            '${config.storageKey}.guardian-integration',
          );
      await store.clear();
      final auth = AuthController(transport, store);
      await auth.restore();
      Future<void> waitFor(Finder finder) async {
        for (var i = 0; i < 100 && finder.evaluate().isEmpty; i++) {
          await tester.pump(const Duration(milliseconds: 150));
        }
        expect(finder, findsWidgets);
        await tester.pumpAndSettle();
      }

      Future<void> tap(String text) async {
        final f = text == 'اختيار الباقة'
            ? find.widgetWithText(FilledButton, text)
            : find.text(text);
        if (f.evaluate().isEmpty) {
          await tester.scrollUntilVisible(
            f,
            220,
            scrollable: find.byType(Scrollable).first,
          );
        }
        await tester.ensureVisible(f.first);
        await tester.tap(f.first);
        await tester.pumpAndSettle();
      }

      Future<void> back() async {
        await tester.tap(find.byType(BackButton).last);
        await tester.pumpAndSettle();
      }

      Future<void> capture(String name) async {
        if (const bool.fromEnvironment('CAPTURE_VISUALS')) {
          await tester.pumpAndSettle();
          await binding.takeScreenshot('phase-d-$name');
        }
      }

      await tester.pumpWidget(AcademyApp(auth: auth, environment: 'Demo'));
      await tester.pumpAndSettle();
      // Dedicated synthetic guardian avoids coupling this journey to auth_smoke's rate window.
      await tester.enterText(find.byType(TextFormField).first, '01099900108');
      await tap('إرسال رمز التحقق');
      await waitFor(find.text('رمز التحقق'));
      await tester.enterText(find.byType(TextFormField).last, '246810');
      await tap('تأكيد الدخول');
      await waitFor(find.text('أبنائي'));
      final home = await auth.request(
        'GET',
        '/api/v1/guardian/home',
      ) as Map<String, dynamic>;
      final children = home['children'] as List;
      expect(children.length, greaterThanOrEqualTo(1));
      final childIds = children.map((x) => x['id'] as String).toList();
      await capture('home');
      await tap('عرض الملف');
      await waitFor(find.text('تقرير اللاعب'));
      await capture('profile');
      await tap('تقرير اللاعب');
      await waitFor(find.text('التقييمات المنشورة · اختر تاريخ التقرير'));
      await tester.tap(find.byIcon(Icons.insights).first);
      await tester.pumpAndSettle();
      await waitFor(find.text('التقييم العام'));
      await capture('report');
      expect(find.text('مخطط الملعب'), findsNothing);
      await back();
      await back();
      await tap('التدريب والمواعيد');
      await waitFor(find.text('الجلسات القادمة'));
      await back();
      await tap('التغذية والصحة');
      await waitFor(find.text('شوفان بالحليب والموز'));
      await capture('nutrition');
      await tap('شوفان بالحليب والموز');
      await waitFor(find.text('القيم الغذائية'));
      await capture('meal');
      await back();
      await back();
      await tap('الإصابات والاستشارات الطبية');
      await waitFor(find.text('السجلات المنشورة لهذا الطفل فقط'));
      expect(find.textContaining('StaffNotes'), findsNothing);
      await back();
      await tap('الصور والفيديوهات');
      await waitFor(find.text('الصور والفيديوهات'));
      await capture('gallery');
      await back();
      await tap('الحضور');
      await waitFor(find.text('حاضر'));
      await back();
      await back();
      await tester.tap(
        find.byKey(const ValueKey('guardian-action-اشتراك جديد')),
      );
      await tester.pumpAndSettle();
      await waitFor(find.text('اسم الطفل'));
      await tap('إرسال طلب الاشتراك');
      expect(find.text('أدخل اسم الطفل.'), findsOneWidget);
      await back();
      await tester.tap(
        find.byKey(const ValueKey('guardian-action-تجديد الاشتراك')),
      );
      await tester.pumpAndSettle();
      await capture('renewal-picker');
      await waitFor(find.text('اختر الطفل والرياضة'));
      await tester.tap(find.byIcon(Icons.sports_soccer).first);
      await tester.pumpAndSettle();
      await waitFor(find.text('اختيار الباقة'));
      await tap('اختيار الباقة');
      await capture('renewal');
      await tap('متابعة إلى الدفع');
      await waitFor(find.text('محاكاة دفع ناجح'));
      await tap('محاكاة دفع ناجح');
      await waitFor(find.text('عرض الإيصال'));
      await tap('عرض الإيصال');
      await waitFor(find.text('الإيصال صادر من سجل التحصيل على الخادم.'));
      await capture('receipt');
      // A fresh session proves private routes are destroyed, then starts the
      // independent renew-for-another journey from Home.
      await auth.logout();
      await tester.pumpAndSettle();
      expect(find.text('رقم الهاتف'), findsOneWidget);
      await tester.enterText(find.byType(TextFormField).first, '01099900108');
      await tap('إرسال رمز التحقق');
      await waitFor(find.text('رمز التحقق'));
      await tester.enterText(find.byType(TextFormField).last, '246810');
      await tap('تأكيد الدخول');
      await waitFor(find.text('أبنائي'));
      await tester.tap(
        find.byKey(const ValueKey('guardian-action-تجديد اشتراك لغيره')),
      );
      await tester.pumpAndSettle();
      await tester.enterText(find.byType(TextField), 'RNW-DEMO-FB-0030-8J7P');
      await tap('التحقق من الكود');
      await waitFor(find.text('اختيار الباقة'));
      await tap('اختيار الباقة');
      await tap('متابعة إلى الدفع');
      await waitFor(find.text('محاكاة دفع ناجح'));
      await tap('محاكاة فشل الدفع');
      await waitFor(find.text('إعادة محاولة الدفع'));
      await tap('إعادة محاولة الدفع');
      await waitFor(find.text('محاكاة دفع ناجح'));
      await tap('محاكاة دفع ناجح');
      await waitFor(find.text('عرض الإيصال'));
      await tap('عرض الإيصال');
      await waitFor(find.text('الإيصال صادر من سجل التحصيل على الخادم.'));
      final after = await auth.request(
        'GET',
        '/api/v1/guardian/home',
      ) as Map<String, dynamic>;
      expect((after['children'] as List).map((x) => x['id']), childIds);
      // Deep private screen must disappear on session end.
      await auth.logout();
      await tester.pumpAndSettle();
      expect(find.text('رقم الهاتف'), findsOneWidget);
      expect(find.text('إيصال التحصيل'), findsNothing);
      await tester.pumpWidget(const SizedBox.shrink());
      auth.dispose();
      transport.close();
    },
  );
}
