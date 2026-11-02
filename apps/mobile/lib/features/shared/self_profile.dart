import 'package:flutter/material.dart';

import '../../core/auth/auth_controller.dart';
import '../../core/theme/academy_brand.dart';
import '../../core/theme/appearance.dart';
import '../../core/theme/ui_text.dart';
import '../guardian/guardian_ui.dart';
import 'private_media.dart';

String personGreetingName(String name) =>
    name.trim().split(RegExp(r'\s+')).take(2).join(' ');

class SelfProfileAvatar extends StatelessWidget {
  const SelfProfileAvatar({
    super.key,
    required this.auth,
    this.size = 38,
    this.square = false,
  });
  final AuthController auth;
  final double size;
  final bool square;
  @override
  Widget build(BuildContext context) {
    final brand = BrandScope.maybeOf(context);
    return PlayerAvatar(
      brand?.profileName ?? auth.session?.displayName ?? '',
      key: ValueKey(
        'self-avatar-${auth.session?.selected?.id}-${brand?.profilePhoto}',
      ),
      auth: auth,
      reference: brand?.profilePhoto,
      adult: true,
      gender: brand?.profileGender,
      size: size,
      square: square,
      border: false,
      avatarRole: auth.session?.selected?.role == 'Coach'
          ? 'coach'
          : auth.session?.selected?.role == 'Guardian'
          ? 'guardian'
          : 'owner',
      identity: auth.session?.userId,
    );
  }
}

class HeaderAppearanceButton extends StatelessWidget {
  const HeaderAppearanceButton({super.key});
  @override
  Widget build(BuildContext context) {
    final prefs = AppearanceScope.of(context);
    return IconButton(
      key: const ValueKey('header-appearance-toggle'),
      tooltip: 'مظهر التطبيق',
      onPressed: prefs == null
          ? null
          : () => showModalBottomSheet<void>(
              context: context,
              showDragHandle: true,
              builder: (sheet) => SafeArea(
                child: Padding(
                  padding: const EdgeInsets.fromLTRB(20, 8, 20, 24),
                  child: Column(
                    mainAxisSize: MainAxisSize.min,
                    children: [
                      const Text(
                        'اختر مظهر التطبيق',
                        style: TextStyle(
                          fontSize: 20,
                          fontWeight: FontWeight.w700,
                        ),
                      ),
                      const SizedBox(height: 16),
                      for (final model in VisualModel.values)
                        ListTile(
                          leading: Icon(model.icon),
                          title: Text(model.label),
                          trailing: prefs.model == model
                              ? const Icon(Icons.check_circle_outline)
                              : null,
                          onTap: () {
                            prefs.update(visualModel: model);
                            Navigator.pop(sheet);
                          },
                        ),
                    ],
                  ),
                ),
              ),
            ),
      icon: Icon(
        prefs?.model.icon ?? Icons.palette_outlined,
        color: Theme.of(context).colorScheme.onSurface,
        size: 21,
      ),
    );
  }
}

class SelfProfileCard extends StatefulWidget {
  const SelfProfileCard({super.key, required this.auth});
  final AuthController auth;
  @override
  State<SelfProfileCard> createState() => _SelfProfileCardState();
}

class _SelfProfileCardState extends State<SelfProfileCard> {
  bool busy = false;
  String? error;
  Future<void> upload() async {
    final brand = BrandScope.maybeOf(context);
    setState(() {
      busy = true;
      error = null;
    });
    try {
      final result = await pickAndUpload(widget.auth, {
        'purpose': 'SelfPhoto',
      }, imageOnly: true);
      if (result != null) {
        await brand?.loadProfile();
      }
    } catch (_) {
      if (mounted) {
        setState(() => error = 'تعذر حفظ الصورة الشخصية. أعد المحاولة.');
      }
    } finally {
      if (mounted) setState(() => busy = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    final brand = BrandScope.maybeOf(context), session = widget.auth.session!;
    return GCard(
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Row(
            children: [
              SelfProfileAvatar(auth: widget.auth, size: 76),
              const SizedBox(width: 14),
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    UiText(
                      'أهلًا، ${personGreetingName(brand?.profileName ?? session.displayName)} 👋',
                      style: const TextStyle(
                        fontSize: 18,
                        fontWeight: FontWeight.w800,
                      ),
                    ),
                    const SizedBox(height: 4),
                    UiText(
                      session.selected?.label ?? 'حسابي',
                      style: TextStyle(
                        color: Theme.of(context).colorScheme.onSurfaceVariant,
                      ),
                    ),
                  ],
                ),
              ),
            ],
          ),
          const SizedBox(height: 10),
          DropdownButtonFormField<String>(
            key: ValueKey('profile-avatar-choice-${brand?.profileGender}'),
            initialValue: ['Male', 'Female'].contains(brand?.profileGender)
                ? brand!.profileGender.toString()
                : '',
            decoration: const InputDecoration(
              labelText: 'الصورة البديلة عند غياب الصورة الشخصية',
            ),
            items: const [
              DropdownMenuItem(value: '', child: UiText('صورة محايدة')),
              DropdownMenuItem(value: 'Male', child: UiText('صورة رجل')),
              DropdownMenuItem(value: 'Female', child: UiText('صورة امرأة')),
            ],
            onChanged: busy
                ? null
                : (value) async {
                    setState(() {
                      busy = true;
                      error = null;
                    });
                    try {
                      await widget.auth.request(
                        'PUT',
                        '/api/v1/me/profile',
                        body: {'gender': value},
                      );
                      await brand?.loadProfile();
                    } catch (_) {
                      if (mounted) {
                        setState(
                          () => error = 'تعذر حفظ اختيار الصورة. حاول مجددًا.',
                        );
                      }
                    } finally {
                      if (mounted) setState(() => busy = false);
                    }
                  },
          ),
          const SizedBox(height: 8),
          OutlinedButton.icon(
            onPressed: busy ? null : upload,
            icon: const Icon(Icons.add_a_photo_outlined, size: 18),
            label: UiText(busy ? 'جارٍ حفظ الصورة…' : 'تغيير صورتي الشخصية'),
          ),
          if (error != null)
            UiText(
              error!,
              style: TextStyle(color: Theme.of(context).colorScheme.error),
            ),
        ],
      ),
    );
  }
}
