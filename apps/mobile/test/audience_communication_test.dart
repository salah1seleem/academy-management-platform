import 'support/visibility.dart';

import 'package:academy_mobile/features/shared/audience_communication.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

import 'v2_role_workspaces_test.dart' show authenticate;
import 'owner_test.dart' show shell;

void main() {
  final data = <String, dynamic>{
    'name': 'مجموعة خيالية',
    'playerCount': 2,
    'recipientCount': 1,
    'canSend': true,
    'canManageLink': true,
    'version': 0,
    'groupName': 'أكاديمية · مجموعة',
    'invitationText': 'دعوة اختيارية',
  };
  testWidgets(
    'preview is server-derived, send uses the same UUID and a double tap stays one send',
    (t) async {
      final calls = <Map<String, dynamic>>[];
      final auth = await authenticate('AcademyOwner', (m, p, b, token) async {
        if (m == 'GET') return data;
        calls.add(Map<String, dynamic>.from(b as Map));
        return p.endsWith('/preview')
            ? {
                'previewHash': 'q',
                'name': 'مجموعة خيالية',
                'message': 'تذكير عام',
                'recipientCount': 1,
                'playerCount': 2,
              }
            : {'sent': 1};
      });
      await t.pumpWidget(shell(AudienceCommunicationPage(auth: auth, id: 'g')));
      await t.pumpAndSettle();
      await t.enterText(find.byType(TextFormField).first, 'تذكير عام');
      await t.pump();
      await revealTestTarget(t, find.text('مراجعة الرسالة والمستلمين'));
      await t.tap(find.text('مراجعة الرسالة والمستلمين'));
      await t.pumpAndSettle();
      expect(find.text('المعاينة · 1 ولي أمر'), findsOneWidget);
      await revealTestTarget(t, find.text('تأكيد الإرسال'));
      await t.pumpAndSettle();
      await t.tap(find.text('تأكيد الإرسال'));
      await t.pumpAndSettle();
      expect(calls.length, 2);
      expect(calls[1]['requestId'], calls[0]['requestId']);
      expect(calls[1]['previewHash'], 'q');
      expect(calls[1].containsKey('recipients'), false);
      expect(find.textContaining('تم إرسال 1 إشعار'), findsOneWidget);
      await t.pumpWidget(const SizedBox());
      auth.dispose();
    },
  );
  testWidgets(
    'no sending controls without permission, manual setup labels remain honest at 200%',
    (t) async {
      final auth = await authenticate(
        'Coach',
        (m, p, b, token) async => {
          ...data,
          'canSend': false,
          'canManageLink': false,
        },
      );
      await t.pumpWidget(
        shell(
          MediaQuery(
            data: const MediaQueryData(
              size: Size(320, 700),
              textScaler: TextScaler.linear(2),
            ),
            child: AudienceCommunicationPage(auth: auth, id: 'g'),
          ),
        ),
      );
      await t.pumpAndSettle();
      expect(find.text('مراجعة الرسالة والمستلمين'), findsNothing);
      await revealTestTarget(t, find.text('واتساب'));
      await t.pumpAndSettle();
      await t.tap(find.text('واتساب'));
      await t.pumpAndSettle();
      expect(find.text('حفظ رابط الدعوة'), findsNothing);
      expect(t.takeException(), isNull);
      await t.pumpWidget(const SizedBox());
      auth.dispose();
    },
  );
}
