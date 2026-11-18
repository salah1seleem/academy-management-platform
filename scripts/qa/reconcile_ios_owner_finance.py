"""Reconcile complete selected business rows, independently of UI counters."""
from decimal import Decimal
import json
from pathlib import Path
import sys

A = '10000000-0000-0000-0000-000000000001'
B = '20000000-0000-0000-0000-000000000002'


def reconcile(folder):
    before = json.loads((folder / 'owner-money-before.json').read_text())
    after = json.loads((folder / 'owner-money-after.json').read_text())
    response = json.loads((folder / 'ios-cloud-response.json').read_text())
    review = response['ownerFinanceReview']
    assert response['allTestsPassed'] is True
    unchanged = ['collections', 'receipts', 'periods', 'players', 'enrollments',
                 'refunds', 'cashRefundPostings', 'cashSessions', 'receivableCreditApplications']
    for name in unchanged:
        assert before[name] == after[name], name
    new = {}
    for name in before:
        old = {r['Id']: r for r in before[name]}
        current = {r['Id']: r for r in after[name]}
        assert len(current) == len(after[name]), (name, 'duplicate IDs')
        for key, row in old.items():
            assert current.get(key) == row, (name, key, 'existing row altered')
        assert [r for r in before[name] if r.get('AcademyId') == B] == [
            r for r in after[name] if r.get('AcademyId') == B], (name, 'B changed')
        new[name] = [r for key, r in current.items() if key not in old]
        assert all(r['AcademyId'] == A for r in new[name]), (name, 'foreign insertion')
    documents = new['receivableDocuments']
    movements = new['receivableMovements']
    expenses = new['operatingExpenses']
    allocations = new['operatingExpenseAllocations']
    amount = lambda value: Decimal(str(value))
    assert sorted(amount(r['Amount']) for r in documents) == list(map(Decimal, ['40', '125', '260', '500', '540']))
    assert sorted(amount(r['Amount']) for r in movements) == list(map(Decimal, ['40', '125', '260', '500']))
    assert sum(amount(r['Amount']) for r in documents) == Decimal('1465')
    assert sum(amount(r['Amount']) for r in movements) == Decimal('925')
    allocated = [r for r in movements if r['CollectionId'] is not None]
    assert len(allocated) == 1 and amount(allocated[0]['Amount']) == 40
    assert allocated[0]['CollectionId'] in {r['Id'] for r in before['collections']}
    assert sorted(amount(r['Amount']) for r in expenses) == [Decimal('137.65'), Decimal('200')]
    assert all(r['Status'] == 3 for r in expenses)
    assert len([r for r in expenses if r['PaidOn'] is None]) == 1
    for expense in expenses:
        shares = [r for r in allocations if r['ExpenseId'] == expense['Id']]
        assert shares and sum(amount(r['Amount']) for r in shares) == amount(expense['Amount'])
    assert all(r['ExpenseId'] in {e['Id'] for e in expenses} for r in allocations)
    control_documents = [r for r in before['receivableDocuments'] if r['AcademyId'] == B]
    control_expenses = [r for r in before['operatingExpenses'] if r['AcademyId'] == B and r['Status'] == 3 and r['PaidOn']]
    assert any(amount(r['Amount']) == 85 for r in control_documents)
    assert sum(amount(r['Amount']) for r in control_expenses) == Decimal('19.99')
    paid = sum(amount(r['Amount']) for r in expenses if r['PaidOn'])
    financial = next(r for r in review['evidence'] if r.get('case') == 'OHF34')
    assert amount(financial['after']['totalExpenses']) - amount(financial['before']['totalExpenses']) == paid
    assert amount(financial['before']['surplus']) - amount(financial['after']['surplus']) == paid
    assert amount(financial['after']['totalIncome']) == amount(financial['before']['totalIncome'])
    expected = {'OHF01','OHF02','OHF03','OHF04','OHF05','OHF06','OHF07','OHF09',
                'OHF20','OHF21','OHF23','OHF24','OHF25','OHF31','OHF32','OHF34',
                'OHF35','OHF36','OHF40','OHF43'}
    assert set(review['executedPassedWithinDeclaredLayer']) == expected
    result = {'case': 'OHF48', 'layer': 'independent SQL/API reconciliation',
              'selectedTables': len(before), 'completeOriginalRowsUnchanged': True,
              'academyBNonzeroControlsUnchanged': True,
              'newRows': {k: len(v) for k, v in new.items()},
              'paidExpenseDelta': str(paid), 'surplusDelta': str(-paid),
              'businessCasesPassedWithinDeclaredLayer': sorted(expected | {'OHF48'}),
              'fullScreenAcceptance': False, 'visualReviewPending': True}
    (folder / 'owner-reconciliation.json').write_text(json.dumps(result, indent=2))
    print(json.dumps(result))


if __name__ == '__main__':
    reconcile(Path(sys.argv[1]))
