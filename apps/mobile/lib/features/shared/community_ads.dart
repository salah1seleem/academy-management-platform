import 'dart:async';
import 'dart:convert';

import 'package:flutter/material.dart';

import '../../core/api/api_client.dart';
import '../../core/auth/auth_controller.dart';
import '../../core/theme/ui_text.dart';
import '../guardian/guardian_ui.dart';
import 'private_media.dart';
import 'matches.dart';

String? _identity(AuthController auth) => auth.session?.selected == null
    ? null
    : '${auth.session!.selected!.academyId}:${auth.session!.selected!.id}:${auth.session!.selected!.contextRevision}';
String _requestId() {
  final h = commandKey().substring(0, 32);
  return '${h.substring(0, 8)}-${h.substring(8, 12)}-${h.substring(12, 16)}-${h.substring(16, 20)}-${h.substring(20)}';
}

const _states = {
  'Draft': 'مسودة',
  'PendingApproval': 'بانتظار الموافقة',
  'Approved': 'معتمد',
  'Active': 'يعرض الآن',
  'Scheduled': 'مجدول',
  'Paused': 'موقوف',
  'Ended': 'انتهت الفترة',
  'Rejected': 'مرفوض',
  'Pending': 'بانتظار المراجعة',
  'Revoked': 'سُحبت الموافقة',
};

class GuardianUrgentMatchAlerts extends StatelessWidget {
  final AuthController auth;
  final List<Json> alerts;
  const GuardianUrgentMatchAlerts({
    super.key,
    required this.auth,
    required this.alerts,
  });
  @override
  Widget build(BuildContext context) => Column(
    children: [
      for (final alert in alerts)
        Card(
          color: Theme.of(context).colorScheme.errorContainer,
          child: Padding(
            padding: const EdgeInsets.all(16),
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: [
                Row(
                  children: [
                    Icon(
                      Icons.notification_important_outlined,
                      color: Theme.of(context).colorScheme.onErrorContainer,
                    ),
                    const SizedBox(width: 8),
                    Expanded(
                      child: UiText(
                        'تحديث عاجل يخص المواعيد',
                        style: TextStyle(
                          fontWeight: FontWeight.bold,
                          color: Theme.of(context).colorScheme.onErrorContainer,
                        ),
                      ),
                    ),
                  ],
                ),
                const SizedBox(height: 8),
                UiText(
                  textOf(alert['message']),
                  style: TextStyle(
                    color: Theme.of(context).colorScheme.onErrorContainer,
                  ),
                ),
                TextButton(
                  onPressed: () => openPage(context, MatchSchedule(auth: auth)),
                  child: const UiText('مراجعة المباريات والمواعيد'),
                ),
              ],
            ),
          ),
        ),
    ],
  );
}

class GuardianSponsoredCard extends StatefulWidget {
  final AuthController auth;
  const GuardianSponsoredCard({super.key, required this.auth});
  @override
  State<GuardianSponsoredCard> createState() => _GuardianSponsoredCardState();
}

class _GuardianSponsoredCardState extends State<GuardianSponsoredCard>
    with WidgetsBindingObserver {
  late final identity = _identity(widget.auth);
  bool dead = false, measured = false;
  Json? ad;
  Timer? exposure, lease;
  ScrollPosition? scroll;
  int generation = 0;
  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addObserver(this);
    widget.auth.addListener(authChanged);
    load();
  }

  void authChanged() {
    if (identity == null || _identity(widget.auth) != identity) {
      dead = true;
      exposure?.cancel();
      lease?.cancel();
      if (mounted) {
        setState(() => ad = null);
      }
    }
  }

  @override
  void didChangeAppLifecycleState(AppLifecycleState state) {
    if (state == AppLifecycleState.resumed) {
      load();
    } else {
      exposure?.cancel();
      lease?.cancel();
      ++generation;
      if (mounted) {
        setState(() => ad = null);
      }
    }
  }

  @override
  void didChangeDependencies() {
    super.didChangeDependencies();
    final next = Scrollable.maybeOf(context)?.position;
    if (scroll != next) {
      scroll?.removeListener(onScroll);
      scroll = next;
      scroll?.addListener(onScroll);
    }
    WidgetsBinding.instance.addPostFrameCallback((_) {
      if (mounted) {
        check();
      }
    });
  }

  bool get live =>
      mounted &&
      !dead &&
      identity != null &&
      _identity(widget.auth) == identity;
  Future<void> load() async {
    final g = ++generation;
    try {
      final result = obj(
        await widget.auth.request('GET', '/api/v1/guardian/community-ads/feed'),
      );
      if (!live || g != generation) {
        return;
      }
      exposure?.cancel();
      lease?.cancel();
      setState(() {
        ad = result['item'] is Map ? obj(result['item']) : null;
        measured = false;
      });
      if (ad != null) {
        final end = DateTime.tryParse(textOf(ad!['endsAtUtc']));
        final duration = end == null
            ? Duration.zero
            : end.difference(DateTime.now().toUtc());
        lease = Timer(
          duration < const Duration(seconds: 30)
              ? (duration.isNegative ? Duration.zero : duration)
              : const Duration(seconds: 30),
          () {
            if (live) {
              setState(() => ad = null);
            }
            exposure?.cancel();
          },
        );
        WidgetsBinding.instance.addPostFrameCallback((_) {
          if (live) {
            check();
          }
        });
      }
    } catch (_) {
      if (live) {
        setState(() => ad = null);
      }
    }
  }

  bool visible() {
    if (!live ||
        ad == null ||
        WidgetsBinding.instance.lifecycleState != AppLifecycleState.resumed ||
        ModalRoute.of(context)?.isCurrent == false) {
      return false;
    }
    final box = context.findRenderObject();
    if (box is! RenderBox || !box.hasSize) {
      return false;
    }
    final rect = box.localToGlobal(Offset.zero) & box.size;
    final screen = Offset.zero & MediaQuery.sizeOf(context);
    final viewport = Scrollable.maybeOf(context)?.context.findRenderObject();
    final clip = viewport is RenderBox && viewport.hasSize
        ? (viewport.localToGlobal(Offset.zero) & viewport.size).intersect(
            screen,
          )
        : screen;
    final intersection = rect.intersect(clip);
    return box.size.height > 0 &&
        box.size.width > 0 &&
        !intersection.isEmpty &&
        intersection.width * intersection.height >=
            box.size.width * box.size.height * .5;
  }

  void onScroll() {
    WidgetsBinding.instance.addPostFrameCallback((_) {
      if (live) {
        check();
      }
    });
  }

  void check() {
    if (measured || !visible()) {
      exposure?.cancel();
      exposure = null;
      return;
    }
    if (exposure?.isActive == true) {
      return;
    }
    final id = ad!['id'];
    exposure = Timer(const Duration(seconds: 1), () async {
      if (!visible() || ad?['id'] != id) {
        return;
      }
      try {
        await widget.auth.request(
          'POST',
          '/api/v1/guardian/community-ads/$id/events',
          body: {'kind': 'Impression'},
        );
        if (live && ad?['id'] == id) {
          measured = true;
        }
      } catch (_) {
        if (live) {
          setState(() => ad = null);
        }
      }
    });
  }

  Future<void> hide() async {
    final id = ad?['id'];
    exposure?.cancel();
    lease?.cancel();
    setState(() => ad = null);
    if (id == null) {
      return;
    }
    try {
      await widget.auth.request(
        'POST',
        '/api/v1/guardian/community-ads/$id/events',
        body: {'kind': 'Hide'},
      );
    } catch (_) {
      if (mounted && live) {
        ScaffoldMessenger.maybeOf(context)?.showSnackBar(
          const SnackBar(
            content: UiText(
              'أُخفي المحتوى هنا، وتعذر حفظ إخفائه لبقية اليوم. يمكنك إيقاف المحتوى من الإعدادات.',
            ),
          ),
        );
      }
    }
  }

  Future<void> open() async {
    final id = ad?['id'];
    if (id == null) {
      return;
    }
    try {
      await widget.auth.request('GET', '/api/v1/guardian/community-ads/$id');
      await widget.auth.request(
        'POST',
        '/api/v1/guardian/community-ads/$id/events',
        body: {'kind': 'Click'},
      );
      if (!mounted || !live) {
        return;
      }
      exposure?.cancel();
      await openPage(
        context,
        GuardianSponsorDetail(auth: widget.auth, id: textOf(id)),
      );
      if (live) {
        await load();
      }
    } catch (_) {
      if (live) {
        setState(() => ad = null);
      }
    }
  }

  @override
  void dispose() {
    WidgetsBinding.instance.removeObserver(this);
    widget.auth.removeListener(authChanged);
    scroll?.removeListener(onScroll);
    exposure?.cancel();
    lease?.cancel();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final a = ad;
    if (a == null || !live) {
      return const SizedBox.shrink();
    }
    return Card(
      child: Padding(
        padding: const EdgeInsets.all(16),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            Row(
              children: [
                const Icon(Icons.campaign_outlined),
                const SizedBox(width: 8),
                const Expanded(
                  child: UiText('محتوى برعاية', style: TextStyle(fontSize: 14)),
                ),
                IconButton(
                  onPressed: hide,
                  tooltip: 'إخفاء الإعلان اليوم',
                  icon: const Icon(Icons.close),
                ),
              ],
            ),
            if (isPrivateAsset(a['imageUrl']))
              Semantics(
                label: textOf(a['imageAlt']),
                image: true,
                child: ClipRRect(
                  borderRadius: BorderRadius.circular(14),
                  child: PrivateImage(
                    auth: widget.auth,
                    path: textOf(a['imageUrl']),
                    height: 150,
                    fit: BoxFit.contain,
                  ),
                ),
              ),
            const SizedBox(height: 10),
            UiText(
              textOf(a['title']),
              style: const TextStyle(fontSize: 19, fontWeight: FontWeight.bold),
            ),
            const SizedBox(height: 6),
            UiText(
              textOf(a['body']),
              maxLines: 3,
              overflow: TextOverflow.ellipsis,
            ),
            const SizedBox(height: 8),
            UiText(
              textOf(a['advertiser']),
              style: const TextStyle(fontSize: 14),
            ),
            const SizedBox(height: 12),
            FilledButton(
              onPressed: open,
              child: UiText(textOf(a['buttonText'])),
            ),
          ],
        ),
      ),
    );
  }
}

class SponsorPreferenceEntry extends StatefulWidget {
  final AuthController auth;
  const SponsorPreferenceEntry({super.key, required this.auth});
  @override
  State<SponsorPreferenceEntry> createState() => _SponsorPreferenceEntryState();
}

class _SponsorPreferenceEntryState extends State<SponsorPreferenceEntry> {
  late final identity = _identity(widget.auth);
  bool show = false;
  @override
  void initState() {
    super.initState();
    load();
  }

  Future<void> load() async {
    try {
      final p = obj(
        await widget.auth.request(
          'GET',
          '/api/v1/guardian/community-ads/preference',
        ),
      );
      if (mounted && identity == _identity(widget.auth)) {
        setState(
          () => show = p['available'] == true || p['inAppOptIn'] == true,
        );
      }
    } catch (_) {}
  }

  @override
  Widget build(BuildContext context) =>
      !show || identity != _identity(widget.auth)
      ? const SizedBox.shrink()
      : GLink('اختيار المحتوى برعاية', Icons.campaign_outlined, () async {
          await openPage(
            context,
            GuardianSponsorPreferences(auth: widget.auth),
          );
          if (mounted) {
            load();
          }
        });
}

class GuardianSponsorPreferences extends StatefulWidget {
  final AuthController auth;
  const GuardianSponsorPreferences({super.key, required this.auth});
  @override
  State<GuardianSponsorPreferences> createState() =>
      _GuardianSponsorPreferencesState();
}

class _GuardianSponsorPreferencesState
    extends State<GuardianSponsorPreferences> {
  late final identity = _identity(widget.auth);
  Json? data;
  String message = '';
  bool busy = false;
  @override
  void initState() {
    super.initState();
    load();
  }

  Future<void> load() async {
    try {
      final p = obj(
        await widget.auth.request(
          'GET',
          '/api/v1/guardian/community-ads/preference',
        ),
      );
      if (mounted && identity == _identity(widget.auth)) {
        setState(() => data = p);
      }
    } catch (e) {
      if (mounted) {
        setState(
          () => message = e is ApiFailure ? e.message : 'تعذر قراءة اختيارك.',
        );
      }
    }
  }

  Future<void> save() async {
    if (busy || data == null) {
      return;
    }
    setState(() => busy = true);
    try {
      await widget.auth.request(
        'PUT',
        '/api/v1/guardian/community-ads/preference',
        body: {
          'inAppOptIn': data!['inAppOptIn'] == true,
          'adultDeclared': data!['adultDeclared'] == true,
        },
      );
      if (mounted && identity == _identity(widget.auth)) {
        setState(
          () => message =
              'تم حفظ اختيارك. إشعارات مواعيد الطفل مستقلة عن هذا الاختيار.',
        );
      }
    } catch (e) {
      if (mounted) {
        setState(
          () => message = e is ApiFailure ? e.message : 'تعذر حفظ اختيارك.',
        );
      }
    } finally {
      if (mounted) {
        setState(() => busy = false);
      }
    }
  }

  @override
  Widget build(BuildContext context) => ListenableBuilder(
    listenable: widget.auth,
    builder: (context, _) =>
        identity == null || identity != _identity(widget.auth)
        ? const SizedBox.shrink()
        : GuardianPage(
            title: 'اختيار المحتوى برعاية',
            child: gList([
              if (message.isNotEmpty) UiText(message),
              if (data == null) const CircularProgressIndicator(),
              if (data != null) ...[
                UiText(textOf(data!['notice'])),
                if (data!['available'] != true)
                  const UiText(
                    'المحتوى غير متاح حاليًا. يمكنك سحب موافقتك المحفوظة رغم ذلك.',
                  ),
                SwitchListTile(
                  title: const UiText('محتوى برعاية داخل التطبيق'),
                  value: data!['inAppOptIn'] == true,
                  onChanged:
                      busy ||
                          data!['available'] != true &&
                              data!['inAppOptIn'] != true
                      ? null
                      : (v) =>
                            setState(() => data = {...data!, 'inAppOptIn': v}),
                ),
                CheckboxListTile(
                  title: const UiText('أقر بأنني صاحب حساب بالغ'),
                  value: data!['adultDeclared'] == true,
                  onChanged: busy
                      ? null
                      : (v) => setState(
                          () => data = {...data!, 'adultDeclared': v == true},
                        ),
                ),
                FilledButton(
                  onPressed: busy ? null : save,
                  child: UiText(busy ? 'جارٍ الحفظ…' : 'حفظ الاختيار'),
                ),
              ],
            ]),
          ),
  );
}

class GuardianSponsorDetail extends StatefulWidget {
  final AuthController auth;
  final String id;
  const GuardianSponsorDetail({
    super.key,
    required this.auth,
    required this.id,
  });
  @override
  State<GuardianSponsorDetail> createState() => _GuardianSponsorDetailState();
}

class _GuardianSponsorDetailState extends State<GuardianSponsorDetail>
    with WidgetsBindingObserver {
  late final identity = _identity(widget.auth);
  Json? ad;
  String error = '';
  bool dead = false;
  int generation = 0;
  Timer? lease;
  bool get live =>
      mounted &&
      !dead &&
      identity != null &&
      _identity(widget.auth) == identity;
  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addObserver(this);
    widget.auth.addListener(authChanged);
    load();
  }

  void authChanged() {
    if (_identity(widget.auth) != identity) {
      dead = true;
      ++generation;
      lease?.cancel();
      if (mounted) setState(() => ad = null);
    }
  }

  Future<void> load() async {
    if (!live) return;
    final request = ++generation;
    lease?.cancel();
    try {
      final value = obj(
        await widget.auth.request(
          'GET',
          '/api/v1/guardian/community-ads/${widget.id}',
        ),
      );
      if (!live || request != generation) return;
      final end = DateTime.tryParse(textOf(value['endsAtUtc']));
      final left = end?.difference(DateTime.now().toUtc()) ?? Duration.zero;
      if (left <= Duration.zero) {
        setState(() {
          ad = null;
          error = 'انتهت فترة هذا المحتوى.';
        });
        return;
      }
      setState(() {
        ad = value;
        error = '';
      });
      lease = Timer(
        left < const Duration(seconds: 30) ? left : const Duration(seconds: 30),
        () {
          if (!live) return;
          setState(() => ad = null);
          load();
        },
      );
    } catch (_) {
      if (live && request == generation) {
        setState(() {
          ad = null;
          error = 'المحتوى غير متاح أو سُحبت الموافقة عليه.';
        });
      }
    }
  }

  @override
  void didChangeAppLifecycleState(AppLifecycleState state) {
    ++generation;
    lease?.cancel();
    if (mounted) setState(() => ad = null);
    if (state == AppLifecycleState.resumed) load();
  }

  @override
  void dispose() {
    ++generation;
    lease?.cancel();
    widget.auth.removeListener(authChanged);
    WidgetsBinding.instance.removeObserver(this);
    super.dispose();
  }

  @override
  Widget build(BuildContext context) => GuardianPage(
    title: 'محتوى برعاية',
    child: !live
        ? const SizedBox.shrink()
        : ad == null
        ? Center(
            child: error.isEmpty
                ? const CircularProgressIndicator()
                : UiText(error),
          )
        : Builder(
            builder: (context) {
              final a = ad!;
              return gList([
                UiText(textOf(a['advertiser'])),
                Heading(textOf(a['title'])),
                if (isPrivateAsset(a['imageUrl']))
                  PrivateImage(
                    auth: widget.auth,
                    path: textOf(a['imageUrl']),
                    height: 200,
                    fit: BoxFit.contain,
                  ),
                UiText(textOf(a['body'])),
                const SizedBox(height: 16),
                const UiText(
                  'صفحة معلومات، وليست دفعًا أو موافقة على مشاركة بياناتك.',
                ),
              ]);
            },
          ),
  );
}

class OwnerCommunityCampaigns extends StatefulWidget {
  final AuthController auth;
  const OwnerCommunityCampaigns({super.key, required this.auth});
  @override
  State<OwnerCommunityCampaigns> createState() =>
      _OwnerCommunityCampaignsState();
}

class _OwnerCommunityCampaignsState extends State<OwnerCommunityCampaigns> {
  late final identity = _identity(widget.auth);
  Json? data, options;
  bool busy = false;
  String message = '', draft = '', key = '';
  @override
  void initState() {
    super.initState();
    load();
  }

  Future<void> load() async {
    try {
      final o = obj(
            await widget.auth.request(
              'GET',
              '/api/v1/manage/community-campaigns/options',
            ),
          ),
          d = obj(
            await widget.auth.request(
              'GET',
              '/api/v1/manage/community-campaigns/',
            ),
          );
      if (mounted && identity == _identity(widget.auth)) {
        setState(() {
          options = o;
          data = d;
        });
      }
    } catch (e) {
      if (mounted) {
        setState(
          () => message = e is ApiFailure ? e.message : 'تعذر قراءة الحملات.',
        );
      }
    }
  }

  Future<void> review(Json r, String action, [String? branch]) async {
    if (busy) {
      return;
    }
    final body = {
      'action': action,
      'version': r['version'],
      'branchId': branch,
    };
    final serialized = jsonEncode({'id': r['id'], ...body});
    if (draft != serialized) {
      draft = serialized;
      key = _requestId();
    }
    setState(() => busy = true);
    try {
      await widget.auth.request(
        'POST',
        '/api/v1/manage/community-campaigns/${r['id']}/review',
        body: {...body, 'requestId': key},
      );
      if (mounted && identity == _identity(widget.auth)) {
        setState(
          () => message = 'تم حفظ المراجعة دون تحصيل أو إرسال خارج التطبيق.',
        );
        await load();
      }
    } catch (e) {
      if (mounted) {
        setState(
          () => message = e is ApiFailure ? e.message : 'تعذر حفظ المراجعة.',
        );
      }
    } finally {
      if (mounted) {
        setState(() => busy = false);
      }
    }
  }

  @override
  Widget build(BuildContext context) => ListenableBuilder(
    listenable: widget.auth,
    builder: (context, _) =>
        identity == null || identity != _identity(widget.auth)
        ? const SizedBox.shrink()
        : GuardianPage(
            title: 'المحتوى برعاية',
            child: gList([
              const UiText(
                'إدارة التصميم وإنشاء نسخه من الداشبورد. هنا معاينة النسخة واعتماد كل فرع أو سحب موافقته وإيقاف العرض.',
              ),
              if (message.isNotEmpty) UiText(message),
              TextButton(onPressed: load, child: const UiText('تحديث')),
              if (options != null) UiText(textOf(options!['measurement'])),
              if (data != null && rows(data!['items']).isEmpty)
                const UiText('لا توجد حملات بعد.'),
              if (data != null)
                for (final c in rows(data!['items'])) _campaign(c),
            ]),
          ),
  );
  Widget _campaign(Json c) {
    final r = rows(c['versions'])
        .firstWhere((r) => r['revision'] == c['currentRevision']);
    final licensed = options?['licensed'] == true,
        optIn = options?['tenantOptIn'] == true;
    return Card(
      child: Padding(
        padding: const EdgeInsets.all(16),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            Heading(textOf(c['internalName'])),
            UiText(
              '${textOf(c['advertiser'])} · نسخة ${r['revision']} · ${_states[r['state']]}',
            ),
            if (isPrivateAsset(r['imageUrl']))
              PrivateImage(
                auth: widget.auth,
                path: textOf(r['imageUrl']),
                height: 150,
                fit: BoxFit.contain,
              ),
            UiText(
              textOf(r['title']),
              style: const TextStyle(fontWeight: FontWeight.bold),
            ),
            UiText(textOf(r['body'])),
            if (r['storedState'] == 'Draft')
              FilledButton(
                onPressed: busy || !licensed ? null : () => review(r, 'Submit'),
                child: const UiText('تقديم للمراجعة'),
              ),
            if (r['storedState'] == 'Approved')
              OutlinedButton(
                onPressed: busy ? null : () => review(r, 'Pause'),
                child: const UiText('إيقاف العرض'),
              ),
            if (r['storedState'] == 'Paused')
              OutlinedButton(
                onPressed: busy || !licensed || !optIn
                    ? null
                    : () => review(r, 'Resume'),
                child: const UiText('استئناف المعتمد'),
              ),
            for (final t in rows(r['targets'])) ...[
              const Divider(),
              UiText('${t['name']} · ${_states[t['decision']]}'),
              if ([
                'PendingApproval',
                'Approved',
                'Paused',
              ].contains(r['storedState']))
                Wrap(
                  spacing: 8,
                  children: [
                    if (t['decision'] != 'Approved')
                      OutlinedButton(
                        onPressed: busy || !licensed || !optIn
                            ? null
                            : () => review(r, 'Approve', textOf(t['branchId'])),
                        child: const UiText('اعتماد هذا الفرع'),
                      ),
                    if (t['decision'] == 'Approved')
                      OutlinedButton(
                        onPressed: busy
                            ? null
                            : () => review(r, 'Revoke', textOf(t['branchId'])),
                        child: const UiText('سحب موافقة الفرع'),
                      )
                    else
                      OutlinedButton(
                        onPressed: busy || !licensed
                            ? null
                            : () => review(r, 'Reject', textOf(t['branchId'])),
                        child: const UiText('رفض هذا الفرع'),
                      ),
                  ],
                ),
            ],
            const Divider(),
            const UiText(
              'القياس مجمع؛ المعاينة وطلب API ليسا مشاهدة. لا تصدير لأسماء أو هواتف.',
            ),
            for (final m in rows(r['metrics']))
              UiText(
                '${m['kind'] == 'Impression'
                    ? 'مشاهدات مؤهلة'
                    : m['kind'] == 'Click'
                    ? 'نقرات'
                    : 'إخفاء'}: ${m['suppressed'] == true ? 'محجوب لحماية الشرائح الصغيرة' : '${m['count']} حدثًا من ${m['accounts']} حسابات'}',
              ),
            ExpansionTile(
              title: const UiText('سجل المراجعات'),
              children: [
                for (final h in rows(r['history']))
                  ListTile(
                    title: UiText(textOf(h['actorName'])),
                    subtitle: UiText(
                      '${h['decision'] == 'Approve'
                          ? 'اعتماد فرع'
                          : h['decision'] == 'Pause'
                          ? 'إيقاف'
                          : h['decision'] == 'Revoke'
                          ? 'سحب موافقة'
                          : 'مراجعة'} · ${day(h['createdAtUtc'])}',
                    ),
                  ),
              ],
            ),
          ],
        ),
      ),
    );
  }
}
