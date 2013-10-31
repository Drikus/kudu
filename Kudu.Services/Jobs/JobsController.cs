using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Web.Http;
using Kudu.Contracts.Jobs;
using Kudu.Contracts.Tracing;
using Kudu.Services.Infrastructure;

namespace Kudu.Services.Jobs
{
    public class JobsController : ApiController
    {
        private readonly ITracer _tracer;
        private readonly IJobsManager<TriggeredJob> _triggeredJobsManager;
        private readonly IJobsManager<AlwaysOnJob> _alwaysOnJobsManager;

        public JobsController(ITracer tracer, IJobsManager<TriggeredJob> triggeredJobsManager, IJobsManager<AlwaysOnJob> alwaysOnJobsManager)
        {
            _tracer = tracer;
            _triggeredJobsManager = triggeredJobsManager;
            _alwaysOnJobsManager = alwaysOnJobsManager;
        }

        [HttpGet]
        public HttpResponseMessage GetAlwaysOnJobs()
        {
            IEnumerable<AlwaysOnJob> alwaysOnJobs = GetJobs(_alwaysOnJobsManager.ListJobs);

            return Request.CreateResponse(HttpStatusCode.OK, alwaysOnJobs);
        }

        [HttpGet]
        public HttpResponseMessage GetTriggeredJobs()
        {
            IEnumerable<TriggeredJob> triggeredJobs = GetJobs(_triggeredJobsManager.ListJobs);

            return Request.CreateResponse(HttpStatusCode.OK, triggeredJobs);
        }

        [HttpGet]
        public HttpResponseMessage GetAllJobs()
        {
            IEnumerable<AlwaysOnJob> alwaysOnJobs = GetJobs(_alwaysOnJobsManager.ListJobs);
            IEnumerable<TriggeredJob> triggeredJobs = GetJobs(_triggeredJobsManager.ListJobs);

            var allJobs = new AllJobs()
            {
                AlwaysOnJobs = alwaysOnJobs,
                TriggeredJobs = triggeredJobs
            };

            return Request.CreateResponse(HttpStatusCode.OK, allJobs);
        }

        private IEnumerable<TJob> GetJobs<TJob>(Func<IEnumerable<TJob>> getJobsFunc) where TJob : JobBase
        {
            IEnumerable<TJob> jobs = getJobsFunc();

            foreach (var job in jobs)
            {
                UpdateJobUrl(job, Request);
            }

            return jobs;
        }

        private void UpdateJobUrl(JobBase job, HttpRequestMessage request)
        {
            job.Url = UriHelper.MakeRelative(request.RequestUri, job.Name);
        }
    }
}