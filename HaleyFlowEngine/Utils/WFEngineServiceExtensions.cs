using Haley.Abstractions;
using Haley.Models;
using Haley.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Haley.Utils {
    public static class WFEngineServiceExtensions {

        public static async Task<IWorkFlowEngine> Build(this WorkFlowEngineMaker input, IAdapterGateway agw) {
            //replace the sql contents, as only we know that.
            var adapterKey = await input.Initialize(agw); //Base names are already coming from the concrete implementation of DBInstanceMaker
            var dal = new MariaWorkFlowDAL(agw, adapterKey);
            return new WorkFlowEngine(dal, input.Options);
        }

        public static IServiceCollection AddWorkFlowEngineService(this IServiceCollection services, IConfiguration configuration, string sectionName = "WorkFlowEngine", bool autoStart = true) {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(configuration);
            if (string.IsNullOrWhiteSpace(sectionName)) throw new ArgumentException("Section name is required.", nameof(sectionName));

            services.Configure<EngineBootstrapOptions>(configuration.GetSection(sectionName)); //This includes the adapter key
            return AddWorkFlowEngineServiceCore(services, autoStart);
        }

        public static IServiceCollection AddWorkFlowEngineService(this IServiceCollection services, Action<EngineBootstrapOptions> configureOptions, bool autoStart = true) {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(configureOptions);

            services.Configure(configureOptions);
            return AddWorkFlowEngineServiceCore(services, autoStart);
        }

        static IServiceCollection AddWorkFlowEngineServiceCore(IServiceCollection services, bool autoStart) {
            var hasIAdapter = services.Any(s => s.ServiceType == typeof(IAdapterGateway));
            var hasAdapter = services.Any(s => s.ServiceType == typeof(AdapterGateway));

            if (!hasIAdapter) {
                if (hasAdapter) {
                    services.TryAddSingleton<IAdapterGateway>(sp => sp.GetRequiredService<AdapterGateway>());
                } else {
                    services.TryAddSingleton<AdapterGateway>();
                    services.TryAddSingleton<IAdapterGateway>(sp => sp.GetRequiredService<AdapterGateway>());
                }
            }

            services.TryAddSingleton(sp => sp.GetRequiredService<IOptions<EngineBootstrapOptions>>().Value);
            services.TryAddSingleton<WorkFlowEngineService>();
            services.TryAddSingleton<IWorkFlowEngineService>(sp => sp.GetRequiredService<WorkFlowEngineService>());
            services.TryAddSingleton<IWorkFlowEngineAccessor>(sp => sp.GetRequiredService<WorkFlowEngineService>());

            if (autoStart) {
                //to make the engine start without a manual call
                //IHostedService is a multi-registration collectoin.
                //TryAddEnumerable to ensure we dont add duplicate registration
                services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, WorkFlowEngineHostedService>());
            }

            return services;
        }
    }
}
