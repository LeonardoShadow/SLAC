namespace SLAC.Core.Configuration;

public class SlacSupabaseOptions
{
    public const string SectionName = "Supabase";

    public string Url { get; set; } = string.Empty;
    public string Key { get; set; } = string.Empty;
    public string ConnectionString { get; set; } = string.Empty;
}
