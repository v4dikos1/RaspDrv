using System.Collections.Concurrent;
using System.IO.Ports;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RaspDrv.Com.Helpers;
using RaspDrv.Com.Models;

namespace RaspDrv.Com;

public class SerialPortMonitorService: ITagDeviceController, IDisposable
{
    private readonly ILogger<SerialPortMonitorService> _logger;
    private readonly ComPortConfig _config;
    private readonly FileSystemWatcher? _watcher;
    private readonly ConcurrentDictionary<string, string> _connectedDevices = new();
    private readonly ConcurrentDictionary<string, string> _symLinksDictionary = new();
    private SerialPort? _serialPort;

    public event EventHandler<TagDeviceEventModel>? OnEventReceived;

    public SerialPortMonitorService(ILogger<SerialPortMonitorService> logger, IOptions<ComPortConfig> config)
    {
        _logger = logger;
        _config = config.Value;

        logger.LogInformation("SerialPortMonitorService is starting.");

        _watcher = new FileSystemWatcher(config.Value.PortName)
        {
            EnableRaisingEvents = true
        };
        _watcher.Created += OnDeviceConnected;
        _watcher.Deleted += OnDeviceDisconnected;
    }

    private void OnDeviceConnected(object sender, FileSystemEventArgs e)
    {
        var filePath = SymlinkResolver.TrimTempSymbols(e.FullPath, 16);
        Task.Run(() => HandleNewDevice(filePath));
    }

    private void OnDeviceDisconnected(object sender, FileSystemEventArgs e)
    {
        var filePath = SymlinkResolver.TrimTempSymbols(e.FullPath, 16);
        _symLinksDictionary.TryGetValue(filePath, out var realPath);
        if (realPath != null && _connectedDevices.TryRemove(realPath, out var serialNumber))
        {
            OnEventReceived?.Invoke(this, new TagDeviceEventModel
            {
                EventType = TagDeviceEventEnum.OnDisconnected,
                Data = serialNumber.Replace("SERNUM=", string.Empty)
            });
            _logger.LogInformation($"Device disconnected: {serialNumber}");
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

            _serialPort?.Close();

            _serialPort = new SerialPort(realDevicePath, _config.BaudRate, Parity.None, 8, StopBits.One)
            {
                ReadTimeout = 10000,
                WriteTimeout = 10000
            };
            _serialPort.DataReceived += SerialPortDataReceived;
            _serialPort.Open();
            _logger.LogInformation($"Port opened successfully: {realDevicePath}");

            _symLinksDictionary[devicePath] = realDevicePath;
            SendCommandToDevice(TagDeviceCommandsEnum.GetSerialNumber);
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
        _serialPort?.Close();

        var path = _connectedDevices.SingleOrDefault(x => x.Value == serialNumber).Key;
        if (path == null)
        {
            _logger.LogError($"Device with serial number {serialNumber} not found.");
            return Task.CompletedTask;
        }

        _serialPort = new SerialPort(path, _config.BaudRate, Parity.None, 8, StopBits.One)
        {
            ReadTimeout = 1000,
            WriteTimeout = 1000
        };
        _serialPort.DataReceived += SerialPortDataReceived;
        _serialPort.Open();

        SendCommandToDevice(TagDeviceCommandsEnum.GetBatteryCharge);

        return Task.CompletedTask;
    }

    private void SendCommandToDevice(TagDeviceCommandsEnum command)
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
            if (_serialPort is { IsOpen: true })
            {
                _serialPort.WriteLine(rawCommand);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error sending command '{command}' to port '{_serialPort?.PortName}': {ex.Message}");
        }
    }

    private void SerialPortDataReceived(object sender, SerialDataReceivedEventArgs e)
    {
        if (_serialPort is { IsOpen: true })
        {
            var response = _serialPort.ReadExisting();
            _logger.LogInformation($"Response from device: {response}");

            if (response.Contains("SERNUM"))
            {
                var serialNumber = response.Replace("SERNUM=", string.Empty).Replace(";", string.Empty);
                _connectedDevices[_serialPort.PortName] = serialNumber;
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
                _logger.LogInformation($"Device {_connectedDevices[_serialPort.PortName]} charge level: {charge}");
                OnEventReceived?.Invoke(this, new TagDeviceEventModel
                {
                    EventType = TagDeviceEventEnum.OnChargeReceived,
                    Data = charge
                });
            }

            _serialPort.Close();
        }
    }

    public void Dispose()
    {
        _logger.LogInformation("SerialPortMonitorService is stopping.");

        if (_watcher != null)
        {
            _watcher.Created -= OnDeviceConnected;
            _watcher.Deleted -= OnDeviceDisconnected;
            _watcher.Dispose();
        }
        _serialPort?.Dispose();
    }
}