import 'package:flutter/material.dart';

import '../../core/api/api_client.dart';
import '../../core/auth/auth_controller.dart';
import '../../core/theme/appearance.dart';
import '../../core/theme/ui_text.dart';
import '../guardian/guardian_ui.dart';

const ownStaffExceptions = '/api/v1/staff/attendance-exceptions';
const manageStaffExceptions = '/api/v1/manage/staff-attendance-exceptions';
String? staffContext(AuthController auth) => auth.session?.selected == null
    ? null
    : '${auth.session!.selected!.academyId}:${auth.session!.selected!.id}:${auth.session!.selected!.contextRevision}';
String exceptionStatus(BuildContext c, Object? v) => switch (v) {
  'Approved' => localized(c, 'مقبول إداريًا', 'Administratively approved'),
  'Rejected' => localized(c, 'مرفوض', 'Rejected'),
  _ => localized(c, 'يحتاج مراجعة', 'Needs review'),
};
String claimTime(Object? value) {
  final d = DateTime.tryParse(textOf(value))?.toLocal();
  return d == null
      ? '—'
      : '${d.year}-${d.month.toString().padLeft(2, '0')}-${d.day.toString().padLeft(2, '0')} ${d.hour.toString().padLeft(2, '0')}:${d.minute.toString().padLeft(2, '0')}';
}

class StaffAttendanceExceptionPage extends StatefulWidget {
  final AuthController auth;
  final bool review;
  const StaffAttendanceExceptionPage({
    super.key,
    required this.auth,
    this.review = false,
  });
  @override
  State<StaffAttendanceExceptionPage> createState() =>
      _StaffAttendanceExceptionPageState();
}

class _StaffAttendanceExceptionPageState
    extends State<StaffAttendanceExceptionPage> {
  final other = TextEditingController();
  late final identity = staffContext(widget.auth);
  late Future<List<Object?>> request = load();
  String operation = 'CheckIn',
      reason = 'تعذر الحصول على موقع دقيق داخل الفرع',
      status = 'Pending';
  String? branch;
  DateTime? date;
  TimeOfDay? time;
  bool busy = false;
  String message = '';
  Json? command;
  String key = commandKey();
  Future<List<Object?>> load() => widget.review
      ? Future.wait([
          widget.auth.request('GET', '$manageStaffExceptions/?status=$status'),
        ])
      : Future.wait([
          widget.auth.request('GET', '$ownStaffExceptions/options'),
          widget.auth.request('GET', '$ownStaffExceptions/'),
        ]);
  @override
  void dispose() {
    other.dispose();
    super.dispose();
  }

  Future<void> refresh() async {
    if (!mounted || identity != staffContext(widget.auth)) return;
    setState(() {
      request = load();
    });
    try {
      await request;
    } catch (_) {
      /* Render read failure with retry. */
    }
  }

  Future<void> save(DateTime now, Json openAttendance) async {
    if (busy || branch == null) return;
    final selectedDate = date ?? now,
        selectedTime = time ?? TimeOfDay.fromDateTime(now);
    final text = reason == 'Other' ? other.text.trim() : reason;
    if (command == null && text.length < 5) {
      setState(() => message = 'اكتب سبب الطلب من خمسة أحرف على الأقل.');
      return;
    }
    if (command == null &&
        operation == 'CheckOut' &&
        openAttendance['id'] == null) {
      setState(
        () => message =
            'الانصراف يحتاج حضورًا مفتوحًا؛ أعد تحميل السجل أو راجع الإدارة.',
      );
      return;
    }
    command ??= {
      'branchId': branch,
      'operation': operation,
      'reason': text,
      if (operation == 'CheckOut') 'targetAttendanceId': openAttendance['id'],
      if (operation == 'CheckOut' &&
          openAttendance['targetWorkPeriodId'] != null)
        'targetWorkPeriodId': openAttendance['targetWorkPeriodId'],
      if (operation == 'CheckOut' &&
          openAttendance['targetWorkPeriodId'] != null)
        'targetWorkPeriodVersion': openAttendance['targetWorkPeriodVersion'],
      'claimedAtUtc': DateTime(
        selectedDate.year,
        selectedDate.month,
        selectedDate.day,
        selectedTime.hour,
        selectedTime.minute,
      ).toUtc().toIso8601String(),
    };
    setState(() {
      busy = true;
      message = '';
    });
    try {
      final result = obj(
        await widget.auth.request(
          'POST',
          '$ownStaffExceptions/',
          body: command,
          idempotencyKey: key,
        ),
      );
      if (mounted && identity == staffContext(widget.auth)) {
        setState(() {
          command = null;
          key = commandKey();
          message = result['status'] == 'Pending'
              ? localized(
                  context,
                  'تم إرسال الطلب للمراجعة؛ لم يُسجل الحضور بعد.',
                  'Request submitted; attendance is not recorded yet.',
                )
              : localized(
                  context,
                  'الطلب موجود بالفعل: ${exceptionStatus(context, result['status'])}',
                  'Request already exists: ${exceptionStatus(context, result['status'])}',
                );
          request = load();
        });
      }
    } catch (e) {
      if (mounted && identity == staffContext(widget.auth)) {
        setState(
          () => message = e is ApiFailure
              ? e.message
              : 'لم يتأكد إرسال الطلب؛ إعادة الإرسال تستخدم البيانات نفسها.',
        );
      }
    } finally {
      if (mounted) setState(() => busy = false);
    }
  }

  @override
  Widget build(BuildContext context) => GuardianPage(
    title: localized(
      context,
      widget.review ? 'مراجعة إثبات الحضور' : 'طلب إثبات حضوري',
      widget.review ? 'Attendance reviews' : 'My attendance claim',
    ),
    child: ListenableBuilder(
      listenable: widget.auth,
      builder: (context, _) {
        if (identity == null || identity != staffContext(widget.auth)) {
          return const SizedBox.shrink();
        }
        return FutureBuilder<List<Object?>>(
          future: request,
          builder: (context, snapshot) {
            if (snapshot.connectionState != ConnectionState.done) {
              return const Center(child: CircularProgressIndicator());
            }
            if (snapshot.hasError) {
              return gList([
                const UiText(
                  'تعذر تحميل طلبات إثبات الحضور أو لا يوجد إذن مناسب.',
                ),
                OutlinedButton(
                  onPressed: refresh,
                  child: const UiText('إعادة المحاولة'),
                ),
              ]);
            }
            final options = obj(snapshot.data!.first);
            final entries = widget.review
                ? rows(options['items'])
                : rows(snapshot.data![1]);
            final now =
                DateTime.tryParse(textOf(options['nowUtc']))?.toLocal() ??
                DateTime.now();
            final sites = rows(options['branches'] ?? <Json>[]);
            final openAttendance = options['openAttendance'] is Map
                ? obj(options['openAttendance'])
                : <String, dynamic>{};
            return RefreshIndicator(
              onRefresh: refresh,
              child: gList([
                UiText(
                  localized(
                    context,
                    'هذا طلب مراجعة إدارية، وليس إثبات بصمة أو موقع. الوقت المدعى بتوقيت جهازك، ووقت الاستلام من الخادم.',
                    'An administrative review request, not biometric or location proof. Claimed time uses your device timezone; receipt time comes from the server.',
                  ),
                ),
                if (message.isNotEmpty) UiText(message),
                if (!widget.review && openAttendance['id'] != null)
                  GCard(
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        const Heading('حضور مفتوح — يحتاج انصرافًا'),
                        UiText(
                          'يوم بدء الحضور بتوقيت الأكاديمية: ${openAttendance['workDate']} · ${openAttendance['checkIn']}',
                        ),
                        const UiText(
                          'طلب الانصراف يرتبط بهذا السجل حتى بعد منتصف الليل. لا يُغلق تلقائيًا.',
                        ),
                      ],
                    ),
                  ),
                if (options['requiresSourceReview'] == true)
                  const EmptyMessage(
                    'تحتاج الإدارة مراجعة فترات الحضور قبل طلب الانصراف؛ لم يغلق أي سجل تلقائيًا.',
                  ),
                if (widget.review) ...[
                  DropdownButtonFormField<String>(
                    isExpanded: true,
                    itemHeight: null,
                    initialValue: status,
                    decoration: const InputDecoration(labelText: 'الحالة'),
                    items: const [
                      DropdownMenuItem(
                        value: 'Pending',
                        child: UiText('يحتاج مراجعة'),
                      ),
                      DropdownMenuItem(
                        value: 'Approved',
                        child: UiText('مقبول إداريًا'),
                      ),
                      DropdownMenuItem(
                        value: 'Rejected',
                        child: UiText('مرفوض'),
                      ),
                      DropdownMenuItem(value: '', child: UiText('الكل')),
                    ],
                    onChanged: (v) {
                      setState(() {
                        status = v ?? '';
                        request = load();
                      });
                    },
                  ),
                  UiText('${options['total']} طلب — أحدث 200 ضمن صلاحياتك'),
                ] else if (options['configured'] != true)
                  const EmptyMessage(
                    'لا يوجد ملف موظف مرتبط بحسابك؛ تواصل مع الإدارة.',
                  )
                else
                  GCard(
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        DropdownButtonFormField<String>(
                          isExpanded: true,
                          itemHeight: null,
                          initialValue: branch,
                          decoration: const InputDecoration(labelText: 'الفرع'),
                          items: [
                            for (final b in sites)
                              DropdownMenuItem(
                                value: textOf(b['id']),
                                child: UiText(textOf(b['name'])),
                              ),
                          ],
                          onChanged: busy || command != null
                              ? null
                              : (v) => setState(() => branch = v),
                        ),
                        const SizedBox(height: 16),
                        DropdownButtonFormField<String>(
                          isExpanded: true,
                          itemHeight: null,
                          initialValue: operation,
                          decoration: const InputDecoration(
                            labelText: 'نوع الطلب',
                          ),
                          items: const [
                            DropdownMenuItem(
                              value: 'CheckIn',
                              child: UiText('حضور'),
                            ),
                            DropdownMenuItem(
                              value: 'CheckOut',
                              child: UiText('انصراف'),
                            ),
                          ],
                          onChanged: busy || command != null
                              ? null
                              : (v) => setState(() => operation = v!),
                        ),
                        const SizedBox(height: 12),
                        Wrap(
                          spacing: 8,
                          runSpacing: 8,
                          children: [
                            OutlinedButton.icon(
                              onPressed: busy || command != null
                                  ? null
                                  : () async {
                                      final d = await showDatePicker(
                                        context: context,
                                        initialDate: date ?? now,
                                        firstDate: DateTime(2000),
                                        lastDate: now,
                                      );
                                      if (d != null && mounted) {
                                        setState(() => date = d);
                                      }
                                    },
                              icon: const Icon(Icons.calendar_today),
                              label: UiText(
                                day((date ?? now).toIso8601String()),
                              ),
                            ),
                            OutlinedButton.icon(
                              onPressed: busy || command != null
                                  ? null
                                  : () async {
                                      final t = await showTimePicker(
                                        context: context,
                                        initialTime:
                                            time ?? TimeOfDay.fromDateTime(now),
                                      );
                                      if (t != null && mounted) {
                                        setState(() => time = t);
                                      }
                                    },
                              icon: const Icon(Icons.schedule),
                              label: UiText(
                                (time ?? TimeOfDay.fromDateTime(now)).format(
                                  context,
                                ),
                              ),
                            ),
                          ],
                        ),
                        const SizedBox(height: 16),
                        DropdownButtonFormField<String>(
                          isExpanded: true,
                          itemHeight: null,
                          initialValue: reason,
                          decoration: const InputDecoration(
                            labelText: 'سبب الطلب',
                          ),
                          items: const [
                            DropdownMenuItem(
                              value: 'تعذر الحصول على موقع دقيق داخل الفرع',
                              child: UiText('الموقع غير دقيق'),
                            ),
                            DropdownMenuItem(
                              value: 'الجهاز لا يدعم وسيلة التحقق المطلوبة',
                              child: UiText('الجهاز غير مدعوم'),
                            ),
                            DropdownMenuItem(
                              value: 'تعذر الاتصال بالشبكة وقت الحضور',
                              child: UiText('انقطاع الشبكة'),
                            ),
                            DropdownMenuItem(
                              value: 'Other',
                              child: UiText('سبب آخر'),
                            ),
                          ],
                          onChanged: busy || command != null
                              ? null
                              : (v) => setState(() => reason = v!),
                        ),
                        if (reason == 'Other') ...[
                          const SizedBox(height: 16),
                          TextField(
                            controller: other,
                            enabled: !busy && command == null,
                            maxLength: 500,
                            decoration: const InputDecoration(
                              labelText: 'وضح سبب الطلب',
                            ),
                          ),
                        ],
                        const SizedBox(height: 12),
                        FilledButton.icon(
                          onPressed:
                              busy ||
                                  branch == null ||
                                  command == null &&
                                      operation == 'CheckOut' &&
                                      openAttendance['id'] == null
                              ? null
                              : () => save(now, openAttendance),
                          icon: const Icon(Icons.fact_check_outlined),
                          label: UiText(
                            busy
                                ? 'جارٍ الإرسال…'
                                : command != null
                                ? 'إعادة تأكيد الطلب نفسه'
                                : 'إرسال للمراجعة',
                          ),
                        ),
                        if (command != null && !busy)
                          TextButton(
                            onPressed: () {
                              setState(() {
                                command = null;
                                key = commandKey();
                              });
                            },
                            child: const UiText(
                              'مراجعة بيانات طلب جديد — تحقق من السجل أولًا',
                            ),
                          ),
                      ],
                    ),
                  ),
                Heading(
                  localized(
                    context,
                    widget.review ? 'طلبات الموظفين' : 'طلباتي — أحدث 100',
                    widget.review
                        ? 'Staff requests'
                        : 'My requests — latest 100',
                  ),
                ),
                if (entries.isEmpty)
                  const EmptyMessage('لا توجد طلبات بهذه الحالة.'),
                for (final r in entries)
                  GCard(
                    onTap: widget.review
                        ? () async {
                            await openPage(
                              context,
                              StaffExceptionReviewPage(
                                auth: widget.auth,
                                id: textOf(r['id']),
                              ),
                            );
                            await refresh();
                          }
                        : null,
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        if (widget.review)
                          UiText('${r['name']} · ${r['branch']}'),
                        UiText(
                          '${r['operation'] == 'CheckIn' ? 'حضور' : 'انصراف'} · ${claimTime(r['claimedAtUtc'])}',
                        ),
                        UiText(exceptionStatus(context, r['status'])),
                        if (!widget.review) UiText(textOf(r['reason'])),
                        if (r['operation'] == 'CheckOut')
                          UiText('مرتبط بيوم بدء الحضور: ${r['workDate']}'),
                        if (r['reviewNote'] != null)
                          UiText('ملاحظة القرار: ${r['reviewNote']}'),
                        if (widget.review) const UiText('عرض ومراجعة الطلب'),
                      ],
                    ),
                  ),
              ]),
            );
          },
        );
      },
    ),
  );
}

class StaffExceptionReviewPage extends StatefulWidget {
  final AuthController auth;
  final String id;
  const StaffExceptionReviewPage({
    super.key,
    required this.auth,
    required this.id,
  });
  @override
  State<StaffExceptionReviewPage> createState() =>
      _StaffExceptionReviewPageState();
}

class _StaffExceptionReviewPageState extends State<StaffExceptionReviewPage> {
  final note = TextEditingController();
  late final identity = staffContext(widget.auth);
  late Future<Object?> request = load();
  bool busy = false;
  Json? command;
  String key = commandKey(), message = '';
  Future<Object?> load() =>
      widget.auth.request('GET', '$manageStaffExceptions/${widget.id}');
  @override
  void dispose() {
    note.dispose();
    super.dispose();
  }

  Future<void> decide(bool approve, Json r) async {
    if (busy) return;
    if (command == null) {
      if (!approve && note.text.trim().length < 5) {
        setState(() => message = 'اكتب سبب الرفض من خمسة أحرف على الأقل.');
        return;
      }
      final confirmed = await showDialog<bool>(
        context: context,
        builder: (c) => AlertDialog(
          title: UiText(
            approve
                ? 'قبول إداري وتسجيل الحضور؟'
                : 'رفض الطلب دون تغيير الحضور؟',
          ),
          content: const UiText(
            'القرار محفوظ باسمك. القبول ليس إثبات بصمة ولا ينشئ خصمًا أو زيادة مالية.',
          ),
          actions: [
            TextButton(
              onPressed: () => Navigator.pop(c, false),
              child: const UiText('رجوع'),
            ),
            FilledButton(
              onPressed: () => Navigator.pop(c, true),
              child: const UiText('تأكيد القرار'),
            ),
          ],
        ),
      );
      if (confirmed != true ||
          !mounted ||
          identity != staffContext(widget.auth)) {
        return;
      }
      command = {
        'approve': approve,
        'note': note.text.trim().isEmpty ? null : note.text.trim(),
        'version': r['version'],
      };
    }
    setState(() {
      busy = true;
      message = '';
    });
    try {
      await widget.auth.request(
        'POST',
        '$manageStaffExceptions/${widget.id}/review',
        body: command,
        idempotencyKey: key,
      );
      if (mounted && identity == staffContext(widget.auth)) {
        setState(() {
          message = command!['approve'] == true
              ? 'تم قبول الطلب وتسجيل الحضور إداريًا.'
              : 'تم رفض الطلب؛ لم يتغير الحضور.';
          command = null;
          request = load();
        });
      }
    } catch (e) {
      if (mounted && identity == staffContext(widget.auth)) {
        setState(
          () => message = e is ApiFailure
              ? e.message
              : 'لم يتأكد القرار؛ أعد إرساله بالمفتاح نفسه.',
        );
      }
    } finally {
      if (mounted) setState(() => busy = false);
    }
  }

  @override
  Widget build(BuildContext context) => GuardianPage(
    title: 'مراجعة إثبات الحضور',
    child: ListenableBuilder(
      listenable: widget.auth,
      builder: (context, _) {
        if (identity == null || identity != staffContext(widget.auth)) {
          return const SizedBox.shrink();
        }
        return FutureBuilder<Object?>(
          future: request,
          builder: (context, snapshot) {
            if (snapshot.connectionState != ConnectionState.done) {
              return const Center(child: CircularProgressIndicator());
            }
            if (snapshot.hasError) {
              return gList([
                const EmptyMessage('تعذر عرض الطلب أو لم يعد ضمن صلاحياتك.'),
                TextButton(
                  onPressed: () => setState(() => request = load()),
                  child: const UiText('إعادة المحاولة'),
                ),
              ]);
            }
            final d = obj(snapshot.data), r = obj(d['request']);
            final attendance = d['currentAttendance'] is Map
                ? obj(d['currentAttendance'])
                : null;
            return gList([
              Heading(textOf(d['name'])),
              UiText(textOf(d['branch'])),
              UiText('يوم العمل بتوقيت الأكاديمية: ${r['workDate']}'),
              if (r['operation'] == 'CheckOut')
                const UiText(
                  'الانصراف مرتبط بيوم بدء الحضور، حتى إذا كان بعد منتصف الليل.',
                ),
              UiText(exceptionStatus(context, r['status'])),
              UiText(
                'الوقت المدعى بتوقيت جهازك: ${claimTime(r['claimedAtUtc'])}',
              ),
              UiText(
                'استلام الخادم بتوقيت جهازك: ${claimTime(r['receivedAtUtc'])}',
              ),
              UiText(textOf(r['reason'])),
              if (r['targetPeriod'] is Map)
                GCard(
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      const Heading('الفترة المحددة عند إرسال الطلب'),
                      UiText(
                        '${obj(r['targetPeriod'])['startDate']} · ${obj(r['targetPeriod'])['startTime']}',
                      ),
                      UiText(
                        'توقيت الفترة: ${obj(r['targetPeriod'])['timeZoneId'] ?? 'غير مؤكد'}',
                      ),
                      const UiText(
                        'القرار يخص هذه الفترة فقط؛ تغيير المصدر يتطلب طلبًا جديدًا.',
                      ),
                    ],
                  ),
                ),
              if (d['currentPeriod'] is Map)
                UiText(
                  'الفترة الحالية: ${obj(d['currentPeriod'])['startTime']} / ${obj(d['currentPeriod'])['endTime'] ?? 'مفتوحة'}',
                ),
              if (d.containsKey('currentAttendance')) ...[
                const Heading('الحضور المسجل — بتوقيت الأكاديمية'),
                if (attendance == null)
                  const UiText('لا يوجد سجل حضور لهذا اليوم.')
                else
                  UiText(
                    '${attendance['status'] == 'Present'
                        ? 'حاضر'
                        : attendance['status'] == 'Absent'
                        ? 'غائب'
                        : 'لم يُسجل'} · ${attendance['checkIn'] ?? '—'} / ${attendance['checkOut'] ?? '—'}',
                  ),
                if (attendance?['checkOutDate'] != null)
                  UiText(
                    'تاريخ الانصراف بتوقيت الأكاديمية: ${attendance!['checkOutDate']}',
                  ),
              ],
              const UiText(
                'هذا طلب إداري، وليس إثبات جهاز. لا يمكن قبول طلبك بنفسك أو تعديل شهر معتمد.',
              ),
              if (r['reviewNote'] != null)
                UiText('ملاحظة القرار: ${r['reviewNote']}'),
              if (message.isNotEmpty) UiText(message),
              if (d['canReview'] == true) ...[
                TextField(
                  controller: note,
                  enabled: !busy && command == null,
                  maxLength: 500,
                  decoration: const InputDecoration(
                    labelText: 'ملاحظة القرار — مطلوبة عند الرفض',
                  ),
                ),
                FilledButton(
                  onPressed: busy ? null : () => decide(true, r),
                  child: UiText(
                    busy
                        ? 'جارٍ التأكيد…'
                        : command != null
                        ? 'إعادة تأكيد القرار نفسه'
                        : 'قبول وتسجيل إداري',
                  ),
                ),
                if (command == null)
                  OutlinedButton(
                    onPressed: busy ? null : () => decide(false, r),
                    child: const UiText('رفض الطلب'),
                  ),
              ] else if (r['status'] == 'Pending')
                const UiText(
                  'تحتاج مراجعة مسؤول آخر مخول، أو لا تملك إذن التعديل.',
                ),
            ]);
          },
        );
      },
    ),
  );
}
