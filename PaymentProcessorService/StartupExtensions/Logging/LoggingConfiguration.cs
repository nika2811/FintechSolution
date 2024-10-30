using Serilog;
using Serilog.Events;
using Serilog.Filters;
using Serilog.Formatting.Compact;

namespace PaymentProcessorService.StartupExtensions.Logging;

public static class LoggingConfiguration
{
    private const string AppName = "PaymentProcessorService";
    private const int DefaultFileSizeLimitMb = 10;
    private const int FlushIntervalSeconds = 1;

    public static WebApplicationBuilder AddCustomLogging(
        this WebApplicationBuilder builder,
        string applicationName = AppName)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var loggingSettings = builder.Configuration
            .GetSection("Logging")
            .Get<LoggingSettings>() ?? new LoggingSettings();

        builder.Logging.ClearProviders();

        builder.Host.UseSerilog((context, loggerConfiguration) =>
            ConfigureSerilog(loggerConfiguration, context.Configuration, loggingSettings, applicationName));

        return builder;
    }

    private static void ConfigureSerilog(
        LoggerConfiguration loggerConfiguration,
        IConfiguration configuration,
        LoggingSettings settings,
        string applicationName)
    {
        ArgumentNullException.ThrowIfNull(loggerConfiguration);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(settings);

        loggerConfiguration
            .ConfigureBaseSettings(settings, applicationName)
            .ConfigureFileSink(settings)
            .ConfigureConsoleSink(settings)
            .ConfigureSeqSink(settings)
            .ConfigureMinimumLevels()
            .ReadFrom.Configuration(configuration);
    }

    private static LoggerConfiguration ConfigureBaseSettings(
        this LoggerConfiguration loggerConfiguration,
        LoggingSettings settings,
        string applicationName)
    {
        return loggerConfiguration
            .MinimumLevel.Is(ParseLogLevel(settings.MinimumLevel))
            .Enrich.FromLogContext()
            .Enrich.WithProperty("Environment", settings.Environment)
            .Enrich.WithProperty("Application", applicationName)
            .Enrich.WithProperty("Version", GetAssemblyVersion())
            .Filter.ByExcluding(Matching.FromSource("Microsoft.AspNetCore.StaticFiles"));
    }

    private static LoggerConfiguration ConfigureFileSink(
        this LoggerConfiguration loggerConfiguration,
        LoggingSettings settings)
    {
        if (string.IsNullOrEmpty(settings.LogFilePath))
            return loggerConfiguration;

        return loggerConfiguration.WriteTo.File(
            new CompactJsonFormatter(),
            settings.LogFilePath,
            rollingInterval: RollingInterval.Day,
            retainedFileCountLimit: settings.RetainedFileCountLimit,
            fileSizeLimitBytes: DefaultFileSizeLimitMb * 1024 * 1024,
            rollOnFileSizeLimit: true,
            shared: true,
            flushToDiskInterval: TimeSpan.FromSeconds(FlushIntervalSeconds)
        );
    }

    private static LoggerConfiguration ConfigureConsoleSink(
        this LoggerConfiguration loggerConfiguration,
        LoggingSettings settings)
    {
        if (!settings.EnableConsole)
            return loggerConfiguration;

        return loggerConfiguration.WriteTo.Console(
            outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}");
    }

    private static LoggerConfiguration ConfigureSeqSink(
        this LoggerConfiguration loggerConfiguration,
        LoggingSettings settings)
    {
        if (string.IsNullOrEmpty(settings.SeqUrl))
            return loggerConfiguration;

        return loggerConfiguration.WriteTo.Seq(settings.SeqUrl);
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

    private static string GetAssemblyVersion()
    {
        return typeof(LoggingConfiguration).Assembly.GetName().Version?.ToString() ?? "1.0.0";
    }

    private static LogEventLevel ParseLogLevel(string level)
    {
        return Enum.TryParse<LogEventLevel>(level, true, out var logLevel)
            ? logLevel
            : LogEventLevel.Information;
    }
}