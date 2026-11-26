using System;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace TabTabGo.Templating
{
    /// <summary>Registers <see cref="TemplatingOptions"/> with the dependency-injection container.</summary>
    public static class TemplatingServiceCollectionExtensions
    {
        /// <summary>
        /// Registers <see cref="TemplatingOptions"/> bound from the <c>TabTabGo.Templating</c> section of
        /// <paramref name="configuration"/> (including its <c>AppSettings</c> sub-section).
        /// </summary>
        /// <param name="services">The service collection.</param>
        /// <param name="configuration">The application configuration.</param>
        /// <returns>The same <paramref name="services"/> for chaining.</returns>
        public static IServiceCollection AddTabTabGoTemplating(this IServiceCollection services, IConfiguration configuration)
        {
            if (services == null) throw new ArgumentNullException(nameof(services));
            if (configuration == null) throw new ArgumentNullException(nameof(configuration));

            services.AddOptions<TemplatingOptions>()
                .Configure(options => configuration.GetSection(TemplatingOptions.SectionName).Bind(options));
            return services;
        }

        /// <summary>Registers <see cref="TemplatingOptions"/> configured by <paramref name="configure"/>.</summary>
        /// <param name="services">The service collection.</param>
        /// <param name="configure">Callback that sets the options.</param>
        /// <returns>The same <paramref name="services"/> for chaining.</returns>
        public static IServiceCollection AddTabTabGoTemplating(this IServiceCollection services, Action<TemplatingOptions> configure)
        {
            if (services == null) throw new ArgumentNullException(nameof(services));
            if (configure == null) throw new ArgumentNullException(nameof(configure));

            services.AddOptions<TemplatingOptions>().Configure(configure);
            return services;
        }
    }
}
