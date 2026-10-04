class Membership {
  final String id, academyId, academyName, role;
  const Membership(this.id, this.academyId, this.academyName, this.role);
  factory Membership.fromJson(Map<String, dynamic> json) => Membership(
    json['id'] as String,
    json['academyId'] as String,
    json['academyName'] as String,
    json['role'] as String,
  );
  String get label => switch (role) {
    'Guardian' => 'ولي أمر',
    'Coach' => 'مدرب',
    'AcademyOwner' => 'مالك',
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
