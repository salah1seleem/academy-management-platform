import 'package:flutter/material.dart';
import 'package:file_picker/file_picker.dart';

import '../../core/auth/auth_controller.dart';
import '../../core/theme/ui_text.dart';

class ReportExportButton extends StatefulWidget {
  const ReportExportButton({super.key, required this.auth, required this.path});
  final AuthController auth;
  final String path;
  @override
  State<ReportExportButton> createState() => _ReportExportButtonState();
}

class _ReportExportButtonState extends State<ReportExportButton> {
  bool busy = false;
  Future<void> save() async {
    if (busy) return;
    setState(() => busy = true);
    try {
      final bytes = await widget.auth.asset(widget.path);
      if (!mounted) return;
      final saved = await FilePicker.saveFile(
        fileName: 'academity-report.csv',
        bytes: bytes,
        mimeType: 'text/csv',
        dialogTitle: 'حفظ التقرير',
      );
      if (mounted && saved != null) {
        ScaffoldMessenger.of(context)
            .showSnackBar(const SnackBar(content: UiText('تم حفظ التقرير.')));
      }
    } catch (_) {
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          const SnackBar(
            content: UiText('تعذر تصدير التقرير؛ راجع الصلاحية وحدود الفترة.'),
          ),
        );
      }
    } finally {
      if (mounted) setState(() => busy = false);
    }
  }

  @override
  Widget build(BuildContext context) => IconButton(
    tooltip: 'تصدير التقرير CSV',
    onPressed: busy ? null : save,
    icon: const Icon(Icons.file_download_outlined),
  );
}
