import 'package:academy_mobile/core/auth/auth_controller.dart';
import 'package:academy_mobile/features/guardian/guardian_ui.dart';
import 'package:flutter_test/flutter_test.dart';

const receivableDemoEnrollment = 'ea8b9203-eca9-9054-e7b4-37d17f9417c6';
const receivableDemoPlayer = '46eac86f-4f04-d1bb-00c7-b3749e2210a4';
const receivableDemoCollection = 'fac17c5f-6012-d75f-a6dd-e487cfb603c3';
const receivableDemoMarker = 'عرض مستحقات V4 اصطناعي';

/// Explicit isolated-demo preparation through existing authorized services.
/// Existing paid source only; no new collections, receipts or periods, no SQL.
Future<Json> prepareReceivableFixture(AuthController auth) async {
  expect(const bool.fromEnvironment('ISOLATED_DEMO'), true);
  expect(auth.session!.selected!.role, 'AcademyOwner');
  expect(
    auth.session!.selected!.academyId,
    '30000000-0000-0000-0000-000000000003',
  );
  final calendar = obj(await auth.request('GET', '/api/v1/academy/calendar'));
  expect(textOf(calendar['today']).compareTo('2026-10-01'), greaterThan(0));
  expect(textOf(calendar['today']).compareTo('2026-11-30'), lessThan(0));
  final options = obj(
    await auth.request(
      'GET',
      '/api/v1/finance/receivables/options?${Uri(queryParameters: {'search': 'آدم خالد'}).query}',
    ),
  );
  expect(
    rows(options['enrollments'])
        .any((e) => e['id'] == receivableDemoEnrollment),
    true,
  );
  final before = obj(
    await auth.request(
      'GET',
      '/api/v1/reports/financial?allDates=true&pageSize=25',
    ),
  );
  Future<String> charge(
    String code,
    String amount,
    String date,
    String description,
  ) async {
    final body = {
      'sportEnrollmentId': receivableDemoEnrollment,
      'planId': null,
      'amount': amount,
      'dueDate': date,
      'source': 'ManualCharge',
      'description': '$receivableDemoMarker — $description',
    };
    final key = 'v4-receivable-$code-20261022-v1';
    final existing = rows(
      obj(
        await auth.request(
          'GET',
          '/api/v1/finance/receivables?playerId=$receivableDemoPlayer',
        ),
      )['items'],
    ).where((row) => row['description'] == body['description']).toList();
    expect(existing.length, lessThanOrEqualTo(1));
    if (existing.isNotEmpty) {
      final document = obj(
        obj(
          await auth.request(
            'GET',
            '/api/v1/finance/receivables/${existing.single['id']}',
          ),
        )['document'],
      );
      expect(document['sportEnrollmentId'], receivableDemoEnrollment);
      expect(document['amount'], amount);
      expect(document['currency'], 'EGP');
      expect(document['dueDate'], date);
      expect(document['source'], 'ManualCharge');
      expect(textOf(document['sourceKey']).endsWith(':$key'), true);
      return textOf(document['id']);
    }
    final saved = obj(
      await auth.request(
        'POST',
        '/api/v1/finance/receivables/',
        body: body,
        idempotencyKey: key,
      ),
    );
    final replay = obj(
      await auth.request(
        'POST',
        '/api/v1/finance/receivables/',
        body: body,
        idempotencyKey: key,
      ),
    );
    expect(replay['id'], saved['id']);
    return textOf(saved['id']);
  }

  final overdue = await charge(
    'overdue',
    '540.00',
    '2026-10-01',
    'مطالبة متأخرة540',
  );
  final future = await charge(
    'future',
    '500.00',
    '2026-11-30',
    'مطالبة غير مستحقة500',
  );
  final credit = await charge(
    'credit',
    '200.00',
    '2026-10-01',
    'مستند مسدد ثم مصحح؛ رصيد لصالح الأسرة100',
  );
  final prior = obj(
    await auth.request('GET', '/api/v1/finance/receivables/$credit'),
  );
  const allocationKey = 'v4-receivable-allocation-20261022-v1';
  if (!rows(prior['movements'])
      .any((m) => textOf(m['sourceKey']).endsWith(':$allocationKey'))) {
    final available = rows(
      await auth.request(
        'GET',
        '/api/v1/finance/receivables/$credit/collections',
      ),
    );
    final source = available.singleWhere(
      (c) => c['id'] == receivableDemoCollection,
    );
    expect(num.parse(textOf(source['available'])), greaterThanOrEqualTo(200));
  }
  for (final movement in [
    (
      allocationKey,
      {
        'kind': 'PaymentAllocation',
        'amount': '200.00',
        'collectionId': receivableDemoCollection,
        'reason': 'تخصيص200 من تحصيل عرض محفوظ900؛ لا قبض جديد',
      },
    ),
    (
      'v4-receivable-note-20261022-v1',
      {
        'kind': 'CreditNote',
        'amount': '100.00',
        'collectionId': null,
        'reason': 'تصحيح100 لمستند العرض بعد سداد200؛ رصيد دائن دون صرف نقدي',
      },
    ),
  ]) {
    final path = '/api/v1/finance/receivables/$credit/movements';
    final existing = rows(prior['movements'])
        .where((m) => m['kind'] == movement.$2['kind'])
        .toList();
    if (existing.isNotEmpty) {
      expect(existing.length, 1);
      for (final field in ['amount', 'collectionId', 'reason']) {
        expect(existing.single[field], movement.$2[field]);
      }
      expect(
        textOf(existing.single['sourceKey']).endsWith(':${movement.$1}'),
        true,
      );
      continue;
    }
    final saved = obj(
      await auth.request(
        'POST',
        path,
        body: movement.$2,
        idempotencyKey: movement.$1,
      ),
    );
    final replay = obj(
      await auth.request(
        'POST',
        path,
        body: movement.$2,
        idempotencyKey: movement.$1,
      ),
    );
    expect(replay['id'], saved['id']);
  }
  final result = obj(
    await auth.request(
      'GET',
      '/api/v1/finance/receivables?playerId=$receivableDemoPlayer',
    ),
  );
  expect(result['outstanding'], '1040.00');
  expect(result['overdue'], '540.00');
  expect(result['notYetDue'], '500.00');
  expect(result['overpaymentCredit'], '100.00');
  expect(rows(result['items']).length, 3);
  final after = obj(
    await auth.request(
      'GET',
      '/api/v1/reports/financial?allDates=true&pageSize=25',
    ),
  );
  expect(after['totalCount'], before['totalCount']);
  expect(after['netAmount'], before['netAmount']);
  return {
    'overdueId': overdue,
    'futureId': future,
    'creditId': credit,
    'summary': result,
  };
}
