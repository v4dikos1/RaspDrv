using Microsoft.Extensions.Logging;
using RaspDrv.Com.Helpers;
using RaspDrv.Com.Models;

namespace RaspDrv.Com;

internal class FileSystemMonitor(ILogger<TagDeviceController> logger, ComPortConfig config, DeviceManager deviceManager)
    : IDisposable
{
    // Watcher для отслеживания подключения новых меток
    private FileSystemWatcher? _deviceWatcher;

    // Watcher для отслеживания удаления/создания корневой папки (/dev/serial/by-path), содержащей файлы меток.
    // Необходимо отслеживать статус корневой папки, т.к при отключении всех меток от хаба, корневая папка меток будет удалена
    // и _deviceWatcher более не будет отслеживать изменения в папке даже при ее дальнейшем пересоздании.
    private FileSystemWatcher? _rootWatcher;

    public void Initialize()
    {
        InitializeRootWatcher();
        InitializeDeviceWatcher();
    }

    private void InitializeRootWatcher()
    {
        _rootWatcher = new FileSystemWatcher("/dev")
        {
            EnableRaisingEvents = true,
            IncludeSubdirectories = false
        };
        _rootWatcher.Created += OnRootCreatedOrDeleted;
        _rootWatcher.Deleted += OnRootCreatedOrDeleted;
    }

    private void InitializeDeviceWatcher()
    {
        _deviceWatcher?.Dispose();
        _deviceWatcher = new FileSystemWatcher(config.PortName)
        {
            EnableRaisingEvents = true
        };
        _deviceWatcher.Created += OnDeviceConnected;
        _deviceWatcher.Deleted += OnDeviceDisconnected;
    }

    private void OnRootCreatedOrDeleted(object sender, FileSystemEventArgs e)
    {
        if (e.FullPath.Equals("/dev/serial", StringComparison.OrdinalIgnoreCase))
        {
            logger.LogInformation($"Root directory change detected: {e.ChangeType}");

            // При пересоздании корневой папки, инициализируем _deviceWatcher заново
            if (e.ChangeType == WatcherChangeTypes.Created)
            {
                // Необходимо проверить, не были ли добавлены новые файлы в папку в момент пересоздания _deviceWatcher.
                // Корневая папка создается в момент добавления нового файла (при подключении устройства).
                deviceManager.InitializeDevices();

                InitializeDeviceWatcher();
            }
        }
    }

    private async void OnDeviceConnected(object sender, FileSystemEventArgs e)
    {
        if (e.FullPath.StartsWith(config.PortName))
        {
            var filePath = SymlinkResolver.TrimTempSymbols(e.FullPath, 16);
            await Task.Run(() => deviceManager.HandleNewDevice(filePath));
        }
    }

    private void OnDeviceDisconnected(object sender, FileSystemEventArgs e)
    {
        if (e.FullPath.StartsWith(config.PortName))
        {
            var filePath = SymlinkResolver.TrimTempSymbols(e.FullPath, 16);
            deviceManager.OnDeviceDisconnected(filePath);
        }
    }

    public void Dispose()
    {
        if (_deviceWatcher != null)
        {
            _deviceWatcher.Created -= OnDeviceConnected;
            _deviceWatcher.Deleted -= OnDeviceDisconnected;
            _deviceWatcher.Dispose();
        }

        if (_rootWatcher != null)
        {
            _rootWatcher.Created -= OnRootCreatedOrDeleted;
            _rootWatcher.Deleted -= OnRootCreatedOrDeleted;
            _rootWatcher.Dispose();
        }
    }
}