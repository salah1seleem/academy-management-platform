import 'dart:convert';

import 'package:flutter/material.dart';

import '../../core/api/api_client.dart';
import '../../core/auth/auth_controller.dart';
import '../../core/theme/ui_text.dart';
import '../guardian/guardian_ui.dart';
import '../owner/owner_followups.dart' show followUpContext, followUpRequestId;
import 'feedback_state.dart';

class OwnerInvestigationAssignment extends StatefulWidget {
  final AuthController auth;
  final Json item;
  final VoidCallback saved;
  const OwnerInvestigationAssignment({
    super.key,
    required this.auth,
    required this.item,
    required this.saved,
  });
  @override
  State<OwnerInvestigationAssignment> createState() => _AssignmentState();
}

class _AssignmentState extends State<OwnerInvestigationAssignment> {
  late final brief = TextEditingController(
    text: widget.item['investigationBrief'] as String? ?? '',
  );
  late String? member = widget.item['investigatorMembershipId'] as String?;
  String? signature, requestId;
  String message = '';
  bool busy = false;
  @override
  void dispose() {
    brief.dispose();
    super.dispose();
  }

  Future<void> save(bool clear) async {
    if (busy) return;
    if (!clear && (member == null || brief.text.trim().length < 5)) {
      setState(
        () => message = 'اختر مسؤولًا واكتب موجزًا من خمسة أحرف على الأقل.',
      );
      return;
    }
    final payload = {
      'investigatorMembershipId': clear ? null : member,
      'brief': clear ? null : brief.text.trim(),
      'version': widget.item['version'],
    };
    final draft = jsonEncode(payload);
    if (signature != draft) {
      signature = draft;
      requestId = followUpRequestId();
    }
    setState(() {
      busy = true;
      message = '';
    });
    try {
      await widget.auth.request(
        'POST',
        '/api/v1/manage/feedback/${widget.item['id']}/assignment',
        body: {...payload, 'requestId': requestId},
      );
      if (mounted) widget.saved();
    } catch (e) {
      if (mounted) {
        setState(
          () =>
              message = e is ApiFailure ? e.message : 'تعذر حفظ تكليف التحقيق.',
        );
      }
    } finally {
      if (mounted) setState(() => busy = false);
    }
  }

  @override
  Widget build(BuildContext context) => GCard(
    child: Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        const Heading('تكليف تحقيق خاص'),
        UiText(
          'المسؤول الحالي: ${widget.item['investigatorName'] ?? 'لم يسند'}',
        ),
        const UiText(
          'اكتب موجزًا مستقلًا دون أسماء الطفل والأسرة أو وسيلة اتصالهم. لا يُنسخ نص الشكوى، ولا تُمنح صلاحية إضافية. لا يجوز إسناد التحقيق لطرف في الملاحظة.',
        ),
        RemoteBody(
          auth: widget.auth,
          path: '/api/v1/manage/feedback/${widget.item['id']}/investigators',
          builder: (context, value) {
            final choices = rows(obj(value)['items'] ?? const []);
            if (choices.isEmpty) {
              return const UiText(
                'لا يوجد إداري مخول مستقل ضمن فرع ورياضة المجموعة. راجع الصلاحيات أولًا.',
              );
            }
            return Column(
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: [
                DropdownButtonFormField<String>(
                  key: ValueKey(member),
                  isExpanded: true,
                  initialValue: choices.any((m) => m['id'] == member)
                      ? member
                      : null,
                  decoration: const InputDecoration(labelText: 'مسؤول التحقيق'),
                  items: choices
                      .map(
                        (m) => DropdownMenuItem(
                          value: textOf(m['id']),
                          child: UiText(textOf(m['name'])),
                        ),
                      )
                      .toList(),
                  onChanged: busy ? null : (v) => setState(() => member = v),
                ),
                const SizedBox(height: 12),
                TextField(
                  controller: brief,
                  maxLength: 500,
                  maxLines: 3,
                  enabled: !busy,
                  decoration: const InputDecoration(
                    labelText: 'موجز يراجعه المالك',
                  ),
                ),
                FilledButton(
                  onPressed: busy || !choices.any((m) => m['id'] == member)
                      ? null
                      : () => save(false),
                  child: const UiText('حفظ تكليف التحقيق'),
                ),
              ],
            );
          },
        ),
        if (widget.item['investigatorMembershipId'] != null)
          TextButton(
            onPressed: busy ? null : () => save(true),
            child: const UiText('إلغاء تكليف التحقيق'),
          ),
        if (message.isNotEmpty) UiText(message),
      ],
    ),
  );
}

class PrivateInvestigationsPage extends StatefulWidget {
  final AuthController auth;
  final String? assignmentId;
  const PrivateInvestigationsPage({
    super.key,
    required this.auth,
    this.assignmentId,
  });
  @override
  State<PrivateInvestigationsPage> createState() =>
      _PrivateInvestigationsState();
}

class _PrivateInvestigationsState extends State<PrivateInvestigationsPage>
    with WidgetsBindingObserver {
  late final identity = followUpContext(widget.auth);
  final note = TextEditingController();
  int revision = 0;
  bool foreground = true, busy = false;
  String message = '';
  String? signature, requestId;
  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addObserver(this);
  }

  @override
  void dispose() {
    WidgetsBinding.instance.removeObserver(this);
    note.dispose();
    super.dispose();
  }

  @override
  void didChangeAppLifecycleState(AppLifecycleState state) {
    setState(() {
      foreground = state == AppLifecycleState.resumed;
      revision++;
      note.clear();
      signature = null;
      requestId = null;
      message = '';
    });
  }

  Future<void> save(Json item) async {
    if (busy || identity != followUpContext(widget.auth)) return;
    if (note.text.trim().length < 5) {
      setState(() => message = 'اكتب نتيجة من خمسة أحرف على الأقل.');
      return;
    }
    final payload = {'version': item['version'], 'note': note.text.trim()};
    final draft = jsonEncode(payload);
    if (signature != draft) {
      signature = draft;
      requestId = followUpRequestId();
    }
    setState(() {
      busy = true;
      message = '';
    });
    try {
      await widget.auth.request(
        'POST',
        '/api/v1/my-followups/investigations/${widget.assignmentId}/transition',
        body: {...payload, 'requestId': requestId},
      );
      if (mounted && identity == followUpContext(widget.auth)) {
        setState(() {
          note.clear();
          revision++;
          signature = null;
          requestId = null;
          message = 'تم حفظ النتيجة الخاصة للمالك فقط.';
        });
      }
    } catch (e) {
      if (mounted && identity == followUpContext(widget.auth)) {
        setState(
          () => message = e is ApiFailure
              ? e.message
              : 'تعذر حفظ النتيجة الخاصة.',
        );
      }
    } finally {
      if (mounted) setState(() => busy = false);
    }
  }

  @override
  Widget build(BuildContext context) => ListenableBuilder(
    listenable: widget.auth,
    builder: (context, _) {
      if (!foreground ||
          widget.auth.session?.selected == null ||
          identity != followUpContext(widget.auth)) {
        return const SizedBox.shrink();
      }
      return GuardianPage(
        title: 'تحقيقات خاصة مسندة إليك',
        child: RemoteBody(
          key: ValueKey(revision),
          auth: widget.auth,
          path:
              '/api/v1/my-followups/investigations${widget.assignmentId == null ? '' : '/${widget.assignmentId}'}',
          builder: (context, value) {
            final data = obj(value);
            return gList([
              const UiText(
                'موجز راجعه المالك، دون ملف الأسرة أو نص شكواها. نتائجك خاصة للمالك؛ ليست ردًا للأسرة أو حلًا أو إغلاقًا للملاحظة.',
              ),
              if (message.isNotEmpty) UiText(message),
              if (widget.assignmentId == null) ...[
                for (final item in rows(data['items'] ?? const []))
                  GCard(
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.stretch,
                      children: [
                        UiText(textOf(item['brief'])),
                        UiText(
                          '${feedbackStates[item['status']]} · الموعد: ${item['dueOn'] ?? 'غير محدد'}',
                        ),
                        OutlinedButton(
                          onPressed: () async {
                            await openPage(
                              context,
                              PrivateInvestigationsPage(
                                auth: widget.auth,
                                assignmentId: textOf(item['assignmentId']),
                              ),
                            );
                            if (mounted) setState(() => revision++);
                          },
                          child: const UiText('فتح تكليف التحقيق'),
                        ),
                      ],
                    ),
                  ),
                if (rows(data['items'] ?? const []).isEmpty)
                  const UiText('لا توجد تحقيقات خاصة مسندة إليك.'),
                if (data['hasMore'] == true)
                  const UiText('تظهر أول 100 نتيجة؛ البقية محفوظة لدى المالك.'),
              ] else ...[
                Heading(feedbackStates[data['status']] ?? 'غير متاحة'),
                GCard(child: UiText(textOf(data['brief']))),
                UiText('الموعد: ${data['dueOn'] ?? 'غير محدد'}'),
                if (data['canEdit'] == true) ...[
                  TextField(
                    controller: note,
                    maxLength: 500,
                    maxLines: 3,
                    enabled: !busy,
                    decoration: const InputDecoration(
                      labelText: 'نتيجة التحقيق للمالك',
                    ),
                  ),
                  FilledButton(
                    onPressed: busy ? null : () => save(data),
                    child: const UiText('حفظ النتيجة الخاصة'),
                  ),
                ] else
                  const UiText('للقراءة فقط وفق صلاحياتك الحالية.'),
                const Heading('آخر نتائجك لهذا التكليف'),
                for (final finding in rows(data['findings'] ?? const []))
                  GCard(child: UiText(textOf(finding['note']))),
              ],
              TextButton(
                onPressed: () => setState(() => revision++),
                child: const UiText('تحديث التحقيقات'),
              ),
            ]);
          },
        ),
      );
    },
  );
}
