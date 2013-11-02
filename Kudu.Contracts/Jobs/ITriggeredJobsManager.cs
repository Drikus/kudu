namespace Kudu.Contracts.Jobs
{
    public interface ITriggeredJobsManager : IJobsManager<TriggeredJob>
    {
        void InvokeTriggeredJob(string jobName);
    }
}