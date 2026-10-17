import 'package:flutter/material.dart';

import '../../core/auth/auth_controller.dart';
import '../../core/theme/appearance.dart';
import '../../core/theme/academy_brand.dart';

/// Role comes from the authenticated context. Local tutorial state grants no access.
class RoleIntroGate extends StatefulWidget {
  final AuthController auth;
  final Appearance appearance;
  final Widget child;
  const RoleIntroGate({
    super.key,
    required this.auth,
    required this.appearance,
    required this.child,
  });
  @override
  State<RoleIntroGate> createState() => _RoleIntroGateState();
}

class _RoleIntroGateState extends State<RoleIntroGate> {
  String? identity() => widget.auth.session?.selected == null
      ? null
      : '${widget.auth.session!.selected!.academyId}:${widget.auth.session!.selected!.id}:${widget.auth.session!.selected!.contextRevision}';
  late final opened = identity();
  String? introKey;
  bool checking = true, seen = false, saving = false;
  String? error;
  int generation = 0;
  @override
  void didChangeDependencies() {
    super.didChangeDependencies();
    final brand = BrandScope.maybeOf(context);
    final role = widget.auth.session?.selected?.role;
    if (role == null || brand?.loading == true) return;
    final theme = brand?.sportTheme ?? 'general';
    final key = widget.appearance.roleIntroKey(role, theme);
    if (key == introKey) return;
    introKey = key;
    checking = true;
    seen = false;
    error = null;
    check(role, theme, ++generation);
  }

  Future<void> check(String role, String theme, int attempt) async {
    bool value = false;
    String? failure;
    try {
      value = await widget.appearance.hasRoleIntro(role, theme);
    } catch (_) {
      failure = 'تعذر قراءة حالة التعريف؛ يمكنك المتابعة لهذه الجلسة.';
    }
    if (!mounted || attempt != generation || identity() != opened) return;
    setState(() {
      checking = false;
      seen = value;
      error = failure;
    });
  }

  Future<void> complete(String role, String theme) async {
    final attempt = generation;
    setState(() => saving = true);
    try {
      await widget.appearance.completeRoleIntro(role, theme);
      if (mounted && attempt == generation && identity() == opened) {
        setState(() {
          seen = true;
          saving = false;
        });
      }
    } catch (_) {
      if (mounted && attempt == generation && identity() == opened) {
        setState(() {
          saving = false;
          error =
              'لم تُحفظ حالة التعريف. أعد المحاولة أو تابع لهذه الجلسة فقط.';
        });
      }
    }
  }

  @override
  Widget build(BuildContext context) => ListenableBuilder(
    listenable: widget.auth,
    builder: (context, _) {
      if (opened == null || identity() != opened) {
        return const SizedBox.shrink();
      }
      final role = widget.auth.session!.selected!.role;
      if (![
        'Guardian',
        'Coach',
        'AcademyAdmin',
        'AcademyOwner',
      ].contains(role)) {
        return widget.child;
      }
      final brand = BrandScope.maybeOf(context);
      if (seen) return widget.child;
      if (checking || brand?.loading == true) {
        return const Scaffold(body: Center(child: CircularProgressIndicator()));
      }
      final theme = brand?.sportTheme ?? 'general';
      return AcademyOnboarding(
        role: role,
        sportTheme: theme,
        features: brand?.features ?? [],
        busy: saving,
        error: error,
        onComplete: () => complete(role, theme),
        onContinueOnce: error == null
            ? null
            : () => setState(() => seen = true),
      );
    },
  );
}
