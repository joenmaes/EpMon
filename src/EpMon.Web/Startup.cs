﻿using EpMon.Data;
using EpMon.Infrastructure;
using EpMon.Web.Extensions;
using EpMon.Web.Jobs;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Prometheus;
using System;
using System.Net;
using System.Threading;
using EpMon.Publisher;


namespace EpMon.Web
{

    public class Startup
    {
        private static MetricPusher _metricPusher;

        public Startup(IWebHostEnvironment env)
        {
            var builder = new ConfigurationBuilder()
                .SetBasePath(env.ContentRootPath)
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
                .AddJsonFile($"appsettings.{env.EnvironmentName}.json", optional: true)
                .AddEnvironmentVariables();
            Configuration = builder.Build();
            
            ServicePointManager.DefaultConnectionLimit = 1000;
            ThreadPool.SetMinThreads(100, 100);
        }
        
        public IConfiguration Configuration { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddDbContext<EpMonContext>();
            
            services.Configure<IISServerOptions>(options =>
            {
                options.AutomaticAuthentication = false;
            });

            services.AddStartupJob<MigrateDatabaseJob>();

            services.AddHttpClient();
            services.AddSingleton<ITokenService, CachedTokenService>();
            services.AddTransient<EndpointMonitor, EndpointMonitor>();
            services.AddTransient<EndpointStore, EndpointStore>();
            services.AddTransient<EndpointService, EndpointService>();
            services.AddSingleton<PrometheusPublisher, PrometheusPublisher>();
            
            var aiKey = Configuration.GetSection("EpMon:ApplicationInsightsKey").Value;
            var publisher = new ApplicationInsightsPublisher(aiKey);
            services.AddSingleton(publisher);
            
            services.AddMvc(options => options.EnableEndpointRouting = false);

            services.AddSwaggerGen(c =>
            {
                c.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo { Title = "EpMon API", Version = "v1" });
            });

            services.AddScheduledJobs();            
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, ILoggerFactory loggerFactory)
        {
            app.UseDeveloperExceptionPage();
            app.UseStaticFiles();
           
            loggerFactory.AddLog4Net();
            
            app.UseMvc(routes =>
            {
                routes.MapRoute(
                    name: "default",
                    template: "{controller=Home}/{action=Index}/{id?}");
            });

            app.UseOpenApiUi(Configuration);
            
            Metrics.SuppressDefaultMetrics();
            var prometheusEndpoint = Configuration.GetSection("EpMon:PrometheusPushGateway").Value;
            if (!string.IsNullOrEmpty(prometheusEndpoint))
            {
                _metricPusher = new MetricPusher(prometheusEndpoint, $"EpMon", Environment.MachineName);
                _metricPusher.Start();
            }
        }
    }
}
