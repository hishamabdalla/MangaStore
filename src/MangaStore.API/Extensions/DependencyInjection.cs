namespace MangaStore.API.Extensions;

using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Asp.Versioning;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using MangaStore.API.Infrastructure;
using MangaStore.API.Infrastructure.Json;
using MangaStore.API.Options;
using MangaStore.Application.Common.Identity;
using MangaStore.Application.Common.Localization;
using MangaStore.Application.Common.Options;

/// <summary>Registers all API-layer services into the DI container.</summary>
public static class DependencyInjection
{
    /// <summary>Adds controllers, Swagger, versioning, JWT authentication, CORS, rate limiting, and the global exception handler.</summary>
    public static IServiceCollection AddAPI(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddControllers(options => options.SuppressAsyncSuffixInActionNames = false)
            .ConfigureApiBehaviorOptions(options => options.SuppressModelStateInvalidFilter = true)
            .AddJsonOptions(options => AddWireConverters(options.JsonSerializerOptions));

        // AddJsonOptions above configures MVC's options only. ProblemDetails is written by
        // IProblemDetailsService, which serialises through Http.Json's separate options object.
        services.ConfigureHttpJsonOptions(options => AddWireConverters(options.SerializerOptions));

        services.AddExceptionHandler<GlobalExceptionHandler>();
        services.AddProblemDetails(options =>
        {
            options.CustomizeProblemDetails = ctx =>
            {
                ctx.ProblemDetails.Extensions["traceId"] = ctx.HttpContext.TraceIdentifier;
            };
        });

        services.AddSwaggerGen(options =>
        {
            options.SwaggerDoc("v1", new OpenApiInfo
            {
                Title = "MangaStore API",
                Version = "v1",
                Description = "MangaStore storefront API.",
            });

            options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT",
                Description = "Enter a valid JWT access token.",
            });

            options.AddSecurityRequirement(new OpenApiSecurityRequirement
            {
                {
                    new OpenApiSecurityScheme
                    {
                        Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" },
                    },
                    []
                },
            });

            string xmlPath = Path.Combine(AppContext.BaseDirectory, $"{Assembly.GetExecutingAssembly().GetName().Name}.xml");
            options.IncludeXmlComments(xmlPath);
        });

        services.AddApiVersioning(options =>
        {
            options.DefaultApiVersion = new ApiVersion(1);
            options.AssumeDefaultVersionWhenUnspecified = true;
            options.ReportApiVersions = true;
            options.ApiVersionReader = new UrlSegmentApiVersionReader();
        }).AddMvc().AddApiExplorer(options =>
        {
            options.GroupNameFormat = "'v'V";
            options.SubstituteApiVersionInUrl = true;
        });

        // JwtOptions is bound by AddInfrastructure, which also issues the tokens validated here.
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUser, CurrentUser>();
        services.AddScoped<IRequestLanguage, RequestLanguage>();

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer();

        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<JwtOptions>>((bearerOptions, jwtOpts) =>
            {
                // Without this the handler rewrites the short claim names we issue ("sub", "role")
                // to long schema URIs, and [Authorize(Roles = ...)] silently stops matching.
                bearerOptions.MapInboundClaims = false;

                bearerOptions.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ValidIssuer = jwtOpts.Value.Issuer,
                    ValidAudience = jwtOpts.Value.Audience,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOpts.Value.Secret)),
                    NameClaimType = AppClaimTypes.Subject,
                    RoleClaimType = AppClaimTypes.Role,

                    // Default is five minutes of grace; an expired access token should be expired.
                    ClockSkew = TimeSpan.Zero,
                };
            });

        services.AddAuthorization();
        services.AddSingleton<IAuthorizationMiddlewareResultHandler, ProblemDetailsAuthorizationResultHandler>();

        services.AddOptions<CorsOptions>()
            .BindConfiguration("Cors")
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddCors(options =>
        {
            options.AddDefaultPolicy(policy =>
            {
                string[] origins = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];

                // A single "*" opens the API to every origin. Safe only because this API never
                // authenticates with cookies — AllowAnyOrigin and AllowCredentials are mutually
                // exclusive, and adding credentials later would silently break every request.
                if (origins.Contains(CorsOptions.AnyOrigin))
                {
                    policy.AllowAnyOrigin();
                }
                else
                {
                    policy.WithOrigins(origins);
                }

                policy.AllowAnyHeader().AllowAnyMethod();
            });
        });

        services.AddOptions<RateLimitOptions>()
            .BindConfiguration("RateLimit")
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddRateLimiter(options =>
        {
            var rateLimitOptions = configuration.GetSection("RateLimit").Get<RateLimitOptions>() ?? new RateLimitOptions();

            options.AddFixedWindowLimiter(RateLimitOptions.DefaultPolicy, limiterOptions =>
            {
                limiterOptions.Window = TimeSpan.FromSeconds(rateLimitOptions.WindowSeconds);
                limiterOptions.PermitLimit = rateLimitOptions.PermitLimit;
                limiterOptions.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
                limiterOptions.QueueLimit = 0;
            });

            // Partitioned by client IP so one attacker cannot exhaust the window for everyone else.
            options.AddPolicy(RateLimitOptions.AuthPolicy, httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    _ => new FixedWindowRateLimiterOptions
                    {
                        Window = TimeSpan.FromSeconds(rateLimitOptions.WindowSeconds),
                        PermitLimit = rateLimitOptions.AuthPermitLimit,
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        QueueLimit = 0,
                    }));

            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
        });

        services.AddResponseCompression();

        return services;
    }

    /// <summary>Applies the wire-format conventions the storefront depends on.</summary>
    /// <param name="options">The serializer options to configure.</param>
    /// <remarks>
    /// Shared by MVC and Http.Json so the two cannot drift. Enums travel as camelCase strings —
    /// an integer renders as <c>stock.0</c> in the client's translation lookup — and timestamps
    /// carry a <c>Z</c> so the browser stops reading them as local time.
    /// </remarks>
    private static void AddWireConverters(JsonSerializerOptions options)
    {
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        options.Converters.Add(new UtcDateTimeConverter());
        options.Converters.Add(new NullableUtcDateTimeConverter());
    }
}
