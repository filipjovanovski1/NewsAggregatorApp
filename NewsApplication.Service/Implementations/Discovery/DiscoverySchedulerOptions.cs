namespace NewsApplication.Service.Implementations.Discovery;

public sealed class DiscoverySchedulerOptions
{
    public bool Enabled { get; set; }
    public int DispatchBatchSize { get; set; } = 1;
    public int FeedBatchSize { get; set; } = 500;
    public int PollBatchSize { get; set; } = 100;
}
