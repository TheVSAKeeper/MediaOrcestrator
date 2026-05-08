namespace MediaOrcestrator.Youtube;

internal sealed class YoutubeOptions
{
    public TimeSpan ApiTotalTimeout { get; set; } = TimeSpan.FromSeconds(90);
    public TimeSpan ApiAttemptTimeout { get; set; } = TimeSpan.FromSeconds(20);
    public int RetryCount { get; set; } = 3;
    public TimeSpan RetryBaseDelay { get; set; } = TimeSpan.FromSeconds(1);
    public TimeSpan PooledConnectionLifetime { get; set; } = TimeSpan.FromMinutes(10);
    public TimeSpan PooledConnectionIdleTimeout { get; set; } = TimeSpan.FromMinutes(2);
    public int CircuitBreakerMinimumThroughput { get; set; } = 10;
    public double CircuitBreakerFailureRatio { get; set; } = 0.5;
    public TimeSpan CircuitBreakerSamplingDuration { get; set; } = TimeSpan.FromSeconds(30);
}
