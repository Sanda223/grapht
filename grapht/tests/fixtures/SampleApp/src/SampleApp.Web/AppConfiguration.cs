namespace SampleApp.Web;

// Name mimics IConfiguration (receiver check is name-based: contains "Configuration").
public class AppConfiguration
{
    public string? this[string key] => null;

    public string? GetSection(string key) => null;

    public string? GetConnectionString(string name) => null;
}

public class FeatureFlags
{
    private readonly AppConfiguration _config = new();

    public bool HighlightEnabled()
    {
        _ = _config.GetConnectionString("Default");
        _ = _config.GetSection("Logging");
        return _config["Features:EnableHighlight"] == "true";
    }
}
