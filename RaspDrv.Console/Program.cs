using Device.RaspDrv;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NLog;
using NLog.Web;
using NLog.Extensions.Logging;
using RaspDrv.ConsoleApp.HostedService;

internal class Program
{
    private static void Main(string[] args)
    {
        Console.WriteLine("Hello, World!");

        var logger = NLog.LogManager.Setup()
            .LoadConfigurationFromFile("nlog.config")
            .GetCurrentClassLogger();

        logger.Info("Starting app...");

        try
        {
            var builder = Host.CreateDefaultBuilder(args)
            .ConfigureLogging(logging =>
            {   
                logging.ClearProviders();
                logging.SetMinimumLevel(Microsoft.Extensions.Logging.LogLevel.Trace);
                logging.AddNLog(); // Добавляем NLog как поставщика логов
            })
            .ConfigureAppConfiguration((hostingContext, config) =>
            {
                config.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
            })
            .ConfigureServices((hostContext, services) =>
            {
                services.Configure<RaspDrvConfig>(hostContext.Configuration.GetSection("RaspDrvConfiguration"));
                services.AddSingleton<IDeviceController, RaspDrvController>();
                services.AddHostedService<Context>();
            })
            .UseNLog();

            var host = builder.Build();

            host.Run();
        }
        catch (Exception ex)
        {
            logger.Error(ex, "Stopped program because of exception");
            throw;
        }
        finally
        {
            NLog.LogManager.Shutdown();
        }
    }
}
