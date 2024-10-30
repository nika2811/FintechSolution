namespace IdentityService.StartupExtensions.Logging;

public sealed class LoggingSettings
{
    public string MinimumLevel { get; set; } = "Information";
    public string Environment { get; set; } = "Development";
    public string LogFilePath { get; set; } = "logs/log-.txt";
    public int RetainedFileCountLimit { get; set; } = 31;
    public bool EnableConsole { get; set; } = true;
}