import 'dart:math' as math;

import 'package:flutter/material.dart';
import 'package:flutter_svg/flutter_svg.dart';

import '../../core/api/api_client.dart';
import '../../core/auth/auth_controller.dart';
import '../../core/theme/app_theme.dart';

typedef Json = Map<String, dynamic>;
Json obj(Object? value) => value as Json;
List<Json> rows(Object? value) => (value as List).cast<Json>();
String textOf(Object? value) => value?.toString() ?? 'غير متاح';
String day(Object? value) =>
    value == null ? 'غير محدد' : value.toString().split('T').first;
String money(Object? value, Object? currency) =>
    '${textOf(value)} ${currency == 'EGP' ? 'ج.م' : textOf(currency)}';
String label(Object? value) =>
    const <String, String>{
      'Active': 'فعال',
      'Expiring': 'ينتهي قريبًا',
      'Expired': 'منتهي',
      'Frozen': 'مجمد',
      'Cancelled': 'ملغي',
      'Scheduled': 'قادم',
      'Pending': 'قيد الانتظار',
      'UnderReview': 'قيد المراجعة',
      'Approved': 'مقبول',
      'Rejected': 'مرفوض',
      'Succeeded': 'تم الدفع',
      'Confirmed': 'مؤكد',
      'Failed': 'فشل الدفع',
      'Present': 'حاضر',
      'Absent': 'غائب',
      'NotRecorded': 'لم يُسجل',
      'Right': 'اليمنى',
      'Left': 'اليسرى',
      'Both': 'كلتاهما',
      'Passing': 'التمرير',
      'Dribbling': 'المراوغة',
      'Speed': 'السرعة',
      'Defending': 'الدفاع',
      'Physical': 'القوة البدنية',
      'Shooting': 'التسديد',
      'Freeze': 'تجميد',
      'Resume': 'استئناف',
      'AddDays': 'إضافة أيام',
      'DeductDays': 'خصم أيام',
      'Cancel': 'إلغاء',
      'Open': 'مفتوح',
      'Monitoring': 'متابعة',
      'Resolved': 'مغلق',
      'Injury': 'إصابة',
      'Consultation': 'استشارة',
      'Sunday': 'الأحد',
      'Monday': 'الاثنين',
      'Tuesday': 'الثلاثاء',
      'Wednesday': 'الأربعاء',
      'Thursday': 'الخميس',
      'Friday': 'الجمعة',
      'Saturday': 'السبت',
      'Small': 'صغيرة',
      'Medium': 'متوسطة',
      'Large': 'كبيرة',
      'Training': 'يوم تدريب',
      'Rest': 'يوم راحة',
    }[value] ??
    'غير محدد';
int? ageOn(Object? birth, Object? today) {
  final b = DateTime.tryParse('$birth'), t = DateTime.tryParse('$today');
  if (b == null || t == null) return null;
  return t.year -
      b.year -
      ((t.month < b.month || (t.month == b.month && t.day < b.day)) ? 1 : 0);
}

String commandKey() => List.generate(
  24,
  (_) => math.Random.secure().nextInt(256).toRadixString(16).padLeft(2, '0'),
).join();
Future<T?> openPage<T>(BuildContext context, Widget page) =>
    Navigator.of(context).push<T>(MaterialPageRoute(builder: (_) => page));

ThemeData guardianTheme() => AcademyTheme.app();

class GuardianPage extends StatelessWidget {
  final String title;
  final Widget child;
  final Widget? bottom;
  final List<Widget>? actions;
  const GuardianPage({
    super.key,
    required this.title,
    required this.child,
    this.bottom,
    this.actions,
  });
  @override
  Widget build(BuildContext context) => Scaffold(
    appBar: AppBar(
      title: Text(
        title,
        style: const TextStyle(fontSize: 18, fontWeight: FontWeight.bold),
      ),
      actions: actions,
    ),
    body: Stack(
      children: [
        const Positioned.fill(child: _AcademyBackdrop()),
        SafeArea(
          child: Center(
            child: ConstrainedBox(
              constraints: const BoxConstraints(maxWidth: 650),
              child: child,
            ),
          ),
        ),
      ],
    ),
    bottomNavigationBar: bottom == null
        ? null
        : SafeArea(
            top: false,
            child: Padding(
              padding: const EdgeInsets.fromLTRB(16, 8, 16, 12),
              child: bottom,
            ),
          ),
  );
}

class _AcademyBackdrop extends StatelessWidget {
  const _AcademyBackdrop();
  @override
  Widget build(BuildContext context) => DecoratedBox(
    decoration: const BoxDecoration(
      gradient: LinearGradient(
        begin: Alignment.topRight,
        end: Alignment.bottomLeft,
        colors: [Color(0xFF111523), AcademyTheme.night, Color(0xFF090C15)],
      ),
    ),
    child: CustomPaint(painter: _StripePainter()),
  );
}

class _StripePainter extends CustomPainter {
  @override
  void paint(Canvas canvas, Size size) {
    final paint = Paint()..color = Colors.white.withValues(alpha: .025);
    for (double y = -size.width; y < size.height + size.width; y += 96) {
      canvas.drawPath(
        Path()
          ..moveTo(0, y)
          ..lineTo(size.width, y - 72)
          ..lineTo(size.width, y - 42)
          ..lineTo(0, y + 30)
          ..close(),
        paint,
      );
    }
  }

  @override
  bool shouldRepaint(covariant CustomPainter oldDelegate) => false;
}

class GCard extends StatelessWidget {
  final Widget child;
  final VoidCallback? onTap;
  const GCard({super.key, required this.child, this.onTap});
  @override
  Widget build(BuildContext context) => Card(
    margin: const EdgeInsets.only(bottom: 14),
    clipBehavior: Clip.antiAlias,
    child: InkWell(
      onTap: onTap,
      child: Padding(padding: const EdgeInsets.all(16), child: child),
    ),
  );
}

class GLink extends StatelessWidget {
  final String title;
  final String? subtitle;
  final IconData icon;
  final VoidCallback onTap;
  const GLink(this.title, this.icon, this.onTap, {super.key, this.subtitle});
  @override
  Widget build(BuildContext context) => Card(
    margin: const EdgeInsets.only(bottom: 12),
    child: ListTile(
      contentPadding: const EdgeInsets.symmetric(horizontal: 16, vertical: 10),
      leading: Icon(icon, color: AcademyTheme.gold),
      title: Text(title),
      subtitle: subtitle == null ? null : Text(subtitle!),
      trailing: const Icon(Icons.chevron_left),
      onTap: onTap,
    ),
  );
}

class Heading extends StatelessWidget {
  final String text;
  const Heading(this.text, {super.key});
  @override
  Widget build(BuildContext context) => Padding(
    padding: const EdgeInsets.symmetric(vertical: 14),
    child: Text(
      text,
      style: const TextStyle(fontSize: 20, fontWeight: FontWeight.bold),
    ),
  );
}

class EmptyMessage extends StatelessWidget {
  final String text;
  const EmptyMessage([this.text = 'لا توجد سجلات متاحة حتى الآن.', Key? key])
    : super(key: key);
  @override
  Widget build(BuildContext context) => Padding(
    padding: const EdgeInsets.all(20),
    child: Text(text, textAlign: TextAlign.center),
  );
}

class StatusPill extends StatelessWidget {
  final Object? status;
  const StatusPill(this.status, {super.key});
  @override
  Widget build(BuildContext context) => Container(
    padding: const EdgeInsets.symmetric(horizontal: 10, vertical: 5),
    decoration: BoxDecoration(
      color: AcademyTheme.gold.withValues(alpha: .12),
      borderRadius: BorderRadius.circular(18),
    ),
    child: Text(
      label(status),
      style: const TextStyle(color: AcademyTheme.gold, fontSize: 12),
    ),
  );
}

class PlayerAvatar extends StatelessWidget {
  final String name;
  final double size;
  const PlayerAvatar(this.name, {super.key, this.size = 64});
  @override
  Widget build(BuildContext context) => Semantics(
    label: 'صورة رمزية للاعب',
    child: Container(
      width: size,
      height: size,
      decoration: BoxDecoration(
        shape: BoxShape.circle,
        border: Border.all(color: AcademyTheme.gold, width: 3),
        color: const Color(0xFF37374A),
      ),
      child: Center(
        child: Text(
          name.isEmpty ? '⚽' : name.characters.first,
          style: TextStyle(
            fontSize: size * .4,
            color: AcademyTheme.gold,
            fontWeight: FontWeight.bold,
          ),
        ),
      ),
    ),
  );
}

// Only packaged, non-sensitive Demo illustrations. No arbitrary host requests or bearer leakage.
class DemoImage extends StatelessWidget {
  final Object? reference;
  final double height;
  const DemoImage(this.reference, {super.key, this.height = 150});
  static const allowed = {
    'catalog-football.svg',
    'catalog-swimming.svg',
    'nutrition-meal.svg',
    'gallery-medal.svg',
    'gallery-training.svg',
  };
  @override
  Widget build(BuildContext context) {
    final path = reference?.toString() ?? '';
    final name = path.split('/').last;
    final nutritionPhoto = RegExp(
      r'^/demo-assets/nutrition/(breakfast|lunch|dinner)-[1-6]\.jpg$',
    ).hasMatch(path);
    return ClipRRect(
      borderRadius: BorderRadius.circular(14),
      child: SizedBox(
        height: height,
        width: double.infinity,
        child: nutritionPhoto
            ? Image.asset(
                'assets/demo/nutrition/$name',
                fit: BoxFit.cover,
                semanticLabel: 'صورة وجبة تجريبية',
              )
            : path == '/demo-assets/$name' && allowed.contains(name)
            ? SvgPicture.asset(
                'assets/demo/$name',
                fit: BoxFit.cover,
                semanticsLabel: 'رسم توضيحي تجريبي',
              )
            : const ColoredBox(
                color: AcademyTheme.card,
                child: Center(
                  child: Icon(Icons.image_not_supported_outlined, size: 40),
                ),
              ),
      ),
    );
  }
}

class RemoteBody extends StatefulWidget {
  final AuthController auth;
  final String path;
  final Widget Function(BuildContext, Object?) builder;
  const RemoteBody({
    super.key,
    required this.auth,
    required this.path,
    required this.builder,
  });
  @override
  State<RemoteBody> createState() => _RemoteBodyState();
}

class _RemoteBodyState extends State<RemoteBody> {
  late Future<Object?> future;
  @override
  void initState() {
    super.initState();
    future = load();
  }

  Future<Object?> load() => widget.auth.request('GET', widget.path);
  @override
  void didUpdateWidget(RemoteBody old) {
    super.didUpdateWidget(old);
    if (old.path != widget.path) future = load();
  }

  @override
  Widget build(BuildContext context) => FutureBuilder<Object?>(
    future: future,
    builder: (context, snapshot) {
      if (snapshot.connectionState != ConnectionState.done) {
        return const Center(
          child: CircularProgressIndicator(semanticsLabel: 'جارٍ التحميل'),
        );
      }
      if (snapshot.hasError) {
        return Center(
          child: Padding(
            padding: const EdgeInsets.all(24),
            child: Column(
              mainAxisSize: MainAxisSize.min,
              children: [
                Text(
                  snapshot.error is ApiFailure
                      ? (snapshot.error as ApiFailure).message
                      : 'تعذر عرض البيانات.',
                ),
                TextButton(
                  onPressed: () => setState(() {
                    future = load();
                  }),
                  child: const Text('إعادة المحاولة'),
                ),
              ],
            ),
          ),
        );
      }
      return RefreshIndicator(
        onRefresh: () async {
          setState(() {
            future = load();
          });
          try {
            await future;
          } catch (_) {
            /* Rendered above. */
          }
        },
        child: widget.builder(context, snapshot.data),
      );
    },
  );
}

Widget gList(List<Widget> children) => ListView(
  padding: const EdgeInsets.all(18),
  physics: const AlwaysScrollableScrollPhysics(),
  children: children,
);

class Facts extends StatelessWidget {
  final List<(String, String)> values;
  const Facts(this.values, {super.key});
  @override
  Widget build(BuildContext context) => LayoutBuilder(
    builder: (context, c) => Wrap(
      spacing: 12,
      runSpacing: 12,
      children: values
          .map(
            (v) => SizedBox(
              width: (c.maxWidth - 12) / 2,
              child: GCard(
                child: Column(
                  children: [
                    Text(
                      v.$2,
                      textAlign: TextAlign.center,
                      style: const TextStyle(
                        fontSize: 25,
                        fontWeight: FontWeight.bold,
                        color: AcademyTheme.gold,
                      ),
                    ),
                    Text(
                      v.$1,
                      textAlign: TextAlign.center,
                      style: const TextStyle(
                        color: Colors.white70,
                        fontSize: 12,
                      ),
                    ),
                  ],
                ),
              ),
            ),
          )
          .toList(),
    ),
  );
}
