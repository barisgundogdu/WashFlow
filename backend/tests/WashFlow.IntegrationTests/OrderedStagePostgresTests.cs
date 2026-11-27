using Microsoft.EntityFrameworkCore;
using Npgsql;
using WashFlow.Application.Scheduling;
using WashFlow.Domain.Catalog;
using WashFlow.Domain.Scheduling;
namespace WashFlow.IntegrationTests;

public sealed partial class SchedulingPostgresTests
{
    private async Task<(WorkAreaView Second,Guid[] Staff,StageTemplatesView Config)> Staged(SeedData s)
    {
        await using var db=Db();await db.ServiceOfferings.Where(x=>x.ServiceId==s.Service).ExecuteUpdateAsync(x=>x.SetProperty(o=>o.Employees,2));
        var e=Service(db);var area=await e.SaveWorkArea(s.Actor,s.Business,null,new("İç alan",1,null));
        var requirement=(await e.Requirements(s.Actor,s.Business)).Single();
        var config=await e.SaveStages(s.Actor,s.Business,s.Service,"Car",new(requirement.Version,[new("Dış yıkama",10,s.Area.Id,1),new("İç temizlik",20,area.Id,2)]));
        return(area,s.Employees.Take(2).Order().ToArray(),config);
    }
    private async Task<(Guid Id,uint Version)> SimpleStageService(SeedData s,Guid area,int duration=10)
    {
        await using var db=Db();var service=new BusinessService{BusinessId=s.Business,Name="Ek hizmet",SearchName="EK HİZMET",Enabled=true};
        db.AddRange(service,new ServiceOffering{ServiceId=service.Id,Category="Car",Currency="TRY",Price=100m,ActiveMinutes=duration,Employees=1});await db.SaveChangesAsync();
        var version=db.Entry(service).Property<uint>("Version").CurrentValue;
        await Service(db).SaveRequirement(s.Actor,s.Business,service.Id,"Car",new(area,true,null));return(service.Id,version);
    }
    [PostgresFact]
    public async Task StagedBookingReservesEachAreaAndRequiredEmployeesOnlyForItsOwnInterval()
    {
        var s=await Seed();var plan=await Staged(s);await using var db=Db();var e=Service(db);
        var second=Array.IndexOf(s.Employees,plan.Staff[0]);var window=s.Windows[second];
        await e.SaveAvailability(s.Actor,s.Business,plan.Staff[0],window.Id,new(Now.AddDays(1).AddHours(10).AddMinutes(10),Now.AddDays(1).AddHours(20),window.Version));
        var booked=(await Create(s,s.Booking() with{EmployeeMembershipIds=plan.Staff})).Value;
        Assert.Equal(2,booked.Stages!.Count);Assert.Equal(5,booked.Reservations.Count);
        var first=booked.Stages[0];var last=booked.Stages[1];
        Assert.Equal(plan.Staff[1],booked.Reservations.Single(x=>x.StageId==first.Id&&x.EmployeeMembershipId!=null).EmployeeMembershipId);Assert.Equal(0,first.OffsetMinutes);Assert.Equal(10,last.OffsetMinutes);
        Assert.All(booked.Reservations.Where(x=>x.StageId==first.Id),x=>{Assert.Equal(booked.StartsAtUtc,x.StartsAtUtc);Assert.Equal(booked.StartsAtUtc.AddMinutes(10),x.EndsAtUtc);});
        Assert.Equal(s.Area.Id,first.WorkAreaId);Assert.Equal(plan.Second.Id,last.WorkAreaId);
        var simple=await SimpleStageService(s,plan.Second.Id);
        var spare=s.Booking() with{ServiceId=simple.Id,ExpectedServiceVersion=simple.Version,EmployeeMembershipIds=[s.Employees[2]]};
        Assert.True((await Create(s,spare)).Created); // the interior area is free during the exterior stage
        await Fault("resourceConflict",()=>Create(s,spare with{ClientRequestId=Guid.NewGuid(),StartLocal="2030-01-02T10:10"}));
        var exterior=await SimpleStageService(s,s.Area.Id);
        Assert.True((await Create(s,spare with{ClientRequestId=Guid.NewGuid(),ServiceId=exterior.Id,ExpectedServiceVersion=exterior.Version,StartLocal="2030-01-02T10:10"})).Created);
        var error=await Assert.ThrowsAsync<PostgresException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"AppointmentStages\" SET \"Name\" = 'Changed' WHERE \"Id\" = {first.Id}"));Assert.Equal(PostgresErrorCodes.CheckViolation,error.SqlState);
    }
    [PostgresFact]
    public async Task StagedRescheduleConflictPreservesOriginalAndSuccessMovesAllStageReservations()
    {
        var s=await Seed();var plan=await Staged(s);var original=(await Create(s,s.Booking() with{EmployeeMembershipIds=plan.Staff})).Value;
        var simple=await SimpleStageService(s,plan.Second.Id);await Create(s,s.Booking("2030-01-02T10:40",2) with{ServiceId=simple.Id,ExpectedServiceVersion=simple.Version});
        await using var db=Db();var e=Service(db);
        await Fault("resourceConflict",()=>e.Reschedule(s.Actor,s.Business,original.Id,new(original.Version,"2030-01-02T10:20",null,plan.Staff)));
        var unchanged=await e.Detail(s.Actor,s.Business,original.Id);Assert.Equal(original.Version,unchanged.Version);Assert.All(unchanged.Reservations,r=>Assert.Null(r.ReleasedAt));Assert.Single(unchanged.Events);
        var moved=await e.Reschedule(s.Actor,s.Business,original.Id,new(original.Version,"2030-01-02T11:00",null,plan.Staff));
        Assert.Equal(original.Stages!.Select(x=>x.Id),moved.Stages!.Select(x=>x.Id));Assert.Equal(10,moved.Reservations.Count);Assert.Equal(5,moved.Reservations.Count(x=>x.ReleasedAt==null));
        var second=moved.Stages![1];Assert.All(moved.Reservations.Where(x=>x.ReleasedAt==null&&x.StageId==second.Id),x=>Assert.Equal(moved.StartsAtUtc.AddMinutes(10),x.StartsAtUtc));
        var cancelled=await e.Cancel(s.Actor,s.Business,moved.Id,new(moved.Version));Assert.All(cancelled.Reservations,r=>Assert.NotNull(r.ReleasedAt));
        Assert.True((await Create(s,s.Booking("2030-01-02T11:00") with{EmployeeMembershipIds=plan.Staff})).Created);
    }
    [PostgresFact]
    public async Task StagedPlanEditsAreVersionedArchivedAndCannotInvalidateConfirmedReservations()
    {
        var s=await Seed();var plan=await Staged(s);await using var db=Db();var e=Service(db);var version=plan.Config.Requirement!.Version;
        var input=new SaveStageTemplatesInput(version,[new("Dış",15,s.Area.Id,1),new("İç",15,plan.Second.Id,2)]);
        var changed=await e.SaveStages(s.Actor,s.Business,s.Service,"Car",input);Assert.NotEqual(version,changed.Requirement!.Version);
        Assert.Equal(4,await db.ServiceStageTemplates.CountAsync(x=>x.BusinessId==s.Business));Assert.Equal(2,await db.ServiceStageTemplates.CountAsync(x=>x.BusinessId==s.Business&&x.ArchivedAt!=null));
        await Fault("scheduleConflict",()=>e.SaveStages(s.Actor,s.Business,s.Service,"Car",input));
        var booked=(await Create(s,s.Booking() with{EmployeeMembershipIds=plan.Staff})).Value;
        await Fault("requirementInUse",()=>e.SaveStages(s.Actor,s.Business,s.Service,"Car",input with{Version=changed.Requirement.Version}));
        await Fault("requirementInUse",()=>e.DisableRequirement(s.Actor,s.Business,s.Service,"Car",new(changed.Requirement.Version)));
        Assert.Equal("Dış",(await e.Detail(s.Actor,s.Business,booked.Id)).Stages![0].Name);
    }
    [PostgresFact]
    public async Task StagedConcurrentRequestsHaveOneWinnerAndFullStageCoverageIsRequired()
    {
        var s=await Seed();var plan=await Staged(s);var booking=s.Booking() with{EmployeeMembershipIds=plan.Staff};
        var results=await Concurrent(s,booking,booking with{ClientRequestId=Guid.NewGuid()});Assert.Single(results,x=>x.Value!=null);Assert.Single(results,x=>x.Code=="resourceConflict");
        var other=await Seed();var otherPlan=await Staged(other);await using var db=Db();var e=Service(db);
        var staff=otherPlan.Staff[1];var index=Array.IndexOf(other.Employees,staff);var window=other.Windows[index];
        await e.SaveAvailability(other.Actor,other.Business,staff,window.Id,new(Now.AddDays(1).AddHours(10).AddMinutes(11),Now.AddDays(1).AddHours(20),window.Version));
        await Fault("resourceConflict",()=>Create(other,other.Booking() with{EmployeeMembershipIds=otherPlan.Staff}));
    }
    [PostgresFact]
    public async Task StagedConfigurationCanStartWithoutLegacyRequirementAndForeignAreaNeverPartiallyWrites()
    {
        var s=await Seed();var foreign=await Seed();await using var db=Db();
        var service=new BusinessService{BusinessId=s.Business,Name="Yeni hizmet",SearchName="YENİ HİZMET",Enabled=true};
        db.AddRange(service,new ServiceOffering{ServiceId=service.Id,Category="Car",Currency="TRY",Price=100m,ActiveMinutes=30,Employees=1});await db.SaveChangesAsync();
        var e=Service(db);var input=new SaveStageTemplatesInput(null,[new("Yıkama",30,foreign.Area.Id,1)]);
        await Fault("resourceNotFound",()=>e.SaveStages(s.Actor,s.Business,service.Id,"Car",input));
        Assert.False(await db.ServiceSchedulingRequirements.AnyAsync(x=>x.ServiceId==service.Id));Assert.False(await db.ServiceStageTemplates.AnyAsync(x=>x.ServiceId==service.Id));
        var saved=await e.SaveStages(s.Actor,s.Business,service.Id,"Car",input with{Stages=[new("Yıkama",30,s.Area.Id,1)]});
        Assert.True(saved.Requirement!.StagePlanEnabled);Assert.False(saved.Requirement.SingleStageConfirmed);Assert.Single(saved.Stages);
        await Fault("scheduleConflict",()=>e.SaveStages(s.Actor,s.Business,service.Id,"Car",input with{Stages=[new("Yıkama",30,s.Area.Id,1)]}));
    }

}
