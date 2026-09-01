namespace NewsApplication.Domain.DTOs.Discovery;

public sealed record PipelineHealthDTO
{
    public int? ActiveJobs { get; init; }
    public int? Workers { get; init; }
    public int? Queued { get; init; }
    public int? QueueCapacity { get; init; }
    public bool? Accepting { get; init; }
}
