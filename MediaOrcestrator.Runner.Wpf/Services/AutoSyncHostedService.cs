using MediaOrcestrator.Domain;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace MediaOrcestrator.Runner.Wpf.Services;

internal sealed class AutoSyncHostedService(
    Orcestrator orcestrator,
    PreloadCompletionSignal preloadSignal,
    ILogger<AutoSyncHostedService> logger) : IHostedService, IDisposable
{
    private static readonly TimeSpan Period = TimeSpan.FromHours(1);

    private CancellationTokenSource? _cts;
    private Task? _loop;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _cts = new();
        _loop = Task.Run(() => RunAsync(_cts.Token), CancellationToken.None);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_cts is null || _loop is null)
        {
            return;
        }

        try
        {
            await _cts.CancelAsync();
        }
        catch
        {
        }

        var completed = await Task.WhenAny(_loop, Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken));

        if (completed != _loop)
        {
            logger.LogWarning("Цикл досинка не остановился в отведённое время");
        }
    }

    public void Dispose()
    {
        _cts?.Dispose();
    }

    private async Task RunAsync(CancellationToken token)
    {
        try
        {
            await preloadSignal.Completion.WaitAsync(token);
        }
        catch (OperationCanceledException)
        {
            logger.LogInformation("Цикл досинка остановлен до первой итерации");
            return;
        }

        while (!token.IsCancellationRequested)
        {
            try
            {
                logger.LogInformation("Получение новых данных из всех хранилищ...");
                await orcestrator.GetStorageFullInfo(false, null, true, cancellationToken: token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Ошибка в цикле досинка, продолжаю по расписанию");
            }

            logger.LogInformation("Цикл завершён. Следующий запуск в {NextRunTime}", DateTime.Now.Add(Period));

            try
            {
                await Task.Delay(Period, token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        logger.LogInformation("Цикл досинка остановлен");
    }
}
