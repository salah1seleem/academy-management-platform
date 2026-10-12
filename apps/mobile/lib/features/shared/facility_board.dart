import 'package:flutter/material.dart';

import '../../core/auth/auth_controller.dart';
import '../../core/theme/appearance.dart';
import '../../core/theme/ui_text.dart';
import '../guardian/guardian_ui.dart';
import 'observation_summary.dart';
import 'occupancy_summary.dart';
import 'visit_history.dart';

class FacilityBoardLink extends StatelessWidget {
  final AuthController auth;
  const FacilityBoardLink({super.key, required this.auth});
  @override
  Widget build(BuildContext context) => FutureBuilder<Object?>(
    future: auth.request('GET', '/api/v1/my-permissions'),
    builder: (context, state) =>
        state.data is List && (state.data as List).contains('facilities.read')
        ? GLink(
            localized(context, 'المرافق والحارات', 'Facilities & lanes'),
            Icons.pool_outlined,
            () => openPage(context, FacilityBoardPage(auth: auth)),
          )
        : const SizedBox.shrink(),
  );
}

class FacilityBoardPage extends StatefulWidget {
  final AuthController auth;
  const FacilityBoardPage({super.key, required this.auth});
  @override
  State<FacilityBoardPage> createState() => _FacilityBoardPageState();
}

class _FacilityBoardPageState extends State<FacilityBoardPage> {
  String? selectedDate;
  @override
  Widget build(BuildContext context) => GuardianPage(
    title: localized(context, 'تشغيل الحارات', 'Lane operations'),
    child: RemoteBody(
      key: ValueKey(selectedDate),
      auth: widget.auth,
      path:
          '/api/v1/facilities/board${selectedDate == null ? '' : '?date=$selectedDate'}',
      builder: (context, value) {
        final board = obj(value), bookings = rows(board['bookings']);
        return gList([
          GCard(
            child: Row(
              children: [
                const Icon(Icons.pool_outlined),
                const SizedBox(width: 12),
                Expanded(
                  child: UiText(
                    '${day(board['date'])} · ${bookings.length} ${localized(context, 'حجز', 'bookings')}',
                  ),
                ),
                IconButton(
                  tooltip: localized(context, 'اختر اليوم', 'Choose day'),
                  icon: const Icon(Icons.calendar_month_outlined),
                  onPressed: () async {
                    final date = await showDatePicker(
                      context: context,
                      initialDate:
                          DateTime.tryParse(textOf(board['date'])) ??
                          DateTime.now(),
                      firstDate: DateTime(2020),
                      lastDate: DateTime(2100),
                    );
                    if (date != null && mounted) {
                      setState(
                        () => selectedDate =
                            '${date.year}-${date.month.toString().padLeft(2, '0')}-${date.day.toString().padLeft(2, '0')}',
                      );
                    }
                  },
                ),
              ],
            ),
          ),
          for (final q in rows(
            board['openVisitQuality'] ?? [],
          ).where((q) => (q['staleOpen'] as num? ?? 0) > 0))
            GCard(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  OccupancySummary(value: q),
                  for (final id
                      in q['staleBookings'] is List
                          ? q['staleBookings'] as List
                          : [])
                    GLink(
                      localized(
                        context,
                        'مراجعة زيارة دون خروج',
                        'Review visit without checkout',
                      ),
                      Icons.history_outlined,
                      () => openPage(
                        context,
                        VisitHistoryPage(
                          auth: widget.auth,
                          bookingId: textOf(id),
                        ),
                      ),
                    ),
                ],
              ),
            ),
          for (final b in bookings)
            GCard(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Row(
                    children: [
                      CircleAvatar(child: UiText(textOf(b['resource']))),
                      const SizedBox(width: 12),
                      Expanded(
                        child: UiText(
                          textOf(
                            b['groupName'] ??
                                localized(
                                  context,
                                  'خدمة محجوزة',
                                  'Reserved service',
                                ),
                          ),
                          style: Theme.of(context).textTheme.titleMedium,
                        ),
                      ),
                    ],
                  ),
                  const SizedBox(height: 10),
                  if (b['observation'] is Map)
                    ObservationSummary(value: obj(b['observation'])),
                  if (b['occupancy'] is Map)
                    OccupancySummary(value: obj(b['occupancy'])),
                  GLink(
                    localized(
                      context,
                      'سجل الدخول والخروج',
                      'Entry and exit history',
                    ),
                    Icons.door_front_door_outlined,
                    () => openPage(
                      context,
                      VisitHistoryPage(
                        auth: widget.auth,
                        bookingId: textOf(b['id']),
                      ),
                    ),
                  ),
                  UiText('${b['facility']} · ${b['space']}'),
                  UiText(
                    '${textOf(b['startTime']).split(':').take(2).join(':')} — ${textOf(b['endTime']).split(':').take(2).join(':')} · ${b['coach'] ?? localized(context, 'بدون مدرب', 'No coach')}',
                  ),
                  UiText(
                    '${localized(context, 'المقاعد', 'Seats')}: ${b['reservedSeats']} / ${b['capacity']}',
                  ),
                  UiText(
                    b['trainingSessionId'] == null
                        ? localized(
                            context,
                            'لم تربط حصة مؤرخة',
                            'No dated session linked',
                          )
                        : '${b['present']} ${localized(context, 'حاضر', 'present')} · ${b['absent']} ${localized(context, 'غائب', 'absent')}',
                  ),
                ],
              ),
            ),
          if (bookings.isEmpty)
            EmptyMessage(
              localized(
                context,
                'لا توجد حجوزات في اليوم المختار.',
                'No bookings for this day.',
              ),
            ),
          UiText(
            localized(
              context,
              'حجز المورد ليس إثبات دفع أو حضور فعلي.',
              'A reservation is not proof of payment or physical attendance.',
            ),
          ),
        ]);
      },
    ),
  );
}
