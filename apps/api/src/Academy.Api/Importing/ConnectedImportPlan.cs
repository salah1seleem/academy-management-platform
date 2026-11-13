using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Academy.Api.Auth;
using Academy.Infrastructure.Identity;
using Academy.Infrastructure.Operations;
using Academy.Infrastructure.People;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Structure;
using Academy.Infrastructure.Subscriptions;
using Academy.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Importing;

public sealed record ConnectedMember(string Enrollment, Gender? Gender, Guid? Level, TrainingRegistrationFormat Format, Guid? Offer, Guid[] Tournaments, Guid? Team);
public sealed record ImportEffect(string Sheet,int Create,int Reuse);
public sealed partial class ConnectedImportPlan
{
    public required ImportWorkbook Legacy {get;set;}
    public required ImportReferences References {get;set;}
    public FriendlyImportInput? Friendly { get; private set; }
    public FriendlyImportHistory? History { get; private set; }
    private bool validateCompletedRows;
    public List<ImportIssue> Errors {get;}=[];
    public List<object> Creates {get;}=[];
    public List<ImportAccountLink> AccountLinks {get;}=[];
    public bool PlatformAccountReview {get;private set;}
    public List<ImportEffect> Effects {get;}=[];
    public Dictionary<string,ConnectedMember> Members {get;}=new(StringComparer.Ordinal);
    public List<RecurringSchedule> Schedules {get;}=[];
    public List<StaffGroupAssignment> Assignments {get;}=[];
    public Dictionary<Guid,TrainingGroup> Groups {get;}=[];
    public Dictionary<Guid,SubscriptionPlan> Plans {get;}=[];
    public int TournamentParticipations => Members.Values.Sum(m=>m.Tournaments.Length);
    public string ExtraHash(string enrollment, bool legacyWorksheetRows)
    {
        var member = Members[enrollment];
        if (History is null) return ImportCommitService.Hash(member);
        var history = History.Fingerprint(enrollment, legacyWorksheetRows);
        return Friendly is not null && Friendly.Extras.TryGetValue(enrollment, out var extras)
            ? ImportCommitService.Hash(new { member, history, extras.Team, extras.Tournaments, extras.Photo })
            : ImportCommitService.Hash(new { member, history });
    }
    public static Guid Id(Guid academy,string source,string kind,string code)=>new(SHA256.HashData(Encoding.UTF8.GetBytes($"connected-import:{academy}:{source}:{kind}:{code}"))[..16]);
    public static string[] Codes(string value)=>value.Split([',','،',';','؛'],StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
    private sealed class CellError(string column,string message):Exception(message){public string Column=>column;}
    private sealed record Input(string Sheet,int Number,Dictionary<string,ImportCell> Cells) {
        public string this[string key]=>Cells.GetValueOrDefault(key)?.Value??"";
        public void Need(bool condition,string field,string message){if(!condition)throw new CellError(field,message+" ["+this[field]+"]");}
        public int? NumberAt(string key,bool required=false,int max=10000){if(this[key]==""&&!required)return null;Need(int.TryParse(this[key],NumberStyles.None,CultureInfo.InvariantCulture,out var n)&&n>=0&&n<=max,key,"أدخل عددًا صحيحًا في النطاق المسموح");return n;}
        public decimal Money(string key){var n=ImportPreview.Amount(this[key]);Need(n>=0,key,"أدخل مبلغًا غير سالب حتى منزلتين عشريتين");return n!.Value;}
        public DateOnly? DateAt(string key,bool required=false){if(this[key]==""&&!required)return null;var d=ImportPreview.Date(this[key]);Need(d.HasValue,key,"استخدم تاريخًا صحيحًا yyyy-MM-dd");return d;}
    }
    public static async Task<ConnectedImportPlan> Build(ImportWorkbook book,FoundationDbContext db,Guid academy,TimeProvider time)
    {
        var currentLayout = AcademyWorkbookSchema.IsCurrent(book);
        var friendly = FriendlyImportSchema.IsFriendly(book) ? FriendlyImportInput.Parse(book) : null;
        if (friendly is not null) book = friendly.Connected;
        var source=ConnectedImportSchema.Meta(book,"كود المصدر")??"";
        var baseRefs=await ImportReferences.Load(db,academy,time);
        var plan=new ConnectedImportPlan{Legacy=baseRefs.Template(DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime)),References=baseRefs};
        plan.Friendly = friendly;
        plan.PlatformAccountReview = db.RequestScopeEnabled && db.RequestAcademyId==academy && db.RequestMembershipId==Guid.Empty &&
            await db.PlatformAdministrators.AnyAsync(p=>p.UserId==db.RequestUserId && p.IsActive && db.Users.Any(u=>u.Id==p.UserId && u.IsActive));
        if (friendly is not null) plan.Errors.AddRange(friendly.Errors);
        if(currentLayout && friendly is not null) {
            var codes=friendly.Players.Keys.ToArray();
            var occupiedCodes=await db.Players.Where(p=>p.AcademyId==academy&&codes.Contains(p.PlayerCode)).Select(p=>new {p.Id,p.PlayerCode}).ToArrayAsync();
            var knownCodes=await db.ImportedEnrollments.Where(m=>m.AcademyId==academy&&m.SourceSystem==source).Select(m=>new {m.PlayerId,m.LegacyPlayerId}).ToArrayAsync();
            foreach(var row in friendly.Players.Values) {
                if(row["Code"].Length>24)plan.Errors.Add(new("اللاعبون",row.Number,"كود اللاعب","استخدم كودًا قصيرًا حتى 24 حرفًا مثل PL01."));
                if(occupiedCodes.Any(p=>p.PlayerCode==row["Code"]&&!knownCodes.Any(m=>m.PlayerId==p.Id&&m.LegacyPlayerId==row["Code"])))plan.Errors.Add(new("اللاعبون",row.Number,"كود اللاعب","الكود مستخدم للاعب قائم من مصدر آخر. اختر كودًا فريدًا؛ لا ندمج اللاعبين تلقائيًا."));
            }
        }
        var inputs=new Dictionary<string,List<Input>>();
        foreach(var (sheet,fields) in ConnectedImportSchema.Sheets){var raw=book.Sheet(sheet).Rows;var list=new List<Input>();inputs[sheet]=list;
            if(raw.Count>ImportSchema.MaxRows+1){plan.Errors.Add(new(sheet,1,"","تجاوزت الورقة حد 1000 صف."));continue;}
            if(raw.Count==0||!raw[0].Select(c=>c.Value).SequenceEqual(fields.Select(f=>f.Header))){plan.Errors.Add(new(sheet,1,"","عناوين الأعمدة لا تطابق القالب المترابط."));continue;}
            for(var i=1;i<raw.Count;i++){var cells=raw[i];if(cells.All(c=>c.Value.Length==0))continue;
                var r=new Input(sheet,i+1,fields.Select((f,j)=>(f.Key,Cell:j<cells.Count?cells[j]:new ImportCell("",true))).ToDictionary(x=>x.Key,x=>x.Cell));
                if(cells.Skip(fields.Length).Any(c=>c.Value!=""))plan.Errors.Add(new(sheet,i+1,"","أعمدة إضافية غير مدعومة."));
                foreach(var f in fields){if(f.Required&&r[f.Key]=="")plan.Errors.Add(new(sheet,i+1,f.Header,"حقل مطلوب."));if(r[f.Key].Length>500)plan.Errors.Add(new(sheet,i+1,f.Header,"النص أطول من الحد المسموح."));}
                list.Add(r);
            }
        }
        // V3 normalization omits unusable player rows. Keep checking the other
        // rows when normalization is the only source of errors so the user can
        // correct independent issues in one pass.
        if (plan.Errors.Count > 0 && (!currentLayout || friendly is null ||
            plan.Errors.Any(error => !friendly.PlayerCellErrors.Contains(error)))) return plan;
        void Issue(Input r,CellError e)=>plan.Errors.Add(new(r.Sheet,r.Number,ConnectedImportSchema.Sheets[r.Sheet].FirstOrDefault(f=>f.Key==e.Column)?.Header??e.Column,e.Message));
        void Each(string sheet,Action<Input> apply){var keys=new HashSet<string>(StringComparer.Ordinal);foreach(var r in inputs[sheet])try{
            if(r.Cells.ContainsKey("Code")){r.Need(Regex.IsMatch(r["Code"],"^[A-Za-z0-9_.:-]{1,80}$",RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(100)),"Code","استخدم كودًا نصيًا قصيرًا ثابتًا");r.Need(keys.Add(r["Code"]),"Code","كود مكرر");r.Need(r.Cells["Code"].IsText,"Code","اكتب الكود كنص");}
            if(sheet=="المدربون")r.Need(r["Name"].Length<=100,"Name","الاسم أطول من الحد المسموح");
            apply(r);
        }catch(CellError e){Issue(r,e);}}
        var maps=new Dictionary<string,Dictionary<string,Guid>>();
        Guid Ref(Input r,string field,string kind,bool optional=false){if(optional&&r[field]=="")return Guid.Empty;r.Need(maps.TryGetValue(kind,out var m)&&m.ContainsKey(r[field]),field,"كود مرجعي غير معروف أو غير صالح");return maps[kind][r[field]];}
        Guid[] Refs(Input r,string field,string kind){var codes=Codes(r[field]);r.Need(codes.Length>0,field,"أدخل كودًا واحدًا على الأقل");return codes.Select(c=>{r.Need(maps.TryGetValue(kind,out var m)&&m.ContainsKey(c),field,"كود مرجعي غير معروف: "+c);return maps[kind][c];}).ToArray();}
        // Explicit IDs may only reuse an exactly compatible, currently visible record. Never overwrite.
        var cache=new Dictionary<Type,Dictionary<Guid,TenantEntity>>();
        async Task Load<T>() where T:TenantEntity{cache[typeof(T)]=(await db.Set<T>().AsNoTracking().Where(e=>e.AcademyId==academy).ToListAsync()).ToDictionary(e=>e.Id,e=>(TenantEntity)e);}
        await Load<Branch>();await Load<Sport>();await Load<AgeCategory>();await Load<TrainingLevel>();await Load<TrainingGroup>();await Load<SubscriptionPlan>();await Load<RecurringSchedule>();await Load<StaffGroupAssignment>();await Load<DiscountRule>();await Load<AcademyTournament>();await Load<MatchTeam>();await Load<PlanBranchPublication>();
        static string Scalar(TenantEntity e) {
            string[] fields=e switch {
                Branch=>["ArabicName"], Sport=>["ArabicName","Kind"], AgeCategory=>["ArabicName","MinimumBirthYear","MaximumBirthYear"],
                TrainingLevel=>["ArabicName","SportId","Rank"],
                TrainingGroup=>["ArabicName","BranchId","SportId","AgeCategoryId","Capacity","TrainingLevelId","RegistrationFormat"],
                DiscountRule=>["ArabicName","Type","Percentage","FixedAmount","Currency","BranchId","SportId","SubscriptionPlanId","StartsOn","EndsOn"],
                SubscriptionPlan=>["ArabicName","SportId","PlanType","Price","Currency","DurationDays","DurationMonths","SessionCount","TrainingFormat","ServiceKind"],
                _=>e.GetType().GetProperties().Where(p=>p.Name is not ("Id" or "AcademyId" or "CreatedAtUtc" or "UpdatedAtUtc" or "Version")&&(p.PropertyType.IsValueType||p.PropertyType==typeof(string))).Select(p=>p.Name).ToArray()
            };
            return JsonSerializer.Serialize(fields.Order().ToDictionary(name=>name,name=>e.GetType().GetProperty(name)!.GetValue(e) is decimal amount ? (object)amount.ToString("G29",CultureInfo.InvariantCulture) : e.GetType().GetProperty(name)!.GetValue(e)));
        }
        T Store<T>(Input r,string kind,T e) where T:TenantEntity{
            var model=db.Model.FindEntityType(typeof(T))!;
            var nameLength=(model.FindProperty("ArabicName")??model.FindProperty("Name"))?.GetMaxLength();
            if(nameLength.HasValue)r.Need(r["Name"].Length<=nameLength,"Name","الاسم أطول من الحد المسموح في السجل");
            e.AcademyId=academy;e.Id=r["ExistingId"]==""?Id(academy,source,kind,r["Code"]):Guid.TryParse(r["ExistingId"],out var id)?id:Guid.Empty;
            r.Need(e.Id!=Guid.Empty,"ExistingId","معرف السجل القائم غير صحيح");
            if(r["ExistingId"]==""&&!cache[typeof(T)].ContainsKey(e.Id)) {
                var indexes=db.Model.FindEntityType(typeof(T))!.GetIndexes().Where(i=>i.IsUnique).ToArray();
                var same=cache[typeof(T)].Values.FirstOrDefault(old=>indexes.Any(index=>index.Properties.All(prop=>typeof(T).GetProperty(prop.Name)!.GetValue(e) is { } value&&Equals(value,typeof(T).GetProperty(prop.Name)!.GetValue(old)))));
                if(same is not null){r.Need(same.IsActive&&Scalar(same)==Scalar(e),"Code","يوجد سجل بنفس الاسم أو المرجع وببيانات مختلفة؛ راجع الاسم أو المعرف القائم");e.Id=same.Id;}
            }
            var existing=cache[typeof(T)].GetValueOrDefault(e.Id);r.Need(r["ExistingId"]==""||existing!=null,"ExistingId","السجل القائم غير متاح ضمن الأكاديمية والصلاحيات");
            r.Need(existing==null||existing.IsActive&&Scalar(existing)==Scalar(e),"Code","الكود مرتبط بسجل مختلف أو متوقف؛ لا يُستبدل تلقائيًا");
            if(!maps.ContainsKey(kind))maps[kind]=new(StringComparer.Ordinal);r.Need(!maps[kind].Values.Contains(e.Id),"Code","لا تربط كودين بنفس السجل");maps[kind][r["Code"]]=e.Id;
            if(existing==null){plan.Creates.Add(e);cache[typeof(T)][e.Id]=e;}return existing is T t?t:e;
        }
        Each("الفروع",r=>Store(r,"branch",new Branch{ArabicName=r["Name"]}));
        SportKind? SportType(string name)=>name.Trim().ToLowerInvariant() switch { "عام" or "general"=>SportKind.General,"كرة قدم" or "كرة القدم" or "football"=>SportKind.Football,"سباحة" or "السباحة" or "swimming"=>SportKind.Swimming,_=>null};
        Each("الرياضات",r=>{
            var kind=SportType(r["Kind"]);r.Need(kind.HasValue,"Kind","نوع الرياضة غير مدعوم");
            var sport=new Sport{ArabicName=r["Name"],Kind=kind!.Value};
            // Only standard names that are translations of the same kind are aliases.
            var aliases=cache[typeof(Sport)].Values.Cast<Sport>().Where(x=>x.IsActive&&x.Kind==kind&&SportType(x.ArabicName)==kind).ToArray();
            if(r["ExistingId"]==""&&SportType(r["Name"])==kind&&aliases.Length==1)sport.ArabicName=aliases[0].ArabicName;
            Store(r,"sport",sport);
        });
        Each("الفئات العمرية",r=>{var min=r.NumberAt("MinYear",max:9998);var max=r.NumberAt("MaxYear",max:9998);r.Need((min==null||min>=1900)&&(max==null||max>=1900)&&!(min>max),"MinYear","حدود الميلاد غير متسقة");Store(r,"age",new AgeCategory{ArabicName=r["Name"],MinimumBirthYear=min,MaximumBirthYear=max});});
        Each("مستويات التدريب",r=>Store(r,"level",new TrainingLevel{ArabicName=r["Name"],SportId=Ref(r,"Sport","sport"),Rank=r.NumberAt("Rank",true)!.Value}));
        var modes=new Dictionary<string,TrainingRegistrationFormat>{{"GROUP",TrainingRegistrationFormat.Group},{"PRIVATE",TrainingRegistrationFormat.Private},{"SEMI",TrainingRegistrationFormat.SemiPrivate}};
        Each("أنماط التدريب",r=>r.Need(modes.ContainsKey(r["Code"])&&r["Name"]==(r["Code"]=="GROUP"?"جماعي":r["Code"]=="PRIVATE"?"خاص":"شبه خاص"),"Code","لا تعدل أنماط التدريب المدعومة"));
        TrainingRegistrationFormat Mode(Input r){r.Need(modes.ContainsKey(r["Mode"]),"Mode","نمط غير مدعوم");return modes[r["Mode"]];}
        var memberships=await db.AcademyMemberships.AsNoTracking().Include(m=>m.User).Where(m=>m.AcademyId==academy).ToDictionaryAsync(m=>m.Id);
        var coachBranches=new Dictionary<Guid,Guid[]>();var coachSports=new Dictionary<Guid,Guid[]>();
        var phones=inputs["المدربون"].Select(r=>EgyptPhoneNormalizer.Normalize(r["Phone"])).Where(p=>p!=null).ToArray();
        var occupied=await db.Users.AsNoTracking().Where(u=>phones.Contains(u.PhoneNumber)).Select(u=>new{u.Id,u.PhoneNumber,u.DisplayName,u.IsActive}).ToArrayAsync();
        Each("المدربون",r=>{var phone=EgyptPhoneNormalizer.Normalize(r["Phone"]);r.Need(phone!=null&&r.Cells["Phone"].IsText,"Phone","الهاتف المصري يجب أن يكون نصًا صالحًا");var branches=Refs(r,"Branches","branch");var sports=Refs(r,"Sports","sport");
            var mid=r["ExistingId"]==""?Id(academy,source,"coach",r["Code"]):Guid.TryParse(r["ExistingId"],out var id)?id:Guid.Empty;
            var existing=memberships.GetValueOrDefault(mid);r.Need(r["ExistingId"]==""||existing!=null,"ExistingId","عضوية المدرب غير متاحة");
            r.Need(existing==null||existing.IsActive&&existing.Role==AcademyRole.Coach&&existing.User.IsActive&&existing.User.PhoneNumber==phone&&existing.User.DisplayName==r["Name"],"Code","عضوية المدرب مختلفة أو متوقفة");
            if(existing==null){
                r.Need(!plan.Creates.OfType<AcademyMembership>().Any(m=>m.Role==AcademyRole.Coach&&plan.Creates.OfType<ApplicationUser>().Any(u=>u.Id==m.UserId&&u.PhoneNumber==phone)),"Phone","رقم المدرب مكرر في الملف");
                var account=occupied.SingleOrDefault(u=>u.PhoneNumber==phone);
                r.Need(account==null||plan.PlatformAccountReview&&account.IsActive&&account.DisplayName==r["Name"],"Phone","الهاتف مرتبط بحساب مختلف أو متوقف؛ راجع هوية المدرب");
                var uid=account?.Id??Id(academy,source,"coach-user",r["Code"]);
                if(account==null)plan.Creates.Add(new ApplicationUser{Id=uid,UserName=phone,NormalizedUserName=phone!.ToUpperInvariant(),PhoneNumber=phone,PhoneNumberConfirmed=false,DisplayName=r["Name"],SecurityStamp=Guid.NewGuid().ToString("N"),ConcurrencyStamp=Guid.NewGuid().ToString("N"),LockoutEnabled=true});
                else {
                    r.Need(!memberships.Values.Any(m=>m.UserId==uid&&m.Role==AcademyRole.Coach),"ExistingId","للمدرب عضوية قائمة بالأكاديمية؛ حددها صراحةً");
                    r.Need(!plan.AccountLinks.Any(x=>x.Role=="مدرب"&&x.Phone==phone),"Phone","رقم المدرب مكرر في الملف");
                    plan.AccountLinks.Add(new("مدرب",r["Name"],phone!));
                }
                plan.Creates.Add(new AcademyMembership{Id=mid,AcademyId=academy,UserId=uid,Role=AcademyRole.Coach,AllBranches=false,AllSports=false});
                foreach(var b in branches)plan.Creates.Add(new MembershipBranchAccess{Id=Id(academy,source,"coach-branch",r["Code"]+b),AcademyId=academy,MembershipId=mid,BranchId=b});foreach(var sp in sports)plan.Creates.Add(new MembershipSportAccess{Id=Id(academy,source,"coach-sport",r["Code"]+sp),AcademyId=academy,MembershipId=mid,SportId=sp});
            }else {r.Need(existing.AllBranches||branches.All(b=>db.MembershipBranchAccesses.Any(a=>a.MembershipId==mid&&a.BranchId==b&&a.IsActive)),"Branches","الفرع خارج نطاق المدرب");r.Need(existing.AllSports||sports.All(s=>db.MembershipSportAccesses.Any(a=>a.MembershipId==mid&&a.SportId==s&&a.IsActive)),"Sports","الرياضة خارج نطاق المدرب");}
            if(!maps.ContainsKey("coach"))maps["coach"]=new();r.Need(!maps["coach"].Values.Contains(mid),"Code","مدرب مكرر بأكواد مختلفة");maps["coach"][r["Code"]]=mid;coachBranches[mid]=branches;coachSports[mid]=sports;
        });
        var bindings=await db.GroupResourceSchedules.AsNoTracking().Where(b=>b.AcademyId==academy&&b.IsActive).Select(b=>b.TrainingGroupId).ToArrayAsync();
        Each("المجموعات",r=>{var b=Ref(r,"Branch","branch");var sp=Ref(r,"Sport","sport");var level=Ref(r,"Level","level",true);var mode=Mode(r);var cap=r.NumberAt("Capacity",true);r.Need(cap>0,"Capacity","السعة يجب أن تكون موجبة");
            r.Need(level==Guid.Empty||((TrainingLevel)cache[typeof(TrainingLevel)][level]).SportId==sp,"Level","مستوى من رياضة مختلفة");
            r.Need(mode==TrainingRegistrationFormat.Group||level!=Guid.Empty&&Guid.TryParse(r["ExistingId"],out var gid)&&bindings.Contains(gid),"Mode","الخاص وشبه الخاص يحتاجان مجموعة قائمة بمستوى وسعة معتمدة وحجز مورد؛ جهزها ثم اربط معرفها");
            var g=Store(r,"group",new TrainingGroup{ArabicName=r["Name"],BranchId=b,SportId=sp,AgeCategoryId=Ref(r,"Age","age"),Capacity=cap,TrainingLevelId=level==Guid.Empty?null:level,RegistrationFormat=mode});
            r.Need(mode==TrainingRegistrationFormat.Group||g.FormatReviewedByUserId.HasValue&&g.FormatReviewedAtUtc.HasValue,"Mode","سعة ونمط المجموعة الخاصة يحتاجان اعتمادًا قائمًا صريحًا");plan.Groups[g.Id]=g;
            foreach(var c in Refs(r,"Coaches","coach")){r.Need(coachBranches[c].Contains(b)&&coachSports[c].Contains(sp),"Coaches","المدرب لا يغطي فرع ورياضة المجموعة");var a=new StaffGroupAssignment{Id=Id(academy,source,"assignment",g.Id+":"+c),AcademyId=academy,AcademyMembershipId=c,TrainingGroupId=g.Id};var old=cache[typeof(StaffGroupAssignment)].Values.Cast<StaffGroupAssignment>().SingleOrDefault(x=>x.TrainingGroupId==g.Id&&x.AcademyMembershipId==c);r.Need(old==null||old.IsActive,"Coaches","إسناد قائم متوقف يحتاج مراجعة");plan.Assignments.Add(old??a);if(old==null)plan.Creates.Add(a);}
        });
        Each("الحصص",r=>{var group=Ref(r,"Group","group");var from=r.DateAt("From",true)!.Value;var to=r.DateAt("To",true)!.Value;r.Need(to>=from&&to.DayNumber-from.DayNumber<=365,"To","فترة الجدول حتى سنة");var day=r.NumberAt("Day",true,6)!.Value;
            r.Need(TimeOnly.TryParseExact(r["Start"],"HH:mm",CultureInfo.InvariantCulture,DateTimeStyles.None,out var start),"Start","وقت البداية غير صحيح");r.Need(TimeOnly.TryParseExact(r["End"],"HH:mm",CultureInfo.InvariantCulture,DateTimeStyles.None,out var end)&&end>start,"End","وقت النهاية بعد البداية");
            var s=Store(r,"schedule",new RecurringSchedule{TrainingGroupId=group,DayOfWeek=(DayOfWeek)day,StartTime=start,EndTime=end,ValidFrom=from,ValidTo=to});
            foreach(var old in cache[typeof(RecurringSchedule)].Values.Cast<RecurringSchedule>().Where(o=>o.IsActive&&o.Id!=s.Id&&o.DayOfWeek==s.DayOfWeek&&o.StartTime<s.EndTime&&s.StartTime<o.EndTime&&!(o.ValidFrom>s.ValidTo||o.ValidTo<s.ValidFrom))){var shared=old.TrainingGroupId==group||plan.Assignments.Any(a=>a.TrainingGroupId==group&&cache[typeof(StaffGroupAssignment)].Values.Cast<StaffGroupAssignment>().Concat(plan.Assignments).Any(b=>b.TrainingGroupId==old.TrainingGroupId&&b.IsActive&&b.AcademyMembershipId==a.AcademyMembershipId));r.Need(!shared,"Group","تعارض موعد المجموعة أو المدرب");}plan.Schedules.Add(s);
        });
        Each("الباقات",r=>{var sp=Ref(r,"Sport","sport");var branches=Refs(r,"Branches","branch");var type=r["Type"] switch{"مدة"=>SubscriptionPlanType.Duration,"حصص"=>SubscriptionPlanType.Sessions,"مزدوجة"=>SubscriptionPlanType.Combined,_=>(SubscriptionPlanType)0};r.Need(type!=0,"Type","نوع باقة غير مدعوم");var days=r.NumberAt("Days",max:3650);var months=r.NumberAt("Months",max:120);var sessions=r.NumberAt("Sessions");r.Need(type==SubscriptionPlanType.Sessions?days==null&&months==null:(days>0&&months==null||months>0&&days==null),"Days","مدة الباقة غير متسقة مع النوع");r.Need(type==SubscriptionPlanType.Duration?sessions==null:sessions>0,"Sessions","حصص الباقة غير متسقة مع النوع");
            var p=Store(r,"product",new SubscriptionPlan{ArabicName=r["Name"],SportId=sp,PlanType=type,Price=r.Money("Price"),Currency=baseRefs.Currency,DurationDays=days,DurationMonths=months,SessionCount=sessions,TrainingFormat=(TrainingFormat)(int)Mode(r),AllBranches=false,Revision=1});plan.Plans[p.Id]=p;
            foreach(var branch in branches.Where(_=>!p.AllBranches)){var pub=new PlanBranchPublication{Id=Id(academy,source,"publication",p.Id+":"+branch),AcademyId=academy,SubscriptionPlanId=p.Id,BranchId=branch};var old=cache[typeof(PlanBranchPublication)].Values.Cast<PlanBranchPublication>().SingleOrDefault(b=>b.SubscriptionPlanId==p.Id&&b.BranchId==branch);r.Need(old==null||old.IsActive,"Branches","نشر الباقة متوقف");r.Need(plan.Creates.Contains(p)||old!=null,"Branches","الباقة القائمة غير منشورة للفرع؛ انشرها من إدارة الباقات قبل الاستيراد");if(old==null)plan.Creates.Add(pub);}
        });
        Each("العروض",r=>{var type=r["Type"] switch{"قيمة ثابتة"=>RenewalDiscountType.FixedAmount,"نسبة مئوية"=>RenewalDiscountType.Percentage,_=>(RenewalDiscountType)0};var value=r.Money("Value");r.Need(type!=0&&value>0&&(type!=RenewalDiscountType.Percentage||value<=100),"Value","نوع أو قيمة الخصم غير صالح");var b=Ref(r,"Branch","branch",true);var sp=Ref(r,"Sport","sport",true);var p=Ref(r,"Plan","product",true);var from=r.DateAt("From");var to=r.DateAt("To");r.Need(!(to<from),"To","نهاية العرض أسبق من بدايته");var offer=Store(r,"offer",new DiscountRule{ArabicName=r["Name"],Type=type,Percentage=type==RenewalDiscountType.Percentage?value:0,FixedAmount=type==RenewalDiscountType.FixedAmount?value:null,BranchId=b==Guid.Empty?null:b,SportId=sp==Guid.Empty?null:sp,SubscriptionPlanId=p==Guid.Empty?null:p,Currency=baseRefs.Currency,StartsOn=from,EndsOn=to,Revision=1,Reason="عرض مستورد بمعاينة واعتماد"});if(plan.Creates.Contains(offer))plan.Creates.Add(DiscountRuleVersion.Create(offer,null,time.GetUtcNow()));});
        Each("البطولات",r=>Store(r,"tournament",new AcademyTournament{Name=r["Name"],SportId=Ref(r,"Sport","sport"),Fee=r.Money("Fee"),Currency=baseRefs.Currency}));
        Each("الفرق",r=>{var b=Ref(r,"Branch","branch");var sp=Ref(r,"Sport","sport");var c=Ref(r,"Coach","coach");r.Need(coachBranches[c].Contains(b)&&coachSports[c].Contains(sp),"Coach","الفريق خارج نطاق المدرب");Store(r,"team",new MatchTeam{Name=r["Name"],BranchId=b,SportId=sp,AgeCategoryId=Ref(r,"Age","age"),CoachMembershipId=c});});
        var rows=new List<IReadOnlyList<ImportCell>>{ConnectedImportSchema.Row(ImportSchema.Enrollments.Select(c=>c.Key).ToArray())};
        Each("الأعضاء",r=>{foreach(var field in new[]{"Player","Enrollment","Family","Branch","Sport","Level","Group","Plan","Offer","Team","Tournaments"})r.Need(r[field]==""||r.Cells[field].IsText,field,"الأكواد يجب أن تكون خلايا نصية");
            var branch=Ref(r,"Branch","branch");var sp=Ref(r,"Sport","sport");var group=Ref(r,"Group","group");var product=Ref(r,"Plan","product");var level=Ref(r,"Level","level",true);
            r.Need(plan.Groups.TryGetValue(group, out var g), "Group", "المجموعة لم تجتز الفحص");
            r.Need(plan.Plans.TryGetValue(product, out var p), "Plan", "الباقة لم تجتز الفحص");
            r.Need(g!.BranchId==branch&&g.SportId==sp,"Group","المجموعة لا تطابق الفرع والرياضة");r.Need((int)p!.TrainingFormat==(int)g.RegistrationFormat,"Plan","نمط الباقة لا يطابق المجموعة");r.Need(level==Guid.Empty||((TrainingLevel)cache[typeof(TrainingLevel)][level]).SportId==sp,"Level","المستوى لا يطابق الرياضة");r.Need(g.TrainingLevelId==null||g.TrainingLevelId==level,"Level","راجع مستوى اللاعب والمجموعة");
            r.Need(r["Gender"] is "" or "ذكر" or "أنثى","Gender","الجنس غير صحيح");var offer=Ref(r,"Offer","offer",true);if(offer!=Guid.Empty)r.Need(DiscountPricing.Eligible((DiscountRule)cache[typeof(DiscountRule)][offer],r.DateAt("Start",true)!.Value,branch,p),"Offer","العرض غير مناسب لهذا التسجيل");
            var tournaments=r["Tournaments"]==""?[]:Refs(r,"Tournaments","tournament");var team=Ref(r,"Team","team",true);r.Need(tournaments.Length==0||team!=Guid.Empty,"Team","المشاركة تحتاج كود فريق صريحًا؛ مجموعة التدريب ليست فريقًا");
            if(team!=Guid.Empty){var tm=(MatchTeam)cache[typeof(MatchTeam)][team];r.Need(tm.SportId==sp&&tm.BranchId==branch&&tm.AgeCategoryId==g.AgeCategoryId,"Team","الفريق لا يطابق فرع ورياضة وفئة اللاعب");}
            foreach(var t in tournaments)r.Need(((AcademyTournament)cache[typeof(AcademyTournament)][t]).SportId==sp,"Tournaments","البطولة لا تطابق رياضة اللاعب");
            r.Need(!plan.Members.ContainsKey(r["Enrollment"]),"Enrollment","كود التسجيل مكرر");plan.Members[r["Enrollment"]]=new(r["Enrollment"],r["Gender"]==""?null:r["Gender"]=="ذكر"?Gender.Male:Gender.Female,level==Guid.Empty?null:level,g.RegistrationFormat,offer==Guid.Empty?null:offer,tournaments,team==Guid.Empty?null:team);
            while (rows.Count < r.Number - 1) rows.Add([]);
            rows.Add(ConnectedImportSchema.Row(r["Player"],r["Enrollment"],r["Family"],r["Name"],r["Guardian"],r["Phone"],r["Birth"],branch.ToString(),sp.ToString(),"",group.ToString(),product.ToString(),r["Start"],r["End"],r["Entitled"],r["Remaining"],r["Pricing"] switch {"صافي متفق"=>"agreed_net","السعر والخصم"=>"price_and_discount",_=>r["Pricing"]},r["Base"],r["Discount"],r["Net"],r["Currency"],r["State"]=="قائم"?"existing":r["State"]=="جديد"?"new":r["State"],r["Notes"]));
            if(!r.Cells["Phone"].IsText)rows[^1]=rows[^1].Select((c,i)=>i==5?c with{IsText=false}:c).ToArray();
        });
        // Validate player identities, financial opening state and capacity through the SAME legacy validator.
        var refs=new List<ImportReference>();
        foreach(var id in maps.GetValueOrDefault("branch",[]).Values)refs.Add(new("branch",id,((Branch)cache[typeof(Branch)][id]).ArabicName));
        foreach(var id in maps.GetValueOrDefault("sport",[]).Values)refs.Add(new("sport",id,((Sport)cache[typeof(Sport)][id]).ArabicName));
        foreach(var g in plan.Groups.Values){var age=(AgeCategory)cache[typeof(AgeCategory)][g.AgeCategoryId];refs.Add(new("group",g.Id,g.ArabicName,g.BranchId,g.SportId,g.AgeCategoryId,age.MinimumBirthYear,age.MaximumBirthYear,g.Capacity,baseRefs.Values.FirstOrDefault(v=>v.Kind=="group"&&v.Id==g.Id)?.Occupied??await db.SportEnrollments.CountAsync(e=>e.IsActive&&e.TrainingGroupId==g.Id)));}
        foreach(var p in plan.Plans.Values){var branches=cache[typeof(PlanBranchPublication)].Values.Cast<PlanBranchPublication>().Concat(plan.Creates.OfType<PlanBranchPublication>()).Where(b=>b.IsActive&&b.SubscriptionPlanId==p.Id).Select(b=>b.BranchId).Distinct().Order().ToArray();refs.Add(new("product",p.Id,p.ArabicName,Sport:p.SportId,Revision:p.Revision,PlanType:p.PlanType,AllBranches:p.AllBranches,Branches:branches));}
        var stamp=ImportCommitService.Hash(book.Sheets.Where(s=>s.Name is not ("دليل الاستخدام" or "الأعضاء" or "الأرصدة الافتتاحية")).OrderBy(s=>s.Name).ToArray());
        foreach (var p in plan.Plans.Values.Where(p=>plan.Creates.Contains(p))) {
            var branchIds = refs.Single(r=>r.Kind=="product"&&r.Id==p.Id).Branches!;
            plan.Creates.Add(PlanPriceVersion.Create(p,branchIds,db.RequestUserId==Guid.Empty?null:db.RequestUserId,time.GetUtcNow()));
        }
        plan.References=new(academy,baseRefs.Currency,refs,stamp);
        plan.Legacy=new(plan.Legacy.Sheets.Select(s=>s.Name switch {
            "Enrollments"=>s with{Rows=rows},
            "OpenBalances"=>s with{Rows=new[]{ConnectedImportSchema.Row(ImportSchema.OpenBalances.Select(c=>c.Key).ToArray())}.Concat(inputs["الأرصدة الافتتاحية"].Select(r=>ConnectedImportSchema.Row(r["Enrollment"],r["Amount"],r["Currency"],r["Cutoff"],r["Reference"],r["Evidence"]))).ToArray()},
            "Metadata"=>s with{Rows=new[]{ConnectedImportSchema.Row("Key","Value"),ConnectedImportSchema.Row("SchemaVersion",ImportSchema.Version),ConnectedImportSchema.Row("AcademyCode",ConnectedImportSchema.Meta(book,"الأكاديمية")??""),ConnectedImportSchema.Row("SourceSystem",source),ConnectedImportSchema.Row("CutoffDate",ConnectedImportSchema.Meta(book,"تاريخ القطع")??""),ConnectedImportSchema.Row("ReferenceStamp",stamp)}},_=>s
        }).ToArray());
        plan.validateCompletedRows = currentLayout && plan.Members.Count > 0;
        if(ConnectedImportSchema.Meta(book,"نسخة القالب")!=ConnectedImportSchema.Version)plan.Errors.Add(new("إعدادات الملف",1,"نسخة القالب","نسخة غير مدعومة."));
        if(inputs["إعدادات الملف"].Select(r=>r["Key"]).Distinct().Count()!=inputs["إعدادات الملف"].Count)plan.Errors.Add(new("إعدادات الملف",1,"الإعداد","إعداد مكرر."));
        foreach(var (sheet,kind) in new[]{("الفروع","branch"),("الرياضات","sport"),("الفئات العمرية","age"),("مستويات التدريب","level"),("المجموعات","group"),("الباقات","product"),("العروض","offer"),("البطولات","tournament"),("الفرق","team"),("الحصص","schedule"),("المدربون","coach")}){var ids=maps.GetValueOrDefault(kind,[]).Values;var created=ids.Count(id=>plan.Creates.Any(e=>e.GetType().GetProperty("Id")?.GetValue(e) is Guid eid&&eid==id));plan.Effects.Add(new(sheet,created,ids.Count()-created));}
        // A player cannot be scheduled into overlapping sports even on the first import.
        var registrationsByPlayer = rows.Skip(1).Select((row, index) => (Row: row, Number: index + 2))
            .Where(r => r.Row.Count > 0).GroupBy(r => r.Row[0].Value);
        foreach (var registrations in registrationsByPlayer)
        {
            var firstRow = registrations.First().Number;
            var genders = registrations.Select(r => plan.Members[r.Row[1].Value].Gender).Where(g => g.HasValue).Distinct();
            if (genders.Count() > 1)
                plan.Errors.Add(new("الأعضاء", firstRow, "ذكر أو أنثى", "بيانات جنس اللاعب متعارضة عبر الرياضات."));
            var groupIds = registrations.Select(r => Guid.Parse(r.Row[10].Value)).ToHashSet();
            var schedule = plan.Schedules.Where(s => groupIds.Contains(s.TrainingGroupId)).ToArray();
            if (schedule.Any(s => schedule.Any(o => o.TrainingGroupId != s.TrainingGroupId &&
                o.DayOfWeek == s.DayOfWeek && o.StartTime < s.EndTime && s.StartTime < o.EndTime &&
                !(o.ValidFrom > s.ValidTo || o.ValidTo < s.ValidFrom))))
                plan.Errors.Add(new("الأعضاء", firstRow, "كود اللاعب *", "تعارض مواعيد اللاعب عبر الرياضات: " + registrations.Key));
        }
        if (friendly is not null) { plan.History = await FriendlyImportHistory.Build(friendly, plan, db, academy, source, time, maps); plan.Errors.AddRange(friendly.Errors.Where(e => !plan.Errors.Contains(e))); }
        return plan;
    }
    public ImportPreviewResult Validate(string hash,ISet<string> existing)
    {
        var result=ImportPreview.Validate(Legacy,References,hash,existing);
        // A structural failure leaves Legacy as an empty internal template. Its
        // metadata/empty-row errors are not errors in the uploaded workbook.
        var rowErrors = Errors.Count > 0 && !validateCompletedRows ? [] : result.Errors;
        ImportIssue Map(ImportIssue issue) => Friendly?.MapIssue(issue) ?? issue;
        var mapped = Errors.Select(Map).ToList();
        var reported = validateCompletedRows ? mapped.Select(e => (e.Sheet, e.Row, e.Column)).ToHashSet() : null;
        foreach (var rowError in rowErrors)
        {
            var issue = Map(ConnectedIssue(rowError));
            if (issue.IsShared || reported?.Contains((issue.Sheet, issue.Row, issue.Column)) != true)
                mapped.Add(issue);
        }
        var errors = mapped.Take(300).ToArray();
        return result with{SchemaVersion=Friendly is null ? ConnectedImportSchema.Version : FriendlyImportSchema.Version,CanApprove=errors.Length==0,Errors=errors,AccountLinks=AccountLinks.ToArray(),Effects=Effects.ToArray(),CoachAssignments=Assignments.Count,TournamentParticipations=TournamentParticipations,Historical=History?.Summary,GuardianCount=Friendly?.Guardians.Count,GuardianRelationships=Friendly?.Rows["علاقات أولياء الأمور"].Count,HistoricalPayments=History?.PaymentsCount??0,HistoricalPaymentTotal=History?.PaymentsTotal??0,
            HistoricalPaymentCurrency = errors.Length == 0 && History?.PaymentsCount > 0 ? References.Currency : null};
    }

    private static ImportIssue ConnectedIssue(ImportIssue issue)
    {
        if (issue.Sheet != "Enrollments") return issue;
        var column = Array.FindIndex(ImportSchema.Enrollments, field => field.Key == issue.Column);
        return issue with
        {
            Sheet = "الأعضاء",
            Column = column < 0 ? issue.Column : ConnectedImportSchema.Sheets["الأعضاء"][column].Header,
            Message = issue.Column == "PricingMode"
                ? "اختر «صافي متفق» مع الصافي وحده، أو «السعر والخصم» مع المبالغ الثلاثة المتطابقة؛ ويمكن ترك المال فارغًا للمراجعة."
                : issue.Message
        };
    }
}
