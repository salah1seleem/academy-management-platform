import 'information_help.dart';

import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_svg/flutter_svg.dart';
import 'package:url_launcher/url_launcher.dart';

import '../../core/auth/auth_controller.dart';
import '../../core/theme/ui_text.dart';
import '../guardian/guardian_ui.dart';
import '../admin/native_operation_form.dart';
import '../owner/owner_followups.dart' show followUpRequestId;

class AudienceCommunicationLink extends StatelessWidget {
  const AudienceCommunicationLink({
    super.key,
    required this.auth,
    required this.id,
    this.team = false,
  });
  final AuthController auth;
  final String id;
  final bool team;
  @override
  Widget build(BuildContext context) => GCard(
    child: Wrap(
      spacing: 8,
      children: [
        TextButton.icon(
          icon: const Icon(Icons.notifications_active_outlined),
          label: const UiText('إرسال إشعار'),
          onPressed: () => openPage(
            context,
            AudienceCommunicationPage(auth: auth, id: id, team: team),
          ),
        ),
        TextButton.icon(
          icon: SvgPicture.asset(
            'assets/branding/whatsapp.svg',
            width: 22,
            height: 22,
            colorFilter: const ColorFilter.mode(
              Color(0xFF159447),
              BlendMode.srcIn,
            ),
          ),
          label: const UiText('ربط واتساب'),
          onPressed: () => openPage(
            context,
            AudienceCommunicationPage(
              auth: auth,
              id: id,
              team: team,
              settings: true,
            ),
          ),
        ),
      ],
    ),
  );
}

class AudienceCommunicationPage extends StatefulWidget {
  const AudienceCommunicationPage({
    super.key,
    required this.auth,
    required this.id,
    this.team = false,
    this.settings = false,
  });
  final AuthController auth;
  final String id;
  final bool team, settings;
  @override
  State<AudienceCommunicationPage> createState() =>
      _AudienceCommunicationPageState();
}

class _AudienceCommunicationPageState
    extends NativeOperationState<AudienceCommunicationPage> {
  @override
  AuthController get auth => widget.auth;
  final text = TextEditingController(), invite = TextEditingController();
  Json? data, preview;
  bool include = false, sent = false;
  late bool whatsapp = widget.settings;
  String requestId = followUpRequestId();
  String get path =>
      '/api/v1/communication/${widget.team ? 'teams' : 'groups'}/${widget.id}';
  String? matchId;
  @override
  void initState() {
    super.initState();
    load();
  }

  @override
  void dispose() {
    text.dispose();
    invite.dispose();
    super.dispose();
  }

  Future<void> load() => run(() async {
    final result = obj(await auth.request('GET', path));
    if (current) {
      setState(() {
        data = result;
        invite.text = result['inviteUrl']?.toString() ?? '';
        preview = null;
      });
    }
  });
  void invalidate() => setState(() {
    preview = null;
    sent = false;
    requestId = followUpRequestId();
  });
  Future<void> saveLink() => run(() async {
    final r = obj(
      await auth.request(
        'PUT',
        '$path/whatsapp',
        body: {
          'inviteUrl': invite.text.trim(),
          'expectedVersion': data!['version'],
          'coachCanSend': data!['canSetCoachPolicy'] == true
              ? data!['coachCanSend'] == true
              : null,
        },
      ),
    );
    if (current) {
      setState(() {
        data!['version'] = r['version'];
        data!['coachCanSend'] = r['coachCanSend'];
        data!['inviteUrl'] = r['linked'] == true ? invite.text.trim() : null;
        preview = null;
        message = r['linked'] == true
            ? 'تم ربط الدعوة. الانضمام يتم داخل واتساب.'
            : 'تم إلغاء ربط الدعوة داخل التطبيق.';
      });
    }
  });
  Future<void> dispatch(bool send) => run(() async {
    final result = obj(
      await auth.request(
        'POST',
        '$path/${send ? 'send' : 'preview'}',
        body: {
          'message': text.text.trim(),
          'requestId': requestId,
          'previewHash': preview?['previewHash'],
          'includeInvitation': include,
          'matchId': matchId,
        },
      ),
    );
    if (current) {
      setState(() {
        if (send) {
          sent = true;
          preview = null;
          message = 'تم إرسال ${result['sent']} إشعار داخل التطبيق.';
        } else {
          preview = result;
        }
      });
    }
  });
  @override
  Widget build(
    BuildContext context,
  ) => page(widget.team ? 'تواصل الفريق' : 'تواصل المجموعة', [
    if (data == null && !busy)
      OutlinedButton(onPressed: load, child: const UiText('إعادة المحاولة')),
    if (data != null) ...[
      Heading(textOf(data!['name'])),
      SegmentedButton<bool>(
        segments: const [
          ButtonSegment(
            value: false,
            icon: Icon(Icons.notifications_outlined),
            label: UiText('إشعار'),
          ),
          ButtonSegment(
            value: true,
            icon: Icon(Icons.link),
            label: UiText('واتساب'),
          ),
        ],
        selected: {whatsapp},
        onSelectionChanged: (v) => setState(() => whatsapp = v.single),
      ),
      const SizedBox(height: 12),
      UiText(
        '${data!['playerCount']} لاعب · ${data!['recipientCount']} ولي أمر',
      ),
      if (!whatsapp)
        const InformationHelp(
          title: 'المستلمون',
          message: 'يصل الإشعار لأولياء أمور لاعبي المجموعة داخل التطبيق، مرة واحدة لكل حساب ولو كان له أكثر من ابن.',
        ),
      if (!whatsapp) ...[
        if (data!['canSend'] == true) ...[
          if (data!['nextEvent'] is Map)
            TextButton.icon(
              onPressed: () {
                final event = obj(data!['nextEvent']);
                text.text =
                    'تذكير ${data!['name']}: ${event['date']} الساعة ${trainingTimeOf(event['time'])}${event['venue'] == null ? '' : ' · ${event['venue']}'}${event['fee'] == null ? '' : ' · رسوم المشاركة ${money(event['fee'], event['currency'])}'}.';
                matchId = widget.team ? textOf(event['id']) : null;
                invalidate();
              },
              icon: const Icon(Icons.event_outlined),
              label: const UiText('استخدام الموعد القادم'),
            ),
          field(
            'رسالة عامة لأولياء الأمور',
            text,
            maxLines: 4,
            changed: invalidate,
          ),
          const InformationHelp(
            title: 'خصوصية الرسالة العامة',
            message: 'هذه الرسالة مشتركة بين جميع أولياء أمور المجموعة. استخدم التواصل الفردي لأي معلومات مالية أو صحية أو تقييم خاص بلاعب.',
          ),
          CheckboxListTile(
            value: include,
            onChanged: data!['inviteUrl'] == null
                ? null
                : (v) {
                    include = v == true;
                    invalidate();
                  },
            title: const UiText('إضافة رابط الانضمام إلى الإشعار'),
          ),
          OutlinedButton.icon(
            onPressed: sent || text.text.trim().isEmpty
                ? null
                : () => dispatch(false),
            icon: const Icon(Icons.preview_outlined),
            label: const UiText('مراجعة الرسالة والمستلمين'),
          ),
          if (preview != null)
            GCard(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.stretch,
                children: [
                  Heading('المعاينة · ${preview!['recipientCount']} ولي أمر'),
                  UiText(
                    '${preview!['playerCount']} لاعب · ${preview!['name']}',
                  ),
                  UiText(textOf(preview!['message'])),
                  if (preview!['includesInvitation'] == true)
                    const UiText(
                      'تتضمن الدعوة داخل التفاصيل؛ لا تُنسخ في نص التنبيه.',
                    ),
                  const UiText(
                    'الحفظ في صندوق التطبيق؛ لا يعني قراءة الرسالة أو تسليم إشعار جهاز.',
                  ),
                  FilledButton(
                    onPressed: (preview!['recipientCount'] as num? ?? 0) == 0
                        ? null
                        : () => dispatch(true),
                    child: const UiText('تأكيد الإرسال'),
                  ),
                ],
              ),
            ),
        ] else
          const EmptyMessage(
            'إرسال الإشعارات يحتاج تفويضًا من إدارة الأكاديمية.',
          ),
      ],
      if (whatsapp)
        ExpansionTile(
          title: const UiText('ربط مجموعة واتساب'),
          initiallyExpanded: true,
          children: [
            ListTile(
              title: const UiText('اسم المجموعة المقترح'),
              subtitle: UiText(textOf(data!['groupName'])),
              trailing: IconButton(
                tooltip: 'نسخ الاسم',
                icon: const Icon(Icons.copy),
                onPressed: () => Clipboard.setData(
                  ClipboardData(text: textOf(data!['groupName'])),
                ),
              ),
            ),
            ListTile(
              title: const UiText('نص الدعوة'),
              subtitle: UiText(textOf(data!['invitationText'])),
              trailing: IconButton(
                tooltip: 'نسخ نص الدعوة',
                icon: const Icon(Icons.copy),
                onPressed: () => Clipboard.setData(
                  ClipboardData(text: textOf(data!['invitationText'])),
                ),
              ),
            ),
            const Padding(
              padding: EdgeInsets.all(12),
              child: UiText(
                'ألصق رابط دعوة مجموعة واتساب هنا. الرسائل العامة تُرسل داخل التطبيق.',
              ),
            ),
            const InformationHelp(
              title: 'كيف يعمل ربط واتساب؟',
              message: 'أنشئ المجموعة في تطبيق واتساب، ثم افتح معلوماتها وانسخ رابط الدعوة. لا تحتاج إدخال رقم مرسل هنا. الرابط يتيح لولي الأمر الانضمام، ولا يرسل رسائل واتساب تلقائيًا. اسم المجموعة ونص الدعوة يُجهزان من بيانات الأكاديمية والمجموعة ويمكن نسخهما.\n\nإعدادات النشر والانضمام وإزالة الأعضاء تُدار من واتساب؛ قد تظهر أرقام أعضاء المجموعة لبعضهم.',
            ),
            if (data!['canSetCoachPolicy'] == true)
              CheckboxListTile(
                value: data!['coachCanSend'] == true,
                onChanged: (v) =>
                    setState(() => data!['coachCanSend'] = v == true),
                title: const UiText(
                  'السماح للمدرب المسند بإرسال إشعارات هذه المجموعة فقط',
                ),
              ),
            if (data!['canManageLink'] == true) ...[
              field(
                'رابط دعوة المجموعة — اختياري',
                invite,
                required: false,
                keyboard: TextInputType.url,
              ),
              OutlinedButton(
                onPressed: saveLink,
                child: const UiText('حفظ رابط الدعوة'),
              ),
            ],
            UiText(
              data!['inviteUrl'] == null
                  ? 'لم تُربط دعوة بعد.'
                  : 'رابط الانضمام محفوظ.',
            ),
          ],
        ),
    ],
  ]);
}

class AudienceAnnouncementPage extends StatefulWidget {
  const AudienceAnnouncementPage({
    super.key,
    required this.auth,
    required this.id,
  });
  final AuthController auth;
  final String id;
  @override
  State<AudienceAnnouncementPage> createState() =>
      _AudienceAnnouncementPageState();
}

class _AudienceAnnouncementPageState
    extends NativeOperationState<AudienceAnnouncementPage> {
  @override
  AuthController get auth => widget.auth;
  Json? data;
  @override
  void initState() {
    super.initState();
    load();
  }

  Future<void> load() => run(() async {
    final r = obj(
      await auth.request(
        'GET',
        '/api/v1/communication/announcements/${widget.id}',
      ),
    );
    if (current) setState(() => data = r);
  });
  Future<void> openInvite() => run(() async {
    final r = obj(
      await auth.request(
        'GET',
        '/api/v1/communication/announcements/${widget.id}',
      ),
    );
    if (!current) return;
    final link = r['inviteUrl']?.toString() ?? '';
    if (!RegExp(r'^https://chat\.whatsapp\.com/[A-Za-z0-9]{20,32}$')
        .hasMatch(link)) {
      setState(() => message = 'الدعوة لم تعد متاحة؛ راجع إدارة الأكاديمية.');
      return;
    }
    if (!await launchUrl(
          Uri.parse(link),
          mode: LaunchMode.externalApplication,
        ) &&
        current) {
      setState(
        () => message = 'تعذر فتح الدعوة. تحقق من تثبيت واتساب أو المتصفح.',
      );
    }
  });
  @override
  Widget build(BuildContext context) => page('إشعار المجموعة', [
    if (data == null && !busy)
      OutlinedButton(onPressed: load, child: const UiText('إعادة المحاولة')),
    if (data != null) ...[
      Heading(textOf(data!['name'])),
      GCard(child: UiText(textOf(data!['message']))),
      UiText('${day(data!['sentOn'])} · ${textOf(data!['sentTime'])}'),
      if (data!['inviteUrl'] != null)
        FilledButton.icon(
          onPressed: openInvite,
          icon: const Icon(Icons.open_in_new),
          label: const UiText('فتح دعوة واتساب'),
        ),
      if (data!['inviteUrl'] != null)
        const UiText(
          'فتح الدعوة لا يعني الانضمام إلى المجموعة. اختيار الانضمام يتم في واتساب.',
        ),
    ],
  ]);
}
