import 'package:flutter/material.dart';

import '../../core/auth/auth_controller.dart';
import '../../core/theme/appearance.dart';
import '../guardian/guardian_ui.dart';
import 'private_media.dart';
import 'player_goals.dart' show goalCommandId;

/// The existing private gallery, with administrative withdrawal; never a public feed.
class AdminMediaGallery extends StatefulWidget {
  final AuthController auth;
  const AdminMediaGallery({super.key, required this.auth});
  @override
  State<AdminMediaGallery> createState() => _AdminGalleryState();
}

class _AdminGalleryState extends State<AdminMediaGallery>
    with WidgetsBindingObserver {
  bool foreground = true;
  int revision = 0;
  String? get identity => widget.auth.session?.selected == null
      ? null
      : '${widget.auth.session!.selected!.id}:${widget.auth.session!.selected!.contextRevision}';
  late final original = identity;
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
  void didChangeAppLifecycleState(AppLifecycleState state) {
    setState(() {
      foreground = state == AppLifecycleState.resumed;
      revision++;
    });
  }

  @override
  Widget build(BuildContext context) => ListenableBuilder(
    listenable: widget.auth,
    builder: (context, _) {
      if (!foreground || identity == null || identity != original) {
        return const SizedBox.shrink();
      }
      return GuardianPage(
        title: localized(
          context,
          'مراجعة وسائط اللاعبين',
          'Review player media',
        ),
        child: RemoteBody(
          key: ValueKey(revision),
          auth: widget.auth,
          path: '/api/v1/assets/gallery?includeWithdrawn=true',
          builder: (context, value) => gList([
            Text(
              localized(
                context,
                'أحدث100 نسخة، ضمن نطاقك. سحب النشر لا يحذف الأصل الإداري ولا يسترجع تنزيلًا سابقًا.',
                'Latest100 within your scope. Withdrawal preserves the administrative original and cannot retrieve previous downloads.',
              ),
            ),
            for (final item in rows(value))
              MediaTile(
                auth: widget.auth,
                media: item,
                footer: Column(
                  crossAxisAlignment: CrossAxisAlignment.stretch,
                  children: [
                    Text(
                      localized(
                        context,
                        item['withdrawn'] == true
                            ? 'مسحوبة من الأسر والمدرب'
                            : 'محفوظة — الوصول مشروط بالتفويض والموافقة',
                        item['withdrawn'] == true
                            ? 'Withdrawn from families and coach'
                            : 'Saved — access requires current authorization and consent',
                      ),
                    ),
                    if (item['withdrawalNote'] != null)
                      Text(textOf(item['withdrawalNote'])),
                    if (item['canWithdraw'] == true &&
                        item['withdrawn'] != true)
                      _Withdrawal(
                        auth: widget.auth,
                        id: textOf(item['id']),
                        onSaved: () => setState(() => revision++),
                      ),
                    if (item['withdrawn'] == true)
                      Text(
                        localized(
                          context,
                          'إعادة النشر تحتاج رفع نسخة ومراجعة جديدتين من المعرض؛ لا يوجد استرجاع تلقائي.',
                          'Republishing requires a new upload and review in the gallery; no automatic restoration.',
                        ),
                      ),
                  ],
                ),
              ),
          ]),
        ),
      );
    },
  );
}

class _Withdrawal extends StatefulWidget {
  final AuthController auth;
  final String id;
  final VoidCallback onSaved;
  const _Withdrawal({
    required this.auth,
    required this.id,
    required this.onSaved,
  });
  @override
  State<_Withdrawal> createState() => _WithdrawalState();
}

class _WithdrawalState extends State<_Withdrawal> {
  final note = TextEditingController();
  bool open = false, busy = false;
  String? intent, signature, error;
  @override
  void dispose() {
    note.dispose();
    super.dispose();
  }

  Future<void> save() async {
    if (busy) return;
    final text = note.text.trim();
    if (signature != text) {
      signature = text;
      intent = goalCommandId();
    }
    setState(() {
      busy = true;
      error = null;
    });
    try {
      await widget.auth.request(
        'POST',
        '/api/v1/assets/${widget.id}/withdraw',
        body: {'requestId': intent, 'note': text.isEmpty ? null : text},
      );
      if (mounted) widget.onSaved();
    } catch (_) {
      if (mounted) {
        setState(
          () => error = localized(
            context,
            'لم نؤكد حفظ السحب. أعد فتح المعرض أو أعد المحاولة بنفس الطلب.',
            'Could not confirm withdrawal. Reopen or retry the same request.',
          ),
        );
      }
    } finally {
      if (mounted) setState(() => busy = false);
    }
  }

  @override
  Widget build(BuildContext context) => !open
      ? OutlinedButton.icon(
          onPressed: () => setState(() => open = true),
          icon: const Icon(Icons.visibility_off_outlined),
          label: Text(localized(context, 'سحب النشر', 'Withdraw publication')),
        )
      : Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            Text(
              localized(
                context,
                'يتوقف وصول الأسرة والمدرب لهذه النسخة، مع حفظ الأصل للإدارة المخولة.',
                'Families and coach lose access to this copy; the authorized administration retains the original.',
              ),
            ),
            const SizedBox(height: 12),
            TextField(
              controller: note,
              enabled: !busy,
              maxLength: 500,
              decoration: InputDecoration(
                labelText: localized(
                  context,
                  'ملاحظة إدارية (اختياري)',
                  'Administrative note (optional)',
                ),
              ),
            ),
            if (error != null) Text(error!),
            FilledButton(
              onPressed: busy ? null : save,
              child: Text(
                localized(context, 'تأكيد سحب النشر', 'Confirm withdrawal'),
              ),
            ),
            TextButton(
              onPressed: busy ? null : () => setState(() => open = false),
              child: Text(localized(context, 'رجوع', 'Back')),
            ),
          ],
        );
}
