namespace NewsApplication.Service.Implementations.Client;

/// <summary>
/// BaseUrl must be the full endpoint, e.g. "https://newsdata.io/api/1/latest"
/// </summary>
public sealed class NewsdataOptions
{
    public string BaseUrl { get; set; } = null!;
    public string ApiKey { get; set; } = null!;
}
