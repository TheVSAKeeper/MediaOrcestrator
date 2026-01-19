using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using MediaOrcestrator.Core;
using MediaOrcestrator.Core.Services;

var configuration = new ConfigurationBuilder()
    .SetBasePath(Directory.GetCurrentDirectory())
    .AddJsonFile("appsettings.json", false, true)
    .AddJsonFile("appsettings.Development.json", true, true)
    .Build();

var services = new ServiceCollection()
    .AddYoutubeChannelDownloader(configuration);

var serviceProvider = services.BuildServiceProvider();
var service = serviceProvider.GetRequiredService<ChannelService>();

var channelUrl = configuration["Channel:Url"];

if (string.IsNullOrWhiteSpace(channelUrl))
{
    throw new InvalidOperationException("URL канала не настроен. Пожалуйста, укажите 'Channel:Url' в appsettings.");
}

var path1 = "E:\\bobgroup\\projects\\mediaOrcestrator\\repo\\MediaOrcestrator\\MediaOrcestrator.Rutube\\bin\\Debug\\net8.0";


var scanner = new InterfaceScanner();
var myInterfaceType = typeof(IMediaSource); // Пример интерфейса
var implementations = scanner.FindImplementations(path1, myInterfaceType);
var x = implementations[0];
var aaaa = (IMediaSource)Activator.CreateInstance(x.Type);
aaaa.Download();


await service.DownloadVideosAsync(channelUrl);

Log.CloseAndFlush();
