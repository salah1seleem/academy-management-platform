import 'v53_owner_financial_records_test.dart' as records;

void main() => records.runFinancialRecordAcceptance(
  dark: true,
  expectedWidth: const int.fromEnvironment('FINANCIAL_RECORD_WIDTH'),
);
