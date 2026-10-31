using Xunit;
namespace IpaDesktop.Tests;

public sealed class QueueViewModelTests
{
 [Fact] public async Task DisconnectKeepsSuccessAndUnstartedRowsAndResumesOnlyAfterRecovery()
 {
  var queue=new OperationQueueViewModel();bool ready=false;string[] resumed=null;
  queue.CanResumeBatch=batch=>ready&&batch.TargetKey=="phone|USB";
  var batch=queue.Start("Установка",new[]{new QueueSeed("1","One","1.0"),new QueueSeed("2","Two","2.0"),new QueueSeed("3","Three","3.0")},"phone|USB","iPhone",keys=>resumed=keys.ToArray());
  queue.SetBusy(true);
  var connection=new DeviceConnectionException(ErrorLocalizer.Describe("No device found"));
  var error=await Assert.ThrowsAsync<DeviceConnectionException>(()=>BatchRunner.Run(new[]{1,2,3},(int row,CancellationToken _) =>
   row==2?Task.FromException(connection):Task.CompletedTask,(_,index)=>queue.Begin(index),_=>queue.Result(null),(_,e)=>queue.Result(e),null,CancellationToken.None));
  queue.Stop(error.Message,false);queue.SetBusy(false);
  Assert.Equal(new[]{QueueItemState.Succeeded,QueueItemState.Uncertain,QueueItemState.Paused},queue.Items.Select(x=>x.State));
  Assert.False(queue.ResumeCommand.CanExecute(null));queue.ResumeCommand.Execute(null);Assert.Null(resumed);
  ready=true;queue.Refresh();Assert.True(queue.ResumeCommand.CanExecute(null));queue.ResumeCommand.Execute(null);
  Assert.Equal(new[]{"2","3"},resumed);
  var original=queue.Items[0];
  queue.Start("Установка",batch.Items.Where(x=>resumed.Contains(x.Seed.Key)).Select(x=>x.Seed),"phone|USB","iPhone",_=>throw new Exception(),batch);
  await BatchRunner.Run(resumed,(_,_)=>Task.CompletedTask,(_,i)=>queue.Begin(i),_=>queue.Result(null),(_,e)=>queue.Result(e),null,CancellationToken.None);
  Assert.Same(original,queue.Items[0]);Assert.Equal(3,queue.Items.Count);Assert.All(queue.Items,x=>Assert.Equal(QueueItemState.Succeeded,x.State));
  Assert.False(queue.ResumeCommand.CanExecute(null));
 }
 [Fact] public void SuccessInManualRetryResolvesOldPendingRowOnlyForSamePhone()
 {
  var queue=new OperationQueueViewModel();queue.CanResumeBatch=_=>true;var seeds=new[]{new QueueSeed("a","App","1.0")};
  queue.Start("Установка",seeds,"first","First phone",_=>{});queue.Begin(1);queue.Stop("Обрыв",false);var first=queue.Items[0];
  queue.Start("Установка",seeds,"other","Other phone",_=>{});queue.Begin(1);queue.Result(null);
  Assert.Equal(QueueItemState.Uncertain,first.State);
  queue.Start("Установка",seeds,"first","First phone",_=>{});queue.Begin(1);queue.Result(null);
  Assert.Equal(QueueItemState.Succeeded,first.State);queue.Selected=first;Assert.False(queue.ResumeCommand.CanExecute(null));
 }
 [Fact] public void CancelPreservesMeasuredProgressAndBlocksLateOutputAndClearingDuringWork()
 {
  var queue=new OperationQueueViewModel();queue.CanResumeBatch=_=>true;
  queue.Start("Скачивание",new[]{new QueueSeed("a","App","Последняя"),new QueueSeed("b","Other","123")},null,"Apple Account",_=>{});
  queue.SetBusy(true);queue.Begin(1);queue.Measure(42,"1 МБ/с");queue.Stop("Отменено",true);queue.Measure(99,"late");
  Assert.Equal(QueueItemState.Interrupted,queue.Items[0].State);Assert.Equal(42,queue.Items[0].Progress);
  Assert.Equal(QueueItemState.Paused,queue.Items[1].State);Assert.False(queue.ResumeCommand.CanExecute(null));
  queue.SetBusy(false);Assert.True(queue.ResumeCommand.CanExecute(null));
 }
}
