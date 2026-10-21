import 'package:flutter/material.dart';
import 'package:url_launcher/url_launcher.dart';

import '../../core/api/api_client.dart';
import '../../core/auth/auth_controller.dart';
import '../../core/theme/appearance.dart';
import '../../core/theme/ui_text.dart';
import '../admin/admin_home.dart';
import '../guardian/guardian_ui.dart';
import '../owner/owner_reports.dart';
import '../shared/role_navigation.dart';
import 'owner_operations.dart';

/// Reuses the existing web write workflows; never transfers a mobile bearer token.
class OwnerQuickOperations extends StatelessWidget {
  final AuthController auth;
  final String environment, webBaseUrl;
  final Future<bool> Function(Uri)? openDashboard;
  const OwnerQuickOperations({
    super.key,
    required this.auth,
    this.environment = 'Production',
    this.webBaseUrl = const String.fromEnvironment('WEB_BASE_URL'),
    this.openDashboard,
  });

  Future<void> web(BuildContext context, String path) async {
    if (auth.session?.selected?.role != 'AcademyOwner') return;
    try {
      final origin = AppConfig(
        environment,
        webBaseUrl.isEmpty && environment == 'Demo'
            ? 'http://127.0.0.1:3260'
            : webBaseUrl,
      ).api;
      final uri = origin.resolve(path);
      final opened =
          await (openDashboard?.call(uri) ??
              launchUrl(uri, mode: LaunchMode.externalApplication));
      if (!opened) throw StateError('not opened');
    } catch (_) {
      if (context.mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          const SnackBar(
            content: UiText(
              'تعذر فتح لوحة الويب. راجع إعداد عنوان لوحة الإدارة أو افتحها من المتصفح.',
            ),
          ),
        );
      }
    }
  }

  @override
  Widget build(BuildContext context) => ListenableBuilder(
    listenable: auth,
    builder: (context, _) {
      if (auth.session?.selected?.role != 'AcademyOwner') {
        return const SizedBox.shrink();
      }
      return GuardianPage(
        title: localized(context, 'العمليات', 'Operations'),
        child: gList([
          const Heading('الإجراءات السريعة'),
          const UiText(
            'التسجيل والتجديد وإضافة المدرب تستخدم نماذج لوحة الويب الحالية. قد يُطلب تسجيل الدخول؛ تحقق من الأكاديمية والدور قبل التأكيد.',
          ),
          for (final action in [
            (
              'اشتراك جديد · تسجيل لاعب',
              Icons.person_add_alt_1_outlined,
              '/dashboard/players/new',
            ),
            (
              'تجديد اشتراك',
              Icons.autorenew_rounded,
              '/dashboard/subscriptions/renew/new',
            ),
            (
              'إضافة مدرب',
              Icons.sports_outlined,
              '/dashboard/academy/coaches/create',
            ),
            ('إنشاء تقييم', Icons.assignment_add, '/dashboard/evaluations/new'),
          ])
            GCard(
              child: ListTile(
                contentPadding: EdgeInsets.zero,
                leading: Icon(action.$2, color: const Color(0xFFEBC85E)),
                title: UiText(action.$1),
                subtitle: const UiText('يفتح لوحة الويب الآمنة'),
                trailing: const Icon(Icons.open_in_new_rounded),
                onTap: () => web(context, action.$3),
              ),
            ),
          GLink(
            'تسجيل حضور',
            Icons.fact_check_outlined,
            () => openPage(context, NativeSessions(auth: auth)),
          ),
          GLink(
            'التقرير المالي',
            Icons.receipt_long_outlined,
            () => openPage(
              context,
              OwnerReports(auth: auth, initialReport: 'financial'),
            ),
          ),
          GLink(
            'طلبات الدفع',
            Icons.payments_outlined,
            () => openPage(context, NativePaymentRequests(auth: auth)),
          ),
          GLink(
            'المتابعات والقرارات',
            Icons.task_alt,
            () => openPage(context, OwnerDecisionsPage(auth: auth)),
          ),
        ]),
      );
    },
  );
}
