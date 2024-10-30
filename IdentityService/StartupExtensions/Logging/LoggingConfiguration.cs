using System.Diagnostics;
using System.Diagnostics.Metrics;
using OpenTelemetry.Exporter;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;
using Serilog.Events;
using Serilog.Filters;
using Serilog.Formatting.Compact;

namespace IdentityService.StartupExtensions.Logging;

public static class LoggingConfiguration
{
    private const string AppName = "IdentityService";
    private const string Version = "1.0.0";
    private static readonly ActivitySource ActivitySource = new(AppName);
    private static readonly Meter Meter = new(AppName);

    public static WebApplicationBuilder AddCustomLogging(
        this WebApplicationBuilder builder,
        string applicationName = AppName)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var loggingSettings = builder.Configuration
            .GetSection("Logging")
            .Get<LoggingSettings>() ?? new LoggingSettings();

        builder.Logging.ClearProviders();
        
        // Configure OpenTelemetry
        ConfigureOpenTelemetry(builder, applicationName);
        
        ConfigureSerilog(builder, loggingSettings, applicationName);


        return builder;
    }
private static void ConfigureOpenTelemetry(WebApplicationBuilder builder, string applicationName)
        {
            var resourceBuilder = ResourceBuilder.CreateDefault()
                .AddService(serviceName: applicationName, serviceVersion: Version, serviceInstanceId: Environment.MachineName)
                .AddAttributes(new Dictionary<string, object>
                {
                    ["deployment.environment"] = builder.Environment.EnvironmentName,
                    ["host.name"] = Environment.MachineName,
                    ["process.runtime.name"] = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription
                });

            builder.Services.AddOpenTelemetry()
                .ConfigureResource(r => r.AddService(applicationName))
                .WithTracing(tracing =>
                {
                    tracing
                        .AddSource(ActivitySource.Name)
                        .SetSampler(new ParentBasedSampler(new TraceIdRatioBasedSampler(1.0)))
                        .AddAspNetCoreInstrumentation()
                        .AddHttpClientInstrumentation()
                        .AddEntityFrameworkCoreInstrumentation()
                        .AddSqlClientInstrumentation()
                        .AddOtlpExporter(options =>
                        {
                            options.Endpoint = new Uri(builder.Configuration["Otlp:Endpoint"] ?? "http://otel-collector:4317");
                            options.Protocol = OtlpExportProtocol.Grpc;
                        });
                })
                .WithMetrics(metrics =>
                {
                    metrics
                        .AddMeter(Meter.Name)
                        .AddRuntimeInstrumentation()
                        .AddAspNetCoreInstrumentation()
                        .AddHttpClientInstrumentation()
                        .AddEventCountersInstrumentation(options =>
                        {
                            options.AddEventSources(
                                "Microsoft.AspNetCore.Hosting",
                                "Microsoft-AspNetCore-Server-Kestrel",
                                "System.Net.Http",
                                "System.Net.NameResolution",
                                "System.Net.Security");
                        })
                        .AddOtlpExporter(options =>
                        {
                            options.Endpoint = new Uri(builder.Configuration["Otlp:Endpoint"] ?? "http://otel-collector:4317");
                            options.Protocol = OtlpExportProtocol.Grpc;
                        });
                });

            builder.Services.AddSingleton<CustomMetrics>(new CustomMetrics(Meter));
        }

        private static void ConfigureSerilog(WebApplicationBuilder builder, LoggingSettings settings, string applicationName)
        {
            ArgumentNullException.ThrowIfNull(builder.Configuration);
            ArgumentNullException.ThrowIfNull(settings);

            Log.Logger = new LoggerConfiguration()
                .ConfigureBaseSettings(settings, applicationName)
                .ConfigureFileSink(settings)
                .ConfigureConsoleSink(settings)
                .ConfigureMinimumLevels()
                .ReadFrom.Configuration(builder.Configuration)
                .CreateLogger();
        }

    private static LoggerConfiguration ConfigureBaseSettings(this LoggerConfiguration loggerConfiguration, LoggingSettings settings, string applicationName)
    {
        return loggerConfiguration
            .MinimumLevel.Is(ParseLogLevel(settings.MinimumLevel))
            .Enrich.FromLogContext()
            .Enrich.WithProperty("Environment", settings.Environment)
            .Enrich.WithProperty("Application", applicationName)
            .Enrich.WithProperty("Version", Version)
            .Filter.ByExcluding(Matching.FromSource("Microsoft.AspNetCore.StaticFiles"));
    }

    private static LoggerConfiguration ConfigureFileSink(
        this LoggerConfiguration loggerConfiguration,
        LoggingSettings settings)
    {
        if (string.IsNullOrEmpty(settings.LogFilePath)) return loggerConfiguration;

        return loggerConfiguration.WriteTo.File(
            new CompactJsonFormatter(),
            settings.LogFilePath,
            rollingInterval: RollingInterval.Day,
            retainedFileCountLimit: settings.RetainedFileCountLimit,
            fileSizeLimitBytes: 10 * 1024 * 1024,
            rollOnFileSizeLimit: true,
            shared: true,
            flushToDiskInterval: TimeSpan.FromSeconds(1)
        );
    }

    private static LoggerConfiguration ConfigureConsoleSink(
        this LoggerConfiguration loggerConfiguration,
        LoggingSettings settings)
    {
        if (!settings.EnableConsole) return loggerConfiguration;

        return loggerConfiguration.WriteTo.Console(
            outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}");
    }
    

    private static LoggerConfiguration ConfigureMinimumLevels(
        this LoggerConfiguration loggerConfiguration)
    {
        return loggerConfiguration
            .MinimumLevel.Override("Microsoft", LogEventLevel.Information)
            .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
            .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Warning)
            .MinimumLevel.Override("System", LogEventLevel.Warning);
    }

    private static LogEventLevel ParseLogLevel(string level) =>
        Enum.TryParse(level, true, out LogEventLevel logLevel) ? logLevel : LogEventLevel.Information;
}


public class CustomMetrics(Meter meter)
{
    private readonly Counter<long> _userRegistrationCounter = meter.CreateCounter<long>(
        name: "identity.user.registrations",
        unit: "{registrations}",
        description: "Number of user registrations");
    private readonly Histogram<double> _authenticationDuration = meter.CreateHistogram<double>(
        name: "identity.authentication.duration",
        unit: "ms",
        description: "Authentication request duration");
    private readonly UpDownCounter<long> _activeUserSessions = meter.CreateUpDownCounter<long>(
        name: "identity.active_sessions",
        unit: "{sessions}",
        description: "Number of active user sessions");

    public void RecordRegistration() => _userRegistrationCounter.Add(1);
    public void RecordAuthenticationDuration(double milliseconds) => _authenticationDuration.Record(milliseconds);
    public void IncrementActiveSessions() => _activeUserSessions.Add(1);
    public void DecrementActiveSessions() => _activeUserSessions.Add(-1);
}