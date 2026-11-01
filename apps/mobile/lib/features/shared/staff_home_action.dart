import 'package:flutter/material.dart';

import '../../core/auth/auth_controller.dart';
import '../../core/theme/ui_text.dart';
import '../guardian/guardian_ui.dart';
import 'native_staff_attendance.dart';

class StaffHomeAttendanceAction extends StatefulWidget {
  const StaffHomeAttendanceAction({super.key, required this.auth});
  final AuthController auth;
  @override
  State<StaffHomeAttendanceAction> createState() =>
      _StaffHomeAttendanceActionState();
}

class _StaffHomeAttendanceActionState extends State<StaffHomeAttendanceAction> {
  String? get identity => widget.auth.session?.selected == null
      ? null
      : '${widget.auth.session!.selected!.id}:${widget.auth.session!.selected!.contextRevision}';
  late final opened = identity;
  late Future<Object?> data = widget.auth.request(
    'GET',
    NativeStaffAttendanceFlow.root,
  );
  @override
  Widget build(BuildContext context) => ListenableBuilder(
    listenable: widget.auth,
    builder: (context, _) {
      if (opened == null || opened != identity) return const SizedBox.shrink();
      return FutureBuilder(
        future: data,
        builder: (context, s) {
          if (s.data is! Map<String, dynamic>) return const SizedBox.shrink();
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
              if (mounted && identity == opened) {
                setState(() {
                  data = widget.auth.request(
                    'GET',
                    NativeStaffAttendanceFlow.root,
                  );
                });
              }
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
