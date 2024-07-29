using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NLog;
using NLog.Extensions.Logging;
using NLog.Web;
using RaspDrv.Com;
using RaspDrv.Com.Models;
using RaspDrvComConsoleApp.HostedService;

namespace RaspDrvComConsoleApp;


public class Program
{
    public static void Main(string[] args)
    {
        var logger = LogManager.Setup()
            .LoadConfigurationFromFile("nlog.config")
            .GetCurrentClassLogger();

        logger.Info("Starting app...");

        try
        {
            var host = Host.CreateDefaultBuilder(args)
                .ConfigureLogging(logging =>
                {
                    logging.ClearProviders();
                    logging.SetMinimumLevel(Microsoft.Extensions.Logging.LogLevel.Trace);
                    logging.AddNLog();
                })
                .ConfigureAppConfiguration((hostingContext, config) =>
                {
                    config.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
                })
                .ConfigureServices((context, services) =>
                {
                    services.AddHostedService<Context>();
                    services.AddSingleton<ITagDeviceController, SerialPortMonitorService>();
                    services.Configure<ComPortConfig>(context.Configuration.GetSection("ComPortConfiguration"));
                })
                .UseNLog()
                .Build();

            host.Run();
        }
        catch (Exception ex)
        {
            logger.Error(ex, "Stopped program because of exception");
            throw;
        }
        finally
        {
            LogManager.Shutdown();
        }
    }
}