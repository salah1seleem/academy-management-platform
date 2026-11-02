import 'package:academy_mobile/features/owner/inventory_preview.dart';
import 'package:academy_mobile/core/theme/app_theme.dart';
import 'package:academy_mobile/core/theme/appearance.dart';
import 'package:academy_mobile/features/guardian/guardian_ui.dart';
import 'package:academy_mobile/features/guardian/guardian_progress.dart';
import 'package:academy_mobile/features/guardian/enrollment_requests.dart';
import 'package:academy_mobile/features/guardian/family_media.dart';
import 'package:academy_mobile/features/guardian/guardian_payments.dart';
import 'package:academy_mobile/features/guardian/sport_store.dart';
import 'package:academy_mobile/features/shared/match_experience.dart';
import 'package:academy_mobile/features/shared/notification_inbox.dart';
import 'package:academy_mobile/core/theme/ui_text.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

import 'v2_role_workspaces_test.dart' as f;
import 'approved_product_variants_test.dart' as v;

void main() {
  testWidgets(
    'owner inventory identifies the color of duplicate sizes without commerce controls',
    (t) async {
      final calls = <String>[];
      final auth = await f.authenticate('AcademyOwner', (m, p, b, k) async {
        calls.add(m);
        return {
          'stockQuantity': 12,
          'variants': [
            {'size': '10', 'color': 'كحلي', 'quantity': 2},
            {'size': '10', 'color': 'أبيض', 'quantity': 2},
          ],
        };
      });
      await t.pumpWidget(
        f.app(
          OwnerInventoryRead(
            auth: auth,
            product: const {'id': 'kit', 'arabicName': 'طقم التدريب'},
          ),
        ),
      );
      await t.pumpAndSettle();
      expect(find.text('كحلي · مقاس 10'), findsOneWidget);
      expect(find.text('أبيض · مقاس 10'), findsOneWidget);
      expect(find.text('شراء'), findsNothing);
      expect(calls.every((m) => m == 'GET'), isTrue);
      expect(t.takeException(), isNull);
      await t.pumpWidget(const SizedBox());
      auth.dispose();
    },
  );
  for (final model in [VisualModel.pitch, VisualModel.premium]) {
    testWidgets('operation route retains full fixed palette: $model', (
      t,
    ) async {
      final prefs = Appearance();
      await prefs.update(visualModel: model);
      ThemeData? inside;
      final theme = AcademyTheme.themed(model);
      await t.pumpWidget(
        MaterialApp(
          theme: theme,
          home: AppearanceScope(
            appearance: prefs,
            child: GuardianPage(
              title: 'اشتراك جديد',
              operation: true,
              child: Builder(
                builder: (context) {
                  inside = Theme.of(context);
                  return const Text('اختيار الباقة');
                },
              ),
            ),
          ),
        ),
      );
      await t.pumpAndSettle();
      expect(
        inside!.colorScheme.surfaceContainerLowest,
        theme.colorScheme.surfaceContainerLowest,
      );
      expect(
        inside!.colorScheme.outlineVariant,
        theme.colorScheme.outlineVariant,
      );
      expect(inside!.colorScheme.onPrimary, theme.colorScheme.onPrimary);
      expect(
        theme.navigationBarTheme.iconTheme!.resolve({})!.color,
        theme.colorScheme.onSurfaceVariant,
      );
      expect(t.takeException(), isNull);
      await t.pumpWidget(const SizedBox());
      prefs.dispose();
    });
  }

  testWidgets(
    'progress previews published criteria and filters each child before full report',
    (t) async {
      final auth = await f.authenticate('Guardian', (m, p, b, k) async => {});
      await t.pumpWidget(
        f.app(
          Scaffold(
            body: GuardianProgress(
              auth: auth,
              children: const [
                {
                  'id': 'y',
                  'arabicName': 'يوسف',
                  'sports': [
                    {
                      'sport': 'كرة القدم',
                      'group': 'منتخب 2013',
                      'performance': {
                        'evaluationId': 'r',
                        'evaluationDate': '2026-10-28',
                        'meanPerformance': 85,
                        'criteria': [
                          {'name': 'السرعة', 'value': 86},
                        ],
                      },
                    },
                  ],
                },
                {
                  'id': 'l',
                  'arabicName': 'ليلى',
                  'sports': [
                    {'sport': 'السباحة', 'group': 'مستوى 2'},
                  ],
                },
              ],
            ),
          ),
        ),
      );
      await t.pumpAndSettle();
      expect(find.text('85'), findsOneWidget);
      expect(find.text('السرعة · 86'), findsOneWidget);
      await t.tap(find.byKey(const ValueKey('family-filter-l')));
      await t.pumpAndSettle();
      expect(find.text('85'), findsNothing);
      expect(
        find.text('لا يوجد تقييم منشور لهذه الرياضة بعد.'),
        findsOneWidget,
      );
      expect(t.takeException(), isNull);
      await t.pumpWidget(const SizedBox());
      auth.dispose();
    },
  );
  testWidgets(
    'notification polling invalidates home only after a new authorized snapshot',
    (t) async {
      var unread = 0;
      var refreshes = 0;
      final auth = await f.authenticate(
        'Guardian',
        (m, p, b, k) async => p == '/api/v1/notifications'
            ? {
                'unread': unread,
                'items': unread == 0
                    ? []
                    : [
                        {'id': 'approved', 'isRead': false},
                      ],
              }
            : [],
      );
      final changes = notificationChanges(auth);
      void changed() {
        refreshes++;
      }

      changes.addListener(changed);
      await t.pumpWidget(f.app(Scaffold(body: NotificationBell(auth: auth))));
      await t.pumpAndSettle();
      expect(refreshes, 0);
      unread = 1;
      await t.pump(const Duration(seconds: 30));
      await t.pumpAndSettle();
      expect(refreshes, 1);
      await t.pump(const Duration(seconds: 30));
      await t.pumpAndSettle();
      expect(refreshes, 1);
      changes.removeListener(changed);
      await t.pumpWidget(const SizedBox());
      auth.dispose();
    },
  );
  testWidgets(
    'existing owned enrollment leads to renewal without duplicate creation',
    (t) async {
      var writes = 0;
      final auth = await f.authenticate('Guardian', (m, p, b, k) async {
        if (m == 'POST') writes++;
        if (p.endsWith('/enrollment-request-options')) {
          return {
            'children': [
              {
                'id': 'c',
                'name': 'يوسف خالد',
                'enrollments': [
                  {
                    'id': 'e',
                    'sportId': 's',
                    'branchId': 'b',
                    'registrationFormat': 'Group',
                  },
                ],
              },
            ],
            'sports': [
              {'id': 's', 'name': 'كرة القدم'},
            ],
            'branches': [
              {'id': 'b', 'name': 'مدينتي'},
            ],
            'plans': [
              {
                'id': 'p',
                'sportId': 's',
                'arabicName': '12 حصة',
                'price': 100,
                'currency': 'EGP',
                'allBranches': true,
                'trainingFormat': 'Group',
              },
            ],
          };
        }
        return [];
      });
      await t.pumpWidget(
        f.app(EnrollmentForm(auth: auth, environment: 'Demo')),
      );
      await t.pumpAndSettle();
      for (final pair in [
        ('الطفل', 'يوسف خالد'),
        ('الرياضة', 'كرة القدم'),
        ('الفرع المفضل', 'مدينتي'),
      ]) {
        await v.choose(t, pair.$1, pair.$2);
      }
      final dropdown = find.widgetWithText(
        DropdownButtonFormField<String>,
        'الباقة',
      );
      await t.ensureVisible(dropdown);
      await t.tap(dropdown);
      await t.pumpAndSettle();
      await t.tap(find.textContaining('12 حصة').last);
      await t.pumpAndSettle();
      final renewal = find.widgetWithText(
        FilledButton,
        'تجديد الاشتراك الحالي',
      );
      await t.ensureVisible(renewal);
      await t.pumpAndSettle();
      await t.drag(find.byType(Scrollable).first, const Offset(0, -120));
      await t.pumpAndSettle();
      await t.tap(renewal);
      await t.pumpAndSettle();
      expect(find.byType(PlanPicker), findsOneWidget);
      expect(writes, 0);
      expect(t.takeException(), isNull);
      await t.pumpWidget(const SizedBox());
      auth.dispose();
    },
  );
  testWidgets('same size in two colors submits only selected color variant', (
    t,
  ) async {
    Map? sent;
    final auth = await f.authenticate('Guardian', (m, p, b, k) async {
      if (p.endsWith('/enrollments')) return v.children;
      if (p.contains('/variants')) {
        return {
          'usesVariants': true,
          'numberedUniform': false,
          'unitPrice': 90,
          'variants': [
            {'id': 'navy', 'size': 'M', 'color': 'كحلي', 'quantity': 2},
            {'id': 'white', 'size': 'M', 'color': 'أبيض', 'quantity': 1},
          ],
        };
      }
      if (m == 'POST' && p.endsWith('/product-sales')) {
        sent = b as Map;
        return {'id': 'sale', 'amount': 90, 'currency': 'EGP'};
      }
      return {};
    });
    await t.pumpWidget(f.app(ProductPurchase(auth: auth, product: v.product)));
    await t.pumpAndSettle();
    await v.choose(t, 'اللون', 'أبيض');
    await v.choose(t, 'المقاس', 'M');
    await v.tap(t, 'متابعة إلى الدفع');
    expect(sent, containsPair('variantId', 'white'));
    expect(t.takeException(), isNull);
    await t.pumpWidget(const SizedBox());
    auth.dispose();
  });
  testWidgets(
    'family gallery keeps linked child reads and deduplicates shared media',
    (t) async {
      final reads = <String>[];
      final auth = await f.authenticate('Guardian', (m, p, b, k) async {
        reads.add(p);
        if (p.startsWith('/api/v1/assets/gallery?playerId=')) {
          return [
            {
              'id': 'shared',
              'contentType': 'video/mp4',
              'caption': 'تدريب الأسرة',
              'url': '/api/v1/assets/shared',
              'createdAtUtc': '2026-10-28',
            },
          ];
        }
        return [];
      });
      await t.pumpWidget(
        f.app(
          Scaffold(
            body: SingleChildScrollView(
              child: FamilyTrainingMedia(
                auth: auth,
                children: const [
                  {'id': 'a', 'arabicName': 'يوسف'},
                  {'id': 'b', 'arabicName': 'ليلى'},
                ],
              ),
            ),
          ),
        ),
      );
      await t.pumpAndSettle();
      expect(reads.where((p) => p.contains('/gallery?playerId=')).toSet(), {
        '/api/v1/assets/gallery?playerId=a',
        '/api/v1/assets/gallery?playerId=b',
      });
      expect(find.text('تدريب الأسرة'), findsOneWidget);
      expect(find.textContaining('يوسف'), findsOneWidget);
      expect(t.takeException(), isNull);
      await t.pumpWidget(const SizedBox());
      auth.dispose();
    },
  );
  testWidgets(
    'winning and losing scores have distinct colors and draw scores match',
    (t) async {
      await t.pumpWidget(
        f.app(
          const Scaffold(
            body: MatchResultCard(
              data: {
                'status': 'Completed',
                'ourScore': 3,
                'opponentScore': 1,
                'team': 'النخبة',
                'opponent': 'النجوم',
              },
            ),
          ),
        ),
      );
      await t.pumpAndSettle();
      final winner = t
          .widget<UiText>(find.widgetWithText(UiText, '3'))
          .style!
          .color;
      final loser = t
          .widget<UiText>(find.widgetWithText(UiText, '1'))
          .style!
          .color;
      expect(winner, loser);
      await t.pumpWidget(
        f.app(
          const Scaffold(
            body: MatchResultCard(
              data: {
                'status': 'Completed',
                'ourScore': 2,
                'opponentScore': 2,
                'team': 'النخبة',
                'opponent': 'النجوم',
              },
            ),
          ),
        ),
      );
      await t.pumpAndSettle();
      final colors = t
          .widgetList<UiText>(find.widgetWithText(UiText, '2'))
          .map((w) => w.style!.color)
          .toSet();
      expect(colors.length, 1);
      expect(colors.single, isNot(winner));
      expect(colors.single, isNot(loser));
      expect(t.takeException(), isNull);
    },
  );
}
