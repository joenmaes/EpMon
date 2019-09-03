﻿using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using EpMon.Data;
using FluentScheduler;
using System.Linq;
using Microsoft.Extensions.Logging;

namespace EpMon.Monitor
{
    public class MonitorOrchestrator : Registry
    {
        private readonly HttpClientFactory _httpClientFactory;
        private readonly EndpointStore _endpointStore;
        private readonly IServiceProvider _serviceProvider;

        public MonitorOrchestrator(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        
            _httpClientFactory = _serviceProvider.GetService<HttpClientFactory>();
            _endpointStore = _serviceProvider.GetService<EndpointStore>();
            
            NonReentrantAsDefault();
            
            Schedule(() => MonitorJobs()).WithName("MonitorJobs").ToRunNow().AndEvery(1).Minutes();
        }
        
        public void MonitorJobs()
        {
            //Check endpoints in database
            foreach (var endpoint in _endpointStore.GetAllEndpoints().Where(x => x.IsActive))
            {
                if (!GetActiveEndpointMonitorJobs().Contains(endpoint.Id))
                {
                    var endpointStore = _serviceProvider.GetService<EndpointStore>();
                    //var logger = _serviceProvider.GetService<ILogger>();

                    JobManager.AddJob(() => new EndpointMonitor(endpoint, _httpClientFactory, endpointStore), 
                        (s) => s.WithName($"MonitorEndpointId={endpoint.Id}")
                        .ToRunNow()
                        .AndEvery(endpoint.CheckInterval).Minutes());
                }
            }

            //Check registered jobs
            foreach (var endpointId in GetActiveEndpointMonitorJobs())
            {
                if (!IsActiveEndpointJob(endpointId))
                {
                    JobManager.RemoveJob("MonitorEndpointId=" + endpointId);
                }

                //TODO: validate when the details of a job are changed (type, interval,...)
            }
        }

        private IEnumerable<int> GetActiveEndpointMonitorJobs()
        {
            return JobManager.AllSchedules
                    .Where(x => x.Name.StartsWith("MonitorEndpoint"))
                    .Select(x => int.Parse(x.Name.Replace("MonitorEndpointId=", "")));
        }

        private bool IsActiveEndpointJob(int endpointId)
        {
            return _endpointStore.GetAllEndpoints().Any(x => x.IsActive && x.Id == endpointId);
        }
    }
}