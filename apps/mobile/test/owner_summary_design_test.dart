import 'package:academy_mobile/core/theme/academy_brand.dart';
import 'package:academy_mobile/core/theme/app_theme.dart';
import 'package:academy_mobile/features/owner/refined_home_panels.dart';
import 'package:academy_mobile/features/owner/summary_hero.dart';
import 'package:academy_mobile/features/shared/subscription_identity_card.dart';
import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_test/flutter_test.dart';

import 'v2_role_workspaces_test.dart' as f;

void main() {
  TestWidgetsFlutterBinding.ensureInitialized();
  setUpAll(() async {
    final font = FontLoader('AcademyArabic')
      ..addFont(rootBundle.load('assets/fonts/AcademyArabic-400.ttf'));
    await font.load();
  });
  final data = {
    'currency': 'EGP',
    'members': {'groups': 12, 'players': 50},
    'collections': {
      'net': 37434,
      'changePercentage': 54.0,
      'weekly': [
        for (var i = 0; i < 8; i++)
          {
            'from': '2026-09-01',
            'to': '2026-10-${i + 10}',
            'amount': [7200, 900, 4500, 0, 900, 6300, 10800, 19434][i],
          },
      ],
    },
    'target': {'amount': 45000, 'achievement': 83},
    'overdue': {'amount': 540},
    'risk': {'players': 10},
    'attendance': {'percentage': 78.8},
    'capacity': {'seats': 130},
  };
  for (final width in [320.0, 375.0, 440.0]) {
    for (final scale in [1.0, 2.0]) {
      for (final dark in [false, true]) {
        testWidgets('summary RTL $width scale $scale dark $dark', (t) async {
          t.view.physicalSize = Size(width, 1200);
          t.view.devicePixelRatio = 1;
          addTearDown(t.view.resetPhysicalSize);
          addTearDown(t.view.resetDevicePixelRatio);
          var target = 0, capacity = 0;
          String? report;
          Map<String, dynamic>? week;
          await t.pumpWidget(
            f.app(
              Theme(
                data: dark ? AcademyTheme.dark() : AcademyTheme.light(),
                child: Scaffold(
                  body: RepaintBoundary(
                    key: const ValueKey('preview'),
                    child: SingleChildScrollView(
                      padding: const EdgeInsets.all(16),
                      child: AcademySummaryPanel(
                        data: data,
                        onTarget: () => target++,
                        onCapacity: () => capacity++,
                        onReport: (v) => report = v,
                        onWeek: (v) => week = v,
                      ),
                    ),
                  ),
                ),
              ),
              scale: scale,
            ),
          );
          await t.pumpAndSettle();
          expect(t.takeException(), isNull);
          final targetButton = find.widgetWithText(
            OutlinedButton,
            'ضبط هدف التحصيل',
          );
          // Keep the action opposite its heading instead of below the totals.
          expect(
            t.getCenter(targetButton).dy,
            closeTo(t.getCenter(find.text('التحصيل خلال الفترة')).dy, 1),
          );
          expect(
            t.getCenter(targetButton).dx,
            lessThan(t.getCenter(find.text('التحصيل خلال الفترة')).dx),
          );
          if (scale == 1) {
            expect(t.getSize(find.byType(SummaryHero)).height, lessThan(290));
          }
          expect(find.byKey(const ValueKey('summary-watermark')), findsNothing);
          for (final label in [
            'مستحقات متأخرة',
            'عدد اللاعبين',
            'متوسط الحضور',
          ]) {
            await t.ensureVisible(find.text(label));
            await t.tap(find.text(label));
          }
          expect(report, 'attendance');
          await t.ensureVisible(find.text('أماكن تدريب متاحة'));
          await t.tap(find.text('أماكن تدريب متاحة'));
          expect(capacity, 1);
          await t.ensureVisible(find.text('ضبط هدف التحصيل'));
          await t.tap(find.text('ضبط هدف التحصيل'));
          expect(target, 1);
          await t.ensureVisible(find.text('19,434'));
          await t.tap(find.text('19,434'));
          expect(week?['amount'], 19434);
          expect(t.takeException(), isNull);
        });
      }
    }
  }
  testWidgets(
    'two tenants without logos never inherit bundled academy branding',
    (t) async {
      final auth = await f.authenticate(
        'AcademyOwner',
        (_, _, _, _) async => {},
      );
      final brand = AcademyBrand(auth);
      await brand.refresh(force: true);
      for (final tenant in ['الأكاديمية الأولى', 'الأكاديمية الثانية']) {
        brand.name = tenant;
        await t.pumpWidget(
          f.app(
            BrandScope(
              brand: brand,
              child: Scaffold(
                body: SummaryHero(data: data, onReport: () {}, onTarget: () {}),
              ),
            ),
          ),
        );
        await t.pumpAndSettle();
        expect(find.byType(Image), findsNothing);
      }
      for (final asset in [
        'assets/branding/professional-academy.png',
        'assets/onboarding/m1.jpg',
      ]) {
        await t.pumpWidget(const SizedBox.shrink());
        final bytes = (await rootBundle.load(asset)).buffer.asUint8List();
        brand.logo = bytes;
        await t.pumpWidget(
          f.app(
            BrandScope(
              brand: brand,
              child: Scaffold(
                body: SingleChildScrollView(
                  child: Column(
                    children: [
                      SubscriptionIdentityCard(
                        data: const {
                          'active': 20,
                          'expiring': 3,
                          'expired': 7,
                          'players': 50,
                        },
                        owner: true,
                        onTotal: () {},
                        onStatus: (_) {},
                      ),
                      SummaryHero(data: data, onReport: () {}, onTarget: () {}),
                    ],
                  ),
                ),
              ),
            ),
          ),
        );
        await t.pumpAndSettle();
        final image = t.widget<Image>(find.byType(Image));
        expect((image.image as MemoryImage).bytes, same(bytes));
        final targetText = t.widget<Text>(find.text('ضبط هدف التحصيل'));
        expect(targetText.maxLines, 1);
        expect(targetText.softWrap, false);
        expect(find.byKey(const ValueKey('summary-watermark')), findsNothing);
      }
      await t.pumpWidget(const SizedBox.shrink());
      brand.dispose();
      auth.dispose();
    },
  );
}
