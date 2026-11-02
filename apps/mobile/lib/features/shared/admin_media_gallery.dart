import 'package:flutter/material.dart';

import '../../core/auth/auth_controller.dart';
import '../../core/theme/appearance.dart';
import '../guardian/guardian_ui.dart';
import 'private_media.dart';
import 'player_goals.dart' show goalCommandId;
import 'media_storage_notice.dart';
import 'media_memories.dart';

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
            MediaStorageNotice(auth: widget.auth),
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
                        item['erased'] == true
                            ? (item['purged'] == true
                                  ? 'تم حذف الملف؛ سجل النشر والحذف محفوظ'
                                  : 'محجوبة عن الجميع؛ إزالة الملف معلقة والمساحة لم تتحرر بعد')
                            : item['expired'] == true
                            ? 'انتهت مدة الاحتفاظ؛ النسخة محجوبة عن الجميع'
                            : item['withdrawn'] == true
                            ? 'مسحوبة من الأسر والمدرب'
                            : 'محفوظة — الوصول مشروط بالتفويض والموافقة',
                        item['erased'] == true
                            ? (item['purged'] == true
                                  ? 'File removed; publication and erasure history retained'
                                  : 'Hidden from everyone; file removal pending and storage not released')
                            : item['expired'] == true
                            ? 'Retention expired; hidden from everyone'
                            : item['withdrawn'] == true
                            ? 'Withdrawn from families and coach'
                            : 'Saved — access requires current authorization and consent',
                      ),
                    ),
                    if (item['withdrawalNote'] != null)
                      Text(textOf(item['withdrawalNote'])),
                    if (item['playerId'] != null ||
                        item['trainingGroupId'] != null)
                      TextButton(
                        onPressed: () => openPage(
                          context,
                          MediaMemoriesPage(
                            auth: widget.auth,
                            playerId: item['playerId'] as String?,
                            groupId: item['trainingGroupId'] as String?,
                          ),
                        ),
                        child: Text(
                          localized(
                            context,
                            'ذكريات الشهر لهذا النطاق',
                            'Monthly memories for this scope',
                          ),
                        ),
                      ),
                    if (item['canWithdraw'] == true &&
                        item['withdrawn'] != true &&
                        item['erased'] != true &&
                        item['expired'] != true)
                      _Withdrawal(
                        auth: widget.auth,
                        id: textOf(item['id']),
                        onSaved: () => setState(() => revision++),
                      ),
                    if (item['canDelete'] == true && item['erased'] != true)
                      _Withdrawal(
                        auth: widget.auth,
                        id: textOf(item['id']),
                        erase: true,
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
  final bool erase;
  final AuthController auth;
  final String id;
  final VoidCallback onSaved;
  const _Withdrawal({
    this.erase = false,
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
      final result = await widget.auth.request(
        'POST',
        '/api/v1/assets/${widget.id}/${widget.erase ? 'delete' : 'withdraw'}',
        body: {'requestId': intent, 'note': text.isEmpty ? null : text},
      );
      if (widget.erase && (result is! Map || result['erased'] != true)) {
        throw StateError('Unconfirmed erasure');
      }
      if (mounted) widget.onSaved();
    } catch (_) {
      if (mounted) {
        setState(
          () => error = localized(
            context,
            widget.erase
                ? 'لم نؤكد الحذف. حدّث المعرض قبل إعادة المحاولة بنفس الطلب.'
                : 'لم نؤكد حفظ السحب. أعد فتح المعرض أو أعد المحاولة بنفس الطلب.',
            widget.erase
                ? 'Could not confirm deletion. Refresh or retry the same request.'
                : 'Could not confirm withdrawal. Reopen or retry the same request.',
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
          label: Text(
            localized(
              context,
              widget.erase ? 'حذف ملف الوسائط' : 'سحب النشر',
              widget.erase ? 'Delete media file' : 'Withdraw publication',
            ),
          ),
        )
      : Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            Text(
              localized(
                context,
                widget.erase
                    ? 'إجراء نهائي: تُحجب النسخة عن الجميع ويُطلب إزالة ملفها مع حفظ سجل النشر والحذف. لا يمكن استرجاعها من التطبيق أو استرجاع تنزيل سابق.'
                    : 'يتوقف وصول الأسرة والمدرب لهذه النسخة، مع حفظ الأصل للإدارة المخولة.',
                widget.erase
                    ? 'Permanent action: hidden from everyone and file removal requested, with publication and erasure history retained. No app restore or recall of previous downloads.'
                    : 'Families and coach lose access to this copy; the authorized administration retains the original.',
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
                localized(
                  context,
                  widget.erase ? 'تأكيد حذف الملف نهائيًا' : 'تأكيد سحب النشر',
                  widget.erase
                      ? 'Confirm permanent deletion'
                      : 'Confirm withdrawal',
                ),
              ),
            ),
            TextButton(
              onPressed: busy ? null : () => setState(() => open = false),
              child: Text(localized(context, 'رجوع', 'Back')),
            ),
          ],
        );
}
