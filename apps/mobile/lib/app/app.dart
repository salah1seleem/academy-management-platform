import 'package:flutter/material.dart';
import 'package:flutter_localizations/flutter_localizations.dart';

import '../core/api/api_client.dart';
import '../core/auth/auth_controller.dart';
import '../core/auth/session.dart';
import '../core/theme/app_theme.dart';
import '../core/widgets/app_widgets.dart';
import '../features/auth/login_page.dart';

class AcademyApp extends StatelessWidget {
  final AuthController auth;
  final String environment;
  const AcademyApp({super.key, required this.auth, required this.environment});
  @override
  Widget build(BuildContext context) => MaterialApp(
    title: 'أكاديميتي',
    debugShowCheckedModeBanner: false,
    locale: const Locale('ar'),
    supportedLocales: const [Locale('ar')],
    localizationsDelegates: GlobalMaterialLocalizations.delegates,
    theme: AcademyTheme.light(),
    home: ListenableBuilder(
      listenable: auth,
      builder: (context, _) {
        if (auth.restoring) {
          return const Scaffold(
            body: Center(
              child: CircularProgressIndicator(
                semanticsLabel: 'استعادة الجلسة',
              ),
            ),
          );
        }
        if (auth.session == null) {
          if (auth.restoreFailed) {
            return AppScaffold(
              title: 'استعادة الجلسة',
              child: Padding(
                padding: const EdgeInsets.all(24),
                child: Column(
                  mainAxisAlignment: MainAxisAlignment.center,
                  children: [
                    ErrorNotice(auth.error ?? 'تعذر استعادة الجلسة.'),
                    PrimaryButton(
                      label: 'إعادة المحاولة',
                      onPressed: auth.restore,
                    ),
                  ],
                ),
              ),
            );
          }
          return LoginPage(auth: auth, environment: environment);
        }
        return RoleFoundation(
          key: ValueKey('${auth.session!.membershipId}'),
          auth: auth,
        );
      },
    ),
  );
}

// Real authenticated role/context shell. Business screens are added in D/F/G, not simulated here.
class RoleFoundation extends StatefulWidget {
  final AuthController auth;
  const RoleFoundation({super.key, required this.auth});
  @override
  State<RoleFoundation> createState() => _RoleFoundationState();
}

class _RoleFoundationState extends State<RoleFoundation> {
  String? error;
  Future<void> select(Membership membership) async {
    try {
      await widget.auth.selectRole(membership.id);
    } on ApiFailure catch (failure) {
      if (mounted) setState(() => error = failure.message);
    } catch (_) {
      if (mounted) {
        setState(() => error = 'تعذر تبديل الدور بأمان. حاول مجددًا.');
      }
    }
  }

  Future<void> logout() async {
    try {
      await widget.auth.logout();
    } catch (_) {
      if (mounted) {
        setState(
          () => error = 'تعذر مسح التخزين الآمن. افتح قفل الجهاز وحاول مجددًا.',
        );
      }
    }
  }

  @override
  Widget build(BuildContext context) {
    final session = widget.auth.session!;
    final membership = session.selected;
    return AppScaffold(
      title: membership?.label ?? 'اختر الأكاديمية والدور',
      child: ListView(
        padding: const EdgeInsets.all(20),
        children: [
          AppCard(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(
                  'أهلًا، ${session.displayName}',
                  style: Theme.of(context).textTheme.headlineSmall,
                ),
                if (membership != null) ...[
                  Text(membership.academyName),
                  Text('الدور الحالي: ${membership.label}'),
                  const SizedBox(height: 12),
                  const Text(
                    'تم ربط الجلسة بأمان. هذا checkpoint لأساس التطبيق؛ شاشات الأعمال تُستكمل في المراحل التالية.',
                  ),
                ] else
                  const Text(
                    'اختر عضوية واحدة صراحةً. لا يتم جمع صلاحيات الأدوار.',
                  ),
              ],
            ),
          ),
          if (error != null) ErrorNotice(error!),
          if (session.memberships.length > 1 || membership == null) ...[
            const SizedBox(height: 16),
            const Text('الأكاديميات والأدوار المتاحة'),
            for (final item in session.memberships)
              Card(
                child: ListTile(
                  title: Text(item.academyName),
                  subtitle: Text(item.label),
                  selected: item.id == session.membershipId,
                  trailing: const Icon(Icons.chevron_left),
                  onTap: widget.auth.busy ? null : () => select(item),
                ),
              ),
          ],
          const SizedBox(height: 20),
          OutlinedButton.icon(
            onPressed: widget.auth.busy ? null : logout,
            icon: const Icon(Icons.logout),
            label: const Text('تسجيل الخروج'),
          ),
        ],
      ),
    );
  }
}
