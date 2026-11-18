"""Independently reconcile real API ledger/statement rows from iOS evidence."""
from decimal import Decimal
import json
from pathlib import Path
import sys

folder = Path(sys.argv[1])


def read(name):
    return json.loads((folder / name).read_text(), parse_float=Decimal)


def money(value):
    return Decimal(str(value))


driver = read('ios-cloud-response.json')
assert driver['allTestsPassed'] is True
review = driver['refundReview']
assert review['source'] == 'installed-ios-real-isolated-api'
data = review['reconciliation']
before, after = data['financialBefore'], data['financialAfter']
ids = set(data['refundIds'])
assert len(ids) == 6
assert after['totalCount'] == before['totalCount'] + 6
assert money(after['totalAmount']) == money(before['totalAmount'])
assert money(after['totalRefunds']) - money(before['totalRefunds']) == Decimal('36.25')
assert money(after['netAmount']) - money(before['netAmount']) == Decimal('-36.25')
for financial in [before, after]:
    sources = financial['sourceSummary']
    assert sum((money(row['amount']) for row in sources), Decimal(0)) == money(financial['netAmount'])
sources_before = {row['key']: money(row['amount']) for row in before['sourceSummary']}
sources_after = {row['key']: money(row['amount']) for row in after['sourceSummary']}
assert set(sources_before) == set(sources_after)
assert sources_after['Refund'] - sources_before['Refund'] == Decimal('-36.25')
assert all(sources_before[key] == sources_after[key] for key in sources_before if key != 'Refund')

old_rows = {row['collectionId']: row for row in before['items']}
new_rows = {row['collectionId']: row for row in after['items']}
assert len(old_rows) == len(before['items']) and len(new_rows) == len(after['items'])
common = set(old_rows) & set(new_rows)
assert common and all(old_rows[key] == new_rows[key] for key in common)
sql = {row['Id']: row for row in read('refund-money-after.json')['refunds']}
for key in ids:
    row = new_rows[key]
    assert row['collectionType'] == 'Refund'
    assert row['receiptId'] == data['receiptId']
    assert row['receiptNumber'] == 'استرداد — ' + data['source']['receiptNumber']
    assert row['currency'] == sql[key]['Currency'] == 'EGP'
    assert row['paymentMethod'] == sql[key]['PayoutMethod'] == 'Online'
    assert money(row['amount']) == 0
    assert money(row['refundAmount']) == money(sql[key]['Amount'])
    assert money(row['netAmount']) == -money(sql[key]['Amount'])
assert sum((money(new_rows[key]['netAmount']) for key in ids), Decimal(0)) == Decimal('-36.25')

operating_before, operating_after = data['operatingBefore'], data['operatingAfter']
for operating in [operating_before, operating_after]:
    income = sum((money(row['amount']) for row in operating['income']), Decimal(0))
    expenses = sum((money(row['amount']) for row in operating['expenses']), Decimal(0))
    assert income == money(operating['totalIncome'])
    assert expenses == money(operating['totalExpenses'])
    assert income - expenses == money(operating['surplus'])
assert money(operating_after['totalIncome']) - money(operating_before['totalIncome']) == Decimal('-36.25')
assert money(operating_after['surplus']) - money(operating_before['surplus']) == Decimal('-36.25')
assert operating_before['expenses'] == operating_after['expenses']
income_before = {row['key']: row for row in operating_before['income']}
income_after = {row['key']: row for row in operating_after['income']}
assert set(income_before) == set(income_after)
assert money(income_after['refund']['amount']) - money(income_before['refund']['amount']) == Decimal('-36.25')
assert all(income_before[key] == income_after[key] for key in income_before if key != 'refund')

summary = {
    'case': 'RFN27',
    'source': 'actual-api-responses-and-full-sql-refund-rows',
    'refundRowsMatchedToSql': 6,
    'newRefundAmount': '36.25',
    'netAndSurplusDelta': '-36.25',
    'unchangedCommonLedgerRows': len(common),
    'financialBefore': {key: before[key] for key in ['totalCount', 'totalAmount', 'totalRefunds', 'netAmount']},
    'financialAfter': {key: after[key] for key in ['totalCount', 'totalAmount', 'totalRefunds', 'netAmount']},
    'operatingBefore': {key: operating_before[key] for key in ['totalIncome', 'totalExpenses', 'surplus']},
    'operatingAfter': {key: operating_after[key] for key in ['totalIncome', 'totalExpenses', 'surplus']},
    'fullLedgerPaginationTested': False,
}
(folder / 'ledger-reconciliation.json').write_text(json.dumps(summary, indent=2, default=str) + '\n')
print(json.dumps(summary, default=str))
