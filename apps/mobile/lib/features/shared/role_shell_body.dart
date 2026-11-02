import 'package:flutter/widgets.dart';

/// Root tab pages share one persistent header. Pushed routes keep their own help.
class RoleShellBody extends InheritedWidget {
  const RoleShellBody({super.key, required super.child});
  static bool contains(BuildContext context) =>
      context.dependOnInheritedWidgetOfExactType<RoleShellBody>() != null;
  @override
  bool updateShouldNotify(RoleShellBody oldWidget) => false;
}
