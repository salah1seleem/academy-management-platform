import '../../core/auth/auth_controller.dart';
import '../guardian/guardian_ui.dart';

/// Presentation of the existing server grants. Every write is checked again by the API.
class StaffCapabilities {
  const StaffCapabilities({
    required this.owner,
    required this.permissions,
    required this.scope,
  });
  final bool owner;
  final List<String> permissions;
  final Json scope;

  bool get canReadProductOrders => allows('payment.read', 'catalog', 'Read');
  bool get canReadAttendanceCorrections =>
      allows('attendance.read', 'attendance', 'Read');

  static Future<StaffCapabilities> load(AuthController auth) async {
    final owner = auth.session?.selected?.role == 'AcademyOwner';
    if (owner) {
      return const StaffCapabilities(owner: true, permissions: [], scope: {});
    }
    final values = await Future.wait([
      auth.request('GET', '/api/v1/my-permissions'),
      auth.request('GET', '/api/v1/my-access'),
    ]);
    return StaffCapabilities(
      owner: false,
      permissions: (values[0] as List).cast<String>(),
      scope: obj(values[1]),
    );
  }

  bool explicitAction(String module, String operation) {
    if (owner) return true;
    if (scope['useActionGrants'] != true) return false;
    final grant = rows(scope['actions'] ?? [])
        .where((g) => g['module'] == module)
        .firstOrNull;
    return grant?['read'] == true &&
        (operation == 'Read' ||
            operation == 'Add' && grant?['add'] == true ||
            operation == 'Edit' && grant?['edit'] == true ||
            operation == 'Write' &&
                grant?['add'] == true &&
                grant?['edit'] == true);
  }

  bool allows(
    String permission,
    String module,
    String operation, [
    List<String> leaves = const [],
  ]) {
    if (owner) return true;
    if (!permissions.contains(permission)) return false;
    if (scope['useActionGrants'] == true) {
      final grant = rows(scope['actions'] ?? [])
          .where((g) => g['module'] == module)
          .firstOrNull;
      if (grant?['read'] != true ||
          operation == 'Add' && grant?['add'] != true ||
          operation == 'Edit' && grant?['edit'] != true ||
          operation == 'Write' &&
              (grant?['add'] != true || grant?['edit'] != true)) {
        return false;
      }
    }
    return leaves.every((key) {
      final leaf = rows(scope['workflowActions'] ?? [])
          .where((g) => g['key'] == key)
          .firstOrNull;
      return leaf?['hasExplicitGrant'] != true || leaf?['allowed'] == true;
    });
  }
}
