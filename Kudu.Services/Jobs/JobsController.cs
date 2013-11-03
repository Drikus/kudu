using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Web.Http;
using Kudu.Contracts.Jobs;
using Kudu.Contracts.Tracing;
using Kudu.Core.Hooks;
using Kudu.Services.Infrastructure;

namespace Kudu.Services.Jobs
{
    public class JobsController : ApiController
    {
        private readonly ITracer _tracer;
        private readonly ITriggeredJobsManager _triggeredJobsManager;
        private readonly IContinuousJobsManager _continuousJobsManager;

        public JobsController(ITracer tracer, ITriggeredJobsManager triggeredJobsManager, IContinuousJobsManager continuousJobsManager)
        {
            _tracer = tracer;
            _triggeredJobsManager = triggeredJobsManager;
            _continuousJobsManager = continuousJobsManager;
        }

        [HttpGet]
        public HttpResponseMessage ListContinuousJobs()
        {
            IEnumerable<ContinuousJob> continuousJobs = ListJobs(_continuousJobsManager.ListJobs, "continuous");

            return Request.CreateResponse(HttpStatusCode.OK, continuousJobs);
        }

        [HttpGet]
        public HttpResponseMessage ListTriggeredJobs()
        {
            IEnumerable<TriggeredJob> triggeredJobs = ListJobs(_triggeredJobsManager.ListJobs, "triggered");

            return Request.CreateResponse(HttpStatusCode.OK, triggeredJobs);
        }

        [HttpGet]
        public HttpResponseMessage ListAllJobs()
        {
            IEnumerable<ContinuousJob> continuousJobs = ListJobs(_continuousJobsManager.ListJobs, "continuous");
            IEnumerable<TriggeredJob> triggeredJobs = ListJobs(_triggeredJobsManager.ListJobs, "triggered");

            var allJobs = new AllJobs()
            {
                ContinuousJobs = continuousJobs,
                TriggeredJobs = triggeredJobs
            };

            return Request.CreateResponse(HttpStatusCode.OK, allJobs);
        }

        [HttpGet]
        public HttpResponseMessage GetContinuousJob(string jobName)
        {
            ContinuousJob continuousJob = _continuousJobsManager.GetJob(jobName);
            if (continuousJob != null)
            {
                UpdateJobUrl(continuousJob, Request, null);
                return Request.CreateResponse(HttpStatusCode.OK, continuousJob);
            }

            return Request.CreateResponse(HttpStatusCode.NotFound);
        }

        [HttpGet]
        public HttpResponseMessage GetTriggeredJob(string jobName)
        {
            TriggeredJob triggeredJob = _triggeredJobsManager.GetJob(jobName);
            if (triggeredJob != null)
            {
                UpdateJobUrl(triggeredJob, Request, null);
                return Request.CreateResponse(HttpStatusCode.OK, triggeredJob);
            }

            return Request.CreateResponse(HttpStatusCode.NotFound);
        }

        [HttpPost]
        public HttpResponseMessage InvokeTriggeredJob(string jobName)
        {
            try
            {
                _triggeredJobsManager.InvokeTriggeredJob(jobName);
                return Request.CreateResponse(HttpStatusCode.Accepted);
            }
            catch (FileNotFoundException)
            {
                return Request.CreateResponse(HttpStatusCode.NotFound);
            }
            catch (ConflictException)
            {
                return Request.CreateResponse(HttpStatusCode.Conflict);
            }
        }

        private IEnumerable<TJob> ListJobs<TJob>(Func<IEnumerable<TJob>> getJobsFunc, string relative) where TJob : JobBase
        {
            IEnumerable<TJob> jobs = getJobsFunc();

            foreach (var job in jobs)
            {
                UpdateJobUrl(job, Request, relative + "/" + job.Name);
            }

            return jobs;
        }

        private void UpdateJobUrl(JobBase job, HttpRequestMessage request, string relative)
        {
            job.Url = relative != null ? UriHelper.MakeRelative(request.RequestUri, relative) : request.RequestUri;
        }
    }
}