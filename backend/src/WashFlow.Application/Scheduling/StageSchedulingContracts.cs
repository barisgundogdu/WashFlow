namespace WashFlow.Application.Scheduling;
public sealed record StageTemplateInput(string Name, int Minutes, Guid WorkAreaId, int Employees);
public sealed record SaveStageTemplatesInput(uint? Version, IReadOnlyList<StageTemplateInput>? Stages);
public sealed record StageTemplateView(Guid Id, Guid BusinessId, int Sequence, string Name, int Minutes, Guid WorkAreaId, int Employees);
public sealed record StageTemplatesView(RequirementView? Requirement, IReadOnlyList<StageTemplateView> Stages);
public sealed record AppointmentStageView(Guid Id, int Sequence, string Name, int OffsetMinutes, int Minutes, Guid WorkAreaId, int Employees);
public interface IStageSchedulingService
{
    Task<StageTemplatesView> Stages(SchedulingActor actor, Guid business, Guid service, string category, CancellationToken ct = default);
    Task<StageTemplatesView> SaveStages(SchedulingActor actor, Guid business, Guid service, string category, SaveStageTemplatesInput input, CancellationToken ct = default);
}
