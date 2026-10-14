class Membership {
  final String id, academyId, academyName, role;
  final String? contextName;
  final int contextRevision;
  final bool allBranches, allSports;
  final List<String> branchNames, sportNames;
  const Membership(
    this.id,
    this.academyId,
    this.academyName,
    this.role, {
    this.contextName,
    this.contextRevision = 0,
    this.allBranches = true,
    this.allSports = true,
    this.branchNames = const [],
    this.sportNames = const [],
  });
  factory Membership.fromJson(Map<String, dynamic> json) => Membership(
    json['id'] as String,
    json['academyId'] as String,
    json['academyName'] as String,
    json['role'] as String,
    contextName: json['contextName'] as String?,
    contextRevision: json['contextRevision'] as int? ?? 0,
    allBranches: json['allBranches'] as bool? ?? true,
    allSports: json['allSports'] as bool? ?? true,
    branchNames: (json['branchNames'] as List? ?? []).cast<String>(),
    sportNames: (json['sportNames'] as List? ?? []).cast<String>(),
  );
  String get scopeLabel =>
      '${allBranches
          ? 'كل الفروع'
          : branchNames.isEmpty
          ? 'لم يمنح فرع'
          : branchNames.join('، ')} · ${allSports
          ? 'كل الرياضات'
          : sportNames.isEmpty
          ? 'لم تمنح رياضة'
          : sportNames.join('، ')}';
  String get label =>
      contextName ??
      switch (role) {
        'Guardian' => 'ولي أمر',
        'Coach' => 'مدرب',
        'AcademyOwner' => 'مالك',
        'AcademyAdmin' => 'إداري',
        _ => 'دور غير مدعوم',
      };
}

class Session {
  final String accessToken, refreshToken, displayName;
  final String? membershipId;
  final DateTime accessExpires;
  final List<Membership> memberships;
  const Session(
    this.accessToken,
    this.refreshToken,
    this.displayName,
    this.membershipId,
    this.accessExpires,
    this.memberships,
  );
  factory Session.fromJson(Object? value) {
    final json = value as Map<String, dynamic>;
    return Session(
      json['accessToken'] as String,
      json['refreshToken'] as String,
      json['displayName'] as String,
      json['membershipId'] as String?,
      DateTime.parse(json['accessExpiresAtUtc'] as String),
      (json['memberships'] as List)
          .map((m) => Membership.fromJson(m as Map<String, dynamic>))
          .toList(growable: false),
    );
  }
  Membership? get selected {
    for (final membership in memberships) {
      if (membership.id == membershipId) return membership;
    }
    return null;
  }
}
