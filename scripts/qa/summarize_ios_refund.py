"""Check actual installed-app evidence against complete PostgreSQL rows."""
import hashlib
import json
from decimal import Decimal
from pathlib import Path
import sys

folder = Path(sys.argv[1])
read = lambda name: json.loads((folder / name).read_text())
driver = read('ios-cloud-response.json')
assert driver['allTestsPassed'] is True
review = driver['refundReview']
assert review['source'] == 'installed-ios-real-isolated-api'
native_ids = {f'RFN{i:02}' for i in [1,3,4,5,6,7,8,9,12,13,14,15,16,17,18,19,20,21,22,23,25,26,27,29,30]}
assert set(review['executedPassed']) == native_ids
assert len(review['executedPassed']) == len(native_ids)
reconciliation = review['reconciliation']
assert reconciliation['totalRefundMinor'] == 3625
assert reconciliation['receiptUnchanged'] is True
assert reconciliation['periodsUnchanged'] is True
ids = set(reconciliation['refundIds'])
assert len(ids) == 6
before, after = read('refund-money-before.json'), read('refund-money-after.json')
assert set(before) == set(after) and len(before) == 10
for table in set(before) - {'refunds'}:
    assert before[table] == after[table], f'Unexpected {table} change'
old = {row['Id']:row for row in before['refunds']}
current = {row['Id']:row for row in after['refunds']}
assert all(current.get(key) == row for key,row in old.items())
new = [row for key,row in current.items() if key not in old]
assert {row['Id'] for row in new} == ids
assert all(row['AcademyId']=='10000000-0000-0000-0000-000000000001' for row in new)
assert all(row['CollectionId']==reconciliation['source']['id'] for row in new)
assert all(row['Currency']=='EGP' and row['PayoutMethod']=='Online' for row in new)
assert sum(Decimal(str(row['Amount'])) for row in new) == Decimal('36.25')
assert len(list(folder.glob('phase-iosrefund-*.png'))) == 14
summary = {
 'executedAutomated':sorted(native_ids | {'RFN28'}),
 'visualReviewPending':['RFN29'],
 'nativeCasesWithWidgetBranches':['RFN09','RFN13','RFN21','RFN25','RFN29'],
 'widgetOnlyCases':['RFN02','RFN10','RFN11','RFN24','RFN31','RFN32'],
 'newRefundCount':6,'newRefundAmount':'36.25','currency':'EGP',
 'unchangedTables':sorted(set(before)-{'refunds'}),
 'beforeSha256':hashlib.sha256((folder/'refund-money-before.json').read_bytes()).hexdigest(),
 'afterSha256':hashlib.sha256((folder/'refund-money-after.json').read_bytes()).hexdigest(),
 'rowsBefore':{key:len(value) for key,value in before.items()},
 'rowsAfter':{key:len(value) for key,value in after.items()},
}
(folder/'refund-summary.json').write_text(json.dumps(summary,indent=2)+'\n')
print(json.dumps(summary))
