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
        public static IServiceCollection AddWorkFlowEngineService(this IServiceCollection services, IConfiguration configuration, string sectionName = "WorkFlowEngine", bool autoStart = true) {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(configuration);
            if (string.IsNullOrWhiteSpace(sectionName)) throw new ArgumentException("Section name is required.", nameof(sectionName));

            services.Configure<EngineServiceOptions>(configuration.GetSection(sectionName));
            return AddWorkFlowEngineServiceCore(services, autoStart);
        }

        public static IServiceCollection AddWorkFlowEngineService(this IServiceCollection services, Action<EngineServiceOptions> configureOptions, bool autoStart = true) {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(configureOptions);

            services.Configure(configureOptions);
            return AddWorkFlowEngineServiceCore(services, autoStart);
        }

        private static IServiceCollection AddWorkFlowEngineServiceCore(IServiceCollection services, bool autoStart) {
            services.TryAddSingleton<IAdapterGateway, AdapterGateway>();
            services.TryAddSingleton(sp => sp.GetRequiredService<IOptions<EngineServiceOptions>>().Value);
            services.TryAddSingleton<WorkFlowEngineService>();
            services.TryAddSingleton<IWorkFlowEngineService>(sp => sp.GetRequiredService<WorkFlowEngineService>());
            services.TryAddSingleton<IWorkFlowEngineAccessor>(sp => sp.GetRequiredService<WorkFlowEngineService>());

            if (autoStart) {
                services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, WorkFlowEngineHostedService>());
            }

            return services;
        }
    }
}
