using System.Threading.Tasks;

namespace Kudu.Contracts.Jobs
{
    public interface ITriggeredJobsManager : IJobsManager<TriggeredJob>
    {
        Task InvokeTriggeredJob(string jobName);
    }
}