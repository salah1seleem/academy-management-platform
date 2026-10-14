import 'package:academy_mobile/core/auth/session.dart';
import 'package:academy_mobile/app/app.dart';
import 'package:academy_mobile/core/auth/auth_controller.dart';
import 'package:flutter_test/flutter_test.dart';

import 'auth_controller_test.dart';

void main() {
  test('Two admin contexts retain independent names, branches and sports', () {
    final strong = Membership.fromJson({
      'id': 'strong',
      'academyId': 'same',
      'academyName': 'أكاديمية العرض',
      'role': 'AcademyAdmin',
      'contextName': 'تشغيل كرة القدم',
      'allBranches': false,
      'allSports': false,
      'branchNames': ['مدينة نصر'],
      'sportNames': ['كرة القدم'],
    });
    final weak = Membership.fromJson({
      'id': 'weak',
      'academyId': 'same',
      'academyName': 'أكاديمية العرض',
      'role': 'AcademyAdmin',
      'contextName': 'متابعة السباحة',
      'contextRevision': 7,
      'allBranches': false,
      'allSports': false,
      'branchNames': ['التجمع'],
      'sportNames': ['السباحة'],
    });
    expect(strong.label, 'تشغيل كرة القدم');
    expect(weak.label, 'متابعة السباحة');
    expect(strong.scopeLabel, 'مدينة نصر · كرة القدم');
    expect(weak.scopeLabel, 'التجمع · السباحة');
    expect(weak.role, 'AcademyAdmin');
    expect(weak.contextRevision, 7);
    expect(weak.id, isNot(strong.id));
  });
  testWidgets(
    'Same-role selector shows names and paired scopes, not two indistinguishable admin roles',
    (tester) async {
      final json = credentials(multi: true);
      json['memberships'] = [
        {
          'id': 'strong',
          'academyId': 'same',
          'academyName': 'أكاديمية العرض',
          'role': 'AcademyAdmin',
          'contextName': 'تشغيل كرة القدم',
          'allBranches': false,
          'allSports': false,
          'branchNames': ['مدينة نصر'],
          'sportNames': ['كرة القدم'],
          'contextRevision': 2,
        },
        {
          'id': 'weak',
          'academyId': 'same',
          'academyName': 'أكاديمية العرض',
          'role': 'AcademyAdmin',
          'contextName': 'متابعة السباحة',
          'allBranches': false,
          'allSports': false,
          'branchNames': ['التجمع'],
          'sportNames': ['السباحة'],
          'contextRevision': 3,
        },
      ];
      final auth = AuthController(
        FakeApi((a, b, c, d) async => json),
        MemoryStore(),
      )..restoring = false;
      await auth.verify('phone', 'challenge', 'otp');
      await tester.pumpWidget(AcademyApp(auth: auth, environment: 'Demo'));
      expect(find.text('اختر الأكاديمية والدور'), findsOneWidget);
      expect(
        find.textContaining('تشغيل كرة القدم\nمدينة نصر · كرة القدم'),
        findsOneWidget,
      );
      expect(
        find.textContaining('متابعة السباحة\nالتجمع · السباحة'),
        findsOneWidget,
      );
      expect(auth.session!.membershipId, isNull);
    },
  );
  test('Legacy membership without context metadata keeps its role label', () {
    final membership = Membership.fromJson({
      'id': 'coach',
      'academyId': 'same',
      'academyName': 'أكاديمية العرض',
      'role': 'Coach',
    });
    expect(membership.label, 'مدرب');
    expect(membership.contextName, isNull);
  });
}
