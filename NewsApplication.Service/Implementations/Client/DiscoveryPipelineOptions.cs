namespace NewsApplication.Service.Implementations.Client;

public sealed class DiscoveryPipelineOptions
{
    public string BaseUrl { get; set; } = "http://localhost:8000";
    public string CallbackBaseUrl { get; set; } = "http://localhost:8080";
    public string SharedSecret { get; set; } = string.Empty;
}
