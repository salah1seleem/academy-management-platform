"""API-only financial preparation for the disposable hosted owner QA suite.

SQL is read-only here. No endpoint, database or academy can be supplied by a caller.
Tokens remain in memory and are never included in evidence or Dart defines.
"""
import datetime as dt
from decimal import Decimal
import json
import os
from pathlib import Path
import subprocess
import sys
import urllib.request

BASE = 'http://127.0.0.1:5397'
A = '10000000-0000-0000-0000-000000000001'
B = '20000000-0000-0000-0000-000000000002'
CITY = '31000000-0000-0000-0000-000000000001'
ZAG = '31000000-0000-0000-0000-000000000003'
FIFTH = '31000000-0000-0000-0000-000000000002'
B_BRANCH = '41000000-0000-0000-0000-000000000001'
P1 = '36000000-0000-0000-0000-000000000001'


def call(method, path, token=None, body=None, key=None):
    assert path.startswith('/api/v1/') and '..' not in path
    headers = {'Accept': 'application/json', 'Content-Type': 'application/json'}
    if token:
        headers['Authorization'] = 'Bearer ' + token
    if key:
        headers['Idempotency-Key'] = key
    request = urllib.request.Request(BASE + path, method=method, headers=headers,
                                     data=None if body is None else json.dumps(body).encode())
    with urllib.request.urlopen(request, timeout=30) as response:
        return json.load(response)


def login(phone, academy):
    challenge = call('POST', '/api/v1/mobile/auth/otp/request', body={'phoneNumber': phone})
    session = call('POST', '/api/v1/mobile/auth/otp/verify', body={
        'phoneNumber': phone, 'challengeId': challenge['challengeId'],
        'code': '246810', 'deviceName': 'Isolated owner fixture'})
    membership = next(m for m in session['memberships']
                      if m['academyId'] == academy and m['role'] == 'AcademyOwner')
    if session['membershipId'] != membership['id']:
        session = call('POST', '/api/v1/mobile/auth/select-role', session['accessToken'],
                       {'membershipId': membership['id']})
    assert session['membershipId'] == membership['id']
    return session['accessToken']


def snapshot():
    result = subprocess.run([
        'psql', '-h', '127.0.0.1', '-p', '55432', '-U', 'academy_ci',
        '-d', 'academy_ios_qa', '-At', '-v', 'ON_ERROR_STOP=1',
        '-f', str(Path(__file__).with_name('ios-owner-financial-snapshot.sql')),
    ], capture_output=True, text=True, check=True)
    return json.loads(result.stdout)


def main():
    assert os.environ.get('GITHUB_ACTIONS') == 'true'
    assert os.environ.get('RUNNER_ENVIRONMENT') == 'github-hosted'
    assert os.environ.get('ASPNETCORE_ENVIRONMENT') == 'Demo'
    assert os.environ.get('Demo__SeedProfile') == 'LegacyRegression'
    folder = Path(os.environ['RUNNER_TEMP']) / 'academy-ios-qa/evidence'
    assert folder.is_dir()
    before = snapshot()
    owner = login('01000000010', A)
    foreign = login('01088800003', B)
    calendar = call('GET', '/api/v1/academy/calendar', owner)
    day = dt.date.fromisoformat(calendar['today'])
    # Remove only positive overdue balances via normal, auditable credit movements.
    prepared = []
    ledger = call('GET', '/api/v1/finance/receivables/', owner)
    for row in ledger['items']:
        if row['overdue'] and Decimal(str(row['outstanding'])) > 0:
            value = call('POST', f"/api/v1/finance/receivables/{row['id']}/movements", owner,
                         {'kind': 'CreditNote', 'amount': row['outstanding'],
                          'collectionId': None, 'reason': 'تهيئة رصيد ديمو الاختبار المستقل'},
                         'ohf-prep-credit-' + row['id'])
            prepared.append({'documentId': row['id'], 'movementId': value['id'],
                             'amount': row['outstanding']})
    remaining = call('GET', '/api/v1/finance/receivables/', owner)
    assert Decimal(remaining['overdue']) == 0
    enrollments = before['enrollments']
    p1 = next(e for e in enrollments if e['AcademyId'] == A and e['PlayerId'] == P1 and e['BranchId'] == CITY)
    p2 = next(e for e in enrollments if e['AcademyId'] == A and e['PlayerId'] != P1 and e['BranchId'] == ZAG)
    other = next(e for e in enrollments if e['AcademyId'] == B)
    charge = call('POST', '/api/v1/finance/receivables/', foreign, {
        'sportEnrollmentId': other['Id'], 'planId': None, 'amount': '85.00',
        'dueDate': (day - dt.timedelta(days=1)).isoformat(), 'source': 'ManualCharge',
        'description': 'ضابط موجب مستقل لعزل الأكاديمية الثانية'}, 'ohf-prep-b-charge')
    service = (day - dt.timedelta(days=45)).isoformat()
    expense = call('POST', '/api/v1/finance/operating-expenses/', foreign, {
        'branchId': B_BRANCH, 'scope': 'Branch', 'targetId': B_BRANCH,
        'category': 'Maintenance', 'periodFrom': service, 'periodTo': service,
        'paidOn': day.isoformat(), 'amount': '19.99', 'currency': 'EGP',
        'note': 'مصروف ديمو مستقل لعزل الأكاديمية الثانية'}, 'ohf-prep-b-expense')
    root = '/api/v1/finance/operating-expenses/' + expense['id']
    detail = call('GET', root, foreign)
    if detail['expense']['status'] != 'Posted':
        if detail['expense']['status'] == 'Draft':
            call('POST', root + '/approve', foreign, {})
        quote = call('POST', root + '/quote', foreign, {})
        call('POST', root + '/post', foreign, {'quoteHash': quote['quoteHash']}, 'ohf-prep-b-post')
    control = call('GET', '/api/v1/reports/owner-cockpit', foreign)
    assert Decimal(str(control['overdue']['amount'])) == 85
    statement = call('GET', '/api/v1/reports/operating-statement?allDates=true', foreign)
    assert Decimal(str(statement['totalExpenses'])) == Decimal('19.99')
    after = snapshot()
    for table in ['collections', 'receipts', 'periods', 'players', 'enrollments',
                  'refunds', 'cashRefundPostings', 'cashSessions', 'receivableCreditApplications']:
        assert before[table] == after[table], table
    fixture = {'today': day.isoformat(), 'serviceDay': service,
               'academyId': A, 'otherAcademyId': B, 'p1': p1, 'p2': p2,
               'branchCity': CITY, 'branchZag': ZAG, 'branchEmpty': FIFTH,
               'controlChargeId': charge['id'], 'controlExpenseId': expense['id'],
               'controlOverdue': '85.00', 'controlPaidExpense': '19.99',
               'preparationCredits': prepared}
    (folder / 'owner-money-before-preparation.json').write_text(json.dumps(before))
    (folder / 'owner-money-before.json').write_text(json.dumps(after))
    (folder / 'owner-finance-fixture.json').write_text(json.dumps(fixture, ensure_ascii=False))
    (folder.parent / 'owner-defines.json').write_text(json.dumps({'OWNER_FINANCE_FIXTURE': json.dumps(fixture)}))
    print('Owner fixture prepared through authorized demo API; immutable source tables verified.')


if __name__ == '__main__':
    main()
