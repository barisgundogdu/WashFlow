using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WashFlow.Application.Finance;
using WashFlow.Application.Scheduling;
using WashFlow.Domain.Businesses;
using WashFlow.Domain.Finance;
using WashFlow.Infrastructure.Persistence;
namespace WashFlow.Infrastructure.Finance;

/// <summary>Owner-only ledger operations serialize with scheduling on the business row.
/// Completed snapshot value is earned revenue; payments never add to it a second time.</summary>
public sealed class EfFinanceService(WashFlowDbContext db,TimeProvider clock):IFinanceService
{
    [System.Diagnostics.CodeAnalysis.DoesNotReturn]
    private static void Fail(string code,string message,int status=409)=>throw new SchedulingFault(status,code,message);
    private static T Found<T>(T? value)where T:class=>value??throw new SchedulingFault(404,"resourceNotFound","Kayıt bulunamadı.");
    private static string Money(decimal amount)=>amount.ToString("0.00",CultureInfo.InvariantCulture);
    private static string Hash(object input)=>Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(input))));
    private static void Page(int page){if(page is<1 or>10000)Fail("validation","Geçerli sayfa seçin.",400);}
    private async Task<T> Run<T>(SchedulingActor actor,Guid business,Func<Business,Task<T>> operation,CancellationToken ct)
    {
        if(db.Database.CurrentTransaction!=null||db.ChangeTracker.HasChanges())throw new InvalidOperationException("Bağımsız finans işlemi gereklidir.");
        db.ChangeTracker.Clear();await using var tx=await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted,ct);
        try
        {
            var b=await db.Businesses.FromSqlInterpolated($"SELECT *, xmin FROM \"Businesses\" WHERE \"Id\" = {business} FOR UPDATE").SingleOrDefaultAsync(ct);
            if(b==null)Fail("forbidden","İşletme sahibi yetkisi gereklidir.",403);
            var member=await db.BusinessMemberships.FromSqlInterpolated($"SELECT *, xmin FROM \"BusinessMemberships\" WHERE \"BusinessId\" = {business} AND \"UserId\" = {actor.UserId} FOR UPDATE").AsNoTracking().SingleOrDefaultAsync(ct);
            if(member==null||member.RevokedAt!=null)Fail("forbidden","İşletme sahibi yetkisi gereklidir.",403);
            var roles=await db.BusinessMembershipRoles.FromSqlInterpolated($"SELECT * FROM \"BusinessMembershipRoles\" WHERE \"BusinessMembershipId\" = {member.Id} AND \"Role\" = 1 FOR UPDATE").AsNoTracking().ToListAsync(ct);
            if(roles.Count!=1)Fail("forbidden","İşletme sahibi yetkisi gereklidir.",403);
            var user=await db.Users.FromSqlInterpolated($"SELECT *, xmin FROM \"Users\" WHERE \"Id\" = {actor.UserId} FOR SHARE").AsNoTracking().SingleOrDefaultAsync(ct);
            var session=await db.DeviceSessions.FromSqlInterpolated($"SELECT *, xmin FROM \"DeviceSessions\" WHERE \"Id\" = {actor.SessionId} AND \"UserId\" = {actor.UserId} FOR SHARE").AsNoTracking().SingleOrDefaultAsync(ct);
            var now=clock.GetUtcNow();if(user==null||user.EmailVerifiedAt==null||session==null||session.RevokedAt!=null||session.CreatedAt>now||session.ExpiresAt<=now||session.SecurityStamp!=user.SecurityStamp)Fail("unauthorized","Oturum geçersiz veya süresi dolmuş.",401);
            var result=await operation(b!);await tx.CommitAsync(ct);return result;
        }
        catch(DbUpdateConcurrencyException){throw new SchedulingFault(409,"scheduleConflict","Kayıt değişti. Güncel verileri yükleyin.");}
        finally{db.ChangeTracker.Clear();}
    }
    private uint Version<T>(T row)where T:class=>db.Entry(row).Property<uint>("Version").CurrentValue;
    private PaymentMethodView MethodView(PaymentMethod m)=>new(m.Id,m.Name,m.ArchivedAtUtc!=null,Version(m));
    private static string Text(string? value,int max,bool required=false)
    {var s=value?.Trim()??"";if(s.Length>max||s.Any(char.IsControl)||(required&&s.Length==0))Fail("validation","Alanları ve metin uzunluklarını kontrol edin.",400);return s;}
    public Task<FinanceSettings> Settings(SchedulingActor actor,Guid business,CancellationToken ct=default)=>Run(actor,business,async _=>
    {
        if(!await db.PaymentMethods.AnyAsync(x=>x.BusinessId==business,ct))
        {foreach(var name in new[]{"Nakit","Kart","Banka havalesi"})db.Add(new PaymentMethod{BusinessId=business,Name=name,ActorUserId=actor.UserId,CreatedAt=clock.GetUtcNow()});await db.SaveChangesAsync(ct);}
        var methods=await db.PaymentMethods.Where(x=>x.BusinessId==business).OrderBy(x=>x.Name).ToListAsync(ct);
        var employees=await(from m in db.BusinessMemberships.AsNoTracking() join role in db.BusinessMembershipRoles.AsNoTracking() on m.Id equals role.BusinessMembershipId join u in db.Users.AsNoTracking() on m.UserId equals u.Id
            where m.BusinessId==business&&m.RevokedAt==null&&role.Role==MembershipRole.Employee orderby u.FullName,m.Id select new FinanceEmployee(m.Id,u.FullName)).ToListAsync(ct);
        return new FinanceSettings(methods.Select(MethodView).ToArray(),employees);
    },ct);
    public Task<PaymentMethodView> AddMethod(SchedulingActor actor,Guid business,MethodInput input,CancellationToken ct=default)=>Run(actor,business,async _=>
    {
        var name=Text(input.Name,100,true);var old=await db.PaymentMethods.SingleOrDefaultAsync(x=>x.BusinessId==business&&x.Name==name,ct);
        if(old!=null){if(old.ArchivedAtUtc!=null)Fail("methodArchived","Bu yöntem arşivlenmiş. Farklı bir ad kullanın.");return MethodView(old);}
        var row=new PaymentMethod{BusinessId=business,Name=name,ActorUserId=actor.UserId,CreatedAt=clock.GetUtcNow()};db.Add(row);await db.SaveChangesAsync(ct);return MethodView(row);
    },ct);
    public Task<PaymentMethodView> ArchiveMethod(SchedulingActor actor,Guid business,Guid id,VersionInput input,CancellationToken ct=default)=>Run(actor,business,async _=>
    {
        var row=Found(await db.PaymentMethods.SingleOrDefaultAsync(x=>x.BusinessId==business&&x.Id==id,ct));if(row.ArchivedAtUtc!=null)return MethodView(row);
        SchedulingRules.RequireVersion(Version(row),input.Version);row.ArchivedAtUtc=clock.GetUtcNow();row.UpdatedAt=clock.GetUtcNow();await db.SaveChangesAsync(ct);return MethodView(row);
    },ct);
    private static FinanceEntryView View(Payment x)=>new(x.Id,x.BusinessId,"payments",Money(x.Amount),x.Currency,x.OccurredAtUtc,null,x.Description,x.Reference,x.WorkOrderId,x.PaymentMethodId,x.PaymentMethodName,null,x.ReversesId,x.CorrectsId,x.CorrectionReason,x.ActorUserId,x.CreatedAt);
    private static FinanceEntryView View(ManualIncome x)=>new(x.Id,x.BusinessId,"manual-income",Money(x.Amount),x.Currency,x.OccurredAtUtc,x.Category,x.Description,x.Reference,null,x.PaymentMethodId,x.PaymentMethodName,null,x.ReversesId,x.CorrectsId,x.CorrectionReason,x.ActorUserId,x.CreatedAt);
    private static FinanceEntryView View(Expense x)=>new(x.Id,x.BusinessId,"expenses",Money(x.Amount),x.Currency,x.OccurredAtUtc,x.Category,x.Description,x.Reference,null,null,null,x.EmployeeMembershipId,x.ReversesId,x.CorrectsId,x.CorrectionReason,x.ActorUserId,x.CreatedAt);
    private static void Kind(string kind){if(kind is not("payments" or"manual-income" or"expenses"))Fail("validation","Geçerli finans kaydı türü seçin.",400);}
    private async Task<decimal> Collected(Guid business,Guid order,CancellationToken ct)=>await db.Payments.Where(x=>x.BusinessId==business&&x.WorkOrderId==order).SumAsync(x=>(decimal?)(x.ReversesId==null?x.Amount:-x.Amount),ct)??0;
    public Task<IReadOnlyList<ReceivableView>> Receivables(SchedulingActor actor,Guid business,string status="unpaid",int page=1,CancellationToken ct=default)=>Run<IReadOnlyList<ReceivableView>>(actor,business,async _=>
    {
        Page(page);if(status is not("all" or"unpaid"))Fail("validation","Geçerli ödeme durumu seçin.",400);
        var query=from w in db.WorkOrders.AsNoTracking() join a in db.Appointments.AsNoTracking() on new{w.BusinessId,Id=w.AppointmentId} equals new{a.BusinessId,a.Id}
            where w.BusinessId==business&&w.CompletedAtUtc!=null&&w.CancelledAtUtc==null select new{w.Id,w.CompletedAtUtc,a.VehiclePlate,a.ServiceName,a.Price};
        if(status=="unpaid")query=query.Where(w=>w.Price>(db.Payments.Where(p=>p.BusinessId==business&&p.WorkOrderId==w.Id).Sum(p=>(decimal?)(p.ReversesId==null?p.Amount:-p.Amount))??0));
        var rows=await query.OrderByDescending(x=>x.CompletedAtUtc).ThenBy(x=>x.Id).Skip((page-1)*20).Take(20).ToListAsync(ct);var result=new List<ReceivableView>();
        foreach(var r in rows){var paid=await Collected(business,r.Id,ct);result.Add(new(r.Id,r.VehiclePlate,r.ServiceName,r.CompletedAtUtc!.Value,Money(r.Price),Money(paid),Money(r.Price-paid),paid==0?"Unpaid":paid<r.Price?"Partial":"Paid"));}return result;
    },ct);
    public Task<IReadOnlyList<FinanceEntryView>> Entries(SchedulingActor actor,Guid business,string kind,int page=1,CancellationToken ct=default)=>Run<IReadOnlyList<FinanceEntryView>>(actor,business,async _=>
    {
        Kind(kind);Page(page);var skip=(page-1)*20;
        return kind switch{
            "payments"=>(await db.Payments.Where(x=>x.BusinessId==business).OrderByDescending(x=>x.CreatedAt).ThenBy(x=>x.Id).Skip(skip).Take(20).ToListAsync(ct)).Select(View).ToArray(),
            "manual-income"=>(await db.ManualIncomes.Where(x=>x.BusinessId==business).OrderByDescending(x=>x.CreatedAt).ThenBy(x=>x.Id).Skip(skip).Take(20).ToListAsync(ct)).Select(View).ToArray(),
            _=>(await db.Expenses.Where(x=>x.BusinessId==business).OrderByDescending(x=>x.CreatedAt).ThenBy(x=>x.Id).Skip(skip).Take(20).ToListAsync(ct)).Select(View).ToArray()};
    },ct);
    private async Task<(FinanceEntryView View,string Hash)?> Prior(Guid business,string kind,Guid request,CancellationToken ct)
    {
        if(kind=="payments"){var x=await db.Payments.SingleOrDefaultAsync(x=>x.BusinessId==business&&x.RequestId==request,ct);return x==null?null:(View(x),x.RequestHash);}
        if(kind=="manual-income"){var x=await db.ManualIncomes.SingleOrDefaultAsync(x=>x.BusinessId==business&&x.RequestId==request,ct);return x==null?null:(View(x),x.RequestHash);}
        var e=await db.Expenses.SingleOrDefaultAsync(x=>x.BusinessId==business&&x.RequestId==request,ct);return e==null?null:(View(e),e.RequestHash);
    }
    public Task<FinanceEntryView> Record(SchedulingActor actor,Guid business,string kind,FinanceEntryInput input,CancellationToken ct=default)=>Run(actor,business,async _=>
    {
        Kind(kind);if(input.RequestId==Guid.Empty||input.OccurredAtUtc>clock.GetUtcNow()||input.OccurredAtUtc.Year<2000)Fail("validation","İstek ve kayıt tarihini kontrol edin.",400);
        var hash=Hash(new{kind,Input=input with{OccurredAtUtc=input.OccurredAtUtc.ToUniversalTime()}});var prior=await Prior(business,kind,input.RequestId,ct);if(prior is{}p){if(p.Hash!=hash)Fail("idempotencyConflict","İstek anahtarı başka kayıt için kullanılmış.");return p.View;}
        decimal amount=0;
        if(!System.Text.RegularExpressions.Regex.IsMatch(input.Amount??"",@"^(0|[1-9][0-9]{0,6})(\.[0-9]{1,2})?$")||!decimal.TryParse(input.Amount,NumberStyles.AllowDecimalPoint,CultureInfo.InvariantCulture,out amount)||amount<=0||amount>9999999.99m||decimal.Round(amount,2)!=amount)Fail("validation","Tutar 0,01 ile 9.999.999,99 TRY arasında ve en fazla iki ondalıklı olmalıdır.",400);
        var description=Text(input.Description,1000);var reference=Text(input.Reference,200);var category=kind=="payments"?null:Text(input.Category,100,true);
        PaymentMethod? method=null;if(kind!="expenses")method=Found(await db.PaymentMethods.SingleOrDefaultAsync(x=>x.BusinessId==business&&x.Id==input.PaymentMethodId&&x.ArchivedAtUtc==null,ct));
        if(kind=="expenses"&&(input.WorkOrderId!=null||input.PaymentMethodId!=null)||kind!="expenses"&&input.EmployeeMembershipId!=null||kind!="payments"&&input.WorkOrderId!=null)Fail("validation","Bu kayıt türüne ait alanları kullanın.",400);
        if(input.EmployeeMembershipId is{}employee&&!await db.BusinessMemberships.AnyAsync(x=>x.Id==employee&&x.BusinessId==business&&x.RevokedAt==null&&db.BusinessMembershipRoles.Any(r=>r.BusinessMembershipId==x.Id&&r.Role==MembershipRole.Employee),ct))Fail("resourceNotFound","Çalışan bulunamadı.",404);
        var now=clock.GetUtcNow();
        if(input.CorrectsId is{}corrects){if(kind!="expenses")Fail("validation","Ödeme ve ek gelir düzeltmesinde önce ters kayıt oluşturun.",400);await ReverseCore(actor,business,kind,corrects,new(Guid.NewGuid(),Text(input.CorrectionReason,500,true)),ct);}
        else if(!string.IsNullOrWhiteSpace(input.CorrectionReason))Fail("validation","Düzeltilecek kayıt seçin.",400);
        object entity;
        if(kind=="payments")
        {
            var work=Found(await db.WorkOrders.SingleOrDefaultAsync(x=>x.BusinessId==business&&x.Id==input.WorkOrderId,ct));if(work.CompletedAtUtc==null||work.CancelledAtUtc!=null)Fail("workNotCompleted","Ödeme yalnızca tamamlanan iş emrine kaydedilebilir.");
            var price=await db.Appointments.Where(x=>x.BusinessId==business&&x.Id==work.AppointmentId).Select(x=>x.Price).SingleAsync(ct);if(amount>price-await Collected(business,work.Id,ct))Fail("overpayment","Ödeme kalan borcu aşamaz.");
            entity=new Payment{BusinessId=business,RequestId=input.RequestId,RequestHash=hash,Amount=amount,OccurredAtUtc=input.OccurredAtUtc.ToUniversalTime(),CreatedAt=now,ActorUserId=actor.UserId,Description=description,Reference=reference,WorkOrderId=work.Id,PaymentMethodId=method!.Id,PaymentMethodName=method.Name};
        }
        else if(kind=="manual-income")entity=new ManualIncome{BusinessId=business,RequestId=input.RequestId,RequestHash=hash,Amount=amount,OccurredAtUtc=input.OccurredAtUtc.ToUniversalTime(),CreatedAt=now,ActorUserId=actor.UserId,Description=description,Reference=reference,Category=category!,PaymentMethodId=method!.Id,PaymentMethodName=method.Name};
        else entity=new Expense{BusinessId=business,RequestId=input.RequestId,RequestHash=hash,Amount=amount,OccurredAtUtc=input.OccurredAtUtc.ToUniversalTime(),CreatedAt=now,ActorUserId=actor.UserId,Description=description,Reference=reference,Category=category!,EmployeeMembershipId=input.EmployeeMembershipId,CorrectsId=input.CorrectsId};
        db.Add(entity);await db.SaveChangesAsync(ct);return entity switch{Payment payment=>View(payment),ManualIncome income=>View(income),Expense expense=>View(expense),_=>throw new InvalidOperationException()};
    },ct);
    public Task<FinanceEntryView> Reverse(SchedulingActor actor,Guid business,string kind,Guid id,ReverseEntryInput input,CancellationToken ct=default)=>Run(actor,business,_=>ReverseCore(actor,business,kind,id,input,ct),ct);
    private async Task<FinanceEntryView> ReverseCore(SchedulingActor actor,Guid business,string kind,Guid id,ReverseEntryInput input,CancellationToken ct)
    {
        Kind(kind);if(input.RequestId==Guid.Empty)Fail("validation","Geçerli istek anahtarı gereklidir.",400);var reason=Text(input.Reason,500,true);var hash=Hash(new{kind,id,Reason=reason,Action="reverse"});var prior=await Prior(business,kind,input.RequestId,ct);
        if(prior is{}p){if(p.Hash!=hash)Fail("idempotencyConflict","İstek anahtarı başka işlem için kullanılmış.");return p.View;}
        FinanceEntryView original;bool reversed;
        if(kind=="payments"){original=View(Found(await db.Payments.SingleOrDefaultAsync(x=>x.BusinessId==business&&x.Id==id,ct)));reversed=await db.Payments.AnyAsync(x=>x.BusinessId==business&&x.ReversesId==id,ct);}
        else if(kind=="manual-income"){original=View(Found(await db.ManualIncomes.SingleOrDefaultAsync(x=>x.BusinessId==business&&x.Id==id,ct)));reversed=await db.ManualIncomes.AnyAsync(x=>x.BusinessId==business&&x.ReversesId==id,ct);}
        else{original=View(Found(await db.Expenses.SingleOrDefaultAsync(x=>x.BusinessId==business&&x.Id==id,ct)));reversed=await db.Expenses.AnyAsync(x=>x.BusinessId==business&&x.ReversesId==id,ct);}
        if(original.ReversesId!=null||reversed)Fail("entryReversed","Kayıt zaten ters çevrilmiş veya kendisi ters kayıttır.");
        var amount=decimal.Parse(original.Amount,CultureInfo.InvariantCulture);var now=clock.GetUtcNow();object row=kind switch{
            "payments"=>new Payment{BusinessId=business,RequestId=input.RequestId,RequestHash=hash,Amount=amount,OccurredAtUtc=original.OccurredAtUtc,CreatedAt=now,ActorUserId=actor.UserId,Description=original.Description,Reference=original.Reference,ReversesId=id,CorrectsId=original.CorrectsId,CorrectionReason=reason,WorkOrderId=original.WorkOrderId!.Value,PaymentMethodId=original.PaymentMethodId!.Value,PaymentMethodName=original.PaymentMethodName!},
            "manual-income"=>new ManualIncome{BusinessId=business,RequestId=input.RequestId,RequestHash=hash,Amount=amount,OccurredAtUtc=original.OccurredAtUtc,CreatedAt=now,ActorUserId=actor.UserId,Description=original.Description,Reference=original.Reference,ReversesId=id,CorrectsId=original.CorrectsId,CorrectionReason=reason,Category=original.Category!,PaymentMethodId=original.PaymentMethodId!.Value,PaymentMethodName=original.PaymentMethodName!},
            _=>new Expense{BusinessId=business,RequestId=input.RequestId,RequestHash=hash,Amount=amount,OccurredAtUtc=original.OccurredAtUtc,CreatedAt=now,ActorUserId=actor.UserId,Description=original.Description,Reference=original.Reference,ReversesId=id,CorrectsId=original.CorrectsId,CorrectionReason=reason,Category=original.Category!,EmployeeMembershipId=original.EmployeeMembershipId}};
        db.Add(row);await db.SaveChangesAsync(ct);return row switch{Payment pmt=>View(pmt),ManualIncome income=>View(income),Expense expense=>View(expense),_=>throw new InvalidOperationException()};
    }
    private static DateTimeOffset Boundary(DateOnly date,TimeZoneInfo zone)
    {
        var wall=DateTime.SpecifyKind(date.ToDateTime(TimeOnly.MinValue),DateTimeKind.Unspecified);
        // A skipped midnight starts at the first valid instant; a fold includes both occurrences.
        for(var i=0;zone.IsInvalidTime(wall)&&i<1440;i++)wall=wall.AddMinutes(1);
        var offset=zone.IsAmbiguousTime(wall)?zone.GetAmbiguousTimeOffsets(wall).Max():zone.GetUtcOffset(wall);
        return new DateTimeOffset(wall,offset).ToUniversalTime();
    }
    public Task<FinanceReport> Report(SchedulingActor actor,Guid business,DateOnly? from=null,DateOnly? to=null,CancellationToken ct=default)=>Run(actor,business,async b=>
    {
        var zone=SchedulingRules.Zone(b.TimeZoneId);var today=DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.GetUtcNow(),zone).DateTime);var first=from??today;if(first.Year is<2000 or>2100)Fail("validation","Rapor tarihini kontrol edin.",400);var last=to??first.AddDays(1);
        if(first.Year<2000||last.Year>2100||last<=first||last.DayNumber-first.DayNumber>366)Fail("validation","Rapor dönemi 1 ile 366 gün arasında olmalıdır.",400);
        var start=Boundary(first,zone);var end=Boundary(last,zone);
        var earned=await(from w in db.WorkOrders.AsNoTracking() join a in db.Appointments.AsNoTracking() on new{w.BusinessId,Id=w.AppointmentId} equals new{a.BusinessId,a.Id}
            where w.BusinessId==business&&w.CompletedAtUtc>=start&&w.CompletedAtUtc<end&&w.CancelledAtUtc==null select new{At=w.CompletedAtUtc!.Value,a.ServiceName,a.Price}).ToListAsync(ct);
        var payments=await db.Payments.AsNoTracking().Where(x=>x.BusinessId==business&&x.OccurredAtUtc>=start&&x.OccurredAtUtc<end).Select(x=>new{x.OccurredAtUtc,x.Amount,x.ReversesId,x.PaymentMethodName}).ToListAsync(ct);
        var incomes=await db.ManualIncomes.AsNoTracking().Where(x=>x.BusinessId==business&&x.OccurredAtUtc>=start&&x.OccurredAtUtc<end).Select(x=>new{x.OccurredAtUtc,x.Amount,x.ReversesId,x.PaymentMethodName}).ToListAsync(ct);
        var expenses=await db.Expenses.AsNoTracking().Where(x=>x.BusinessId==business&&x.OccurredAtUtc>=start&&x.OccurredAtUtc<end).Select(x=>new{x.OccurredAtUtc,x.Amount,x.ReversesId,x.Category}).ToListAsync(ct);
        var totalEarned=earned.Sum(x=>x.Price);var collected=payments.Sum(x=>x.ReversesId==null?x.Amount:-x.Amount);var manual=incomes.Sum(x=>x.ReversesId==null?x.Amount:-x.Amount);var spent=expenses.Sum(x=>x.ReversesId==null?x.Amount:-x.Amount);
        var allEarned=await(from w in db.WorkOrders.AsNoTracking() join a in db.Appointments.AsNoTracking() on new{w.BusinessId,Id=w.AppointmentId} equals new{a.BusinessId,a.Id} where w.BusinessId==business&&w.CompletedAtUtc!=null&&w.CancelledAtUtc==null select(decimal?)a.Price).SumAsync(ct)??0;
        var allCollected=await db.Payments.Where(x=>x.BusinessId==business).SumAsync(x=>(decimal?)(x.ReversesId==null?x.Amount:-x.Amount),ct)??0;
        var serviceBuckets=earned.GroupBy(x=>x.ServiceName).OrderBy(x=>x.Key).Select(x=>new FinanceBucket(x.Key,Money(x.Sum(r=>r.Price)))).ToArray();
        var expenseBuckets=expenses.GroupBy(x=>x.Category).OrderBy(x=>x.Key).Select(x=>new FinanceBucket(x.Key,Money(x.Sum(r=>r.ReversesId==null?r.Amount:-r.Amount)))).ToArray();
        var methodBuckets=payments.Select(x=>(Name:x.PaymentMethodName,Amount:x.ReversesId==null?x.Amount:-x.Amount)).Concat(incomes.Select(x=>(Name:x.PaymentMethodName,Amount:x.ReversesId==null?x.Amount:-x.Amount))).GroupBy(x=>x.Name).OrderBy(x=>x.Key).Select(x=>new FinanceBucket(x.Key,Money(x.Sum(r=>r.Amount)))).ToArray();
        DateOnly Day(DateTimeOffset instant)=>DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant,zone).DateTime);
        var trends=Enumerable.Range(0,last.DayNumber-first.DayNumber).Select(i=>{var day=first.AddDays(i);return new FinanceTrend(day.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture),Money(earned.Where(x=>Day(x.At)==day).Sum(x=>x.Price)),Money(payments.Where(x=>Day(x.OccurredAtUtc)==day).Sum(x=>x.ReversesId==null?x.Amount:-x.Amount)),Money(incomes.Where(x=>Day(x.OccurredAtUtc)==day).Sum(x=>x.ReversesId==null?x.Amount:-x.Amount)),Money(expenses.Where(x=>Day(x.OccurredAtUtc)==day).Sum(x=>x.ReversesId==null?x.Amount:-x.Amount)));}).ToArray();
        return new FinanceReport(first,last,today.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture),zone.Id,"TRY",Money(totalEarned),Money(collected),Money(manual),Money(spent),Money(totalEarned+manual-spent),Money(collected+manual-spent),Money(allEarned-allCollected),serviceBuckets,expenseBuckets,methodBuckets,trends);
    },ct);
}
