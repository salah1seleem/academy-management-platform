using Academy.Api.Auth;
using Academy.Infrastructure.Attendance;
using Academy.Infrastructure.Operations;
using Academy.Infrastructure.People;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Structure;
using Academy.Infrastructure.Subscriptions;
using Academy.Infrastructure.Tenancy;
using Academy.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Demo;

// Opt-in historic synthetic source for reviewing post-contract commission. Never auto-approves a payable or payout.
public static class PostContractPrivateDemoSeed
{
    public static Guid Id(string key) => FootballDemoSeed.Id("post-contract-private-demo/" + key);
    public static async Task Seed(FoundationDbContext db, IHostEnvironment env, DemoOptions options,TimeProvider clock,UserManager<ApplicationUser> users)
    {
        if (!env.IsEnvironment("Demo") || !options.SeedEnabled || options.SeedProfile != "Football") throw new InvalidOperationException("مصادر الخاص التجريبية تتطلب Demo وFootball صريحين.");
        var academy = FootballDemoSeed.AcademyId;
        if (!await db.Academies.AnyAsync(a => a.Id == academy && a.Slug == FootballDemoSeed.Slug && a.EnglishName == FootballDemoSeed.Marker)) throw new InvalidOperationException("لا تهيئة مالية لبيانات غير معروفة.");
        await using var tx = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({"post-contract-private-demo-v1"},0))");
        var package = await db.PrivateServicePackages.SingleOrDefaultAsync(p => p.Id == Id("package"));
        if (package is not null)
        {
            if (package.AcademyId != academy || package.DocumentId != Id("document") || package.SessionCount != 8) throw new InvalidOperationException("مرجع مصادر الخاص التجريبي محجوز بسياق مختلف.");
            return; // Owner allocations/credits/decisions are never replayed or overwritten.
        }
        var today=await Academy.Api.Slice4.AttendanceCalendar.TodayAsync(academy,db,clock);var end=new DateOnly(today.Year,today.Month,1).AddDays(-1);var stamp=clock.GetUtcNow();
        var owner=FootballDemoSeed.Id("member/owner");var actor=FootballDemoSeed.Id("user/owner");var branch=FootballDemoSeed.Id("branch/0");var sport=FootballDemoSeed.Id("sport");var coach=Id("member");
        var user=await users.FindByIdAsync(Id("user").ToString());
        if(user is null){user=new ApplicationUser{Id=Id("user"),UserName="closed.coach.football@example.test",Email="closed.coach.football@example.test",EmailConfirmed=true,DisplayName="مدرب عقد منتهٍ — عرض اصطناعي",PhoneNumber=FootballDemoSeed.Phone(980),PhoneNumberConfirmed=true,CreatedAtUtc=stamp,UpdatedAtUtc=stamp};var result=await users.CreateAsync(user,options.StaffPassword);if(!result.Succeeded)throw new InvalidOperationException("تعذر إنشاء هوية العرض المحجوزة.");}
        else if(user.Email!="closed.coach.football@example.test"||user.PhoneNumber!=FootballDemoSeed.Phone(980))throw new InvalidOperationException("هوية العرض محجوزة ببيانات مختلفة؛ لم نغيرها.");
        if(await db.Employees.AnyAsync(e=>e.Id==Id("employee"))||await db.AcademyMemberships.AnyAsync(m=>m.Id==coach))throw new InvalidOperationException("مصدر العرض جزئي أو معدل؛ لم نكتب فوقه.");
        db.AcademyMemberships.Add(new AcademyMembership{Id=coach,AcademyId=academy,UserId=user.Id,Role=AcademyRole.Coach,CreatedAtUtc=stamp,UpdatedAtUtc=stamp});
        db.Employees.Add(new Employee{Id=Id("employee"),AcademyId=academy,MembershipId=coach,Name=user.DisplayName,Phone=user.PhoneNumber!,JobTitle="مدرب عرض بعقد سابق",BranchId=branch,MonthlySalary=3000,Currency="EGP",ContractStart=new(2000,1,1),ContractEnd=end,CreatedAtUtc=stamp,UpdatedAtUtc=stamp});
        await db.SaveChangesAsync();
        var agreement=new CoachCompensationAgreement{Id=Id("agreement"),AcademyId=academy,EmployeeId=Id("employee"),CoachMembershipId=coach,BranchId=branch,SportId=sport,EffectiveFrom=end,EffectiveTo=end,BaseSalarySnapshot=3000,Currency="EGP",VariableMethod="Percent",VariableValue=40,Eligibility="DeliveredCollected",BaseCoverage="GroupOnly",CreatedByMembershipId=owner,IdempotencyKey="post-contract-private-demo-v1",CreatedAtUtc=stamp,UpdatedAtUtc=stamp};
        db.CoachCompensationAgreements.Add(agreement);await db.SaveChangesAsync();
        db.CoachCompensationApprovals.Add(new(){Id=Id("approval"),AcademyId=academy,AgreementId=agreement.Id,EmployeeId=agreement.EmployeeId,BranchId=branch,SportId=sport,Revision=1,ApprovedByMembershipId=owner,ApprovedAtUtc=stamp,CreatedAtUtc=stamp,UpdatedAtUtc=stamp});await db.SaveChangesAsync();
        var player = await db.Players.SingleAsync(p => p.Id == FootballDemoSeed.Id("player/0") && p.AcademyId == academy);
        var age = await db.AgeCategories.Where(a => a.AcademyId == academy && a.IsActive).Select(a => a.Id).FirstAsync();
        db.TrainingGroups.Add(new() { Id = Id("group"), AcademyId = academy, ArabicName = "خاص — تسوية بعد نهاية عقد اصطناعي", BranchId = branch, SportId = sport, AgeCategoryId = age, Capacity = 1, RegistrationFormat = TrainingRegistrationFormat.Private, FormatReviewedByUserId = actor, FormatReviewedAtUtc = stamp, CreatedAtUtc = stamp, UpdatedAtUtc = stamp });
        db.StaffGroupAssignments.Add(new() { Id = Id("assignment"), AcademyId = academy, AcademyMembershipId = coach, TrainingGroupId = Id("group"), CreatedAtUtc = stamp, UpdatedAtUtc = stamp });
        db.SportEnrollments.Add(new() { Id = Id("enrollment"), AcademyId = academy, PlayerId = player.Id, SportId = sport, BranchId = branch, TrainingGroupId = Id("group"), GroupAssignedFrom = end, RegistrationFormat = TrainingRegistrationFormat.Private, CreatedAtUtc = stamp, UpdatedAtUtc = stamp });
        var plan = new SubscriptionPlan { Id = Id("plan"), AcademyId = academy, SportId = sport, ArabicName = "8 حصص خاصة — عقد منتهٍ اصطناعي", PlanType = SubscriptionPlanType.Sessions, TrainingFormat = TrainingFormat.Private, SessionCount = 8, Price = 2000, Currency = "EGP", Revision = 1, CreatedAtUtc = stamp, UpdatedAtUtc = stamp };
        db.SubscriptionPlans.Add(plan);
        db.ReceivableDocuments.Add(new() { Id = Id("document"), AcademyId = academy, SportEnrollmentId = Id("enrollment"), PlayerId = player.Id, PlayerNameSnapshot = player.ArabicName, BranchId = branch, PlanId = plan.Id, PlanNameSnapshot = plan.ArabicName, PlanPriceSnapshot = 2000, Description = "مثال اصطناعي: 8 حصص، تنفيذ2، تحصيل مخصص1000، أجر40%", Amount = 2000, Currency = "EGP", DueDate = end, Source = ReceivableSource.ManualCharge, SourceKey = "post-contract-private-demo-v1", ContentHash = "DEMO-PRIVATE-V1", ActorUserId = actor, CreatedAtUtc = stamp, UpdatedAtUtc = stamp });
        // Reuse existing confirmed payment/collection/receipt records for a partial
        // synthetic invoice payment. Deliberately no subscription activation/reset.
        db.RenewalRequests.Add(new() { Id = Id("renewal"), AcademyId = academy, SportEnrollmentId = Id("enrollment"), SportId = sport, SubscriptionPlanId = plan.Id, RequestedByUserId = actor, RequestedAtUtc = stamp, OriginalAmount = 1000, FinalAmount = 1000, AmountExpected = 1000, Currency = "EGP", Status = RenewalRequestStatus.Paid, IdempotencyKey = "post-contract-private-demo-v1", CreatedAtUtc = stamp, UpdatedAtUtc = stamp });
        db.PaymentRequests.Add(new() { Id = Id("payment"), AcademyId = academy, RenewalRequestId = Id("renewal"), Amount = 1000, Currency = "EGP", Status = PaymentRequestStatus.Confirmed, Provider = "InternalTest", ProviderEnvironment = "Demo", ProviderReference = "post-contract-private-demo-v1", CheckoutReference = "post-contract-private-demo-v1", ConfirmedAtUtc = stamp, CreatedAtUtc = stamp, UpdatedAtUtc = stamp });
        db.Collections.Add(new() { Id = Id("collection"), AcademyId = academy, SportEnrollmentId = Id("enrollment"), RenewalRequestId = Id("renewal"), PaymentRequestId = Id("payment"), Amount = 1000, Currency = "EGP", PaymentMethod = "InternalTest", Provider = "InternalTest", ProviderReference = "post-contract-private-demo-v1", ConfirmedAtUtc = stamp, ConfirmedBy = actor.ToString(), CreatedAtUtc = stamp, UpdatedAtUtc = stamp });
        db.Receipts.Add(new() { Id = Id("receipt"), AcademyId = academy, CollectionId = Id("collection"), ReceiptNumber = "DEMO-CLOSED-PRIVATE-1000", PlayerId = player.Id, SportEnrollmentId = Id("enrollment"), SubscriptionPlanId = plan.Id, PlayerNameSnapshot = player.ArabicName, SportNameSnapshot = "كرة القدم", PlanNameSnapshot = plan.ArabicName, OriginalAmount = 1000, FinalAmount = 1000, Amount = 1000, Currency = "EGP", PaidAtUtc = stamp, PaymentMethod = "InternalTest", ProviderReference = "post-contract-private-demo-v1", CreatedAtUtc = stamp, UpdatedAtUtc = stamp });
        db.ReceivableMovements.Add(new() { Id = Id("allocation"), AcademyId = academy, DocumentId = Id("document"), Kind = ReceivableMovementKind.PaymentAllocation, Amount = 1000, Currency = "EGP", CollectionId = Id("collection"), Reason = "تخصيص تحصيل جزئي اصطناعي فقط", SourceKey = "post-contract-private-demo-v1", ContentHash = "DEMO-PRIVATE-V1", ActorUserId = actor, CreatedAtUtc = stamp, UpdatedAtUtc = stamp });
        await db.SaveChangesAsync();
        db.PrivateServicePackages.Add(new() { Id = Id("package"), AcademyId = academy, DocumentId = Id("document"), SportEnrollmentId = Id("enrollment"), BranchId = branch, SportId = sport, SessionCount = 8, TrainingFormat = "Private", Currency = "EGP", ReviewedByMembershipId = owner, CreatedAtUtc = stamp, UpdatedAtUtc = stamp });
        for (var i = 1; i <= 2; i++)
        {
            var day = end; var now = stamp;
            db.TrainingSessions.Add(new() { Id = Id("session/" + i), AcademyId = academy, TrainingGroupId = Id("group"), BranchId = branch, SportId = sport, AgeCategoryId = age, SessionDate = day, StartTime = new(10+i,0), EndTime = new(11+i,0), Source = TrainingSessionSource.Manual, CreatedByUserId = actor, CreatedAtUtc = stamp, UpdatedAtUtc = stamp });
            db.PrivateServiceDeliveries.Add(new() { Id = Id("delivery/" + i), AcademyId = academy, PackageId = Id("package"), TrainingSessionId = Id("session/" + i), SportEnrollmentId = Id("enrollment"), EmployeeId = agreement.EmployeeId, CoachMembershipId = coach, AgreementId = agreement.Id, BranchId = branch, SportId = sport, Ordinal = i, ServiceDate = day, StartTime = new(10+i,0), EndTime = new(11+i,0), ApprovedByMembershipId = owner, CreatedAtUtc = now, UpdatedAtUtc = now });
        }
        await db.SaveChangesAsync(); await tx.CommitAsync();
    }
}
