import 'dart:convert';

import 'package:flutter/material.dart';

import '../../core/auth/auth_controller.dart';
import '../../core/theme/appearance.dart';
import '../guardian/guardian_ui.dart';
import 'player_goals.dart' show goalCommandId;

class GroupMediaConsentCard extends StatefulWidget {
  final AuthController auth;
  final String playerId;
  final VoidCallback? onSaved;
  const GroupMediaConsentCard({
    super.key,
    required this.auth,
    required this.playerId,
    this.onSaved,
  });
  @override
  State<GroupMediaConsentCard> createState() => _ConsentState();
}

class _ConsentState extends State<GroupMediaConsentCard>
    with WidgetsBindingObserver {
  String? get identity => widget.auth.session?.selected == null
      ? null
      : '${widget.auth.session!.selected!.id}:${widget.auth.session!.selected!.contextRevision}';
  late final original = identity;
  bool foreground = true, busy = false;
  int revision = 0;
  String? signature, request, error;
  String get path =>
      '/api/v1/guardian/children/${widget.playerId}/group-media-consent';
  String l(String ar, String en) => localized(context, ar, en);
  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addObserver(this);
  }

  @override
  void dispose() {
    WidgetsBinding.instance.removeObserver(this);
    super.dispose();
  }

  @override
  void didChangeAppLifecycleState(AppLifecycleState s) {
    setState(() {
      foreground = s == AppLifecycleState.resumed;
      revision++;
      error = null;
    });
  }

  Future<void> save(Json d) async {
    if (busy) return;
    final allowed = d['allowed'] != true;
    final confirmed = await showDialog<bool>(
      context: context,
      builder: (context) => AlertDialog(
        title: Text(
          l(
            allowed ? 'السماح بمشاركة طفلي؟' : 'سحب موافقة المشاركة؟',
            allowed ? 'Allow group sharing?' : 'Withdraw sharing consent?',
          ),
        ),
        content: Text(textOf(d['notice'])),
        actions: [
          TextButton(
            onPressed: () => Navigator.pop(context, false),
            child: Text(l('رجوع', 'Back')),
          ),
          FilledButton(
            onPressed: () => Navigator.pop(context, true),
            child: Text(l('تأكيد اختياري', 'Confirm my choice')),
          ),
        ],
      ),
    );
    if (confirmed != true || !mounted || identity != original || !foreground) {
      return;
    }
    final next = jsonEncode({
      'path': path,
      'revision': d['revision'],
      'allowed': allowed,
    });
    if (signature != next) {
      signature = next;
      request = goalCommandId();
    }
    setState(() {
      busy = true;
      error = null;
    });
    try {
      await widget.auth.request(
        'POST',
        path,
        body: {
          'allowed': allowed,
          'revision': d['revision'],
          'requestId': request,
        },
      );
      if (!mounted || identity != original) return;
      signature = null;
      request = null;
      setState(() => revision++);
      widget.onSaved?.call();
    } catch (_) {
      if (mounted && identity == original) {
        setState(
          () => error = l(
            'لم نؤكد حفظ اختيار المشاركة؛ أعد فتحه ثم حاول مرة أخرى.',
            'Could not confirm the saved choice; reopen and retry.',
          ),
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
      if (!foreground || identity == null || identity != original) {
        return const SizedBox.shrink();
      }
      return RemoteBody(
        key: ValueKey('$identity:$revision'),
        auth: widget.auth,
        path: path,
        builder: (context, v) {
          final d = obj(v);
          return GCard(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: [
                const Icon(Icons.verified_user_outlined),
                Text(
                  l('خصوصية صور المجموعة', 'Group media privacy'),
                  style: Theme.of(context).textTheme.titleLarge,
                ),
                Text(textOf(d['notice'])),
                const SizedBox(height: 12),
                Text(
                  '${d['player']} · ${l(d['allowed'] == true ? 'المشاركة مسموحة باختيارك' : 'المشاركة غير مسموحة', d['allowed'] == true ? 'Sharing allowed by your choice' : 'Sharing not allowed')}',
                ),
                Text(
                  l(
                    'إعادة السماح لا تعيد نشر النسخ القديمة تلقائيًا، ولا يؤثر الرفض على الاشتراك أو صورة طفلك الخاصة.',
                    'Allowing again never republishes old copies automatically. Refusal does not affect subscriptions or your child-only photos.',
                  ),
                ),
                if (error != null) Text(error!),
                FilledButton(
                  onPressed: busy ? null : () => save(d),
                  child: Text(
                    l(
                      d['allowed'] == true
                          ? 'سحب موافقة المشاركة'
                          : 'السماح بالمشاركة داخل المجموعة',
                      d['allowed'] == true
                          ? 'Withdraw sharing consent'
                          : 'Allow sharing within the group',
                    ),
                  ),
                ),
              ],
            ),
          );
        },
      );
    },
  );
}
