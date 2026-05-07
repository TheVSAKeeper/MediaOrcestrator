using MediaOrcestrator.Modules;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Timeout;
using System.Net;

namespace MediaOrcestrator.Rutube;

public sealed class RutubeModule : IPluginModule
{
    public void Register(IServiceCollection services)
    {
        services.AddOptions<RutubeOptions>();

        services
            .AddHttpClient(RutubeServiceFactory.ApiClientName, ConfigureApiClient)
            .ConfigurePrimaryHttpMessageHandler(CreateHandler)
            .AddResilienceHandler("rutube-api", BuildResiliencePipeline);

        services
            .AddHttpClient(RutubeServiceFactory.UploadClientName, ConfigureUploadClient)
            .ConfigurePrimaryHttpMessageHandler(CreateHandler);

        services.AddSingleton<IRutubeServiceFactory, RutubeServiceFactory>();
        services.AddSingleton<ISourceType, RutubeChannel>();
    }

    private static void ConfigureApiClient(IServiceProvider sp, HttpClient client)
    {
        client.Timeout = Timeout.InfiniteTimeSpan;
    }

    private static void ConfigureUploadClient(IServiceProvider sp, HttpClient client)
    {
        var options = sp.GetRequiredService<IOptions<RutubeOptions>>().Value;
        client.Timeout = options.UploadTimeout;
    }

    private static SocketsHttpHandler CreateHandler(IServiceProvider sp)
    {
        var options = sp.GetRequiredService<IOptions<RutubeOptions>>().Value;

        return new()
        {
            UseCookies = false,
            PooledConnectionLifetime = options.PooledConnectionLifetime,
            PooledConnectionIdleTimeout = options.PooledConnectionIdleTimeout,
        };
    }

    private static void BuildResiliencePipeline(ResiliencePipelineBuilder<HttpResponseMessage> builder, ResilienceHandlerContext context)
    {
        var options = context.ServiceProvider.GetRequiredService<IOptions<RutubeOptions>>().Value;

        builder
            .AddTimeout(new TimeoutStrategyOptions
            {
                Timeout = options.ApiTotalTimeout,
                Name = "rutube-api-total-timeout",
            })
            .AddRetry(new()
            {
                Name = "rutube-api-retry",
                MaxRetryAttempts = options.RetryCount,
                Delay = options.RetryBaseDelay,
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
                ShouldHandle = args => ValueTask.FromResult(IsTransientFailure(args.Outcome)),
            })
            .AddCircuitBreaker(new()
            {
                Name = "rutube-api-circuit-breaker",
                MinimumThroughput = options.CircuitBreakerMinimumThroughput,
                FailureRatio = options.CircuitBreakerFailureRatio,
                SamplingDuration = options.CircuitBreakerSamplingDuration,
                ShouldHandle = args => ValueTask.FromResult(IsTransientFailure(args.Outcome)),
            })
            .AddTimeout(new TimeoutStrategyOptions
            {
                Timeout = options.ApiAttemptTimeout,
                Name = "rutube-api-attempt-timeout",
            });
    }

    private static bool IsTransientFailure(Outcome<HttpResponseMessage> outcome)
    {
        if (outcome.Exception is HttpRequestException or TimeoutRejectedException)
        {
            return true;
        }

        if (outcome.Result is null)
        {
            return false;
        }

        var status = outcome.Result.StatusCode;

        return status == HttpStatusCode.RequestTimeout
               || status == HttpStatusCode.TooManyRequests
               || (int)status >= 500;
    }
}
