import 'package:flutter/material.dart';
import 'package:flutter_svg/flutter_svg.dart';
import 'package:url_launcher/url_launcher.dart';

import '../../core/auth/auth_controller.dart';
import '../../core/api/api_client.dart';
import '../../core/theme/ui_text.dart';
import '../guardian/guardian_ui.dart';

/// A permitted linked contact, freshly read from the server. Opens a draft;
/// never sends a message or writes to the device address book.
class GuardianContactAction extends StatefulWidget {
  const GuardianContactAction({
    super.key,
    required this.auth,
    this.playerId,
    this.guardianId,
    this.openUrl,
  }) : assert((playerId == null) != (guardianId == null));
  final AuthController auth;
  final String? playerId, guardianId;
  final Future<bool> Function(Uri)? openUrl;
  @override
  State<GuardianContactAction> createState() => _GuardianContactActionState();
}

Uri? guardianChatUri(Object? value) {
  final uri = value is String ? Uri.tryParse(value) : null;
  return uri != null &&
          uri.scheme == 'https' &&
          uri.host == 'wa.me' &&
          uri.userInfo.isEmpty &&
          !uri.hasPort &&
          RegExp(r'^/201\d{9}$').hasMatch(uri.path)
      ? uri
      : null;
}

class _GuardianContactActionState extends State<GuardianContactAction> {
  bool busy = false;
  String? get scope {
    final s = widget.auth.session?.selected;
    return s == null ? null : '${s.academyId}:${s.id}:${s.contextRevision}';
  }

  Future<void> open() async {
    if (busy || scope == null) return;
    final expected = scope;
    setState(() => busy = true);
    void notice(String message) {
      if (mounted && expected == scope) {
        ScaffoldMessenger.of(context)
            .showSnackBar(SnackBar(content: UiText(message)));
      }
    }

    try {
      final path = widget.playerId != null
          ? '/api/v1/people/players/${widget.playerId}/guardian-contacts'
          : '/api/v1/people/guardians/${widget.guardianId}/contact';
      final data = obj(await widget.auth.request('GET', path));
      if (!mounted || expected != scope) return;
      final contacts = rows(data['contacts'] ?? [])
          .where((c) => guardianChatUri(c['url']) != null)
          .toList();
      if (contacts.isEmpty) {
        notice(
          widget.auth.session?.selected?.role == 'Coach'
              ? 'لا يوجد رقم صحيح لولي أمر مرتبط. اطلب من الإدارة استكمال بيانات التواصل.'
              : 'لا يوجد رقم صحيح لولي أمر مرتبط. راجع بيانات التواصل من بطاقة ولي الأمر في الملف.',
        );
        return;
      }
      Json? chosen;
      if (contacts.length == 1) {
        chosen = contacts.single;
      } else {
        chosen = await showModalBottomSheet<Json>(
          context: context,
          isScrollControlled: true,
          builder: (sheet) => ListenableBuilder(
            listenable: widget.auth,
            builder: (context, _) => expected != scope
                ? const SizedBox.shrink()
                : SafeArea(
                    child: ConstrainedBox(
                      constraints: BoxConstraints(
                        maxHeight: MediaQuery.sizeOf(context).height * .75,
                      ),
                      child: ListView(
                        shrinkWrap: true,
                        padding: const EdgeInsets.all(20),
                        children: [
                          const UiText(
                            'اختر ولي الأمر للتواصل',
                            style: TextStyle(
                              fontSize: 18,
                              fontWeight: FontWeight.w700,
                            ),
                          ),
                          for (final c in contacts)
                            ListTile(
                              title: UiText(textOf(c['name'])),
                              subtitle: Column(
                                crossAxisAlignment: CrossAxisAlignment.start,
                                children: [
                                  UiText(
                                    textOf(c['relationship'] ?? 'ولي الأمر'),
                                  ),
                                  Directionality(
                                    textDirection: TextDirection.ltr,
                                    child: UiText(textOf(c['phone'])),
                                  ),
                                ],
                              ),
                              trailing: const Icon(Icons.chevron_left),
                              onTap: () => Navigator.pop(sheet, c),
                            ),
                          TextButton(
                            onPressed: () => Navigator.pop(sheet),
                            child: const UiText('إلغاء'),
                          ),
                        ],
                      ),
                    ),
                  ),
          ),
        );
      }
      if (chosen == null || !mounted || expected != scope) return;
      final uri = guardianChatUri(chosen['url'])!;
      final opened =
          await (widget.openUrl?.call(uri) ??
              launchUrl(uri, mode: LaunchMode.externalApplication));
      if (!opened) {
        notice('تعذر فتح واتساب أو المتصفح. تحقق من توفرهما ثم حاول مجددًا.');
      }
    } on ApiFailure catch (e) {
      notice(e.message);
    } catch (_) {
      notice('تعذر فتح التواصل الآن. تحقق من الاتصال وحاول مجددًا.');
    } finally {
      if (mounted) setState(() => busy = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    if (widget.auth.session?.selected?.role == 'Guardian') {
      return const SizedBox.shrink();
    }
    final green = Theme.of(context).brightness == Brightness.dark
        ? const Color(0xFF7DE3AA)
        : const Color(0xFF146C43);
    return Padding(
      padding: const EdgeInsets.symmetric(vertical: 8),
      child: OutlinedButton.icon(
        style: OutlinedButton.styleFrom(
          foregroundColor: green,
          side: const BorderSide(color: Color(0xFF219B61)),
          padding: const EdgeInsets.symmetric(vertical: 13, horizontal: 14),
        ),
        onPressed: busy ? null : open,
        icon: SvgPicture.asset(
          'assets/branding/whatsapp.svg',
          width: 21,
          height: 21,
          colorFilter: const ColorFilter.mode(
            Color(0xFF146C43),
            BlendMode.srcIn,
          ),
          excludeFromSemantics: true,
        ),
        label: UiText(
          busy
              ? 'جارٍ التحقق من جهة التواصل…'
              : widget.playerId != null
              ? 'التواصل مع ولي الأمر عبر واتساب'
              : 'التواصل عبر واتساب',
        ),
      ),
    );
  }
}
