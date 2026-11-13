import 'package:flutter/material.dart';

import '../../core/auth/auth_controller.dart';
import '../../core/theme/ui_text.dart';
import '../guardian/guardian_ui.dart';
import 'native_staff_attendance.dart';
import 'staff_attendance_exceptions.dart' show staffContext;

class StaffHomeAttendanceAction extends StatefulWidget {
  const StaffHomeAttendanceAction({super.key, required this.auth});
  final AuthController auth;
  @override
  State<StaffHomeAttendanceAction> createState() =>
      _StaffHomeAttendanceActionState();
}

class _StaffHomeAttendanceActionState extends State<StaffHomeAttendanceAction>
    with WidgetsBindingObserver {
  String? get identity => staffContext(widget.auth);
  late final opened = identity;
  late Future<Object?> data = widget.auth.request(
    'GET',
    NativeStaffAttendanceFlow.root,
  );

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

  void refresh() {
    if (!mounted || opened == null || identity != opened) return;
    setState(() {
      data = widget.auth.request('GET', NativeStaffAttendanceFlow.root);
    });
  }

  @override
  void didChangeAppLifecycleState(AppLifecycleState state) {
    if (state == AppLifecycleState.resumed &&
        (ModalRoute.of(context)?.isCurrent ?? true)) {
      refresh();
    }
  }

  @override
  Widget build(BuildContext context) => ListenableBuilder(
    listenable: widget.auth,
    builder: (context, _) {
      if (opened == null || opened != identity) return const SizedBox.shrink();
      return FutureBuilder(
        future: data,
        builder: (context, s) {
          if (s.connectionState != ConnectionState.done ||
              s.data is! Map<String, dynamic>) {
            return const SizedBox.shrink();
          }
          final d = obj(s.data);
          if (!rows(d['branches'] ?? <Json>[])
                  .any((b) => b['enabled'] == true) &&
              d['hasOpenAttendance'] != true) {
            return const SizedBox.shrink();
          }
          return GCard(
            onTap: () async {
              await openPage(
                context,
                NativeStaffAttendancePage(auth: widget.auth),
              );
              refresh();
            },
            child: Row(
              children: [
                Icon(
                  Icons.fingerprint,
                  color: Theme.of(context).colorScheme.primary,
                ),
                const SizedBox(width: 12),
                Expanded(
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      const UiText(
                        'حضور وانصراف',
                        style: TextStyle(fontWeight: FontWeight.w700),
                      ),
                      if (d['hasOpenAttendance'] == true)
                        const UiText(
                          'لديك تسجيل حضور مفتوح',
                          style: TextStyle(fontSize: 11),
                        ),
                    ],
                  ),
                ),
                const Icon(Icons.chevron_right, size: 18),
              ],
            ),
          );
        },
      );
    },
  );
}
