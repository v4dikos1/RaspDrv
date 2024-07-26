using Device.RaspDrv;
using Device.RaspDrv.Helpers;
using Microsoft.Extensions.Hosting;
using System;

namespace RaspDrv.ConsoleApp.HostedService
{
    internal class Context : IHostedService
    {
        private Timer _timer;
        private readonly IDeviceController _device;

        public Context(IDeviceController device)
        {
            _device = device;
            _device.OnCommandReceived += _device_OnCommandReceived;
        }

        private void _device_OnCommandReceived(object? sender, Device.RaspDrv.Helpers.Models.DeviceCommandModel e)
        {
            Console.WriteLine($"Command received via event: {e.Command}");
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
                        await Send();
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

        private async Task Send()
        {
            var command = new DeviceCommandRequestBuilder()
                .SetEventType(Device.RaspDrv.Helpers.Models.DeviceCommandTypeEnum.OpenDoor)
                .SetCellNumber("133_1")
                .Build();

            await _device.SendCommand(command);
        }

        private void ShowMenu()
        {
            Console.WriteLine("\nMenu:\n");
            Console.WriteLine("1. Send message");
            Console.WriteLine("2. Exit");
            Console.Write("\nChoose an option: ");
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            DoWork();
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }
    }
}
