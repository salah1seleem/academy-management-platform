import 'package:flutter/material.dart';
import 'package:flutter_secure_storage/flutter_secure_storage.dart';

import 'academy_brand.dart';
import 'visual_model.dart';
export 'visual_model.dart';
import 'approved_role_introduction.dart';
import '../widgets/brand_motion.dart';

class Appearance extends ChangeNotifier {
  bool dark = true, onboarded = true;
  VisualModel model = VisualModel.dark;
  Locale locale = const Locale('ar');
  final FlutterSecureStorage? storage;
  static const roleIntroVersion = 2;
  String? introUser;
  final Map<String, int> _introPages = {};
  Future<void> _progressWrites = Future<void>.value();
  void bindIntroUser(String? userId) {
    introUser = userId;
  }

  final Set<String> _seenRoleIntros = {};
  Appearance({this.storage});
  String roleIntroKey(String role, String sportTheme) =>
      'academy.role-intro.$roleIntroVersion.${introUser ?? 'local'}.$role';
  Future<bool> hasRoleIntro(String role, String sportTheme) async {
    final key = roleIntroKey(role, sportTheme);
    if (_seenRoleIntros.contains(key)) return true;
    final seen = await storage?.read(key: key) == 'yes';
    if (seen) _seenRoleIntros.add(key);
    return seen;
  }

  Future<void> completeRoleIntro(String role, String sportTheme) async {
    final key = roleIntroKey(role, sportTheme);
    await storage?.write(key: key, value: 'yes');
    _seenRoleIntros.add(key);
  }

  Future<int> roleIntroPage(String role, String sportTheme) async {
    final key = '${roleIntroKey(role, sportTheme)}.page';
    return _introPages[key] ??
        int.tryParse(await storage?.read(key: key) ?? '') ??
        0;
  }

  Future<void> saveRoleIntroPage(String role, String sportTheme, int page) {
    final key = '${roleIntroKey(role, sportTheme)}.page';
    _introPages[key] = page;
    final next = _progressWrites.catchError((Object _) {}).then((_) async {
      await storage?.write(key: key, value: '$page');
    });
    _progressWrites = next;
    return next;
  }

  Future<void> load() async {
    if (storage == null) return;
    final saved = await storage!.read(key: 'academy.appearance');
    model =
        VisualModel.values.where((m) => m.name == saved).firstOrNull ??
        VisualModel.dark;
    dark = model != VisualModel.light;
    locale = Locale(
      await storage!.read(key: 'academy.language') == 'en' ? 'en' : 'ar',
    );
    onboarded = await storage!.read(key: 'academy.onboarded') == 'yes';
    notifyListeners();
  }

  Future<void> update({
    bool? night,
    VisualModel? visualModel,
    String? language,
    bool? completed,
  }) async {
    if (visualModel != null) {
      model = visualModel;
      dark = model != VisualModel.light;
    } else if (night != null) {
      dark = night;
      model = dark ? VisualModel.dark : VisualModel.light;
    }
    if (language != null) locale = Locale(language == 'en' ? 'en' : 'ar');
    if (completed != null) onboarded = completed;
    notifyListeners();
    await storage?.write(key: 'academy.appearance', value: model.name);
    await storage?.write(key: 'academy.language', value: locale.languageCode);
    await storage?.write(
      key: 'academy.onboarded',
      value: onboarded ? 'yes' : 'no',
    );
  }
}

class AppearanceScope extends InheritedNotifier<Appearance> {
  const AppearanceScope({
    super.key,
    required Appearance appearance,
    required super.child,
  }) : super(notifier: appearance);
  static Appearance? of(BuildContext context) =>
      context.dependOnInheritedWidgetOfExactType<AppearanceScope>()?.notifier;
}

String localized(BuildContext context, String arabic, String english) =>
    Localizations.localeOf(context).languageCode == 'en' ? english : arabic;

class AppearanceSettings extends StatelessWidget {
  const AppearanceSettings({super.key});
  @override
  Widget build(BuildContext context) {
    final preferences = AppearanceScope.of(context);
    if (preferences == null) return const SizedBox.shrink();
    return Card(
      child: Column(
        children: [
          ListTile(
            title: Text(localized(context, 'اللغة', 'Language')),
            trailing: DropdownButton<String>(
              value: preferences.locale.languageCode,
              items: const [
                DropdownMenuItem(value: 'ar', child: Text('العربية')),
                DropdownMenuItem(value: 'en', child: Text('English')),
              ],
              onChanged: (v) => preferences.update(language: v),
            ),
          ),
        ],
      ),
    );
  }
}

class AcademyCrest extends StatelessWidget {
  final double size;
  final bool homeEmphasis;
  const AcademyCrest({super.key, this.size = 72, this.homeEmphasis = false});
  @override
  Widget build(BuildContext context) {
    final brand = BrandScope.maybeOf(context);
    return Container(
      width: size,
      height: size,
      decoration: BoxDecoration(
        borderRadius: BorderRadius.circular(size * .3),
        color: homeEmphasis && brand?.artwork?.colors.isNotEmpty == true
            ? Colors.white
            : brand?.logoBackground ?? Colors.white,
      ),
      padding: EdgeInsets.all(size * (homeEmphasis ? .018 : .07)),
      child: brand?.logo != null
          ? CanonicalAcademyLogo(trimMargin: homeEmphasis)
          : brand?.signedIn == true
          ? Icon(
              Icons.school_outlined,
              color: brand!.headerColor,
              size: size * .58,
            )
          : ColoredBox(
              color: platformNavy,
              child: PlatformMark(size: size),
            ),
    );
  }
}

/// Live layout crop removes upload padding, never redraws or stretches a logo.
class CanonicalAcademyLogo extends StatelessWidget {
  const CanonicalAcademyLogo({super.key, this.trimMargin = true});
  final bool trimMargin;
  @override
  Widget build(BuildContext context) {
    final brand = BrandScope.maybeOf(context),
        bytes = brand?.logo,
        art = brand?.artwork;
    if (bytes == null) return const SizedBox.shrink();
    final image = Image.memory(
      bytes,
      fit: BoxFit.contain,
      errorBuilder: (_, _, _) => const Icon(Icons.school_outlined),
    );
    if (!trimMargin || art == null) return image;
    return FittedBox(
      fit: BoxFit.contain,
      child: SizedBox(
        width: art.bounds.width,
        height: art.bounds.height,
        child: ClipRect(
          child: Stack(
            children: [
              Positioned(
                left: -art.bounds.left,
                top: -art.bounds.top,
                width: art.size.width,
                height: art.size.height,
                child: image,
              ),
            ],
          ),
        ),
      ),
    );
  }
}

class AcademyOnboarding extends StatefulWidget {
  final String role, sportTheme;
  final List<String> features;
  final Future<void> Function() onComplete;
  final bool busy, canPop;
  final String? error;
  final VoidCallback? onContinueOnce;
  final int initialPage;
  final ValueChanged<int>? onPageChanged;
  const AcademyOnboarding({
    super.key,
    required this.role,
    required this.sportTheme,
    required this.features,
    required this.onComplete,
    this.busy = false,
    this.canPop = false,
    this.error,
    this.onContinueOnce,
    this.initialPage = 0,
    this.onPageChanged,
  });
  @override
  State<AcademyOnboarding> createState() => _AcademyOnboardingState();
}

class _AcademyOnboardingState extends State<AcademyOnboarding> {
  @override
  Widget build(BuildContext context) => ApprovedRoleIntroduction(
    initialPage: widget.initialPage,
    onPageChanged: widget.onPageChanged,
    role: widget.role,
    sportTheme: widget.sportTheme,
    training: widget.features.contains('training'),
    academyName: BrandScope.maybeOf(context)?.name ?? 'I Academy One',
    crest: const AcademyCrest(size: 40),
    busy: widget.busy,
    canPop: widget.canPop,
    error: widget.error,
    onContinueOnce: widget.onContinueOnce,
    onComplete: widget.onComplete,
  );
}
