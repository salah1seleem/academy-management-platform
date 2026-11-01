import 'package:flutter/material.dart';

import '../../core/theme/ui_text.dart';
import '../../core/widgets/brand_motion.dart';
import '../../core/widgets/platform_splash.dart';

/// A single calm, keyboard-friendly entry flow before selecting an academy.
class ApprovedLoginLayout extends StatelessWidget {
  const ApprovedLoginLayout({
    super.key,
    required this.verifying,
    required this.demo,
    required this.form,
  });
  final bool verifying, demo;
  final Widget form;
  @override
  Widget build(BuildContext context) {
    final c = Theme.of(context).colorScheme;
    final keyboard = MediaQuery.viewInsetsOf(context).bottom > 0;
    final hero = keyboard ? 126.0 : 226.0;
    return Scaffold(
      body: SafeArea(
        child: Center(
          child: ConstrainedBox(
            constraints: const BoxConstraints(maxWidth: 650),
            child: ListView(
              padding: const EdgeInsets.only(bottom: 24),
              keyboardDismissBehavior: ScrollViewKeyboardDismissBehavior.onDrag,
              children: [
                Container(
                  key: const ValueKey('approved-login-hero'),
                  height: hero,
                  padding: EdgeInsets.all(keyboard ? 20 : 32),
                  decoration: const BoxDecoration(
                    gradient: LinearGradient(
                      begin: Alignment.topLeft,
                      end: Alignment.bottomRight,
                      colors: [
                        Color(0xFF071B32),
                        platformNavy,
                        Color(0xFF0E3B52),
                      ],
                    ),
                    borderRadius: BorderRadius.vertical(
                      bottom: Radius.circular(32),
                    ),
                  ),
                  child: Center(
                    child: SizedBox(
                      width: keyboard ? 130 : 190,
                      child: const FittedBox(child: PlatformWordmark()),
                    ),
                  ),
                ),
                Padding(
                  padding: const EdgeInsets.fromLTRB(20, 22, 20, 0),
                  child: Container(
                    padding: const EdgeInsets.all(22),
                    decoration: BoxDecoration(
                      color: c.surface,
                      borderRadius: BorderRadius.circular(26),
                      border: Border.all(
                        color: c.outlineVariant.withValues(alpha: .8),
                      ),
                    ),
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.stretch,
                      children: [
                        Row(
                          children: [
                            Container(
                              padding: const EdgeInsets.all(10),
                              decoration: BoxDecoration(
                                color: c.primary.withValues(alpha: .10),
                                borderRadius: BorderRadius.circular(13),
                              ),
                              child: Icon(
                                verifying
                                    ? Icons.lock_outline_rounded
                                    : Icons.waving_hand_outlined,
                                color: c.primary,
                                size: 23,
                              ),
                            ),
                            const SizedBox(width: 12),
                            Expanded(
                              child: UiText(
                                verifying ? 'أدخل رمز التحقق' : 'تسجيل الدخول',
                                style: Theme.of(context)
                                    .textTheme
                                    .headlineSmall,
                              ),
                            ),
                          ],
                        ),
                        const SizedBox(height: 12),
                        UiText(
                          verifying
                              ? 'أدخل رمز التحقق المكوّن من ٦ أرقام.'
                              : 'ادخل برقم هاتفك المسجل لدى الأكاديمية.',
                          style: Theme.of(context).textTheme.bodyMedium!
                              .copyWith(color: c.onSurfaceVariant),
                        ),
                        const SizedBox(height: 22),
                        Row(
                          children: [
                            Expanded(
                              child: Container(
                                height: 3,
                                decoration: BoxDecoration(
                                  color: c.primary,
                                  borderRadius: BorderRadius.circular(4),
                                ),
                              ),
                            ),
                            const SizedBox(width: 6),
                            Expanded(
                              child: Container(
                                height: 3,
                                decoration: BoxDecoration(
                                  color: verifying
                                      ? c.primary
                                      : c.outlineVariant,
                                  borderRadius: BorderRadius.circular(4),
                                ),
                              ),
                            ),
                          ],
                        ),
                        const SizedBox(height: 24),
                        form,
                      ],
                    ),
                  ),
                ),
                Padding(
                  padding: const EdgeInsets.fromLTRB(30, 20, 30, 8),
                  child: Row(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      Icon(
                        Icons.shield_outlined,
                        size: 16,
                        color: c.onSurfaceVariant,
                      ),
                      const SizedBox(width: 7),
                      Expanded(
                        child: UiText(
                          demo
                              ? 'بيئة عرض تجريبي فقط — لا تُرسل رسالة SMS حقيقية.'
                              : 'وصول آمن إلى حسابك وبيانات أكاديميتك.',
                          style: Theme.of(context).textTheme.bodySmall,
                        ),
                      ),
                    ],
                  ),
                ),
              ],
            ),
          ),
        ),
      ),
    );
  }
}
