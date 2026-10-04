import 'package:flutter/material.dart';

import '../../core/api/api_client.dart';
import '../../core/auth/auth_controller.dart';
import 'guardian_ui.dart';
import 'guardian_payments.dart';

class EnrollmentForm extends StatefulWidget {
  final AuthController auth;
  final String environment;
  const EnrollmentForm({
    super.key,
    required this.auth,
    this.environment = 'Production',
  });
  @override
  State<EnrollmentForm> createState() => _EnrollmentFormState();
}

class _EnrollmentFormState extends State<EnrollmentForm> {
  final form = GlobalKey<FormState>();
  final name = TextEditingController(), notes = TextEditingController();
  int kind = 2;
  String? child, sport, branch, dob, error;
  bool busy = false;
  Json? submitted;
  final key = commandKey();
  @override
  void dispose() {
    name.dispose();
    notes.dispose();
    super.dispose();
  }

  Future<void> submit() async {
    if (busy) return;
    if (submitted == null) {
      if (!form.currentState!.validate()) return;
      if (kind == 2 && dob == null) {
        setState(() => error = 'اختر تاريخ الميلاد.');
        return;
      }
      submitted = {
        'requestType': kind,
        'existingPlayerId': kind == 1 ? child : null,
        'newChildArabicName': kind == 2 ? name.text.trim() : null,
        'newChildDateOfBirth': kind == 2 ? dob : null,
        'newChildGender': null,
        'sportId': sport,
        'preferredBranchId': branch,
        'notes': notes.text.trim().isEmpty ? null : notes.text.trim(),
      };
    }
    setState(() {
      busy = true;
      error = null;
    });
    try {
      await widget.auth.request(
        'POST',
        '/api/v1/guardian/enrollment-requests',
        body: submitted,
        idempotencyKey: key,
      );
      if (mounted) {
        Navigator.of(context).pushReplacement(
          MaterialPageRoute<void>(
            builder: (_) => EnrollmentRequests(
              auth: widget.auth,
              environment: widget.environment,
            ),
          ),
        );
      }
    } on ApiFailure catch (e) {
      if (mounted) {
        setState(() {
          error = e.message;
          if ([400, 404, 409, 422].contains(e.status)) submitted = null;
        });
      }
    } finally {
      if (mounted) setState(() => busy = false);
    }
  }

  Future<void> pickDate() async {
    final date = await showDatePicker(
      context: context,
      initialDate: DateTime(2018, 1, 1),
      firstDate: DateTime(1990),
      lastDate: DateTime.now(),
    );
    if (date != null && mounted) {
      setState(() => dob = day(date.toIso8601String()));
    }
  }

  @override
  Widget build(BuildContext context) => GuardianPage(
    title: 'اشتراك جديد',
    child: RemoteBody(
      auth: widget.auth,
      path: '/api/v1/guardian/enrollment-request-options',
      builder: (context, value) {
        final d = obj(value);
        return Form(
          key: form,
          child: gList([
            const Text(
              'أرسل الطلب لتراجعه الأكاديمية وتحدد المجموعة. لا ينشئ الطلب دفعًا أو اشتراكًا فعالًا.',
            ),
            const SizedBox(height: 16),
            AbsorbPointer(
              absorbing: busy || submitted != null,
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.stretch,
                children: [
                  SegmentedButton<int>(
                    segments: const [
                      ButtonSegment(value: 2, label: Text('طفل جديد')),
                      ButtonSegment(value: 1, label: Text('طفل مرتبط')),
                    ],
                    selected: {kind},
                    onSelectionChanged: (s) => setState(() => kind = s.single),
                  ),
                  const SizedBox(height: 20),
                  if (kind == 1)
                    select(
                      'الطفل',
                      rows(d['children']),
                      child,
                      (v) => setState(() => child = v),
                    )
                  else ...[
                    TextFormField(
                      controller: name,
                      maxLength: 180,
                      decoration: const InputDecoration(labelText: 'اسم الطفل'),
                      validator: (v) => v == null || v.trim().length < 2
                          ? 'أدخل اسم الطفل.'
                          : null,
                    ),
                    OutlinedButton.icon(
                      onPressed: pickDate,
                      icon: const Icon(Icons.calendar_month),
                      label: Text(dob ?? 'تاريخ الميلاد'),
                    ),
                  ],
                  const SizedBox(height: 16),
                  select(
                    'الرياضة',
                    rows(d['sports']),
                    sport,
                    (v) => setState(() => sport = v),
                  ),
                  const SizedBox(height: 16),
                  select(
                    'الفرع المفضل',
                    rows(d['branches']),
                    branch,
                    (v) => setState(() => branch = v),
                  ),
                  const SizedBox(height: 16),
                  TextFormField(
                    controller: notes,
                    maxLength: 600,
                    maxLines: 3,
                    decoration: const InputDecoration(
                      labelText: 'ملاحظات — اختياري',
                    ),
                  ),
                ],
              ),
            ),
            if (error != null) Text(error!),
            if (submitted != null && !busy)
              const Text(
                'لمنع الطلب المكرر، أعد إرسال نفس البيانات من هنا عند عودة الاتصال.',
              ),
            FilledButton(
              onPressed: busy ? null : submit,
              child: Text(busy ? 'جارٍ الإرسال…' : 'إرسال طلب الاشتراك'),
            ),
          ]),
        );
      },
    ),
  );
  Widget select(
    String title,
    List<Json> items,
    String? value,
    ValueChanged<String?> change,
  ) => DropdownButtonFormField<String>(
    initialValue: value,
    isExpanded: true,
    decoration: InputDecoration(labelText: title),
    items: items
        .map(
          (x) =>
              DropdownMenuItem<String>(value: x['id'], child: Text(x['name'])),
        )
        .toList(),
    onChanged: change,
    validator: (v) => v == null ? 'اختر $title.' : null,
  );
}

class EnrollmentRequests extends StatelessWidget {
  final AuthController auth;
  final String environment;
  const EnrollmentRequests({
    super.key,
    required this.auth,
    this.environment = 'Production',
  });
  @override
  Widget build(BuildContext context) => GuardianPage(
    title: 'طلبات الاشتراك',
    child: RemoteBody(
      auth: auth,
      path: '/api/v1/guardian/enrollment-requests',
      builder: (context, value) => gList([
        if (rows(value).isEmpty) const EmptyMessage('لا توجد طلبات حتى الآن.'),
        for (final d in rows(value))
          GCard(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Heading(textOf(d['child'])),
                Text('${d['sport']} · ${d['branch']}'),
                StatusPill(d['status']),
                Text(day(d['createdAtUtc'])),
                if (d['guardianVisibleReason'] != null)
                  Text(d['guardianVisibleReason']),
                if (d['status'] == 'Approved' &&
                    d['createdSportEnrollmentId'] != null)
                  FilledButton(
                    onPressed: () => openPage(
                      context,
                      PlanPicker(
                        auth: auth,
                        environment: environment,
                        enrollmentId: d['createdSportEnrollmentId'],
                        childName: d['child'],
                      ),
                    ),
                    child: const Text('اختيار باقة والاشتراك'),
                  ),
              ],
            ),
          ),
      ]),
    ),
  );
}
