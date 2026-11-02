const feedbackStates = <String, String>{
  'Open': 'تنتظر المراجعة',
  'Reviewed': 'تمت المراجعة',
  'InProgress': 'قيد المعالجة',
  'AwaitingInfo': 'بانتظار معلومات',
  'Resolved': 'تم الرد والحل',
  'Closed': 'مغلقة',
  'Reopened': 'أعيد فتحها',
};

const feedbackSubjects = <String, String>{
  'Coach': 'المدرب',
  'Schedule': 'المواعيد',
  'Administration': 'الإدارة',
  'Facility': 'المرفق',
  'Water': 'المياه',
  'Payment': 'الدفع',
  'Other': 'موضوع آخر',
};
const feedbackUrgencies = <String, String>{
  'Routine': 'عادية',
  'Urgent': 'عاجلة',
  'Safety': 'سلامة أو إساءة',
};

String feedbackUtcTime(Object? value) {
  final date = DateTime.tryParse(value?.toString() ?? '')?.toUtc();
  if (date == null) return 'وقت غير متاح';
  String two(int n) => n.toString().padLeft(2, '0');
  return '${date.year}/${two(date.month)}/${two(date.day)} · ${two(date.hour)}:${two(date.minute)}';
}

const feedbackTransitions = <String, List<String>>{
  'Open': ['InProgress'],
  'Reviewed': ['InProgress'],
  'Reopened': ['InProgress'],
  'InProgress': ['AwaitingInfo'],
  'AwaitingInfo': ['InProgress'],
  'Resolved': ['Closed', 'Reopened'],
  'Closed': ['Reopened'],
};
