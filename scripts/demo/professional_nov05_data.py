"""Additional fictional records for the Nov 5 isolated football dataset.

Called inside prepare_professional_demo's guarded transaction.
"""
import calendar
import datetime as dt
import hashlib
from decimal import Decimal


def enrich_nov05(day, now, academy, stable_id, cursor, rows, insert, update, row,
                 players, groups, branches, coaches, owner, admin, experience,
                 session_template, plan, renewal_template, payment_template):
    update('AcademyMemberships', admin['Id'], UseActionGrants=True, AllBranches=True, AllSports=True)
    modules = ('academy', 'players', 'subscriptions', 'payments', 'attendance',
               'evaluations', 'catalog', 'nutrition', 'medical', 'media', 'finance',
               'reports', 'payroll', 'settings', 'staff', 'news', 'matches',
               'facilities', 'followups')
    existing = {grant['Module']: grant for grant in rows('StaffActionGrants', '"MembershipId"=%s', (admin['Id'],))}
    for module in modules:
        permissions = dict(Read=True, Add=True, Edit=True, Delete=True)
        if module in existing:
            update('StaffActionGrants', existing[module]['Id'], IsActive=True, **permissions)
        else:
            insert('StaffActionGrants', row(f'nov05/admin-grant/{module}', MembershipId=admin['Id'],
                                            Module=module, **permissions))
    update('AcademyExperiences', experience['Id'], CoachAttendanceEnabled=True, CoachAttendancePolicyConfirmed=True)
    coaches_by_id = {member['Id']: index for index, member in enumerate(coaches)}
    for employee in rows('Employees'):
        index = coaches_by_id.get(employee['MembershipId'])
        if index is not None:
            update('Employees', employee['Id'],
                   DemoPortraitReference=f'/demo-assets/portraits/coach-0{index % 2 + 1}.jpg')
    for product in rows('SportCatalogItems'):
        stock = product['StockQuantity']
        quantities = [stock // 4, stock // 3, stock - stock // 4 - stock // 3]
        for size, quantity in zip(('S', 'M', 'L'), quantities):
            insert('ProductStockVariants', row(f'nov05/variant/{product["Id"]}/{size}',
                ProductId=product['Id'], Size=size, Color='', Quantity=quantity, ShirtNumber=None))
        update('SportCatalogItems', product['Id'], UsesVariants=True, StockRevision=max(1, product['StockRevision']))

    for i, _, enrollment, _, guardian in players[130:134]:
        key = f'nov05/pending/{i}'
        renewal_id, payment_id = stable_id(key + '/renewal'), stable_id(key + '/payment')
        submitted = now - dt.timedelta(days=2, hours=5)
        insert('RenewalRequests', dict(renewal_template, Id=renewal_id,
            SportEnrollmentId=enrollment['Id'], RequestedByUserId=guardian['UserId'],
            SportId=plan['SportId'], SubscriptionPlanId=plan['Id'], PaymentRequestId=None,
            Status='PaymentInProgress', OriginalAmount=plan['Price'], DiscountAmount=0,
            DiscountType=None, DiscountValue=None, DiscountReason=None,
            DiscountRuleIdSnapshot=None, DiscountRuleRevisionSnapshot=None,
            DiscountAppliedByUserId=None, DiscountAppliedAtUtc=None,
            FinalAmount=plan['Price'], AmountExpected=plan['Price'], IdempotencyKey=key,
            RequestedAtUtc=submitted, CreatedAtUtc=submitted, UpdatedAtUtc=submitted))
        insert('PaymentRequests', dict(payment_template, Id=payment_id, RenewalRequestId=renewal_id,
            Provider='Manual', ProviderEnvironment='Manual', ProviderReference=key, CheckoutReference=key,
            Status='Pending', Amount=plan['Price'], ConfirmedAtUtc=None, LastProviderEventAtUtc=None,
            CreatedAtUtc=submitted, UpdatedAtUtc=submitted))
        update('RenewalRequests', renewal_id, PaymentRequestId=payment_id)

    # Three most recent completed slots for each group, with explicit attendance.
    for index, group in enumerate(groups):
        start = dt.time(12 + index // 2)
        end = dt.time(12 + index // 2, 55)
        roster = [(i, e) for i, _, e, _, _ in players if e['TrainingGroupId'] == group['Id']]
        for back in (1, 3, 5):
            date = day - dt.timedelta(days=back)
            session_id = stable_id(f'nov05/session/{index}/{back}')
            at = dt.datetime.combine(date, dt.time(20), dt.timezone.utc)
            insert('TrainingSessions', dict(session_template, Id=session_id, IsActive=True,
                TrainingGroupId=group['Id'], BranchId=group['BranchId'], SportId=group['SportId'],
                AgeCategoryId=group['AgeCategoryId'], SessionDate=date, StartTime=start, EndTime=end,
                Status='Held', RecurringScheduleId=None, CreatedByUserId=owner['UserId'],
                Notes='حصة من سجل الديمو', CreatedAtUtc=at, UpdatedAtUtc=at))
            for i, enrollment in roster:
                if i >= 130:
                    continue
                insert('PlayerAttendances', row(f'nov05/attendance/{index}/{back}/{i}',
                    TrainingSessionId=session_id, TrainingGroupId=group['Id'],
                    SportEnrollmentId=enrollment['Id'], Status='Absent' if i < 10 else 'Present',
                    Notes=None, RecordedByUserId=admin['UserId'], ConsumedSubscriptionPeriodId=None))
        # One editable, dated session per group for the staff demo.
        insert('TrainingSessions', dict(session_template, Id=stable_id(f'nov05/today/{index}'),
            IsActive=True, TrainingGroupId=group['Id'], BranchId=group['BranchId'], SportId=group['SportId'],
            AgeCategoryId=group['AgeCategoryId'], SessionDate=day, StartTime=start, EndTime=end,
            Status='Scheduled', RecurringScheduleId=None, CreatedByUserId=owner['UserId'],
            Notes='تدريب اليوم', CreatedAtUtc=now, UpdatedAtUtc=now))

    # Posted branch expenses with exact penny allocations. November is month-to-date.
    for month in range(6, day.month + 1):
        first = dt.date(day.year, month, 1)
        last = min(day, dt.date(day.year, month, calendar.monthrange(day.year, month)[1]))
        for index, branch in enumerate(branches):
            branch_groups = [g for g in groups if g['BranchId'] == branch['Id']]
            for category, base in (('إيجار الملاعب', 5400), ('كهرباء ومرافق', 1050), ('رواتب المدربين', 7800)):
                key = f'nov05/expense/{month}/{index}/{base}'
                fingerprint = hashlib.sha256(key.encode()).hexdigest()
                amount = (base + index * 160 + month * 45) if month != day.month else (base + index * 160) // 5
                paid = dt.datetime.combine(last, dt.time(1), dt.timezone.utc)
                expense_id = stable_id(key)
                insert('OperatingExpenses', row(key, BranchId=branch['Id'], Scope=3, TrainingGroupId=None,
                    ResourceId=None, SportId=None, Category=category, PeriodFrom=first, PeriodTo=last,
                    PaidOn=last, Amount=amount, Currency='EGP', Note='مصروف اصطناعي لديمو الأكاديمية',
                    Status=3, CreatedByUserId=owner['UserId'], ApprovedByUserId=owner['UserId'],
                    ApprovedAtUtc=paid, PostedByUserId=owner['UserId'], PostedAtUtc=paid,
                    PostRequestKey=key, PostQuoteHash=fingerprint, RequestKey=key, RequestHash=fingerprint))
                part, remainder = divmod(amount * 100, len(branch_groups))
                for number, group in enumerate(branch_groups):
                    insert('OperatingExpenseAllocations', row(f'{key}/allocation/{number}', ExpenseId=expense_id,
                        TrainingGroupId=group['Id'], Weight=1,
                        Amount=Decimal(part + (remainder if number == 0 else 0)) / 100,
                        Currency='EGP', ServiceKind='Training'))

    fixtures = rows('AcademyMatchs')
    if fixtures:
        upcoming = fixtures[0]
        update('AcademyMatchs', upcoming['Id'], MatchDate=day, StartTime=dt.time(18, 30),
               Opponent='نجوم شرق القاهرة', Venue='ملعب مدينتي')
        team = rows('MatchTeams', '"Id"=%s', (upcoming['TeamId'],))[0]
        participants = rows('MatchParticipants', '"MatchId"=%s', (upcoming['Id'],))
        for index, (opponent, own, other, days) in enumerate((
            ('براعم المستقبل', 4, 1, 6), ('نجوم العاصمة', 0, 2, 13), ('فريق الرواد', 2, 2, 20),
        )):
            key = f'nov05/match/{index}'
            match_id = stable_id(key)
            fingerprint = hashlib.sha256(key.encode()).hexdigest()
            result_date = day - dt.timedelta(days=days)
            stamp = dt.datetime.combine(result_date, dt.time(18), dt.timezone.utc)
            insert('AcademyMatchs', dict(upcoming, Id=match_id, Opponent=opponent,
                MatchDate=result_date, Venue='ملعب مدينتي', Notes=None, OurScore=own, OpponentScore=other,
                PublishedAtUtc=stamp, PublishedFingerprint=fingerprint, ResultFinalized=True, ResultRevision=1,
                ResultPublishedAtUtc=stamp, ResultFingerprint=fingerprint, CreatedAtUtc=stamp, UpdatedAtUtc=stamp))
            for participant in participants:
                insert('MatchParticipants', dict(participant, Id=stable_id(f'{key}/{participant["Id"]}'),
                    MatchId=match_id, CreatedAtUtc=stamp, UpdatedAtUtc=stamp))
            for goal in range(1, own + 1):
                scorer = participants[(goal - 1) % len(participants)]['PlayerId'] if participants and goal <= 2 else None
                insert('MatchGoalSlots', row(f'{key}/goal/{goal}', MatchId=match_id, Ordinal=goal,
                    PlayerId=scorer, Kind='Player' if scorer else 'Unknown'))
            if team['CoachMembershipId'] is not None:
                insert('MatchCoachAssignments', row(f'{key}/coach', MatchId=match_id,
                    CoachMembershipId=team['CoachMembershipId'], AssignedByMembershipId=owner['Id'],
                    Sequence=1, Source='DemoFixture'))
            insert('MatchResultChanges', row(f'{key}/result', MatchId=match_id,
                ActorMembershipId=owner['Id'], Revision=1, Before={},
                After={'ourScore': own, 'opponentScore': other, 'finalized': True}))
