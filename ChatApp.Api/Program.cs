using System.Security.Claims;
using System.Threading.RateLimiting;
using Amazon.Runtime;
using Amazon.S3;
using ChatApp.Api.Common.Errors;
using ChatApp.Api.Common.Auth;
using ChatApp.Api.Features.Realtime;
using ChatApp.Infrastructure.Data;
using FluentValidation;
using FluentValidation.AspNetCore;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using Scalar.AspNetCore;
using Serilog;

namespace ChatApp.Api;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        Log.Logger = new LoggerConfiguration()
            .WriteTo.Console()
            .CreateBootstrapLogger();

        try
        {
            Log.Information("ChatApp.Api starting up");
            var builder = WebApplication.CreateBuilder(args);

            builder.Services.AddSerilog((services, lc) => lc
                .ReadFrom.Configuration(builder.Configuration)
                .ReadFrom.Services(services)
                .Enrich.FromLogContext()
                .WriteTo.Console());

            // Controllers + SignalR
            builder.Services.AddControllers();
            builder.Services.AddSignalR();

            // FluentValidation auto-validation
            builder.Services.AddFluentValidationAutoValidation();
            builder.Services.AddValidatorsFromAssemblyContaining<Features.Messaging.Validators.SendMessageRequestValidator>();

            // Rate limiting: global + named policies
            builder.Services.AddRateLimiter(options =>
            {
                options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
                options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(ctx =>
                {
                    var key = ctx.User.FindFirst("sub")?.Value
                        ?? ctx.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                        ?? ctx.Connection.RemoteIpAddress?.ToString()
                        ?? "anonymous";
                    return RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 120,
                        Window = TimeSpan.FromMinutes(1)
                    });
                });
                options.AddPolicy("write", ctx =>
                {
                    var key = ctx.User.FindFirst("sub")?.Value
                        ?? ctx.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                        ?? ctx.Connection.RemoteIpAddress?.ToString()
                        ?? "anonymous";
                    return RateLimitPartition.GetFixedWindowLimiter($"write:{key}", _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 30,
                        Window = TimeSpan.FromMinutes(1)
                    });
                });
                options.AddPolicy("search", ctx =>
                {
                    var key = ctx.User.FindFirst("sub")?.Value
                        ?? ctx.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                        ?? ctx.Connection.RemoteIpAddress?.ToString()
                        ?? "anonymous";
                    return RateLimitPartition.GetFixedWindowLimiter($"search:{key}", _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 20,
                        Window = TimeSpan.FromMinutes(1)
                    });
                });
            });

            // OpenAPI + Scalar UI
            builder.Services.AddOpenApi(options =>
            {
                options.AddDocumentTransformer((doc, _, _) =>
                {
                    doc.Info.Title = "ChatApp API";
                    doc.Info.Version = "v1";
                    var scheme = new OpenApiSecurityScheme
                    {
                        Type = SecuritySchemeType.Http,
                        Scheme = "bearer",
                        BearerFormat = "JWT",
                        In = ParameterLocation.Header,
                        Description = "Supabase JWT"
                    };
                    doc.Components ??= new OpenApiComponents();
                    doc.Components.SecuritySchemes.Add("Bearer", scheme);
                    return Task.CompletedTask;
                });
            });

            // Database
            var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
                ?? "Host=localhost;Port=5432;Database=chatapp;Username=postgres;Password=postgres";
            builder.Services.AddDbContext<AppDbContext>(opt => opt.UseNpgsql(connectionString));

            // Redis Caching (memory cache fallback so StoriesController can resolve IDistributedCache)
            var redisConnectionString = builder.Configuration.GetConnectionString("Redis");
            if (!string.IsNullOrEmpty(redisConnectionString))
            {
                builder.Services.AddStackExchangeRedisCache(options =>
                {
                    options.Configuration = redisConnectionString;
                    options.InstanceName = "ChatApp_";
                });
            }
            else
            {
                builder.Services.AddDistributedMemoryCache();
            }

            // Auth Services
            builder.Services.AddScoped<Microsoft.AspNetCore.Identity.IPasswordHasher<ChatApp.Infrastructure.Data.Entities.User>, Microsoft.AspNetCore.Identity.PasswordHasher<ChatApp.Infrastructure.Data.Entities.User>>();
            builder.Services.AddScoped<ChatApp.Api.Features.Auth.ITokenService, ChatApp.Api.Features.Auth.TokenService>();
            builder.Services.AddScoped<ChatApp.Api.Features.Auth.IEmailService, ChatApp.Api.Features.Auth.SmtpEmailService>();
            builder.Services.AddSingleton<ChatApp.Api.Features.Auth.ITokenBlacklistService, ChatApp.Api.Features.Auth.TokenBlacklistService>();

            // Auth: JWT (Native .NET + Postgres)
            var jwtSecret = builder.Configuration["Authentication:Jwt:SecretKey"]
                ?? builder.Configuration["Authentication:Supabase:JwtSecret"]
                ?? "default_super_secret_key_minimum_32_characters_long_for_chatapp_jwt_validation";
            if (jwtSecret.Length < 32)
            {
                jwtSecret = jwtSecret.PadRight(32, '0');
            }
            var key = new SymmetricSecurityKey(System.Text.Encoding.UTF8.GetBytes(jwtSecret));

            builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
                .AddJwtBearer(options =>
                {
                    options.TokenValidationParameters = new TokenValidationParameters
                    {
                        ValidateIssuerSigningKey = true,
                        IssuerSigningKey = key,
                        ValidateIssuer = false,
                        ValidateAudience = false,
                        ClockSkew = TimeSpan.FromMinutes(1)
                    };
                    // SignalR token via query string & token blacklist validation
                    options.Events = new JwtBearerEvents
                    {
                        OnMessageReceived = context =>
                        {
                            var accessToken = context.Request.Query["access_token"];
                            var path = context.HttpContext.Request.Path;
                            if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hubs"))
                            {
                                context.Token = accessToken;
                            }
                            return Task.CompletedTask;
                        },
                        OnTokenValidated = context =>
                        {
                            var blacklist = context.HttpContext.RequestServices.GetRequiredService<ChatApp.Api.Features.Auth.ITokenBlacklistService>();
                            var jti = context.Principal?.FindFirst(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Jti)?.Value;
                            if (!string.IsNullOrEmpty(jti) && blacklist.IsRevoked(jti))
                            {
                                context.Fail("Token has been revoked.");
                            }
                            return Task.CompletedTask;
                        }
                    };
                });
            builder.Services.AddAuthorization();

            // R2 / S3 storage client (optional - API runs without it)
            var r2Config = builder.Configuration.GetSection("Storage:R2");
            builder.Services.AddSingleton<IAmazonS3>(sp =>
            {
                var endpoint = r2Config["Endpoint"] ?? "https://example.invalid";
                var accessKey = r2Config["AccessKey"] ?? "unused";
                var secretKey = r2Config["SecretKey"] ?? "unused";
                var config = new AmazonS3Config
                {
                    ServiceURL = endpoint,
                    ForcePathStyle = true
                };
                return new AmazonS3Client(new BasicAWSCredentials(accessKey, secretKey), config);
            });

            // CORS
            builder.Services.AddCors(options =>
            {
                options.AddPolicy("AllowWebClient", policy =>
                {
                    policy.WithOrigins("http://localhost:3000")
                        .AllowAnyHeader()
                        .AllowAnyMethod()
                        .AllowCredentials();
                });
            });

            var app = builder.Build();

            app.UseMiddleware<GlobalExceptionMiddleware>();
            app.UseMiddleware<CorrelationIdMiddleware>();

            app.UseSerilogRequestLogging();

            if (app.Environment.IsDevelopment())
            {
                app.MapScalarApiReference(options =>
                {
                    options.OpenApiRoutePattern = "/openapi/{documentName}.json";
                    options.Authentication = new ScalarAuthenticationOptions
                    {
                        PreferredSecurityScheme = "Bearer"
                    };
                });
            }
            app.MapOpenApi();

            app.UseCors("AllowWebClient");
            app.UseRateLimiter();
            app.UseAuthentication();
            app.UseAuthorization();

            app.MapGet("/healthz", () => Results.Ok(new { status = "ok" })).AllowAnonymous();
            app.MapGet("/readyz", async (AppDbContext db) =>
            {
                var canConnect = await db.Database.CanConnectAsync();
                return canConnect
                    ? Results.Ok(new { status = "ready" })
                    : Results.StatusCode(503);
            }).AllowAnonymous();

            app.MapHub<ChatHub>("/hubs/chat");
            app.MapControllers();

            if (app.Configuration.GetValue<bool>("ApplyMigrationsOnStartup", false))
            {
                Log.Information("Applying EF Core migrations on startup...");
                using var scope = app.Services.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                await db.Database.MigrateAsync();
                Log.Information("Database migrations applied successfully.");
            }

            Log.Information("ChatApp.Api configured, starting");
            await app.RunAsync();
            return 0;
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "ChatApp.Api terminated unexpectedly");
            return 1;
        }
        finally
        {
            await Log.CloseAndFlushAsync();
        }
    }
}
