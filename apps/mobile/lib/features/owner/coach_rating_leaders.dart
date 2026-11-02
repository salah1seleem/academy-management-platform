import 'package:flutter/material.dart';

import '../../core/auth/auth_controller.dart';
import '../../core/theme/ui_text.dart';
import '../guardian/guardian_ui.dart';
import 'owner_feedback.dart';

class OwnerCoachRatingLeaders extends StatelessWidget {
  const OwnerCoachRatingLeaders({
    super.key,
    required this.auth,
    this.branch = '',
    this.from = '',
    this.to = '',
  });
  final AuthController auth;
  final String branch, from, to;
  @override
  Widget build(BuildContext context) {
    final q = Uri(
      queryParameters: {
        if (branch.isNotEmpty) 'branchId': branch,
        if (from.isNotEmpty) 'from': from,
        if (to.isNotEmpty) 'to': to,
        if (from.isEmpty && to.isEmpty) 'allDates': 'true',
      },
    ).query;
    return RemoteBody(
      auth: auth,
      path: '/api/v1/manage/feedback/coach-rankings?$q',
      builder: (context, value) {
        final d = obj(value);
        Widget list(String title, String key) => GCard(
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              Heading(title),
              UiText('${d['minimumReviews']} تقييمات على الأقل لكل مدرب'),
              if (rows(d[key]).isEmpty)
                const UiText('لا توجد عينة تقييم كافية في الفترة المختارة.'),
              for (final row in rows(d[key]))
                ListTile(
                  dense: true,
                  contentPadding: EdgeInsets.zero,
                  title: UiText(textOf(obj(row)['name'])),
                  subtitle: UiText('${obj(row)['reviewCount']} تقييمات'),
                  trailing: UiText('${obj(row)['averageRating']} / 5'),
                  onTap: () => openPage(
                    context,
                    OwnerFeedbackInbox(
                      auth: auth,
                      initialFilters: {
                        'kind': 'Rating',
                        'coachId': textOf(obj(row)['id']),
                        if (branch.isNotEmpty) 'branchId': branch,
                        if (from.isNotEmpty) 'from': from,
                        if (to.isNotEmpty) 'to': to,
                      },
                    ),
                  ),
                ),
              TextButton(
                onPressed: () => openPage(
                  context,
                  OwnerFeedbackInbox(
                    auth: auth,
                    initialFilters: {
                      'kind': 'Rating',
                      if (branch.isNotEmpty) 'branchId': branch,
                      if (from.isNotEmpty) 'from': from,
                      if (to.isNotEmpty) 'to': to,
                    },
                  ),
                ),
                child: const UiText('عرض التقييمات'),
              ),
            ],
          ),
        );
        return Column(
          children: [
            list('أعلى 5 مدربين تقييمًا', 'highest'),
            list('أقل 5 مدربين تقييمًا', 'lowest'),
          ],
        );
      },
    );
  }
}
