using WashFlow.Domain.WorkOrders;
namespace WashFlow.UnitTests;
public sealed class WorkOrderDomainTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;
    private static WorkOrder Order() => WorkOrder.Create(Guid.NewGuid(),Guid.NewGuid(),[("Dış yıkama",10),("İç temizlik",20)],[Guid.NewGuid()],Guid.NewGuid(),Guid.NewGuid(),Now,new string('a',64));
    [Fact] public void StagesEnforceSequenceAndCompletionDoesNotOccurAtEstimate()
    {
        var order=Order();var actor=Guid.NewGuid();
        Assert.Throws<InvalidOperationException>(()=>order.Apply(order.Stages[1].Id,"start",actor,Guid.NewGuid(),Now));
        order.Apply(order.Stages[0].Id,"start",actor,Guid.NewGuid(),Now);
        Assert.Null(order.CompletedAtUtc); Assert.Equal(WorkStageStatus.InProgress,order.Stages[0].Status);
        order.Apply(order.Stages[0].Id,"complete",actor,Guid.NewGuid(),Now.AddMinutes(30));
        Assert.Null(order.CompletedAtUtc);Assert.Equal(WorkStageStatus.Ready,order.Stages[1].Status);
        order.Apply(order.Stages[1].Id,"start",actor,Guid.NewGuid(),Now.AddMinutes(30));
        order.Apply(order.Stages[1].Id,"complete",actor,Guid.NewGuid(),Now.AddMinutes(40));
        Assert.Equal(Now.AddMinutes(40),order.CompletedAtUtc);
        Assert.Throws<InvalidOperationException>(()=>order.Apply(order.Stages[1].Id,"complete",actor,Guid.NewGuid(),Now.AddMinutes(41)));
        Assert.Equal(5,order.Events.Count);
    }
    [Fact] public void PauseResumeAndExplicitOwnerCancellationRetainHistory()
    {
        var order=Order();var actor=Guid.NewGuid();var stage=order.Stages[0];
        order.Apply(stage.Id,"start",actor,Guid.NewGuid(),Now);order.Apply(stage.Id,"pause",actor,Guid.NewGuid(),Now.AddMinutes(1));
        Assert.Throws<InvalidOperationException>(()=>order.Apply(stage.Id,"complete",actor,Guid.NewGuid(),Now.AddMinutes(2)));
        order.Apply(stage.Id,"resume",actor,Guid.NewGuid(),Now.AddMinutes(4));Assert.Equal(180000,stage.PausedMilliseconds);
        order.Cancel(actor,Guid.NewGuid(),Now.AddMinutes(5));Assert.Equal(Now,stage.StartedAtUtc);Assert.Null(stage.CompletedAtUtc);Assert.Equal(WorkStageStatus.Cancelled,stage.Status);Assert.NotNull(order.CancelledAtUtc);
    }
}
