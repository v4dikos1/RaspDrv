using Microsoft.Extensions.Hosting;
using RaspDrv.Com;
using RaspDrv.Com.Models;

namespace RaspDrvComConsoleApp.HostedService;

internal class Context : IHostedService
{
    private readonly ITagDeviceController _tagDeviceController;

    public Context(ITagDeviceController tagDeviceController)
    {
        _tagDeviceController = tagDeviceController;
        _tagDeviceController.OnEventReceived += OnEventReceived;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        DoWork();
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }

    private void OnEventReceived(object? sender, TagDeviceEventModel tagDeviceEventModel)
    {
        Console.WriteLine($"Event received: {tagDeviceEventModel.EventType} {tagDeviceEventModel.Data}");
    }

    private async void DoWork()
    {
        while (true)
        {
            ShowMenu();

            var option = Console.ReadLine();

            switch (option)
            {
                case "1":
                    Console.WriteLine("Serial number: ");
                    var serialNumber = Console.ReadLine();
                    await _tagDeviceController.GetChargeLevel(serialNumber);
                    break;
                case "2":
                    Environment.Exit(0);
                    break;
                default:
                    Console.WriteLine("Invalid option");
                    break;
            }
        }
    }

    private void ShowMenu()
    {
        Console.WriteLine("\nMenu:\n");
        Console.WriteLine("1. Get charge level");
        Console.WriteLine("2. Exit");
        Console.Write("\nChoose an option: ");
    }
}