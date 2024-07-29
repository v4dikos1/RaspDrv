using System.Collections.Concurrent;
using System.IO.Ports;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RaspDrv.Com.Helpers;
using RaspDrv.Com.Models;

namespace RaspDrv.Com
{
    public class SerialPortMonitorService : ITagDeviceController, IDisposable
    {
        private readonly ILogger<SerialPortMonitorService> _logger;
        private readonly ComPortConfig _config;
        private FileSystemWatcher? _deviceWatcher;
        private FileSystemWatcher? _rootWatcher;
        private readonly ConcurrentDictionary<string, string> _connectedDevices = new();
        private readonly ConcurrentDictionary<string, string> _symLinksDictionary = new();
        private readonly ConcurrentDictionary<string, SerialPort> _serialPorts = new();
        private readonly CancellationTokenSource _cancellationTokenSource = new();
        private bool _fileSystemWatcherInitialized = false;

        public event EventHandler<TagDeviceEventModel>? OnEventReceived;

        public SerialPortMonitorService(ILogger<SerialPortMonitorService> logger, IOptions<ComPortConfig> config)
        {
            _logger = logger;
            _config = config.Value;

            logger.LogInformation("SerialPortMonitorService is starting.");
            InitializeDevices();
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
            _deviceWatcher = new FileSystemWatcher(_config.PortName)
            {
                EnableRaisingEvents = true
            };
            _deviceWatcher.Created += OnDeviceConnected;
            _deviceWatcher.Deleted += OnDeviceDisconnected;
        }

        private async void OnRootCreatedOrDeleted(object sender, FileSystemEventArgs e)
        {
            if (e.FullPath.Equals("/dev/serial", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogInformation($"Root directory change detected: {e.ChangeType}");

                if (e.ChangeType == WatcherChangeTypes.Created && !_fileSystemWatcherInitialized)
                {
                    await ScanForNewDevices();
                    InitializeDeviceWatcher();
                    _fileSystemWatcherInitialized = true;
                }
            }
        }

        private async Task ScanForNewDevices()
        {
            try
            {
                var devices = Directory.GetFiles(_config.PortName);
                foreach (var devicePath in devices)
                {
                    var realDevicePath = SymlinkResolver.TrimTempSymbols(devicePath, 16);
                    if (File.Exists(realDevicePath))
                    {
                        await Task.Run(() => HandleNewDevice(devicePath));
                    }
                    else
                    {
                        _logger.LogError($"Real device path '{realDevicePath}' does not exist.");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error scanning for new devices: {ex.Message}");
            }
        }

        private async void OnDeviceConnected(object sender, FileSystemEventArgs e)
        {
            if (e.FullPath.StartsWith(_config.PortName))
            {
                var filePath = SymlinkResolver.TrimTempSymbols(e.FullPath, 16);
                await Task.Run(() => HandleNewDevice(filePath));
            }
        }

        private async void InitializeDevices()
        {
            try
            {
                var devices = Directory.GetFiles(_config.PortName);
                foreach (var devicePath in devices)
                {
                    var realDevicePath = SymlinkResolver.TrimTempSymbols(devicePath, 16);
                    if (File.Exists(realDevicePath))
                    {
                        await Task.Run(() => HandleNewDevice(devicePath));
                    }
                    else
                    {
                        _logger.LogError($"Real device path '{realDevicePath}' does not exist.");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error initializing devices: {ex.Message}");
            }
        }

        private void OnDeviceDisconnected(object sender, FileSystemEventArgs e)
        {
            if (e.FullPath.StartsWith(_config.PortName))
            {
                var filePath = SymlinkResolver.TrimTempSymbols(e.FullPath, 16);
                _symLinksDictionary.TryGetValue(filePath, out var realPath);
                if (realPath != null && _connectedDevices.TryRemove(realPath, out var serialNumber))
                {
                    _serialPorts.TryRemove(realPath, out var port);
                    port?.Dispose();

                    OnEventReceived?.Invoke(this, new TagDeviceEventModel
                    {
                        EventType = TagDeviceEventEnum.OnDisconnected,
                        Data = serialNumber
                    });
                    _logger.LogInformation($"Device disconnected: {serialNumber}");
                }
            }
        }

        private void HandleNewDevice(string devicePath)
        {
            try
            {
                var realDevicePath = SymlinkResolver.GetRealPath(devicePath);

                if (!File.Exists(realDevicePath))
                {
                    _logger.LogError($"Real device path '{realDevicePath}' does not exist.");
                    return;
                }

                if (!_serialPorts.TryGetValue(realDevicePath, out var existingPort))
                {
                    existingPort = new SerialPort(realDevicePath, _config.BaudRate, Parity.None, 8, StopBits.One)
                    {
                        ReadTimeout = 10000,
                        WriteTimeout = 10000
                    };
                    existingPort.DataReceived += SerialPortDataReceived;
                    existingPort.Open();
                    _serialPorts[realDevicePath] = existingPort;
                }

                _logger.LogInformation($"Port opened successfully: {realDevicePath}");

                _symLinksDictionary[devicePath] = realDevicePath;
                SendCommandToDevice(TagDeviceCommandsEnum.GetSerialNumber, existingPort);
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogError($"Access to the port '{devicePath}' is denied: {ex.Message}");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error handling new device {devicePath}: {ex.Message}");
            }
        }

        public Task GetChargeLevel(string serialNumber)
        {
            var path = _connectedDevices.SingleOrDefault(x => x.Value == serialNumber).Key;
            if (path == null)
            {
                _logger.LogError($"Device with serial number {serialNumber} not found.");
                return Task.CompletedTask;
            }

            if (!_serialPorts.TryGetValue(path, out var existingPort))
            {
                existingPort = new SerialPort(path, _config.BaudRate, Parity.None, 8, StopBits.One)
                {
                    ReadTimeout = 1000,
                    WriteTimeout = 1000
                };
                existingPort.DataReceived += SerialPortDataReceived;
                existingPort.Open();
                _serialPorts[path] = existingPort;
            }

            SendCommandToDevice(TagDeviceCommandsEnum.GetBatteryCharge, existingPort);

            return Task.CompletedTask;
        }

        private void SendCommandToDevice(TagDeviceCommandsEnum command, SerialPort serialPort)
        {
            string rawCommand;
            switch (command)
            {
                case TagDeviceCommandsEnum.GetVersion:
                    rawCommand = "GVERSION";
                    break;
                case TagDeviceCommandsEnum.GetSerialNumber:
                    rawCommand = "GSERNUM";
                    break;
                case TagDeviceCommandsEnum.GetBatteryCharge:
                    rawCommand = "GBATTCHARGE";
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(command), command, null);
            }

            try
            {
                if (serialPort.IsOpen)
                {
                    serialPort.WriteLine(rawCommand);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error sending command '{command}' to port '{serialPort.PortName}': {ex.Message}");
            }
        }

        private void SerialPortDataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            var serialPort = (SerialPort)sender;
            if (serialPort.IsOpen)
            {
                var response = serialPort.ReadExisting();
                _logger.LogInformation($"Response from device: {response}");

                if (response.Contains("SERNUM"))
                {
                    var serialNumber = response.Replace("SERNUM=", string.Empty).Replace(";", string.Empty);
                    _connectedDevices[serialPort.PortName] = serialNumber;
                    _logger.LogInformation($"Device connected: {response}");
                    OnEventReceived?.Invoke(this, new TagDeviceEventModel
                    {
                        EventType = TagDeviceEventEnum.OnConnected,
                        Data = serialNumber
                    });
                }
                else if (response.Contains("BATTCHARGE"))
                {
                    var charge = response.Replace("BATTCHARGE=", string.Empty).Replace(";", string.Empty);
                    _logger.LogInformation($"Device {_connectedDevices[serialPort.PortName]} charge level: {charge}");
                    OnEventReceived?.Invoke(this, new TagDeviceEventModel
                    {
                        EventType = TagDeviceEventEnum.OnChargeReceived,
                        Data = charge
                    });
                }
            }
        }

        public void Dispose()
        {
            _logger.LogInformation("SerialPortMonitorService is stopping.");

            _cancellationTokenSource.Cancel();

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

            foreach (var port in _serialPorts.Values)
            {
                port.Dispose();
            }
        }
    }
}
