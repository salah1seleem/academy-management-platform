import '../shared/role_home_navigation.dart';
import '../shared/operation_design.dart';
import '../../core/theme/academy_brand.dart';
import '../shared/experience_ui.dart';
import '../shared/self_profile.dart';
import '../shared/profile_avatar.dart';
import '../../core/widgets/home_skeleton.dart';
import '../../core/widgets/page_status.dart';

import 'package:academy_mobile/core/theme/ui_text.dart';

import 'dart:math' as math;

import 'package:flutter/material.dart';
import 'package:flutter_svg/flutter_svg.dart';

import '../../core/api/api_client.dart';
import '../../core/auth/auth_controller.dart';
import '../../core/theme/app_theme.dart';
import '../../core/theme/ocean_palette.dart';
import '../shared/private_media.dart';
import '../shared/role_shell_body.dart';

typedef Json = Map<String, dynamic>;
Json obj(Object? value) => value as Json;
List<Json> rows(Object? value) => (value as List).cast<Json>();
String performanceScoreText(num value) =>
    value.toStringAsFixed(1).replaceFirst(RegExp(r'\.0$'), '');
String textOf(Object? value) => value?.toString() ?? 'غير متاح';
String day(Object? value) =>
    value == null ? 'غير محدد' : value.toString().split('T').first;
String trainingTimeOf(Object? value) {
  final match = RegExp(r'^(\d{1,2}):(\d{2})').firstMatch('$value');
  if (match == null) return 'غير محدد';
  final hour = int.parse(match[1]!), minute = int.parse(match[2]!);
  if (hour > 23 || minute > 59) return 'غير محدد';
  return '${hour % 12 == 0 ? 12 : hour % 12}:${match[2]} ${hour < 12 ? 'صباحًا' : 'مساءً'}';
}

String amountText(Object? value) {
  final number = value is num ? value : num.tryParse('$value');
  if (number == null || !number.isFinite) return textOf(value);
  final parts = number.toStringAsFixed(2).split('.');
  final whole = parts.first.replaceAllMapped(
    RegExp(r'(\d)(?=(\d{3})+(?!\d))'),
    (m) => '${m[1]},',
  );
  return parts.last == '00' ? whole : '$whole.${parts.last}';
}

String money(Object? value, Object? currency) =>
    '${amountText(value)} ${currency == 'EGP' ? 'ج.م' : textOf(currency)}';

class PaymentRequestReference extends StatelessWidget {
  const PaymentRequestReference({super.key, required this.order});
  final Json order;

  @override
  Widget build(BuildContext context) {
    final reference = order['reference'];
    final date = DateTime.tryParse('${order['createdOn']}');
    if ((reference is! String || reference.isEmpty) && date == null) {
      return const SizedBox.shrink();
    }
    final theme = Theme.of(context);
    return Padding(
      padding: const EdgeInsets.symmetric(vertical: 4),
      child: Wrap(
        spacing: 12,
        runSpacing: 4,
        children: [
          if (reference is String && reference.isNotEmpty)
            UiText(
              "${ui(context, 'رقم الطلب')} \u2066$reference\u2069",
              style: theme.textTheme.bodySmall,
            ),
          if (date != null)
            UiText(
              MaterialLocalizations.of(context).formatShortDate(date),
              style: theme.textTheme.bodySmall?.copyWith(
                color: theme.colorScheme.onSurfaceVariant,
              ),
            ),
        ],
      ),
    );
  }
}

/// Currency stays secondary without changing the canonical amount or rounding.
class MoneyText extends StatelessWidget {
  const MoneyText(
    this.value,
    this.currency, {
    super.key,
    this.size = 28,
    this.color,
  });
  final Object? value, currency;
  final double size;
  final Color? color;
  @override
  Widget build(BuildContext context) => Text.rich(
    TextSpan(
      children: [
        TextSpan(
          text: amountText(value),
          style: TextStyle(
            fontSize: size,
            fontWeight: FontWeight.w700,
            color: color,
          ),
        ),
        TextSpan(
          text: ' ${currency == 'EGP' ? 'ج.م' : textOf(currency)}',
          style: TextStyle(
            fontSize: size < 24 ? 10 : 12,
            color: color ?? Theme.of(context).colorScheme.onSurfaceVariant,
          ),
        ),
      ],
    ),
    semanticsLabel: money(value, currency),
  );
}

String label(Object? value) =>
    const <String, String>{
      'Draft': 'مسودة',
      'Published': 'منشور لولي الأمر',
      'PublishedInternal': 'للاستخدام الداخلي',
      'Superseded': 'إصدار سابق',
      'Active': 'فعال',
      'Expiring': 'ينتهي قريبًا',
      'Expired': 'منتهي',
      'Frozen': 'مجمد',
      'Cancelled': 'ملغي',
      'Scheduled': 'قادم',
      'Pending': 'قيد الانتظار',
      'PendingProof': 'بانتظار إثبات التحويل',
      'UnderReview': 'قيد المراجعة',
      'Approved': 'مقبول',
      'Rejected': 'مرفوض',
      'Succeeded': 'تم الدفع',
      'Paid': 'تم الدفع',
      'Confirmed': 'مؤكد',
      'Failed': 'فشل الدفع',
      'Present': 'حاضر',
      'Late': 'متأخر',
      'Excused': 'معتذر',
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
      'DaysAdded': 'إضافة أيام',
      'SessionsAdded': 'إضافة حصص',
      'DaysDeducted': 'خصم أيام',
      'FreezeStarted': 'تجميد',
      'FreezeEnded': 'استئناف',
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
    }[value] ??
    'غير محدد';
int? ageOn(Object? birth, Object? today) {
  final b = DateTime.tryParse('$birth'), t = DateTime.tryParse('$today');
  if (b == null || t == null) return null;
  return t.year -
      b.year -
      ((t.month < b.month || (t.month == b.month && t.day < b.day)) ? 1 : 0);
}

String trainingTimeRange(Object? start, Object? end) =>
    'من ${trainingTimeOf(start)} إلى ${trainingTimeOf(end)}';

String sessionDateLabel(Object? value) {
  final d = DateTime.tryParse('$value');
  if (d == null) return 'موعد غير محدد';
  const days = [
    'الاثنين',
    'الثلاثاء',
    'الأربعاء',
    'الخميس',
    'الجمعة',
    'السبت',
    'الأحد',
  ];
  const months = [
    'يناير',
    'فبراير',
    'مارس',
    'أبريل',
    'مايو',
    'يونيو',
    'يوليو',
    'أغسطس',
    'سبتمبر',
    'أكتوبر',
    'نوفمبر',
    'ديسمبر',
  ];
  return '${days[d.weekday - 1]} ${d.day} ${months[d.month - 1]} ${d.year}';
}

String commandKey() => List.generate(
  24,
  (_) => math.Random.secure().nextInt(256).toRadixString(16).padLeft(2, '0'),
).join();
Future<T?> openPage<T>(BuildContext context, Widget page) =>
    Navigator.of(context).push<T>(MaterialPageRoute(builder: (_) => page));

ThemeData guardianTheme({
  Color primary = AcademyTheme.red,
  Color secondary = AcademyTheme.night,
}) => AcademyTheme.dark(primary: primary);

class GuardianPage extends StatelessWidget {
  final String title;
  final Widget child;
  final Widget? bottom;
  final VoidCallback? onBack;
  final List<Widget>? actions;
  final bool showAppBar;
  final bool operation;
  const GuardianPage({
    super.key,
    required this.title,
    required this.child,
    this.bottom,
    this.onBack,
    this.actions,
    this.showAppBar = true,
    this.operation = false,
  });
  @override
  Widget build(BuildContext context) {
    final embedded = showAppBar && RoleShellBody.contains(context);
    final content = Center(
      child: ConstrainedBox(
        constraints: const BoxConstraints(maxWidth: 650),
        child: operation
            ? Theme(data: operationTheme(context), child: child)
            : child,
      ),
    );
    final brand = BrandScope.maybeOf(context);
    final role = switch (brand?.auth.session?.selected?.role) {
      'AcademyOwner' => 'المالك',
      'AcademyAdmin' => 'الإداري',
      'Coach' => 'المدرب',
      _ => 'ولي الأمر',
    };
    final titleSize = title.length > 26 ? 18.0 : 23.0;
    final titleMeasure =
        TextPainter(
          text: TextSpan(
            text: ui(context, title),
            style: Theme.of(context).textTheme.titleLarge
                ?.copyWith(fontSize: titleSize, fontWeight: FontWeight.w800),
          ),
          textDirection: Directionality.of(context),
          textScaler: MediaQuery.textScalerOf(context),
          maxLines: 2,
        )..layout(
          maxWidth: math.max(
            80,
            math.min(650, MediaQuery.sizeOf(context).width) -
                96 -
                (actions?.length ?? 0) * 48,
          ),
        );
    final toolbarHeight = math.max(56.0, titleMeasure.height + 20);
    titleMeasure.dispose();
    final navigationBar = AppBar(
      primary: brand?.signedIn != true,
      toolbarHeight: toolbarHeight,
      title: UiText(
        title,
        maxLines: 2,
        overflow: TextOverflow.ellipsis,
        style: TextStyle(fontSize: titleSize, fontWeight: FontWeight.w800),
      ),
      leading: onBack != null && Navigator.canPop(context)
          ? BackButton(onPressed: onBack)
          : brand?.auth.session?.selected?.role == 'AcademyOwner' &&
                Navigator.canPop(context)
          ? BackButton(
              onPressed: () {
                ownerHomeAction(brand!.auth)?.call();
                Navigator.of(context).popUntil((route) => route.isFirst);
              },
            )
          : null,
      actions: actions,
    );
    return Scaffold(
      appBar: !showAppBar || embedded
          ? null
          : brand?.signedIn == true
          ? PreferredSize(
              preferredSize: Size.fromHeight(
                ExperienceHeader.heightFor(context, brand!.auth, role) +
                    navigationBar.preferredSize.height,
              ),
              child: SafeArea(
                bottom: false,
                child: Column(
                  mainAxisSize: MainAxisSize.min,
                  children: [
                    ExperienceHeader(
                      auth: brand.auth,
                      subtitle: role,
                      lesson: '',
                      account: GuardianPage(
                        title: 'حسابي',
                        child: SelfProfileCard(auth: brand.auth),
                      ),
                    ),
                    navigationBar,
                  ],
                ),
              ),
            )
          : navigationBar,
      body: SafeArea(
        child: embedded
            ? Column(
                children: [
                  Align(
                    child: ConstrainedBox(
                      constraints: const BoxConstraints(maxWidth: 650),
                      child: Padding(
                        padding: const EdgeInsets.symmetric(
                          horizontal: 18,
                          vertical: 4,
                        ),
                        child: ConstrainedBox(
                          constraints: const BoxConstraints(minHeight: 48),
                          child: Row(
                            children: [
                              Expanded(
                                child: Semantics(
                                  header: true,
                                  child: UiText(
                                    title,
                                    maxLines: 2,
                                    overflow: TextOverflow.ellipsis,
                                    style: TextStyle(
                                      fontSize: title.length > 26 ? 18 : 23,
                                      fontWeight: FontWeight.w800,
                                    ),
                                  ),
                                ),
                              ),
                              ...?actions,
                            ],
                          ),
                        ),
                      ),
                    ),
                  ),
                  Expanded(child: content),
                ],
              )
            : content,
      ),
      bottomNavigationBar: bottom == null
          ? null
          : PageActionBar(child: bottom!),
    );
  }
}

class GCard extends StatelessWidget {
  final Widget child;
  final VoidCallback? onTap;
  final EdgeInsetsGeometry padding;
  const GCard({
    super.key,
    required this.child,
    this.onTap,
    this.padding = const EdgeInsets.all(18),
  });
  @override
  Widget build(BuildContext context) => Card(
    margin: const EdgeInsets.only(bottom: 12),
    clipBehavior: Clip.antiAlias,
    child: InkWell(
      onTap: onTap,
      child: Padding(padding: padding, child: child),
    ),
  );
}

class GLink extends StatelessWidget {
  final String title;
  final String? subtitle;
  final IconData icon;
  final VoidCallback onTap;
  final bool compact;
  const GLink(
    this.title,
    this.icon,
    this.onTap, {
    super.key,
    this.subtitle,
    this.compact = false,
  });
  @override
  Widget build(BuildContext context) => Card(
    margin: EdgeInsets.only(bottom: compact ? 6 : 12),
    child: ListTile(
      dense: compact,
      minTileHeight: compact ? 52 : 64,
      contentPadding: EdgeInsets.symmetric(
        horizontal: compact ? 12 : 16,
        vertical: compact ? 0 : 5,
      ),
      leading: compact
          ? null
          : Container(
              width: 42,
              height: 42,
              decoration: BoxDecoration(
                color: Theme.of(context).colorScheme.primary
                    .withValues(alpha: .09),
                borderRadius: BorderRadius.circular(13),
              ),
              child: Icon(
                icon,
                size: 21,
                color: Theme.of(context).colorScheme.primary,
              ),
            ),
      title: UiText(
        title,
        style: TextStyle(
          fontSize: compact ? 13 : 15,
          fontWeight: FontWeight.w600,
        ),
      ),
      subtitle: subtitle == null ? null : UiText(subtitle!),
      trailing: Icon(
        Directionality.of(context) == TextDirection.rtl
            ? Icons.chevron_left_rounded
            : Icons.chevron_right_rounded,
        textDirection: TextDirection.ltr,
        size: 24,
        color: Theme.of(context).colorScheme.onSurfaceVariant,
      ),
      onTap: onTap,
    ),
  );
}

class Heading extends StatelessWidget {
  final String text;
  const Heading(this.text, {super.key});
  @override
  Widget build(BuildContext context) => Padding(
    padding: const EdgeInsets.only(top: 20, bottom: 12),
    child: Semantics(
      header: true,
      child: UiText(
        text,
        style: TextStyle(fontSize: 20, fontWeight: FontWeight.bold),
      ),
    ),
  );
}

class EmptyMessage extends StatelessWidget {
  final String text;
  const EmptyMessage([this.text = 'لا توجد سجلات متاحة حتى الآن.', Key? key])
    : super(key: key);
  @override
  Widget build(BuildContext context) => Padding(
    padding: const EdgeInsets.symmetric(horizontal: 20, vertical: 28),
    child: StatusPanel(message: text),
  );
}

class StatusPill extends StatelessWidget {
  final Object? status;
  final double fontSize;
  final String? displayLabel;
  const StatusPill(
    this.status, {
    super.key,
    this.fontSize = 12,
    this.displayLabel,
  });
  @override
  Widget build(BuildContext context) {
    final c = Theme.of(context).colorScheme;
    final value = status?.toString().toLowerCase() ?? '';
    final good = [
      'active',
      'approved',
      'accepted',
      'confirmed',
      'paid',
      'completed',
      'present',
      'published',
      'publishedinternal',
    ].contains(value);
    final waiting = [
      'pending',
      'expiring',
      'draft',
      'review',
      'pendingreview',
      'partial',
    ].contains(value);
    final bad = [
      'expired',
      'rejected',
      'failed',
      'overdue',
      'absent',
      'suspended',
    ].contains(value);
    final dark = Theme.of(context).brightness == Brightness.dark;
    final ocean = OceanPalette.active(Theme.of(context));
    final color = good
        ? (ocean
              ? OceanPalette.positive
              : dark
              ? const Color(0xFF72D6B3)
              : const Color(0xFF18795C))
        : waiting
        ? (ocean
              ? OceanPalette.warning
              : dark
              ? const Color(0xFFF2C76C)
              : const Color(0xFF94600E))
        : bad
        ? c.error
        : c.onSurfaceVariant;
    return Container(
      padding: const EdgeInsets.symmetric(horizontal: 10, vertical: 6),
      decoration: BoxDecoration(
        color: color.withValues(alpha: .10),
        borderRadius: BorderRadius.circular(10),
      ),
      child: Row(
        mainAxisSize: MainAxisSize.min,
        children: [
          Container(
            width: 5,
            height: 5,
            decoration: BoxDecoration(color: color, shape: BoxShape.circle),
          ),
          const SizedBox(width: 6),
          Flexible(
            child: UiText(
              displayLabel ?? label(status),
              style: TextStyle(
                color: color,
                fontSize: fontSize,
                fontWeight: FontWeight.w700,
              ),
            ),
          ),
        ],
      ),
    );
  }
}

class PlayerAvatar extends StatelessWidget {
  final String name;
  final double size;
  final double? height;
  final AlignmentGeometry alignment;
  final AuthController? auth;
  final Object? reference;
  final bool adult;
  final Object? gender;
  final bool square, border;
  final String? avatarRole, identity;
  const PlayerAvatar(
    this.name, {
    super.key,
    this.size = 64,
    this.height,
    this.alignment = Alignment.center,
    this.auth,
    this.reference,
    this.adult = false,
    this.gender,
    this.square = false,
    this.border = false,
    this.avatarRole,
    this.identity,
  });
  @override
  Widget build(BuildContext context) => Semantics(
    label: adult ? 'صورة العضو' : 'صورة اللاعب',
    child: Container(
      width: size,
      height: height ?? size,
      decoration: BoxDecoration(
        shape: square ? BoxShape.rectangle : BoxShape.circle,
        border: square || !border
            ? null
            : Border.all(
                color: Theme.of(context).colorScheme.primary,
                width: 3,
              ),
        color: OceanPalette.active(Theme.of(context))
            ? OceanPalette.raised
            : const Color(0xFF37374A),
      ),
      clipBehavior: Clip.antiAlias,
      child: auth != null && isPrivateAsset(reference)
          ? PrivateImage(
              auth: auth!,
              path: reference.toString(),
              height: height ?? size,
              alignment: alignment,
              fallback: IllustratedAvatar(
                adult: adult,
                gender: gender,
                identity: identity ?? name,
                role: avatarRole,
              ),
            )
          : RegExp(
              r'^/demo-assets/portraits/(?:(?:child|coach|guardian)-0[12]|reference-child-0[123])\.jpg$',
            ).hasMatch('$reference')
          ? Image.asset(
              'assets/demo/portraits/${reference.toString().split('/').last}',
              fit: BoxFit.cover,
              alignment: alignment,
              errorBuilder: (_, _, _) => IllustratedAvatar(
                adult: adult,
                gender: gender,
                identity: identity ?? name,
                role: avatarRole,
              ),
            )
          : IllustratedAvatar(
              adult: adult,
              gender: gender,
              identity: identity ?? name,
              role: avatarRole,
            ),
    ),
  );
}

// Only packaged, non-sensitive Demo illustrations. No arbitrary host requests or bearer leakage.
class DemoImage extends StatelessWidget {
  final Object? reference;
  final double height;
  final BorderRadius borderRadius;
  const DemoImage(
    this.reference, {
    super.key,
    this.height = 150,
    this.borderRadius = const BorderRadius.all(Radius.circular(14)),
  });
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
      borderRadius: borderRadius,
      child: SizedBox(
        height: height,
        width: double.infinity,
        child:
            const {
              '/demo-assets/catalog-iacademy-kit.png',
              '/demo-assets/catalog-iacademy-ball.png',
              '/demo-assets/catalog-iacademy-socks.png',
              '/demo-assets/catalog-iacademy-training.png',
              '/demo-assets/professional-kit-red.jpg',
              '/demo-assets/professional-kit-turquoise.jpg',
              '/demo-assets/professional-kit-black.jpg',
            }.contains(path)
            ? Image.asset(
                'assets/demo/$name',
                fit: BoxFit.contain,
                semanticLabel: 'منتج رياضي I Academy One',
              )
            : nutritionPhoto
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
            : ColoredBox(
                color: OceanPalette.active(Theme.of(context))
                    ? OceanPalette.raised
                    : AcademyTheme.card,
                child: const Center(
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
  final ScrollController? pagingController;
  final bool autoPaginate;
  final Widget? notFound;
  final Widget Function(BuildContext, Object?) builder;
  const RemoteBody({
    super.key,
    required this.auth,
    required this.path,
    required this.builder,
    this.pagingController,
    this.autoPaginate = true,
    this.notFound,
  });
  @override
  State<RemoteBody> createState() => _RemoteBodyState();
}

class _RemoteBodyState extends State<RemoteBody> {
  late Future<Object?> future;
  late String? openedIdentity;
  Object? accumulated;
  bool loadingMore = false, exhausted = false;
  Object? moreError;
  Object? fatalError;
  int generation = 0, cursor = 0, consumed = 0;
  String? get identity => widget.auth.session?.selected == null
      ? null
      : '${widget.auth.session!.selected!.academyId}:${widget.auth.session!.selected!.id}:${widget.auth.session!.selected!.contextRevision}';
  Uri get uri => Uri.parse(widget.path);
  String? get cursorKey => !widget.autoPaginate
      ? null
      : uri.queryParameters.containsKey('page')
      ? 'page'
      : uri.queryParameters.containsKey('offset')
      ? 'offset'
      : null;
  String? collection(Object? value) {
    if (value is! Json ||
        ((value['total'] ?? value['totalCount'] ?? value['count']) is! num &&
            value['hasMore'] is! bool) ||
        cursorKey == null) {
      return null;
    }
    for (final name in ['items', 'players', 'rows', 'entries']) {
      if (value[name] is List) return name;
    }
    return null;
  }

  bool get hasMore {
    if (exhausted || collection(accumulated) == null) return false;
    final data = accumulated as Json;
    return data['hasMore'] is bool
        ? data['hasMore'] == true
        : consumed <
              ((data['total'] ?? data['totalCount'] ?? data['count']) as num);
  }

  @override
  void initState() {
    super.initState();
    openedIdentity = identity;
    widget.auth.addListener(authChanged);
    widget.pagingController?.addListener(controllerChanged);
    future = load();
  }

  void authChanged() {
    if (mounted && openedIdentity != identity) {
      setState(() {
        generation++;
        accumulated = null;
      });
    }
  }

  void controllerChanged() {
    final c = widget.pagingController;
    if (c != null && c.hasClients && c.position.extentAfter < 240) loadMore();
  }

  Future<Object?> load() async {
    final version = ++generation;
    accumulated = null;
    consumed = 0;
    exhausted = false;
    loadingMore = false;
    moreError = null;
    fatalError = null;
    cursor =
        int.tryParse(uri.queryParameters[cursorKey ?? ''] ?? '') ??
        (cursorKey == 'page' ? 1 : 0);
    final value = await widget.auth.request('GET', widget.path);
    if (version == generation && mounted && openedIdentity == identity) {
      accumulated = value;
      final key = collection(value);
      if (key != null) consumed = ((value as Json)[key] as List).length;
    }
    return value;
  }

  Future<void> loadMore() async {
    if (!mounted ||
        openedIdentity != identity ||
        loadingMore ||
        !hasMore ||
        moreError != null) {
      return;
    }
    final version = generation, key = collection(accumulated)!;
    final data = accumulated as Json;
    final size =
        (data['pageSize'] as num?)?.toInt() ??
        int.tryParse(uri.queryParameters['pageSize'] ?? '') ??
        (data[key] as List).length;
    final next = cursorKey == 'page' ? cursor + 1 : cursor + size;
    setState(() => loadingMore = true);
    try {
      final value = await widget.auth.request(
        'GET',
        uri
            .replace(
              queryParameters: {...uri.queryParameters, cursorKey!: '$next'},
            )
            .toString(),
      );
      if (!mounted || version != generation || openedIdentity != identity) {
        return;
      }
      final result = obj(value), added = (result[key] as List);
      final known = {
        for (final row in data[key] as List)
          if (row is Map && row['id'] != null) row['id'],
      };
      final unique = added
          .where(
            (row) => row is! Map || row['id'] == null || known.add(row['id']),
          )
          .toList();
      setState(() {
        accumulated = {
          ...result,
          key: [...data[key] as List, ...unique],
        };
        consumed += added.length;
        cursor = next;
        exhausted = unique.isEmpty;
      });
    } catch (error) {
      if (mounted && version == generation && openedIdentity == identity) {
        setState(() {
          if (error is ApiFailure && [401, 403, 404].contains(error.status)) {
            accumulated = null;
            fatalError = error;
            exhausted = true;
          } else {
            moreError = error;
          }
        });
      }
    } finally {
      if (mounted && version == generation && openedIdentity == identity) {
        setState(() => loadingMore = false);
      }
    }
  }

  @override
  void didUpdateWidget(RemoteBody old) {
    super.didUpdateWidget(old);
    if (old.auth != widget.auth) {
      old.auth.removeListener(authChanged);
      widget.auth.addListener(authChanged);
      openedIdentity = identity;
    }
    if (old.pagingController != widget.pagingController) {
      old.pagingController?.removeListener(controllerChanged);
      widget.pagingController?.addListener(controllerChanged);
    }
    if (old.path != widget.path ||
        old.auth != widget.auth ||
        old.autoPaginate != widget.autoPaginate) {
      future = load();
    }
  }

  @override
  void dispose() {
    widget.auth.removeListener(authChanged);
    widget.pagingController?.removeListener(controllerChanged);
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    if (openedIdentity != identity) return const SizedBox.shrink();
    return FutureBuilder<Object?>(
      future: future,
      builder: (context, snapshot) {
        if (snapshot.connectionState != ConnectionState.done) {
          if (widget.path == '/api/v1/guardian/home') {
            return const HomeSkeleton(role: 'Guardian');
          }
          return const PageLoading();
        }
        if (snapshot.hasError || fatalError != null) {
          final failure = fatalError ?? snapshot.error;
          if (failure is ApiFailure &&
              failure.status == 404 &&
              widget.notFound != null) {
            return widget.notFound!;
          }
          final denied =
              failure is ApiFailure && [401, 403].contains(failure.status);
          return PageStatus(
            child: StatusPanel(
              title: denied ? 'لا يمكن عرض هذه الصفحة' : 'تعذر تحميل البيانات',
              icon: denied
                  ? Icons.lock_outline_rounded
                  : Icons.wifi_off_rounded,
              urgent: true,
              message: (fatalError ?? snapshot.error) is ApiFailure
                  ? ((fatalError ?? snapshot.error) as ApiFailure).message
                  : 'تعذر عرض البيانات.',
              onAction: () => setState(() {
                future = load();
              }),
              actionLabel: 'إعادة المحاولة',
            ),
          );
        }
        Widget content;
        try {
          content = widget.builder(context, accumulated ?? snapshot.data);
        } catch (_) {
          return const EmptyMessage(
            'تعذر قراءة بيانات الشاشة. حدّثها وحاول مجددًا.',
          );
        }
        if (cursorKey != null) {
          final body = content;
          content = NotificationListener<ScrollNotification>(
            onNotification: (n) {
              if (n.depth == 0 &&
                  n.metrics.axis == Axis.vertical &&
                  n.metrics.extentAfter < 240) {
                loadMore();
              }
              return false;
            },
            child: LayoutBuilder(
              builder: (context, bounds) {
                final footer = [
                  if (loadingMore)
                    const Padding(
                      padding: EdgeInsets.all(8),
                      child: LinearProgressIndicator(
                        semanticsLabel: 'تحميل المزيد',
                      ),
                    ),
                  if (moreError != null)
                    TextButton.icon(
                      onPressed: () {
                        setState(() => moreError = null);
                        loadMore();
                      },
                      icon: const Icon(Icons.refresh),
                      label: const UiText('تعذر تحميل المزيد · إعادة المحاولة'),
                    ),
                ];
                return Column(
                  mainAxisSize: bounds.hasBoundedHeight
                      ? MainAxisSize.max
                      : MainAxisSize.min,
                  children: [
                    if (bounds.hasBoundedHeight)
                      Expanded(child: body)
                    else
                      body,
                    ...footer,
                  ],
                );
              },
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
            } catch (_) {}
          },
          child: content,
        );
      },
    );
  }
}

Widget gList(
  List<Widget> children, {
  Key? key,
  ScrollController? controller,
  VoidCallback? onNearEnd,
}) => NotificationListener<ScrollNotification>(
  onNotification: (notice) {
    if (notice.depth == 0 &&
        notice.metrics.axis == Axis.vertical &&
        notice.metrics.extentAfter < 240) {
      onNearEnd?.call();
    }
    return false;
  },
  child: ListView(
    controller: controller,
    keyboardDismissBehavior: ScrollViewKeyboardDismissBehavior.onDrag,
    key: key,
    padding: const EdgeInsets.fromLTRB(20, 8, 20, 28),
    physics: const AlwaysScrollableScrollPhysics(),
    children: children,
  ),
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
              width: MediaQuery.textScalerOf(context).scale(1) > 1.4
                  ? c.maxWidth
                  : (c.maxWidth - 12) / 2,
              child: GCard(
                child: Column(
                  children: [
                    UiText(
                      v.$2,
                      textAlign: TextAlign.center,
                      style: TextStyle(
                        fontSize: 25,
                        fontWeight: FontWeight.w800,
                        color: Theme.of(context).colorScheme.primary,
                      ),
                    ),
                    UiText(
                      v.$1,
                      textAlign: TextAlign.center,
                      style: TextStyle(
                        color: Theme.of(context).colorScheme.onSurfaceVariant,
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
