using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RaspDrv.Com.Models;

namespace RaspDrv.Com;

public class TagDeviceController : ITagDeviceController, IDisposable
{
    private readonly ILogger<TagDeviceController> _logger;
    private readonly DeviceManager _deviceManager;
    private readonly FileSystemMonitor _fileSystemMonitor;
    private readonly CancellationTokenSource _cancellationTokenSource = new();

    public event EventHandler<TagDeviceEventModel>? OnEventReceived;

    public TagDeviceController(ILogger<TagDeviceController> logger, IOptions<ComPortConfig> config)
    {
        _logger = logger;

        _deviceManager = new DeviceManager(logger, config.Value, OnEventReceived);
        _fileSystemMonitor = new FileSystemMonitor(logger, config.Value, _deviceManager);

        _deviceManager.InitializeDevices();
        _fileSystemMonitor.Initialize();
    }

    public Task GetChargeLevel(string serialNumber) => _deviceManager.GetChargeLevel(serialNumber);

    public void Dispose()
    {
        _logger.LogInformation("SerialPortMonitorService is stopping.");

        _cancellationTokenSource.Cancel();

        _fileSystemMonitor.Dispose();
        _deviceManager.Dispose();
    }
}