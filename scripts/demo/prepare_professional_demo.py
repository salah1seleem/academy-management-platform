"""Idempotent synthetic film dataset. Never operates on the live project DB."""
from pathlib import Path
import sys,json,uuid,datetime,decimal,hashlib
import os
ROOT=Path(__file__).resolve().parents[2]
import psycopg
from psycopg import sql
from psycopg.rows import dict_row
from psycopg.types.json import Jsonb
def require(condition,message):
 if not condition: raise RuntimeError(message)
require(os.environ.get('ASPNETCORE_ENVIRONMENT') == 'Demo', 'Demo environment required')
parts=dict(x.split('=',1) for x in os.environ['ACADEMY_DEMO_CONNECTION'].split(';') if '=' in x)
DB=parts['Database']
require(DB.startswith('academy_professional_demo_'), 'Reserved isolated Demo database required')
require(parts.get('Host') in ('localhost','127.0.0.1'), 'Only a local isolated database is allowed')
ACADEMY=uuid.UUID('30000000-0000-0000-0000-000000000003');NS=uuid.UUID('44204c54-29b1-4a3d-8c87-b996e8552026')
def mid(key):return uuid.uuid5(NS,key)
con=psycopg.connect(host=parts['Host'],port=parts['Port'],user=parts['Username'],password=parts.get('Password',''),dbname=DB,row_factory=dict_row)
D=datetime.date.fromisoformat(os.environ['ACADEMY_DEMO_DATE']);NOW=datetime.datetime.combine(D,datetime.time(9),datetime.timezone.utc);MONEY=decimal.Decimal('1000.00')
def prepare():
 with con.transaction(),con.cursor() as c:
  c.execute('SELECT "Slug","EnglishName" FROM "Academies" WHERE "Id"=%s',(ACADEMY,));mark=c.fetchone();require(mark=={'Slug':'football-demo','EnglishName':'Football Demo Dataset v1'}, 'Reserved synthetic academy marker required')
  def allrows(t,where='"AcademyId"=%s',args=(ACADEMY,)):
   c.execute(sql.SQL('SELECT * FROM {} WHERE '+where).format(sql.Identifier(t)),args);return c.fetchall()
  def insert(t,r):
   names=list(r);vals=[Jsonb(v)if isinstance(v,(dict,list))else v for v in r.values()]
   c.execute(sql.SQL('INSERT INTO {} ({}) VALUES ({}) ON CONFLICT ("Id") DO NOTHING').format(sql.Identifier(t),sql.SQL(',').join(map(sql.Identifier,names)),sql.SQL(',').join(sql.Placeholder()*len(names))),vals)
  def update(t,key,**vals):
   c.execute(sql.SQL('UPDATE {} SET {} WHERE "Id"=%s').format(sql.Identifier(t),sql.SQL(',').join(sql.SQL('{}=%s').format(sql.Identifier(k))for k in vals)),[*[Jsonb(v)if isinstance(v,(dict,list))else v for v in vals.values()],key])
  # A second synthetic tenant makes unauthorized resource reads reviewable.
  foreign_academy=mid('isolation/academy');foreign_player=mid('isolation/player')
  original=allrows('Academies','"Id"=%s',(ACADEMY,))[0]
  insert('Academies',dict(original,Id=foreign_academy,ArabicName='أكاديمية اختبار العزل',EnglishName='Synthetic isolation fixture',Slug='professional-isolation-demo'))
  sample=allrows('Players')[0]
  insert('Players',dict(sample,Id=foreign_player,AcademyId=foreign_academy,PlayerCode='ISO-0001',ArabicName='لاعب اختبار عزل خيالي',PhotoReference=None))
  c.execute('SELECT 1 FROM "Players" WHERE "Id"=%s',(mid('player/299'),))
  if c.fetchone():print('Professional dataset already prepared; no reset or duplicate collections.');return
  # The Football base has its own future slots. Keep historical attendance,
  # but cancel their future slots so Professional's timetable supplies them.
  for group_index in range(6):
   for week in [0,1]:
    original_id=uuid.UUID(bytes_le=hashlib.sha256(f'football-demo/v1/session/{group_index}/{week}'.encode()).digest()[:16])
    c.execute('UPDATE "TrainingSessions" SET "IsActive"=false,"Status"=%s WHERE "AcademyId"=%s AND "Id"=%s AND "Status"=%s AND "SessionDate">=%s',('Cancelled',ACADEMY,original_id,'Scheduled',D))
  players=sorted(allrows('Players'),key=lambda r:r['PlayerCode']);enrolls=allrows('SportEnrollments');byplayer={r['PlayerId']:r for r in enrolls};groups=allrows('TrainingGroups');groups.sort(key=lambda r:r['ArabicName']);oldgroups={r['Id']:r for r in groups}
  # Preserve the principal player in its existing seeded group for evaluations.
  base_player=next(r for r in players if r['PlayerCode']=='FB-0001');base_en=byplayer[base_player['Id']];g0=next(r for r in groups if r['Id']==base_en['TrainingGroupId']);groups=[g0]+[r for r in groups if r['Id']!=g0['Id']]
  branches=allrows('Branches');branches.sort(key=lambda r:r['ArabicName'])
  for i,row in enumerate(branches):update('Branches',row['Id'],ArabicName=f'تحضير فرع {i}')
  for row,name in zip(branches,['فرع مدينتي','فرع الشروق','فرع الرحاب']):update('Branches',row['Id'],ArabicName=name)
  update('Academies',ACADEMY,ArabicName='Professional Academy')
  exp=allrows('AcademyExperiences')[0];update('AcademyExperiences',exp['Id'],DisplayName='Professional Academy')
  members=allrows('AcademyMemberships');owner=next(r for r in members if r['Role']=='AcademyOwner');admin=next(r for r in members if r['Role']=='AcademyAdmin');guardian_member=next(r for r in members if r['Role']=='Guardian')
  update('Users',owner['UserId'],DisplayName='أحمد سامي');update('Users',admin['UserId'],DisplayName='مريم عادل')
  guardians=allrows('GuardianProfiles');guardians.sort(key=lambda r:str(r['Id']));links=allrows('GuardianPlayerLinks');principal_guard=next(g for g in guardians if g['Id']==next(x['GuardianId']for x in links if x['PlayerId']==base_player['Id']));guardians=[principal_guard]+[g for g in guardians if g['Id']!=principal_guard['Id']]
  base_user=allrows('Users','"Id"=%s',(principal_guard['UserId'],))[0]
  given=['يوسف','سليم','آدم','عمر','ياسين','آسر','حمزة','مروان','زياد','علي','مالك','سيف','أدهم','كريم','حسن','أنس','إياد','فارس','معتز','مصطفى']
  fathers=['خالد','أحمد','محمد','محمود','حسام','شريف','طارق','وليد','هشام','إبراهيم','سامح','عادل','كامل','عماد','مصطفى']
  families=['سليم','الشناوي','عاطف','منصور','بدوي','ناجي','شوقي','فوزي','إسماعيل','حسن','مراد','رمضان','سعد','جمال','صبري']
  for i in range(150):
   name=f'{fathers[i%15]} {families[(i//15)%15]}'
   if i==0:name='خالد سليم'
   if i<len(guardians):g=guardians[i];update('GuardianProfiles',g['Id'],DisplayName=name,PhotoReference=f'/demo-assets/portraits/guardian-0{i+1}.jpg' if i<2 else None);update('Users',g['UserId'],DisplayName=name)
   else:
    uid=mid(f'user/guardian/{i}');phone=f'+2010999{10000+i:05d}';user=dict(base_user,Id=uid,DisplayName=name,UserName=f'guardian{i}.promo@example.test',NormalizedUserName=f'GUARDIAN{i}.PROMO@EXAMPLE.TEST',Email=f'guardian{i}.promo@example.test',NormalizedEmail=f'GUARDIAN{i}.PROMO@EXAMPLE.TEST',PhoneNumber=phone,SecurityStamp=str(mid(f'security/{i}')),ConcurrencyStamp=str(mid(f'concurrency/{i}')),PasswordHash=None)
    insert('Users',user);insert('AcademyMemberships',dict(guardian_member,Id=mid(f'member/guardian/{i}'),UserId=uid));g=dict(principal_guard,Id=mid(f'guardian/{i}'),UserId=uid,ContactPhone=phone,DisplayName=name,PhotoReference=None);insert('GuardianProfiles',g);guardians.append(g)
  assignments=allrows('StaffGroupAssignments');schedules=allrows('RecurringSchedules');sessions=allrows('TrainingSessions');base_session=sessions[0]
  for i,cat in enumerate(allrows('AgeCategories')):update('AgeCategories',cat['Id'],ArabicName=f'تحضير فئة {i}')
  # New sports exist only in this synthetic tenant; football history stays football.
  football=allrows('Sports')[0]; sport_ids=[football['Id'],mid('sport/swimming'),mid('sport/basketball')]
  for sid,name,english,kind in [(sport_ids[1],'السباحة','Swimming','Swimming'),(sport_ids[2],'كرة السلة','Basketball','General')]:
   insert('Sports',dict(football,Id=sid,ArabicName=name,EnglishName=english,Kind=kind))
  criteria=allrows('EvaluationCriteria')
  template_criterion=criteria[0]
  for sid,names in [(sport_ids[1],['الثقة في الماء','التنفس','حركة الذراعين','حركة الرجلين']), (sport_ids[2],['التمرير','التصويب','التحكم بالكرة','التعاون'])]:
   for index,name in enumerate(names):
    insert('EvaluationCriteria',dict(template_criterion,Id=mid('criterion/'+str(sid)+'/'+str(index)),SportId=sid,ArabicName=name,EnglishName=None,Description='معيار تدريب اصطناعي للعرض',FootballAxis=None,DisplayOrder=index+1,Weight=decimal.Decimal('1')))
  for i in range(18):
   old=groups[i%6];group=old if i<6 else dict(old,Id=mid(f'group/{i}'));year=2013+i%6;branch=group['BranchId'] if i<6 else branches[i%3]['Id'];name=f'منتخب {year}'+('' if i<6 else ' — '+('ب' if i<12 else 'ج'))
   if i>=6:
    group.update(ArabicName=name,BranchId=branch);insert('TrainingGroups',group);groups.append(group)
   sport_id=sport_ids[0 if i<9 else 1 if i<15 else 2]
   sport_label=['كرة القدم','السباحة','كرة السلة'][0 if i<9 else 1 if i<15 else 2]
   name=f'{sport_label} · مواليد {year} — {i+1:02d}'
   update('TrainingGroups',group['Id'],ArabicName=name,BranchId=branch,Capacity=24,SportId=sport_id);group.update(ArabicName=name,BranchId=branch,SportId=sport_id)
   update('AgeCategories',group['AgeCategoryId'],ArabicName=f'مواليد {year}',MinimumBirthYear=year,MaximumBirthYear=year)
   if i>=6:
    a=next((a for a in assignments if a['TrainingGroupId']==old['Id']),assignments[0]);insert('StaffGroupAssignments',dict(a,Id=mid(f'assignment/{i}'),TrainingGroupId=group['Id']))
   template=next((s for s in schedules if s['TrainingGroupId']==old['Id']),schedules[0])
   for j,weekday in enumerate([6,1,3]):
    sc=dict(template,Id=mid(f'schedule/{i}/{j}'),TrainingGroupId=group['Id'],DayOfWeek=weekday if isinstance(template['DayOfWeek'],int)else['Saturday','Monday','Wednesday'][j],StartTime=datetime.time(16+i%3,30),EndTime=datetime.time(17+i%3,30));insert('RecurringSchedules',sc)
    for week in range(4):
     date=D+datetime.timedelta(days=(weekday-(D.weekday()+1)%7)%7+week*7);st=dict(base_session,IsActive=True,Id=mid(f'session/{i}/{j}/{week}'),TrainingGroupId=group['Id'],BranchId=branch,SportId=sport_id,AgeCategoryId=group['AgeCategoryId'],SessionDate=date,StartTime=sc['StartTime'],EndTime=sc['EndTime'],RecurringScheduleId=sc['Id'],Status='Scheduled');insert('TrainingSessions',st)
  # Keep original schedules aligned rather than showing inconsistent duplicate slots.
  for row in schedules:c.execute('UPDATE "RecurringSchedules" SET "IsActive"=false WHERE "Id"=%s',(row['Id'],))
  plans=allrows('SubscriptionPlans');plan=next(p for p in plans if p['PlanType']=='Combined');update('SubscriptionPlans',plan['Id'],ArabicName='باقة 12 حصة — شهر',Price=MONEY,DurationDays=30,SessionCount=12)
  plan.update(Price=MONEY,DurationDays=30,SessionCount=12,ArabicName='باقة 12 حصة — شهر')
  plans_by_sport={sport_ids[0]:plan}
  publications=allrows('PlanBranchPublications')
  for sid in sport_ids[1:]:
   cloned=dict(plan,Id=mid('plan/'+str(sid)),SportId=sid,ArabicName='باقة 12 حصة — '+('سباحة' if sid==sport_ids[1] else 'كرة السلة'))
   insert('SubscriptionPlans',cloned);plans_by_sport[sid]=cloned
   for pub in publications:
    if pub['SubscriptionPlanId']==plan['Id']:
     insert('PlanBranchPublications',dict(pub,Id=mid('publication/'+str(sid)+'/'+str(pub['BranchId'])),SubscriptionPlanId=cloned['Id']))
  period=next(p for p in allrows('SubscriptionPeriods')if p['CollectionId'] is not None)
  collection=allrows('Collections','"Id"=%s',(period['CollectionId'],))[0];renewal=allrows('RenewalRequests','"Id"=%s',(collection['RenewalRequestId'],))[0];payment=allrows('PaymentRequests','"Id"=%s',(collection['PaymentRequestId'],))[0];receipt=allrows('Receipts','"CollectionId"=%s',(collection['Id'],))[0]
  # Replace only the seeded current periods in this new, guarded fixture database.
  c.execute('UPDATE "SubscriptionPeriods" SET "IsActive"=false WHERE "AcademyId"=%s',(ACADEMY,))
  all_names=[]
  def paid_bundle(key,player,enroll,start,end,guardian,remaining):
   ids={k:mid(key+'/'+k)for k in ['renewal','payment','collection','receipt','period']};paid=datetime.datetime.combine(start,datetime.time(10,30),datetime.timezone.utc)
   rr=dict(renewal,Id=ids['renewal'],SportEnrollmentId=enroll['Id'],RequestedByUserId=guardian['UserId'],SportId=plan['SportId'],SubscriptionPlanId=plan['Id'],AmountExpected=MONEY,FinalAmount=MONEY,OriginalAmount=MONEY,PaymentRequestId=None,IdempotencyKey='promo-'+key,RequestedAtUtc=paid,CreatedAtUtc=paid,UpdatedAtUtc=paid);insert('RenewalRequests',rr)
   insert('PaymentRequests',dict(payment,Id=ids['payment'],RenewalRequestId=ids['renewal'],Amount=MONEY,ProviderReference='PROMO-'+str(ids['payment']),ConfirmedAtUtc=paid,CreatedAtUtc=paid,UpdatedAtUtc=paid));update('RenewalRequests',ids['renewal'],PaymentRequestId=ids['payment'])
   insert('Collections',dict(collection,Id=ids['collection'],SportEnrollmentId=enroll['Id'],RenewalRequestId=ids['renewal'],PaymentRequestId=ids['payment'],Amount=MONEY,ProviderReference='PROMO-'+str(ids['payment']),ConfirmedAtUtc=paid,CreatedAtUtc=paid,UpdatedAtUtc=paid))
   insert('Receipts',dict(receipt,Id=ids['receipt'],CollectionId=ids['collection'],ReceiptNumber='DEMO-'+ids['receipt'].hex,PlayerId=player['Id'],SportEnrollmentId=enroll['Id'],SubscriptionPlanId=plan['Id'],PlayerNameSnapshot=player['ArabicName'],SportNameSnapshot=('كرة القدم' if plan['SportId']==sport_ids[0] else 'السباحة' if plan['SportId']==sport_ids[1] else 'كرة السلة'),PlanNameSnapshot=plan['ArabicName'],Amount=MONEY,FinalAmount=MONEY,OriginalAmount=MONEY,PaidAtUtc=paid,ProviderReference='PROMO-'+str(ids['payment']),CreatedAtUtc=paid,UpdatedAtUtc=paid))
   insert('SubscriptionPeriods',dict(period,Id=ids['period'],SportEnrollmentId=enroll['Id'],SportId=plan['SportId'],SubscriptionPlanId=plan['Id'],CollectionId=ids['collection'],StartDate=start,EndDate=end,InitialSessions=12,RemainingSessions=remaining,PriceSnapshot=MONEY,PlanNameSnapshot='باقة 12 حصة',DurationDaysSnapshot=30,SessionCountSnapshot=12,PlanTypeSnapshot='Combined',Status='Active'if end>=D else'Expired',IsActive=True,CreatedAtUtc=paid,UpdatedAtUtc=paid))
  for i in range(300):
   group_index=i%9 if i<30 else i%18
   group=groups[group_index];plan=plans_by_sport[group['SportId']];year=2013+group_index%6;name=f'{given[i%20]} {fathers[(i//20)%15]} {families[(i//60)%15]}'
   if i==0:name='يوسف خالد سليم'
   if i==1:name='سليم خالد سليم'
   pl=players[i]if i<30 else dict(base_player,Id=mid(f'player/{i}'))
   values=dict(PlayerCode=f'PL{i+1:03d}',ArabicName=name,DateOfBirth=datetime.date(year,3,i%25+1),PhotoReference=f'/demo-assets/portraits/child-0{i+1}.jpg'if i<2 else None)
   if i>=30:pl.update(values);insert('Players',pl)
   else:update('Players',pl['Id'],**values);pl.update(values)
   en=byplayer[pl['Id']]if i<30 else dict(base_en,Id=mid(f'enrollment/{i}'),PlayerId=pl['Id'])
   ev=dict(TrainingGroupId=group['Id'],BranchId=group['BranchId'],SportId=group['SportId'],Status='Active')
   if i>=30:en.update(ev);insert('SportEnrollments',en)
   else:update('SportEnrollments',en['Id'],**ev);en.update(ev)
   guardian=guardians[i//2]
   oldlink=next((l for l in links if l['PlayerId']==pl['Id']),None)
   if oldlink:update('GuardianPlayerLinks',oldlink['Id'],GuardianId=guardian['Id'])
   else:insert('GuardianPlayerLinks',dict(links[0],Id=mid(f'link/{i}'),PlayerId=pl['Id'],GuardianId=guardian['Id']))
   start=D-datetime.timedelta(days=27 if i==0 or 250<=i<274 else 35+i%8 if i>=275 else i%18)
   paid_bundle(f'current/{i}',pl,en,start,start+datetime.timedelta(days=29),guardian,2 if i==0 or 250<=i<274 else 0 if i>=275 else 8+i%5)
   all_names.append({'code':values['PlayerCode'],'name':name,'birthYear':year,'group':group['ArabicName']})
   if i<20:
    for month in range(5,10):
     start=datetime.date(2026,month,1+i%10);paid_bundle(f'history/{month}/{i}',pl,en,start,start+datetime.timedelta(days=29),guardian,0)
  # Natural display names and synthetic adult portraits for the visible coaches.
  coaches=[m for m in members if m['Role']=='Coach']
  for i,m in enumerate(coaches):update('Users',m['UserId'],DisplayName=['محمود حسن','كريم عادل','حسام سامي','عمرو طارق'][i%4])
  products=allrows('SportCatalogItems');base_product=products[0]
  for i,(color,label,stock)in enumerate([('red','الأحمر',24),('turquoise','التركواز',18),('black','الأسود',12)]):
   r=dict(base_product,Id=base_product['Id']if i==0 else mid('product/'+color),ArabicName='طقم Professional — '+label,ArabicDescription='طقم التدريب الرسمي للأكاديمية.',ImageReference='/demo-assets/professional-kit-'+color+'.jpg',DisplayPrice=decimal.Decimal('650'),StockQuantity=stock,DisplayOrder=i+1,UsesVariants=False)
   if i==0:update('SportCatalogItems',r['Id'],**{k:v for k,v in r.items()if k!='Id'})
   else:insert('SportCatalogItems',r)
  # Price and histories are genuine coherent test records, not patched dashboard totals.
  c.execute('SELECT count(*) AS n FROM "Players" WHERE "AcademyId"=%s',(ACADEMY,));require(c.fetchone()['n']==300, 'Expected exactly 300 synthetic players')
  c.execute('SELECT count(*) AS n FROM "Sports" WHERE "AcademyId"=%s AND "IsActive"',(ACADEMY,));require(c.fetchone()['n']==3, 'Expected exactly three sports')
 (ROOT/'output/professional-demo-20261102').mkdir(parents=True,exist_ok=True)
 (ROOT/'output/professional-demo-20261102/dataset-preview.json').write_text(json.dumps({'fictional':True,'database':DB,'academy':str(ACADEMY),'players':300,'branches':['مدينتي','الشروق','الرحاب'],'sports':['كرة القدم','السباحة','كرة السلة'],'sample':all_names[:12]},ensure_ascii=False,indent=2))
 print('Prepared 300 fictional players, 150 guardians, 18 groups, 3 branches, 3 sports; linked payments and history in isolated DB.')

prepare()
