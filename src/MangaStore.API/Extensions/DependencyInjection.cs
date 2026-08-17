namespace MangaStore.API.Extensions;

using System.Reflection;
using System.Text;
using System.Threading.RateLimiting;
using Asp.Versioning;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using MangaStore.API.Infrastructure;
using MangaStore.API.Options;
using MangaStore.Application.Common.Identity;
using MangaStore.Application.Common.Options;

/// <summary>Registers all API-layer services into the DI container.</summary>
public static class DependencyInjection
{
    /// <summary>Adds controllers, Swagger, versioning, JWT authentication, CORS, rate limiting, and the global exception handler.</summary>
    public static IServiceCollection AddAPI(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddControllers(options => options.SuppressAsyncSuffixInActionNames = false)
            .ConfigureApiBehaviorOptions(options => options.SuppressModelStateInvalidFilter = true);

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
                policy.WithOrigins(origins).AllowAnyHeader().AllowAnyMethod();
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
        services.AddOutputCache();

        return services;
    }
}
