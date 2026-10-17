import 'package:flutter/material.dart';

import '../../core/auth/auth_controller.dart';
import '../../core/theme/appearance.dart';
import '../../core/theme/ui_text.dart';
import '../guardian/guardian_ui.dart';

String _identity(AuthController auth) {
  final m = auth.session?.selected;
  return '${m?.academyId}:${m?.id}:${m?.contextRevision}';
}

String _copy(BuildContext context, Object? value) {
  final t = obj(value);
  return localized(context, textOf(t['ar']), textOf(t['en']));
}

String _section(BuildContext context, String value) => switch (value) {
  'setup' => localized(context, 'البدء والإعداد', 'Getting started'),
  'overview' => localized(context, 'قراءة الأداء', 'Performance'),
  'subscriptions' => localized(
    context,
    'اللاعبون والاشتراكات',
    'Players & subscriptions',
  ),
  'operations' => localized(context, 'التشغيل اليومي', 'Daily operations'),
  'development' => localized(context, 'تطوير اللاعب', 'Player development'),
  'finance' => localized(context, 'المالية والمستحقات', 'Finance & dues'),
  _ => localized(context, 'كل الأقسام', 'All sections'),
};

class HelpEntry extends StatelessWidget {
  final AuthController auth;
  const HelpEntry({super.key, required this.auth});
  @override
  Widget build(BuildContext context) => ListenableBuilder(
    listenable: auth,
    builder: (context, _) => auth.session?.selected == null
        ? const SizedBox.shrink()
        : _HelpAvailability(key: ValueKey(_identity(auth)), auth: auth),
  );
}

class _HelpAvailability extends StatefulWidget {
  final AuthController auth;
  const _HelpAvailability({super.key, required this.auth});
  @override
  State<_HelpAvailability> createState() => _HelpAvailabilityState();
}

class _HelpAvailabilityState extends State<_HelpAvailability>
    with WidgetsBindingObserver {
  bool available = false;
  int generation = 0;
  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addObserver(this);
    load();
  }

  Future<void> load() async {
    final request = ++generation;
    if (mounted) setState(() => available = false);
    try {
      final data = obj(
        await widget.auth.request('GET', '/api/v1/help/availability'),
      );
      if (mounted && request == generation) {
        setState(() => available = data['available'] == true);
      }
    } catch (_) {
      /* Help must not block normal work. */
    }
  }

  @override
  void didChangeAppLifecycleState(AppLifecycleState state) {
    if (state == AppLifecycleState.resumed) load();
  }

  @override
  void dispose() {
    generation++;
    WidgetsBinding.instance.removeObserver(this);
    super.dispose();
  }

  @override
  Widget build(BuildContext context) => available
      ? GLink(
          localized(context, 'دليل الاستخدام', 'User guides'),
          Icons.menu_book_outlined,
          () => openPage(context, HelpPage(auth: widget.auth)),
        )
      : const SizedBox.shrink();
}

class HelpPage extends StatelessWidget {
  final AuthController auth;
  final String? id;
  const HelpPage({super.key, required this.auth, this.id});
  @override
  Widget build(BuildContext context) => ListenableBuilder(
    listenable: auth,
    builder: (context, _) => auth.session?.selected == null
        ? GuardianPage(
            title: localized(context, 'دليل الاستخدام', 'User guides'),
            child: Center(
              child: UiText(
                localized(
                  context,
                  'سجل الدخول لعرض الدليل.',
                  'Sign in to view your guides.',
                ),
              ),
            ),
          )
        : _HelpReader(
            key: ValueKey('${_identity(auth)}:$id'),
            auth: auth,
            id: id,
          ),
  );
}

class _HelpReader extends StatefulWidget {
  final AuthController auth;
  final String? id;
  const _HelpReader({super.key, required this.auth, this.id});
  @override
  State<_HelpReader> createState() => _HelpReaderState();
}

class _HelpReaderState extends State<_HelpReader> with WidgetsBindingObserver {
  final search = TextEditingController();
  String section = '';
  List<Map<String, dynamic>>? lessons;
  bool failed = false;
  int generation = 0;
  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addObserver(this);
    load();
  }

  @override
  void didChangeAppLifecycleState(AppLifecycleState state) {
    if (state == AppLifecycleState.resumed) load();
  }

  Future<void> load() async {
    final request = ++generation;
    setState(() {
      lessons = null;
      failed = false;
    });
    try {
      final path = widget.id == null
          ? '/api/v1/help/lessons?${Uri(queryParameters: {'search': search.text.trim(), 'section': section}).query}'
          : '/api/v1/help/lessons/${Uri.encodeComponent(widget.id!)}';
      final data = obj(await widget.auth.request('GET', path));
      if (mounted && request == generation) {
        setState(
          () => lessons = widget.id == null ? rows(data['items']) : [data],
        );
      }
    } catch (_) {
      if (mounted && request == generation) setState(() => failed = true);
    }
  }

  @override
  void dispose() {
    generation++;
    search.dispose();
    WidgetsBinding.instance.removeObserver(this);
    super.dispose();
  }

  Widget notice() => UiText(
    localized(
      context,
      'أدلة مكتوبة حسب صلاحياتك؛ الفيديوهات بانتظار رفع المحتوى. قراءة الدليل لا تمنح صلاحية تنفيذ.',
      'Written guides for your permissions; videos await upload. Reading does not grant action permissions.',
    ),
    style: Theme.of(context).textTheme.bodySmall,
  );
  @override
  Widget build(BuildContext context) => GuardianPage(
    title: localized(context, 'دليل الاستخدام', 'User guides'),
    child: gList([
      GCard(
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            const Icon(Icons.auto_stories_outlined),
            const SizedBox(height: 12),
            Heading(
              localized(
                context,
                widget.id == null ? 'كيف نساعدك اليوم؟' : 'خطوة بخطوة',
                widget.id == null ? 'How can we help?' : 'Step by step',
              ),
            ),
            const SizedBox(height: 8),
            notice(),
          ],
        ),
      ),
      if (widget.id == null) ...[
        TextField(
          controller: search,
          maxLength: 100,
          textInputAction: TextInputAction.search,
          onSubmitted: (_) => load(),
          decoration: InputDecoration(
            labelText: localized(context, 'البحث في الأدلة', 'Search guides'),
            prefixIcon: const Icon(Icons.search),
          ),
        ),
        DropdownButtonFormField<String>(
          key: ValueKey(section),
          initialValue: section,
          isExpanded: true,
          decoration: InputDecoration(
            labelText: localized(context, 'القسم', 'Section'),
          ),
          items: [
            for (final s in [
              '',
              'setup',
              'overview',
              'subscriptions',
              'operations',
              'development',
              'finance',
            ])
              DropdownMenuItem(value: s, child: UiText(_section(context, s))),
          ],
          onChanged: (v) {
            setState(() => section = v ?? '');
            load();
          },
        ),
        Wrap(
          spacing: 12,
          children: [
            FilledButton.icon(
              onPressed: load,
              icon: const Icon(Icons.search),
              label: UiText(localized(context, 'بحث', 'Search')),
            ),
            TextButton(
              onPressed: () {
                search.clear();
                section = '';
                load();
              },
              child: UiText(localized(context, 'مسح', 'Clear')),
            ),
          ],
        ),
      ],
      if (failed)
        GCard(
          child: Column(
            children: [
              UiText(
                localized(
                  context,
                  'تعذر عرض الدليل؛ قد لا يكون متاحًا ضمن ميزات الأكاديمية وصلاحياتك.',
                  'Could not display the guide; it may not be available for your academy and permissions.',
                ),
              ),
              TextButton(
                onPressed: load,
                child: UiText(localized(context, 'إعادة المحاولة', 'Retry')),
              ),
            ],
          ),
        )
      else if (lessons == null)
        const Center(child: CircularProgressIndicator())
      else if (lessons!.isEmpty)
        UiText(
          localized(
            context,
            'لا توجد أدلة ضمن هذا البحث وصلاحياتك الحالية.',
            'No guides match this search and your current permissions.',
          ),
        )
      else
        for (final lesson in lessons!)
          GCard(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                UiText(
                  _section(context, textOf(lesson['section'])),
                  style: Theme.of(context).textTheme.labelMedium,
                ),
                const SizedBox(height: 10),
                Heading(_copy(context, lesson['title'])),
                const SizedBox(height: 8),
                UiText(_copy(context, lesson['summary'])),
                if (widget.id == null)
                  TextButton.icon(
                    onPressed: () => openPage(
                      context,
                      HelpPage(auth: widget.auth, id: textOf(lesson['id'])),
                    ),
                    icon: const Icon(Icons.arrow_forward),
                    label: UiText(
                      localized(
                        context,
                        lesson['contentKind'] == 'AwaitingContent'
                            ? 'حالة المحتوى'
                            : 'اقرأ الخطوات',
                        lesson['contentKind'] == 'AwaitingContent'
                            ? 'Content status'
                            : 'Read steps',
                      ),
                    ),
                  )
                else if (lesson['contentKind'] == 'AwaitingContent')
                  UiText(
                    localized(
                      context,
                      'بانتظار استكمال المحتوى؛ لا يوجد شرح تنفيذي منشور بعد.',
                      'Content is pending; no published workflow guide yet.',
                    ),
                  )
                else ...[
                  for (final (i, step) in rows(lesson['steps']).indexed)
                    Padding(
                      padding: const EdgeInsets.symmetric(vertical: 14),
                      child: Row(
                        crossAxisAlignment: CrossAxisAlignment.start,
                        children: [
                          CircleAvatar(radius: 15, child: UiText('${i + 1}')),
                          const SizedBox(width: 12),
                          Expanded(child: UiText(_copy(context, step))),
                        ],
                      ),
                    ),
                  const Divider(),
                  UiText(
                    localized(
                      context,
                      'دليل مكتوب · إصدار ${lesson['version']} · لا يوجد فيديو منشور بعد.',
                      'Written guide · Version ${lesson['version']} · No video published yet.',
                    ),
                    style: Theme.of(context).textTheme.bodySmall,
                  ),
                ],
              ],
            ),
          ),
      IconButton(
        tooltip: localized(context, 'تحديث الأدلة', 'Refresh guides'),
        onPressed: load,
        icon: const Icon(Icons.refresh),
      ),
    ]),
  );
}
