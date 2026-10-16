import 'package:flutter/material.dart';

import '../../core/auth/auth_controller.dart';
import '../../core/theme/appearance.dart';
import '../../core/theme/ui_text.dart';
import '../guardian/guardian_ui.dart';
import 'staff_attendance_exceptions.dart';

class StaffWorkPeriodsPage extends StatefulWidget {
  final AuthController auth;
  const StaffWorkPeriodsPage({super.key, required this.auth});
  @override
  State<StaffWorkPeriodsPage> createState() => _StaffWorkPeriodsPageState();
}

class _StaffWorkPeriodsPageState extends State<StaffWorkPeriodsPage> {
  late final identity = staffContext(widget.auth);
  String? selectedDate;
  int revision = 0;
  Future<void> pickDate(String today, String current) async {
    final picked = await showDatePicker(
      context: context,
      firstDate: DateTime(2000),
      lastDate: DateTime.parse(today),
      initialDate: DateTime.parse(current),
      confirmText: localized(context, 'عرض اليوم', 'Show day'),
      cancelText: localized(context, 'إلغاء', 'Cancel'),
    );
    if (!mounted || identity != staffContext(widget.auth) || picked == null) {
      return;
    }
    setState(
      () => selectedDate =
          '${picked.year}-${picked.month.toString().padLeft(2, '0')}-${picked.day.toString().padLeft(2, '0')}',
    );
  }

  @override
  Widget build(BuildContext context) => GuardianPage(
    title: localized(context, 'فترات عملي', 'My work periods'),
    child: ListenableBuilder(
      listenable: widget.auth,
      builder: (context, _) {
        if (identity == null || identity != staffContext(widget.auth)) {
          return const SizedBox.shrink();
        }
        return RemoteBody(
          key: ValueKey('$identity:$selectedDate:$revision'),
          auth: widget.auth,
          path:
              '/api/v1/staff/work-periods${selectedDate == null ? '' : '?date=$selectedDate'}',
          builder: (context, value) {
            final d = obj(value);
            if (d['configured'] != true) {
              return gList([
                const EmptyMessage(
                  'لم يربط ملف موظف فعال بحسابك. تواصل مع الإدارة.',
                ),
              ]);
            }
            final entries = rows(d['items']);
            final total = d['totalWorkedSeconds'];
            return gList([
              GCard(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Heading(
                      localized(
                        context,
                        'يوم العمل: ${day(d['workDate'])}',
                        'Workday: ${day(d['workDate'])}',
                      ),
                    ),
                    UiText(
                      localized(
                        context,
                        'الأوقات حسب المنطقة المحفوظة مع الفترة. الفاصل بين الفترات ليس عملًا، ولا تنشئ هذه الشاشة راتبًا أو خصمًا.',
                        'Times use each period’s saved timezone. Gaps are not work; this view does not create pay or deductions.',
                      ),
                    ),
                    Wrap(
                      spacing: 12,
                      children: [
                        OutlinedButton.icon(
                          onPressed: () => pickDate(
                            textOf(d['today']),
                            textOf(d['workDate']),
                          ),
                          icon: const Icon(Icons.calendar_month_outlined),
                          label: UiText(
                            localized(context, 'اختيار يوم', 'Choose day'),
                          ),
                        ),
                        TextButton.icon(
                          onPressed: () => setState(() => revision++),
                          icon: const Icon(Icons.refresh),
                          label: UiText(localized(context, 'تحديث', 'Refresh')),
                        ),
                      ],
                    ),
                    UiText(
                      total == null
                          ? localized(
                              context,
                              'الإجمالي غير مكتمل أو غير مؤكد',
                              'Total is incomplete or uncertain',
                            )
                          : localized(
                              context,
                              'إجمالي الفترات: ${(total as num).toInt() ~/ 60} دقيقة',
                              'Total periods: ${total.toInt() ~/ 60} minutes',
                            ),
                    ),
                  ],
                ),
              ),
              if (entries.isEmpty)
                EmptyMessage(
                  localized(
                    context,
                    'لا توجد فترات لهذا اليوم.',
                    'No periods for this day.',
                  ),
                ),
              for (var i = 0; i < entries.length; i++)
                GCard(
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      Heading(
                        localized(
                          context,
                          'الفترة ${i + 1}',
                          'Period ${i + 1}',
                        ),
                      ),
                      UiText(
                        '${day(entries[i]['startDate'])} · ${textOf(entries[i]['startTime']).substring(0, 5)}',
                      ),
                      UiText(
                        entries[i]['endTime'] == null
                            ? localized(
                                context,
                                'فترة مفتوحة — لم يسجل انصراف',
                                'Open period — no checkout recorded',
                              )
                            : '${day(entries[i]['endDate'])} · ${textOf(entries[i]['endTime']).substring(0, 5)}',
                      ),
                      UiText(
                        entries[i]['trustSource'] == 'LegacyLocal'
                            ? localized(
                                context,
                                'سجل محلي قديم — مدة غير مؤكدة',
                                'Legacy local record — duration uncertain',
                              )
                            : entries[i]['trustSource'] == 'ServerVerified'
                            ? localized(
                                context,
                                'بداية بتوقيع متحقق خادميًا — لا يضمن سلامة الموقع',
                                'Server-verified start signature, not a GPS integrity guarantee',
                              )
                            : entries[i]['trustSource'] == 'MixedVerifiedEnd'
                            ? localized(
                                context,
                                'بداية إدارية وانصراف بتوقيع متحقق خادميًا',
                                'Administrative start, server-verified checkout signature',
                              )
                            : localized(
                                context,
                                'توقيت معلن إداريًا، وليس إثبات جهاز',
                                'Administratively declared time, not device proof',
                              ),
                      ),
                      if (entries[i]['workedSeconds'] != null)
                        UiText(
                          localized(
                            context,
                            '${(entries[i]['workedSeconds'] as num).toInt() ~/ 60} دقيقة',
                            '${(entries[i]['workedSeconds'] as num).toInt() ~/ 60} minutes',
                          ),
                        ),
                    ],
                  ),
                ),
              UiText(
                localized(
                  context,
                  'تصحيح الفترات من الإدارة مع حفظ الأصل والسبب. لا يمكنك تعديل الفترات أو راتبك من هذه الشاشة.',
                  'Corrections are made by management with preserved evidence. You cannot edit periods or your salary here.',
                ),
              ),
            ]);
          },
        );
      },
    ),
  );
}
