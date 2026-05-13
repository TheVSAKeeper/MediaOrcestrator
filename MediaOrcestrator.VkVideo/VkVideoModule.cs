using MediaOrcestrator.Modules;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Timeout;
using System.Net;

namespace MediaOrcestrator.VkVideo;

public sealed class VkVideoModule : IPluginModule
{
    public void Register(IServiceCollection services)
    {
        services.AddOptions<VkVideoOptions>();

        services
            .AddHttpClient(VkVideoServiceFactory.ApiClientName, ConfigureApiClient)
            .ConfigurePrimaryHttpMessageHandler(CreateHandler)
            .AddResilienceHandler("vkvideo-api", BuildResiliencePipeline);

        services
            .AddHttpClient(VkVideoServiceFactory.UploadClientName, ConfigureUploadClient)
            .ConfigurePrimaryHttpMessageHandler(CreateHandler);

        services.AddSingleton<IVkVideoServiceFactory, VkVideoServiceFactory>();
        services.AddSingleton<ISourceType, VkVideoChannel>();
    }

    private static void ConfigureApiClient(
        IServiceProvider sp,
        HttpClient client)
    {
        client.Timeout = Timeout.InfiniteTimeSpan;
    }

    private static void ConfigureUploadClient(
        IServiceProvider sp,
        HttpClient client)
    {
        var options = sp.GetRequiredService<IOptions<VkVideoOptions>>().Value;
        client.Timeout = options.UploadTimeout;
    }

    private static SocketsHttpHandler CreateHandler(IServiceProvider sp)
    {
        var options = sp.GetRequiredService<IOptions<VkVideoOptions>>().Value;

        return new()
        {
            UseCookies = false,
            PooledConnectionLifetime = options.PooledConnectionLifetime,
            PooledConnectionIdleTimeout = options.PooledConnectionIdleTimeout,
        };
    }

    private static void BuildResiliencePipeline(
        ResiliencePipelineBuilder<HttpResponseMessage> builder,
        ResilienceHandlerContext context)
    {
        var options = context.ServiceProvider.GetRequiredService<IOptions<VkVideoOptions>>().Value;

        builder
            .AddTimeout(new TimeoutStrategyOptions
            {
                Timeout = options.ApiTotalTimeout,
                Name = "vkvideo-api-total-timeout",
            })
            .AddRetry(new()
            {
                Name = "vkvideo-api-retry",
                MaxRetryAttempts = options.RetryCount,
                Delay = options.RetryBaseDelay,
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
                ShouldHandle = args => ValueTask.FromResult(IsTransientFailure(args.Outcome)),
            })
            .AddCircuitBreaker(new()
            {
                Name = "vkvideo-api-circuit-breaker",
                MinimumThroughput = options.CircuitBreakerMinimumThroughput,
                FailureRatio = options.CircuitBreakerFailureRatio,
                SamplingDuration = options.CircuitBreakerSamplingDuration,
                ShouldHandle = args => ValueTask.FromResult(IsTransientFailure(args.Outcome)),
            })
            .AddTimeout(new TimeoutStrategyOptions
            {
                Timeout = options.ApiAttemptTimeout,
                Name = "vkvideo-api-attempt-timeout",
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
