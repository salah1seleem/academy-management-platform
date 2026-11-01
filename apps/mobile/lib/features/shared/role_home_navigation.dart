import 'package:flutter/foundation.dart';

import '../../core/auth/auth_controller.dart';

final _ownerHome = Expando<VoidCallback>('owner home');
VoidCallback? ownerHomeAction(AuthController auth) => _ownerHome[auth];
void registerOwnerHome(AuthController auth, VoidCallback? action) =>
    _ownerHome[auth] = action;
