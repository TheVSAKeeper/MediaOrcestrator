namespace MediaOrcestrator.VkVideo;

public sealed class VkVideoOptions
{
    public TimeSpan ApiTotalTimeout { get; set; } = TimeSpan.FromSeconds(90);
    public TimeSpan ApiAttemptTimeout { get; set; } = TimeSpan.FromSeconds(20);
    public TimeSpan UploadTimeout { get; set; } = Timeout.InfiniteTimeSpan;
    public int RetryCount { get; set; } = 3;
    public TimeSpan RetryBaseDelay { get; set; } = TimeSpan.FromSeconds(1);
    public TimeSpan PooledConnectionLifetime { get; set; } = TimeSpan.FromMinutes(10);
    public TimeSpan PooledConnectionIdleTimeout { get; set; } = TimeSpan.FromMinutes(2);
    public int CircuitBreakerMinimumThroughput { get; set; } = 10;
    public double CircuitBreakerFailureRatio { get; set; } = 0.5;
    public TimeSpan CircuitBreakerSamplingDuration { get; set; } = TimeSpan.FromSeconds(30);
    public int MinRequestIntervalMs { get; set; } = 350;
    public int RateLimitMaxRetries { get; set; } = 4;
}
