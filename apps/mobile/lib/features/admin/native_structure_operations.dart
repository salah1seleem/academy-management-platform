import 'native_player_placement.dart';

import 'dart:async';

import '../shared/directory_cards.dart';

import 'package:flutter/material.dart';

import '../../core/auth/auth_controller.dart';
import '../../core/api/api_client.dart';
import '../../core/theme/ui_text.dart';
import '../guardian/guardian_ui.dart';
import 'native_operation_form.dart';

class NativeCoachAssignment extends StatefulWidget {
  const NativeCoachAssignment({
    super.key,
    required this.auth,
    this.coachId,
    this.groupId,
    this.branchId,
  });
  final AuthController auth;
  final String? coachId, groupId, branchId;
  @override
  State<NativeCoachAssignment> createState() => _NativeCoachAssignmentState();
}

class _NativeCoachAssignmentState
    extends NativeOperationState<NativeCoachAssignment> {
  @override
  AuthController get auth => widget.auth;
  late String? coach = widget.coachId, group = widget.groupId;
  late final options = auth.request('GET', '/api/v1/manage/structure/options');
  @override
  Widget build(BuildContext context) => page('إسناد مدرب لمجموعة', [
    FutureBuilder(
      future: options,
      builder: (context, s) {
        if (s.hasError) {
          return const EmptyMessage(
            'تعذر تحميل المدربين والمجموعات ضمن صلاحياتك.',
          );
        }
        if (!s.hasData) return const Center(child: CircularProgressIndicator());
        final data = obj(s.data);
        if (coach == null && group != null) {
          final currentGroup = rows(data['groups'])
              .where((g) => g['id'] == group)
              .firstOrNull;
          final ids = currentGroup?['coachIds'];
          if (ids is List && ids.isNotEmpty) coach = textOf(ids.first);
          final assigned = coach == null ? (currentGroup?['coaches']) : null;
          if (assigned is List && assigned.isNotEmpty) {
            final first = assigned.first;
            if (first is Map) {
              coach = textOf(first['id']);
            } else {
              final matches = rows(data['coaches'])
                  .where((c) => c['arabicName'] == first)
                  .toList();
              if (matches.length == 1) coach = textOf(matches.first['id']);
            }
          }
        }
        return GCard(
          child: Column(
            children: [
              choice(
                'المدرب',
                coach,
                rows(data['coaches']),
                (v) => setState(() {
                  coach = v;
                  message = null;
                }),
              ),
              choice(
                'المجموعة',
                group,
                rows(data['groups'])
                    .where(
                      (g) =>
                          widget.branchId == null ||
                          g['branchId'] == widget.branchId,
                    )
                    .toList(),
                (v) => setState(() {
                  group = v;
                  if (widget.coachId == null &&
                      rows(data['groups']).any(
                        (g) =>
                            g['id'] == v &&
                            (g['coaches'] as List? ?? []).isNotEmpty,
                      )) {
                    coach = null;
                  }
                  message = null;
                }),
              ),
              FilledButton(
                onPressed: () => run(() async {
                  if (!form.currentState!.validate()) return;
                  final selectedCoach = coach!, selectedGroup = group!;
                  final id = await auth.request(
                    'POST',
                    '/api/v1/manage/groups/$selectedGroup/coaches',
                    body: {'membershipId': selectedCoach},
                  );
                  final stored = obj(
                    await auth.request(
                      'GET',
                      '/api/v1/manage/groups/$selectedGroup/coaches/$selectedCoach',
                    ),
                  );
                  if (stored['id'] != id ||
                      stored['trainingGroupId'] != selectedGroup ||
                      stored['academyMembershipId'] != selectedCoach ||
                      stored['isActive'] != true) {
                    throw StateError('assignment read mismatch');
                  }
                  if (current) setState(() => message = 'تم حفظ الإسناد.');
                }),
                child: const UiText('حفظ الإسناد'),
              ),
            ],
          ),
        );
      },
    ),
  ]);
}

class NativeCoachCreate extends StatefulWidget {
  const NativeCoachCreate({super.key, required this.auth, this.inline = false});
  final bool inline;
  final AuthController auth;
  @override
  State<NativeCoachCreate> createState() => _NativeCoachCreateState();
}

class _NativeCoachCreateState extends NativeOperationState<NativeCoachCreate> {
  @override
  AuthController get auth => widget.auth;
  final name = TextEditingController(),
      phone = TextEditingController(),
      email = TextEditingController();
  bool active = true, created = false;
  @override
  void dispose() {
    name.dispose();
    phone.dispose();
    email.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) => page('إضافة مدرب', [
    GCard(
      child: Column(
        children: [
          field('الاسم بالعربية', name),
          field('رقم الهاتف', phone, keyboard: TextInputType.phone),
          field(
            'البريد الإلكتروني — اختياري',
            email,
            required: false,
            keyboard: TextInputType.emailAddress,
          ),
          SwitchListTile(
            title: const UiText('حساب نشط'),
            value: active,
            onChanged: (v) => setState(() => active = v),
          ),
          FilledButton(
            onPressed: created
                ? null
                : () => run(() async {
                    if (!form.currentState!.validate()) return;
                    final result = obj(
                      await auth.request(
                        'POST',
                        '/api/v1/manage/coaches',
                        body: {
                          'displayName': name.text.trim(),
                          'phoneNumber': phone.text.trim(),
                          'email': email.text.trim().isEmpty
                              ? null
                              : email.text.trim(),
                          'isActive': active,
                          'groupIds': <String>[],
                        },
                      ),
                    );
                    if (current) {
                      setState(() {
                        created = true;
                        message = 'تم حفظ المدرب.';
                      });
                      if (widget.inline && context.mounted) {
                        Navigator.pop(context, result);
                        return;
                      }
                      if (active &&
                          context.mounted &&
                          auth.session?.selected?.role == 'AcademyOwner') {
                        await openPage(
                          context,
                          NativeCoachAssignment(
                            auth: auth,
                            coachId: textOf(result['membershipId']),
                          ),
                        );
                      }
                    }
                  }),
            child: const UiText('حفظ المدرب'),
          ),
        ],
      ),
    ),
  ]);
}

class NativeGroupEdit extends StatefulWidget {
  const NativeGroupEdit({super.key, required this.auth, required this.id});
  final AuthController auth;
  final String id;
  @override
  State<NativeGroupEdit> createState() => _NativeGroupEditState();
}

class _NativeGroupEditState extends NativeOperationState<NativeGroupEdit> {
  @override
  AuthController get auth => widget.auth;
  final name = TextEditingController(),
      english = TextEditingController(),
      capacity = TextEditingController();
  String? branch, sport, category;
  bool loaded = false;
  late final data = Future.wait([
    auth.request('GET', '/api/v1/manage/structure/groups/${widget.id}'),
    auth.request('GET', '/api/v1/manage/structure/options'),
  ]);
  @override
  void dispose() {
    name.dispose();
    english.dispose();
    capacity.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) => page('تعديل مجموعة', [
    FutureBuilder(
      future: data,
      builder: (context, s) {
        if (s.hasError) {
          return const EmptyMessage(
            'تعذر تحميل المجموعة أو أنها خارج نطاق صلاحياتك.',
          );
        }
        if (!s.hasData) return const Center(child: CircularProgressIndicator());
        final record = obj(s.data![0]), options = obj(s.data![1]);
        if (!loaded) {
          loaded = true;
          name.text = textOf(record['arabicName']);
          english.text = textOf(record['englishName']);
          capacity.text = textOf(record['capacity']);
          branch = record['branchId'];
          sport = record['sportId'];
          category = record['ageCategoryId'];
        }
        return GCard(
          child: Column(
            children: [
              field('اسم المجموعة', name),
              field('الاسم بالإنجليزية — اختياري', english, required: false),
              choice(
                'الفرع',
                branch,
                rows(options['branches']),
                (v) => setState(() => branch = v),
              ),
              choice(
                'الرياضة',
                sport,
                rows(options['sports']),
                (v) => setState(() => sport = v),
              ),
              choice(
                'الفئة العمرية',
                category,
                rows(options['categories']),
                (v) => setState(() => category = v),
              ),
              Padding(
                padding: const EdgeInsets.only(bottom: 14),
                child: TextFormField(
                  controller: capacity,
                  keyboardType: TextInputType.number,
                  decoration: const InputDecoration(
                    labelText: 'السعة — اختياري',
                  ),
                  validator: (v) =>
                      v != null &&
                          v.trim().isNotEmpty &&
                          (int.tryParse(v) == null || int.parse(v) < 1)
                      ? 'أدخل عددًا صحيحًا موجبًا للسعة.'
                      : null,
                ),
              ),
              FilledButton(
                onPressed: () => run(() async {
                  if (!form.currentState!.validate()) return;
                  await auth.request(
                    'PUT',
                    '/api/v1/manage/structure/groups/${widget.id}',
                    body: {
                      'arabicName': name.text.trim(),
                      'englishName': english.text.trim(),
                      'branchId': branch,
                      'sportId': sport,
                      'ageCategoryId': category,
                      'capacity': int.tryParse(capacity.text),
                      'description': null,
                    },
                  );
                  if (current) setState(() => message = 'تم حفظ المجموعة.');
                }),
                child: const UiText('حفظ التعديلات'),
              ),
            ],
          ),
        );
      },
    ),
  ]);
}

class NativeGroupPicker extends StatelessWidget {
  const NativeGroupPicker({
    super.key,
    required this.auth,
    required this.title,
    required this.open,
    this.branchId,
  });
  final AuthController auth;
  final String title;
  final String? branchId;
  final Widget Function(String) open;
  @override
  Widget build(BuildContext context) => GuardianPage(
    title: title,
    child: RemoteBody(
      auth: auth,
      path: '/api/v1/manage/structure/options',
      builder: (context, v) => SearchableDirectory(
        items: rows(obj(v)['groups'])
            .where(
              (g) =>
                  branchId == null ||
                  branchId!.isEmpty ||
                  g['branchId'] == branchId,
            )
            .toList(),
        label: 'بحث عن مجموعة',
        itemBuilder: (context, g) => GroupDirectoryCard(
          group: g,
          onTap: () => openPage(context, open(textOf(g['id']))),
        ),
      ),
    ),
  );
}

class NativeGroupPlacement extends StatefulWidget {
  const NativeGroupPlacement({
    super.key,
    required this.auth,
    required this.groupId,
  });
  final AuthController auth;
  final String groupId;
  @override
  State<NativeGroupPlacement> createState() => _NativeGroupPlacementState();
}

class _NativeGroupPlacementState
    extends NativeOperationState<NativeGroupPlacement> {
  @override
  AuthController get auth => widget.auth;
  final search = TextEditingController();
  final selected = <String>{};
  Json? preview, frozen, recorded;
  bool uncertain = false, completed = false;
  String key = commandKey();
  int offset = 0, searchRevision = 0;
  Timer? debounce;
  String get candidatePath =>
      '/api/v1/manage/groups/${widget.groupId}/placement-candidates?${Uri(queryParameters: {'search': search.text.trim(), 'offset': '0'}).query}';
  void refresh() {
    if (uncertain || completed) return;
    setState(() {
      preview = null;
      frozen = null;
      recorded = null;
      key = commandKey();
      message = null;
      error = null;
      selected.clear();
      searchRevision++;
    });
  }

  Future<void> save() => run(() async {
    if (preview == null || completed) return;
    frozen ??= {
      'enrollmentIds': selected.toList(),
      'fingerprint': preview!['fingerprint'],
    };
    final ids = (frozen!['enrollmentIds'] as List).cast<String>().toSet();
    var written = recorded != null;
    try {
      recorded ??= obj(
        await auth.request(
          'POST',
          '/api/v1/manage/groups/${widget.groupId}/placement',
          body: frozen,
          idempotencyKey: key,
        ),
      );
      written = true;
      final revision = obj(
        await auth.request(
          'GET',
          '/api/v1/manage/groups/${widget.groupId}/placement-revisions/${recorded!['id']}',
        ),
      );
      final actualIds = (revision['assignedEnrollmentIds'] as List)
          .cast<String>()
          .toSet();
      if (recorded!['id'] == null ||
          revision['id'] != recorded!['id'] ||
          revision['trainingGroupId'] != widget.groupId ||
          recorded!['placed'] != ids.length ||
          revision['placed'] != ids.length ||
          actualIds.length != ids.length ||
          !actualIds.containsAll(ids) ||
          day(revision['effectiveDate']) != day(preview!['effectiveDate'])) {
        throw const ApiFailure(
          0,
          'لم يتأكد تطابق سجل التسكين مع الاختيار المراجع.',
        );
      }
      final group = obj(
        await auth.request(
          'GET',
          '/api/v1/manage/groups/${widget.groupId}/details',
        ),
      );
      if (obj(group['group'])['id'] != widget.groupId ||
          !rows(group['players'])
              .map((p) => p['id'])
              .toSet()
              .containsAll(ids)) {
        throw const ApiFailure(
          0,
          'لم تتأكد قراءة اللاعبين في المجموعة المقصودة.',
        );
      }
      if (current) {
        setState(() {
          message = 'تم تسكين ${ids.length} لاعب.';
          completed = true;
          uncertain = false;
          preview = null;
          frozen = null;
          recorded = null;
        });
      }
    } catch (e) {
      if (current) {
        setState(() {
          uncertain =
              written || e is! ApiFailure || e.status == 0 || e.status >= 500;
          if (!uncertain) frozen = null;
        });
      }
      rethrow;
    }
  });

  @override
  void dispose() {
    debounce?.cancel();
    search.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) => page('تسكين لاعب', [
    if (completed)
      OutlinedButton(
        onPressed: () {
          setState(() => completed = false);
          refresh();
        },
        child: const UiText('تسكين لاعب آخر'),
      ),
    if (!uncertain && !completed) ...[
      field(
        'اسم اللاعب أو الكود',
        search,
        required: false,
        changed: () {
          debounce?.cancel();
          debounce = Timer(const Duration(milliseconds: 300), () {
            if (current) refresh();
          });
        },
      ),
      OutlinedButton(
        onPressed: () {
          offset = 0;
          refresh();
        },
        child: const UiText('بحث'),
      ),
      RemoteBody(
        key: ValueKey(searchRevision),
        auth: auth,
        path: candidatePath,
        pagingController: operationScroll,
        builder: (context, value) {
          final data = obj(value), conflicts = data['conflicts'] as List? ?? [];
          return Column(
            children: [
              Padding(
                padding: const EdgeInsets.only(bottom: 12),
                child: Row(
                  children: [
                    Chip(
                      label: UiText(
                        '${data['currentPlayers']} لاعب في المجموعة',
                      ),
                    ),
                    const SizedBox(width: 8),
                    if (data['capacity'] != null)
                      UiText('السعة ${data['capacity']}'),
                  ],
                ),
              ),
              if (rows(data['players']).isEmpty)
                const EmptyMessage(
                  'لا يوجد لاعب مطابق. ابحث بالاسم أو كود اللاعب.',
                ),
              for (final c in conflicts) UiText(textOf(c)),
              for (final p in rows(data['players']))
                PlayerDirectoryCard(
                  auth: auth,
                  player: {...p, 'arabicName': p['player']},
                  enrollments: [p],
                  selected: selected.contains(p['id']),
                  openLabel: 'نقل لهذه المجموعة',
                  onOpen:
                      p['trainingGroupId'] != null &&
                          p['trainingGroupId'] != widget.groupId
                      ? () async {
                          await openPage(
                            context,
                            NativePlayerPlacement(
                              auth: auth,
                              enrollment: p,
                              action: PlayerPlacementAction.group,
                              initialTargetGroupId: widget.groupId,
                            ),
                          );
                          if (current) refresh();
                        }
                      : null,
                  onSelection:
                      conflicts.isNotEmpty ||
                          (p['reasons'] as List? ?? []).isNotEmpty
                      ? null
                      : (v) => setState(() {
                          preview = null;
                          if (v == true) {
                            selected.add(textOf(p['id']));
                          } else {
                            selected.remove(p['id']);
                          }
                        }),
                ),
              const SizedBox.shrink(),
            ],
          );
        },
      ),
      OutlinedButton(
        onPressed: selected.isEmpty
            ? null
            : () => run(() async {
                final result = obj(
                  await auth.request(
                    'POST',
                    '/api/v1/manage/groups/${widget.groupId}/placement/preview',
                    body: {'enrollmentIds': selected.toList()},
                  ),
                );
                if (current) {
                  setState(() {
                    preview = result;
                    frozen = null;
                    recorded = null;
                    key = commandKey();
                  });
                }
              }),
        child: const UiText('معاينة التسكين'),
      ),
    ],
    if (preview != null)
      GCard(
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            UiText(
              '${preview!['selected']} لاعب · السريان من ${preview!['effectiveDate']}',
            ),
            for (final c in preview!['conflicts'] as List) UiText(textOf(c)),
            for (final p in rows(preview!['players']))
              UiText('${p['player']} ${(p['reasons'] as List).join(' · ')}'),
            const UiText('لا يُنشئ التسكين تحصيلًا أو حضورًا سابقًا.'),
            FilledButton(
              onPressed:
                  (preview!['conflicts'] as List).isNotEmpty ||
                      rows(preview!['players'])
                          .any((p) => (p['reasons'] as List).isNotEmpty)
                  ? null
                  : save,
              child: UiText(
                uncertain ? 'إعادة التحقق من التسكين نفسه' : 'تأكيد التسكين',
              ),
            ),
          ],
        ),
      ),
  ]);
}
