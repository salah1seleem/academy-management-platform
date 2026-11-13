import 'package:flutter/material.dart';

import '../../core/auth/auth_controller.dart';
import '../../core/theme/ui_text.dart';
import '../guardian/guardian_ui.dart';
import 'private_media.dart';

class PaymentProofImage extends StatefulWidget {
  const PaymentProofImage({
    super.key,
    required this.auth,
    required this.path,
    this.onAvailabilityChanged,
  });
  final AuthController auth;
  final String path;
  final ValueChanged<bool>? onAvailabilityChanged;

  @override
  State<PaymentProofImage> createState() => _PaymentProofImageState();
}

class _PaymentProofImageState extends State<PaymentProofImage> {
  final transformation = TransformationController();
  final preview = GlobalKey();
  int attempt = 0;
  bool? available;
  bool zoomed = false;

  @override
  void dispose() {
    transformation.dispose();
    super.dispose();
  }

  void toggleZoom() {
    setState(() => zoomed = !zoomed);
    final size = preview.currentContext?.size;
    transformation.value = zoomed && size != null
        ? (Matrix4.diagonal3Values(2, 2, 1)
            ..setTranslationRaw(-size.width / 2, -size.height / 2, 0))
        : Matrix4.identity();
  }

  void retry() => setState(() {
    available = null;
    attempt++;
  });

  @override
  Widget build(BuildContext context) {
    final source = attempt;
    return GCard(
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          const Heading('إثبات الدفع'),
          if (available == true)
            Align(
              alignment: AlignmentDirectional.centerStart,
              child: TextButton.icon(
                onPressed: toggleZoom,
                icon: Icon(zoomed ? Icons.zoom_out : Icons.zoom_in),
                label: UiText(zoomed ? 'الحجم الأصلي' : 'تكبير'),
              ),
            ),
          InteractiveViewer(
            key: preview,
            transformationController: transformation,
            panEnabled: zoomed,
            scaleEnabled: zoomed,
            child: PrivateImage(
              key: ValueKey(source),
              auth: widget.auth,
              path: widget.path,
              height: available == false ? 0 : 260,
              fit: BoxFit.contain,
              onAvailabilityChanged: (value) {
                if (!mounted || attempt != source) return;
                if (!value) transformation.value = Matrix4.identity();
                setState(() {
                  available = value;
                  if (!value) zoomed = false;
                });
                widget.onAvailabilityChanged?.call(value);
              },
              fallback: const SizedBox.shrink(),
            ),
          ),
          if (available == false) ...[
            const SizedBox(height: 12),
            const UiText('تعذر فتح صورة الإثبات.', textAlign: TextAlign.center),
            TextButton(
              onPressed: retry,
              child: const UiText('إعادة تحميل الصورة'),
            ),
          ],
        ],
      ),
    );
  }
}
