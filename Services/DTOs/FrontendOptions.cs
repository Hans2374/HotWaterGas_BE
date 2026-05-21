namespace Services.DTOs;

public class FrontendOptions
{
    public const string SectionName = "Frontend";

    public string BaseUrl { get; set; } = string.Empty;
    public string SuccessUrl { get; set; } = string.Empty;
    public string CancelUrl { get; set; } = string.Empty;
}
