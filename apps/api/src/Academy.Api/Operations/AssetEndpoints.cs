using System.Security.Claims;
using Academy.Api.Auth;
using Academy.Api.Configuration;
using Academy.Api.Slice3;
using Academy.Infrastructure.Operations;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Tenancy;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Operations;

public static class AssetEndpoints
{
    public static void MapAssetEndpoints(this WebApplication app)
    {
        var api = app.MapGroup("/api/v1/assets").RequireAuthorization(AcademyPermissions.TenantAccess);
        api.MapPost("/upload", Upload).AddEndpointFilter<CsrfFilter>();
        api.MapGet("/{id:guid}", Download);
        api.MapGet("/gallery", Gallery);
    }
    internal static string Root(IConfiguration config, IWebHostEnvironment env) => Path.GetFullPath(config["Media:PrivateRoot"] ?? Path.Combine(env.ContentRootPath, "local-uploads"));
    private static Guid Actor(ClaimsPrincipal user) => Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
    private static Task<bool> Linked(FoundationDbContext db, Guid academy, Guid user, Guid player) => db.GuardianPlayerLinks.AnyAsync(x => x.AcademyId == academy && x.Guardian.UserId == user && x.PlayerId == player && x.IsActive && x.Guardian.IsActive && x.Player.IsActive);
    private static Task<bool> Assigned(FoundationDbContext db, Guid academy, Guid user, Guid group) => db.StaffGroupAssignments.AnyAsync(x => x.AcademyId == academy && x.AcademyMembership.UserId == user && x.TrainingGroupId == group && x.IsActive && x.AcademyMembership.IsActive && x.TrainingGroup.IsActive);

    private static async Task<IResult> Upload(HttpContext context, CurrentTenant tenant, FoundationDbContext db, IAuthorizationService authorization, IConfiguration config, IWebHostEnvironment env, ISubscriptionClock clock, TimeProvider time)
    {
        if (!context.Request.HasFormContentType) return Results.BadRequest(new { message = "اختر ملف صورة أو فيديو." });
        IFormCollection form;
        try { form = await context.Request.ReadFormAsync(); }
        catch (InvalidDataException) { return Results.BadRequest(new { message = "حجم الملف أكبر من المسموح." }); }
        var file = form.Files.GetFile("file"); var purpose = form["purpose"].ToString();
        var t = (await tenant.ResolveAsync())!; var actor = Actor(context.User);
        Guid? playerId = Guid.TryParse(form["playerId"], out var player) ? player : null;
        Guid? groupId = Guid.TryParse(form["groupId"], out var group) ? group : null;
        if (file is null || file.Length < 12 || file.Length > 50 * 1024 * 1024 || form.Files.Count != 1 || form["caption"].ToString().Length > 300)
            return Results.BadRequest(new { message = "ارفع ملفًا واحدًا لا يتجاوز 50 ميجابايت." });
        if (purpose is not ("Photo" or "Media" or "Proof" or "News" or "StaffPhoto" or "Campaign")) return Results.BadRequest();
        if(purpose=="Media"&&!await context.RequestServices.GetRequiredService<AcademyConfigurationService>().Enabled(t.AcademyId,"media"))return Results.Forbid();
        if(purpose=="Campaign"&&(!await CommunityAds.FullOwner(db,t)||!await context.RequestServices.GetRequiredService<AcademyConfigurationService>().Enabled(t.AcademyId,"community_ads")||playerId.HasValue||groupId.HasValue||file.Length>10*1024*1024))return Results.Forbid();
        if (purpose == "Proof")
        {
            if (t.Role != AcademyRole.Guardian || playerId.HasValue || groupId.HasValue) return Results.Forbid();
        }
        else
        {
            var permission = purpose == "StaffPhoto" ? AcademyPermissions.PayrollManage : purpose == "Photo" ? AcademyPermissions.PeopleManage : AcademyPermissions.PlayerMediaManage;
            var staffAllowed = purpose=="Campaign" || (await authorization.AuthorizeAsync(context.User, permission)).Succeeded;
            if (!staffAllowed && !(purpose == "Media" && t.Role == AcademyRole.Coach && groupId.HasValue && !playerId.HasValue && await Assigned(db, t.AcademyId, actor, groupId.Value))) return Results.Forbid();
            if (purpose == "News" && (playerId.HasValue || groupId.HasValue)) return Results.BadRequest();
            if (purpose == "StaffPhoto" && (playerId.HasValue || groupId.HasValue)) return Results.BadRequest();
            if (purpose == "Photo" && (!playerId.HasValue || groupId.HasValue)) return Results.BadRequest();
            if (purpose == "Media" && playerId.HasValue == groupId.HasValue) return Results.BadRequest(new { message = "اختر لاعبًا أو مجموعة واحدة." });
            if (playerId.HasValue && !await db.Players.AnyAsync(x => x.AcademyId == t.AcademyId && x.Id == playerId)) return Results.NotFound();
            if (groupId.HasValue && !await db.TrainingGroups.AnyAsync(x => x.AcademyId == t.AcademyId && x.Id == groupId && x.IsActive)) return Results.NotFound();
        }
        await using var source = file.OpenReadStream();
        var header = new byte[12]; await source.ReadExactlyAsync(header, context.RequestAborted);
        var detected = DetectType(header);
        if (detected is null || detected != file.ContentType || ((purpose is "Photo" or "Proof" or "News" or "StaffPhoto" or "Campaign") && !detected.StartsWith("image/")))
            return Results.BadRequest(new { message = "الأنواع المدعومة: JPEG وPNG وWebP، وMP4 وMOV." });
        if (detected.StartsWith("image/") && file.Length > 8 * 1024 * 1024) return Results.BadRequest(new { message = "الصورة لا تتجاوز 8 ميجابايت." });
        ProcessedMedia? processed = null;
        try
        {
            // Expensive decoding precedes locks. Live authority/audience are checked again below
            // before publishing; a permission or consent revoked during processing still wins.
            if (purpose is "Media" or "Photo")
            {
                await using var original = file.OpenReadStream();
                processed = await context.RequestServices.GetRequiredService<PrivateMediaProcessor>().Prepare(original, detected, Root(config, env), context.RequestAborted);
            }
        }
        catch (MediaProcessingException error) { return Results.Json(new { message = error.Message }, statusCode: error.Status); }
        using var prepared = processed;
        await using var transaction=purpose is "Media" or "Photo"?await db.Database.BeginTransactionAsync():null;
        if(purpose is "Media" or "Photo")
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"grants:{t.AcademyId}:{t.MembershipId}"},0))");
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM \"AcademyMemberships\" WHERE \"Id\"={t.MembershipId} AND \"AcademyId\"={t.AcademyId} FOR SHARE");
            var live=await db.AcademyMemberships.AsNoTracking().SingleOrDefaultAsync(m=>m.Id==t.MembershipId&&m.UserId==actor&&m.IsActive&&m.User.IsActive&&m.Academy.IsActive&&(!m.PrimaryMembershipId.HasValue||db.AcademyMemberships.Any(p=>p.Id==m.PrimaryMembershipId&&p.IsActive&&p.User.IsActive)));
            if(live==null||live.Role!=t.Role)return Results.Forbid();
            if(t.Role==AcademyRole.AcademyAdmin&&!live.AllBranches)
            {
                var branches=await db.MembershipBranchAccesses.Where(a=>a.MembershipId==t.MembershipId&&a.IsActive&&db.Branches.Any(b=>b.Id==a.BranchId&&b.IsActive)).Select(a=>a.BranchId).ToArrayAsync();
                if(!db.BranchScopeRestricted||db.RequestBranchIds.Any(b=>!branches.Contains(b)))return Results.Forbid();
            }
            if(!live.AllSports)
            {
                var sports=await db.MembershipSportAccesses.Where(a=>a.MembershipId==t.MembershipId&&a.IsActive).Select(a=>a.SportId).ToArrayAsync();
                if(!db.SportScopeRestricted||db.RequestSportIds.Any(s=>!sports.Contains(s)))return Results.Forbid();
            }
            if(playerId.HasValue&&!await db.Players.AnyAsync(p=>p.Id==playerId))return Results.NotFound();
            if(groupId.HasValue&&!await db.TrainingGroups.AnyAsync(g=>g.Id==groupId&&g.IsActive))return Results.NotFound();
        }
        if(purpose=="Photo"&&!(await authorization.AuthorizeAsync(context.User,context,AcademyPermissions.PeopleManage)).Succeeded)return Results.Forbid();
        if(purpose=="Media")
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({"config:"+t.AcademyId},0))");
            if(groupId.HasValue)
            {
                foreach(var key in new[]{"config:"+t.AcademyId,$"timetable:{t.AcademyId}:{groupId.Value}",$"group-capacity:{t.AcademyId}:{groupId.Value}"})await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({key},0))");
                if(!await GroupMediaPrivacy.Staff(t,db,context,groupId.Value,true))return Results.Forbid();
            }
            else if(t.Role is not(AcademyRole.AcademyOwner or AcademyRole.AcademyAdmin)||!(await authorization.AuthorizeAsync(context.User,context,AcademyPermissions.PlayerMediaManage)).Succeeded)return Results.Forbid();
            await GroupMediaPrivacy.Lock(db,t.AcademyId);
        }
        var now = purpose=="Media"?time.GetUtcNow():clock.UtcNow;
        var asset = new PrivateAsset { Id = Guid.NewGuid(), AcademyId = t.AcademyId, UploadedByUserId = actor, ContentType = prepared?.ContentType ?? detected, StorageName = Guid.NewGuid().ToString("N"), Length = prepared?.Length ?? file.Length, Purpose = purpose, PlayerId = playerId, TrainingGroupId = groupId, Caption = form["caption"].ToString().Trim(), CreatedAtUtc = now, UpdatedAtUtc = now };
        if(purpose=="Media")
        {
            var problem=await GroupMediaPrivacy.Prepare(asset,form,db,t,context,time);
            if(problem!=null)return Results.UnprocessableEntity(new{message=problem});
            if(!await context.RequestServices.GetRequiredService<PrivateMediaLifecycle>().Admit(asset))return Results.Json(new{message="لا يمكن نشر الملف: راجع إتاحة الوسائط وحصة تخزين الأكاديمية. لا تُحرر الملفات المسحوبة المساحة قبل إزالتها فعليًا."},statusCode:409);
        }
        var root = Root(config, env); Directory.CreateDirectory(root); var path = Path.Combine(root, asset.StorageName);
        if (prepared != null) File.Move(prepared.Path, path);
        else
        {
            await using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            await output.WriteAsync(header, context.RequestAborted); await source.CopyToAsync(output, context.RequestAborted);
        }
        db.PrivateAssets.Add(asset);
        if (purpose == "Photo")
        {
            var row = await db.Players.SingleAsync(x => x.AcademyId == t.AcademyId && x.Id == playerId);
            row.PhotoReference = $"/api/v1/assets/{asset.Id}"; row.UpdatedAtUtc = now;
        }
        try { await db.SaveChangesAsync(); if(transaction!=null)await transaction.CommitAsync(); }
        catch { File.Delete(path); throw; }
        return Results.Created($"/api/v1/assets/{asset.Id}", new { asset.Id, url = $"/api/v1/assets/{asset.Id}", asset.ContentType,asset.MediaPolicyVersion,asset.MediaEventOn,asset.RetainUntilUtc,asset.DeleteOnExpiry });
    }

    internal static string? DetectType(byte[] b)
    {
        if (b[0] == 0xff && b[1] == 0xd8 && b[2] == 0xff) return "image/jpeg";
        if (b.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })) return "image/png";
        if (System.Text.Encoding.ASCII.GetString(b, 0, 4) == "RIFF" && System.Text.Encoding.ASCII.GetString(b, 8, 4) == "WEBP") return "image/webp";
        if (System.Text.Encoding.ASCII.GetString(b, 4, 4) == "ftyp") return System.Text.Encoding.ASCII.GetString(b, 8, 4) == "qt  " ? "video/quicktime" : "video/mp4";
        return null;
    }

    private static async Task<bool> CanRead(PrivateAsset asset, TenantMembership t, ClaimsPrincipal principal, FoundationDbContext db, IAuthorizationService authorization, ISubscriptionClock clock,TimeProvider time)
    {
        var user = Actor(principal); var policy = asset.Purpose switch { "StaffPhoto" => AcademyPermissions.PayrollRead, "Photo" => AcademyPermissions.PeopleManage, "Proof" => AcademyPermissions.PaymentRead, _ => AcademyPermissions.PlayerMediaRead };
        if ((t.Role is AcademyRole.AcademyOwner or AcademyRole.AcademyAdmin) && (await authorization.AuthorizeAsync(principal, policy)).Succeeded) return true;
        if (!asset.IsActive||asset.Purpose=="Media"&&await db.PrivateMediaWithdrawals.AnyAsync(w=>w.AssetId==asset.Id)) return false;
        if (asset.Purpose == "StaffPhoto") return false;
        if (asset.Purpose == "Proof") return asset.UploadedByUserId == user;
        var now = clock.UtcNow;
        if (asset.Purpose == "News") return await db.AcademyNews.AnyAsync(x => x.AcademyId == t.AcademyId && x.ImageAssetId == asset.Id && x.IsActive && x.PublishedAtUtc <= now && x.PublishedAtUtc.Value.AddDays(x.VisibleDays) > now);
        if (asset.PlayerId.HasValue && (asset.Purpose!="Media"||asset.MediaPolicyVersion==1) && await Linked(db, t.AcademyId, user, asset.PlayerId.Value)) return true;
        if (asset.Purpose == "Media" && asset.TrainingGroupId.HasValue)
        {
            if(t.Role==AcademyRole.Coach)return await GroupMediaPrivacy.Assigned(db,t,asset.TrainingGroupId.Value);
            if(t.Role!=AcademyRole.Guardian||!await GroupMediaPrivacy.Shareable(db).AnyAsync(a=>a.Id==asset.Id))return false;
            var today=await Academy.Api.Slice4.AttendanceCalendar.TodayAsync(t.AcademyId,db,time);
            return await GroupMediaPrivacy.CurrentAudience(db,asset.TrainingGroupId.Value,today).AnyAsync(x=>x.AssetId==asset.Id&&GroupMediaPrivacy.Links(db).Any(l=>l.PlayerId==x.PlayerId&&l.Guardian.UserId==user));
        }
        return false;
    }
    private static async Task<IResult> Download(Guid id,string? access, HttpContext context, CurrentTenant tenant, FoundationDbContext db, IAuthorizationService authorization, IConfiguration config, IWebHostEnvironment env, ISubscriptionClock clock, AcademyConfigurationService features,PrivateMediaLinks links)
    {
        context.Response.Headers.CacheControl="private, no-store";
        var t = (await tenant.ResolveAsync())!;
        var asset = await db.PrivateAssets.AsNoTracking().SingleOrDefaultAsync(x => x.AcademyId == t.AcademyId && x.Id == id);
        if(asset?.Purpose=="Media"&&(asset.RetainUntilUtc<=context.RequestServices.GetRequiredService<TimeProvider>().GetUtcNow()||await db.PrivateMediaErasures.AnyAsync(e=>e.AssetId==id)))return Results.NotFound();
        if(asset?.Purpose=="Campaign")
        {
            var allowed=await CommunityAds.FullOwner(db,t);
            if(!allowed&&t.Role==AcademyRole.Guardian)
            {
                var revisions=await db.CommunityCampaignRevisions.Where(r=>r.ImageAssetId==id).Select(r=>r.Id).ToArrayAsync();
                foreach(var revision in revisions)if(await CommunityAds.AuthorizedTarget(db,features,t,Actor(context.User),revision,context.RequestServices.GetRequiredService<TimeProvider>(),clock.Today)!=null){allowed=true;break;}
            }
            if(!allowed||!asset.IsActive)return Results.NotFound();
            var campaignPath=Path.Combine(Root(config,env),asset.StorageName);if(!File.Exists(campaignPath))return Results.NotFound();context.Response.Headers.CacheControl="private, no-store";context.Response.Headers["X-Content-Type-Options"]="nosniff";return Results.File(campaignPath,asset.ContentType);
        }
        if (asset is not null && asset.Purpose is "Media" or "News" && !await features.Enabled(t.AcademyId, asset.Purpose == "News" ? "news" : "media")) return Results.NotFound();
        if (asset is null || !await CanRead(asset, t, context.User, db, authorization, clock,context.RequestServices.GetRequiredService<TimeProvider>())) return Results.NotFound();
        var path = Path.Combine(Root(config, env), asset.StorageName); if (!File.Exists(path)) return Results.NotFound();
        if(asset.Purpose=="Media")
        {
            // Existing authenticated entry URLs remain usable by Web/native clients. Only the
            // redirected, short-lived URL serves bytes; it is still useless without live authority.
            var revision=await db.AcademyMemberships.AsNoTracking().Where(m=>m.Id==t.MembershipId&&m.AcademyId==t.AcademyId&&m.UserId==Actor(context.User)&&m.Role==t.Role&&m.IsActive&&m.User.IsActive&&m.Academy.IsActive).Select(m=>(int?)m.ContextRevision).SingleOrDefaultAsync();
            if(revision==null)return Results.NotFound();
            var scope=new MediaLinkContext(t.AcademyId,id,Actor(context.User),t.MembershipId,t.Role.ToString(),revision.Value,context.User.FindFirstValue("mobile_session_id")??"web");
            context.Response.Headers["Referrer-Policy"]="no-referrer";
            context.Response.Headers["Cross-Origin-Resource-Policy"]="same-origin";
            if(access==null)return Results.Redirect($"/api/v1/assets/{id}?access={Uri.EscapeDataString(links.Issue(scope))}");
            if(!links.Allows(access,scope))return Results.NotFound();
            if(HttpMethods.IsGet(context.Request.Method)&&!await context.RequestServices.GetRequiredService<PrivateMediaLifecycle>().ReserveDownload(t.AcademyId,Actor(context.User),asset.Length))return Results.Json(new{message="بلغت حد تنزيل الوسائط اليومي. يعود الحد مع بداية يوم UTC التالي؛ لا يتغير الاشتراك أو حق الوصول الدائم."},statusCode:429);
            // Open within the authorized request, not later by pathname: a concurrent purge
            // between existence check and open must be a 404, not an unhandled file-result 500.
            FileStream media;
            try{media=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read|FileShare.Delete);}
            catch(FileNotFoundException){return Results.NotFound();}catch(DirectoryNotFoundException){return Results.NotFound();}
            context.Response.Headers["X-Content-Type-Options"]="nosniff";
            return Results.File(media,asset.ContentType,enableRangeProcessing:true);
        }
        context.Response.Headers.CacheControl = "private, no-store"; context.Response.Headers["X-Content-Type-Options"] = "nosniff";
        return Results.File(path, asset.ContentType, enableRangeProcessing: true);
    }
    private static async Task<IResult> Gallery(Guid? playerId, Guid? groupId,bool? includeWithdrawn, CurrentTenant tenant, ClaimsPrincipal principal, FoundationDbContext db, IAuthorizationService authorization,TimeProvider time,HttpContext context)
    {
        var t = (await tenant.ResolveAsync())!; var user = Actor(principal);
        context.Response.Headers.CacheControl="private, no-store";
        var query = db.PrivateAssets.AsNoTracking().Where(x => x.AcademyId == t.AcademyId && x.Purpose == "Media" && x.IsActive);
        var staff=t.Role is AcademyRole.AcademyOwner or AcademyRole.AcademyAdmin;
        var now=time.GetUtcNow();if(!staff)query=query.Where(x=>(x.RetainUntilUtc==null||x.RetainUntilUtc>now)&&!db.PrivateMediaErasures.Any(e=>e.AssetId==x.Id));
        if(!staff||includeWithdrawn!=true)query=query.Where(x=>!db.PrivateMediaWithdrawals.Any(w=>w.AssetId==x.Id));
        if (t.Role == AcademyRole.Guardian)
        {
            if (!playerId.HasValue || !await Linked(db, t.AcademyId, user, playerId.Value)) return Results.NotFound();
            var today=await Academy.Api.Slice4.AttendanceCalendar.TodayAsync(t.AcademyId,db,time);
            var shareable=GroupMediaPrivacy.Shareable(db);
            query = query.Where(x => x.MediaPolicyVersion==1&&(x.PlayerId == playerId || x.TrainingGroupId!=null&&db.TrainingGroups.Any(g=>g.Id==x.TrainingGroupId&&g.IsActive)&&shareable.Any(a=>a.Id==x.Id)&&db.PrivateAssetAudiences.Any(a=>a.AssetId==x.Id&&a.PlayerId==playerId&&a.IsActive&&a.SportEnrollmentId!=null&&db.SportEnrollments.Any(e=>e.Id==a.SportEnrollmentId&&e.PlayerId==a.PlayerId&&e.IsActive&&e.Player.IsActive&&e.Branch.IsActive&&e.Sport.IsActive&&(x.TrainingSessionId!=null?FoundationDbContext.IsSessionAssigned(e.AcademyId,e.Id,x.TrainingSessionId.Value)&&(FoundationDbContext.AssignedGroupAt(e.AcademyId,e.Id,today)==x.TrainingGroupId||FoundationDbContext.AssignedGroupAt(e.AcademyId,e.Id,x.MediaEventOn!.Value)!=x.TrainingGroupId&&FoundationDbContext.AssignedGroupAt(e.AcademyId,e.Id,today)==FoundationDbContext.AssignedGroupAt(e.AcademyId,e.Id,x.MediaEventOn.Value)&&db.TrainingGroups.Any(home=>home.Id==FoundationDbContext.AssignedGroupAt(e.AcademyId,e.Id,today)&&home.IsActive)):FoundationDbContext.AssignedGroupAt(e.AcademyId,e.Id,today)==x.TrainingGroupId)))));
        }
        else if (t.Role == AcademyRole.Coach)
        {
            if (!groupId.HasValue || !await GroupMediaPrivacy.Assigned(db,t,groupId.Value)) return Results.NotFound();
            query = query.Where(x => x.TrainingGroupId == groupId);
        }
        else
        {
            if (!(await authorization.AuthorizeAsync(principal, AcademyPermissions.PlayerMediaRead)).Succeeded) return Results.Forbid();
            if (playerId.HasValue) query = query.Where(x => x.PlayerId == playerId || db.PrivateAssetAudiences.Any(e => e.AcademyId == t.AcademyId && e.PlayerId == playerId && e.AssetId == x.Id)); if (groupId.HasValue) query = query.Where(x => x.TrainingGroupId == groupId);
        }
        var canWithdraw=staff&&await MediaPublication.CanWithdraw(t,context,authorization,db);
        var canPublish=staff&&await MediaPublication.CanPublish(t,context,authorization,db);
        var canDelete=staff&&await PrivateMediaLifecycle.CanDelete(t,context,db,authorization);
        return Results.Ok(await query.OrderByDescending(x => x.CreatedAtUtc).Take(100).Select(x => new { x.Id, x.PlayerId, x.TrainingGroupId,x.TrainingSessionId, x.ContentType, x.Caption, x.CreatedAtUtc,x.MediaEventOn,x.MediaPolicyVersion,x.RetainUntilUtc,x.DeleteOnExpiry,expired=x.RetainUntilUtc!=null&&x.RetainUntilUtc<=now,erased=db.PrivateMediaErasures.Any(e=>e.AssetId==x.Id),purged=db.PrivateMediaPurges.Any(p=>p.AssetId==x.Id),canDelete,withdrawn=db.PrivateMediaWithdrawals.Any(w=>w.AssetId==x.Id),canWithdraw,canPublish,withdrawnAtUtc=staff?db.PrivateMediaWithdrawals.Where(w=>w.AssetId==x.Id).Select(w=>(DateTimeOffset?)w.CreatedAtUtc).SingleOrDefault():null,withdrawalNote=staff?db.PrivateMediaWithdrawals.Where(w=>w.AssetId==x.Id).Select(w=>w.Note).SingleOrDefault():null, url = "/api/v1/assets/" + x.Id }).ToListAsync());
    }
}
