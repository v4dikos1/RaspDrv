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

    // Интервал "Стабильности системы". После создания корневой папки необходим небольшой интервал времени для того, чтобы файловая система "устоялась":
    // FileSystemWatcher может прислать событие о том, что корневая папка была создана, но при попытке обращения к этой папке будет валиться ошибка о том,
    // что папки не существует. Выждав интервал, ошибки вызываться не будет
    private readonly int _stabilityInterval = 100;

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

    private bool InitializeDeviceWatcher()
    {
        _deviceWatcher?.Dispose();
        try
        {
            _deviceWatcher = new FileSystemWatcher(config.PortName)
            {
                EnableRaisingEvents = true
            };
            _deviceWatcher.Created += OnDeviceConnected;
            _deviceWatcher.Deleted += OnDeviceDisconnected;
            return true;
        }
        catch (Exception ex)
        {
            logger.LogError("Error during creating FileSystemWatcher: {Message}", ex.Message);
            return false;
        }
    }

    private async void OnRootCreatedOrDeleted(object sender, FileSystemEventArgs e)
    {
        if (e.FullPath.Equals("/dev/serial", StringComparison.OrdinalIgnoreCase))
        {
            logger.LogInformation($"Root directory change detected: {e.ChangeType}");

            // При пересоздании корневой папки, инициализируем _deviceWatcher заново
            if (e.ChangeType == WatcherChangeTypes.Created)
            {
                await Task.Delay(_stabilityInterval);
                InitializeDeviceWatcher();

                // Необходимо проверить, не были ли добавлены новые файлы в папку в момент пересоздания _deviceWatcher.
                // Корневая папка создается в момент добавления нового файла (при подключении устройства).
                deviceManager.InitializeDevices();
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