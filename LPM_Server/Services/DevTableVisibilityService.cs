using Microsoft.Extensions.Configuration;

public class DevTableVisibilityService
{
    private readonly IConfiguration _configuration;
    public bool ShowDevTables { get; }

    public DevTableVisibilityService(IConfiguration configuration)
    {
        _configuration = configuration;
        ShowDevTables = _configuration.GetValue<bool>("ShowDevTables");
    }
}