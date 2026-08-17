namespace MangaStore.Application;

using System.Reflection;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using MangaStore.Application.Common.Events;
using MangaStore.Application.Common.Validation;

/// <summary>Registers all Application-layer services into the DI container.</summary>
public static class DependencyInjection
{
    /// <summary>Adds AutoMapper, FluentValidation, validation service, and all application services discovered by convention.</summary>
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        var assembly = Assembly.GetExecutingAssembly();

        services.AddAutoMapper(cfg => cfg.AddMaps(assembly));
        services.AddValidatorsFromAssembly(assembly);
        services.AddScoped<IValidationService, ValidationService>();

        services.Scan(scan => scan
            .FromAssemblies(assembly)
            .AddClasses(c => c
                .InNamespaces("MangaStore.Application.Features")
                .Where(t => t.Name.EndsWith("Service", StringComparison.Ordinal)))
            .AsImplementedInterfaces()
            .WithScopedLifetime());

        services.Scan(scan => scan
            .FromAssemblies(assembly)
            .AddClasses(c => c.AssignableTo(typeof(IDomainEventListener<>)))
            .AsImplementedInterfaces()
            .WithScopedLifetime());

        return services;
    }
}
