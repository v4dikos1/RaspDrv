using System.Collections.Concurrent;
using System.IO.Ports;
using AutoGraph;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace RaspDrv.Com;

public class SerialPortMonitorService(ILogger<SerialPortMonitorService> logger) : IHostedService, ITagDeviceController
{
    private const string DirectoryPath = "/dev/serial/by-path";
    private FileSystemWatcher? _watcher;
    private readonly ConcurrentDictionary<string, string> _connectedDevices = new();
    private SerialPort? _serialPort;

    public event EventHandler<TagDeviceEventModel>? OnEventReceived;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("SerialPortMonitorService is starting.");

        _watcher = new FileSystemWatcher(DirectoryPath)
        {
            EnableRaisingEvents = true
        };
        _watcher.Created += OnDeviceConnected;
        _watcher.Deleted += OnDeviceDisconnected;

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("SerialPortMonitorService is stopping.");

        if (_watcher != null)
        {
            _watcher.Created -= OnDeviceConnected;
            _watcher.Deleted -= OnDeviceDisconnected;
            _watcher.Dispose();
        }

        return Task.CompletedTask;
    }

    private void OnDeviceConnected(object sender, FileSystemEventArgs e)
    {
        var filePath = TrimTemSymbols(e.FullPath, 16);
        Task.Run(() => HandleNewDevice(filePath));
    }

    private void OnDeviceDisconnected(object sender, FileSystemEventArgs e)
    {
        var filePath = TrimTemSymbols(e.FullPath, 16);
        var realPath = SymlinkResolver.GetRealPath(filePath);
        if (_connectedDevices.TryRemove(realPath, out var serialNumber))
        {
            OnEventReceived?.Invoke(this, new TagDeviceEventModel
            {
                EventType = TagDeviceEventEnum.OnDisconnected,
                Data = serialNumber.Replace("SERNUM=", string.Empty)
            });
            logger.LogInformation($"Device disconnected: {realPath}");
        }
    }

    private void HandleNewDevice(string devicePath)
    {
        try
        {
            var realDevicePath = SymlinkResolver.GetRealPath(devicePath);

            if (!File.Exists(realDevicePath))
            {
                logger.LogError($"Real device path '{realDevicePath}' does not exist.");
                return;
            }

            _serialPort?.Close();

            _serialPort = new SerialPort(realDevicePath, 115200, Parity.None, 8, StopBits.One)
            {
                ReadTimeout = 10000,
                WriteTimeout = 10000
            };
            _serialPort.DataReceived += SerialPortDataReceived;
            _serialPort.Open();
            logger.LogInformation($"Port opened successfully: {realDevicePath}");

            // Считывание серийного номера с метки
            SendCommandToDevice(TagDeviceCommandsEnum.GetSerialNumber);
        }
        catch (UnauthorizedAccessException ex)
        {
            logger.LogError($"Access to the port '{devicePath}' is denied: {ex.Message}");
        }
        catch (Exception ex)
        {
            logger.LogError($"Error handling new device {devicePath}: {ex.Message}");
        }
    }

    public async Task SendCommand(TagDeviceCommandsEnum command, string serialNumber)
    {
        _serialPort?.Close();

        var path = _connectedDevices.SingleOrDefault(x => x.Value == serialNumber).Key;
        if (path == null)
        {
            logger.LogError($"Device with serial number {serialNumber} not found.");
        }

        _serialPort = new SerialPort(path, 115200, Parity.None, 8, StopBits.One)
        {
            ReadTimeout = 10000,
            WriteTimeout = 10000
        };
        _serialPort.DataReceived += SerialPortDataReceived;
        _serialPort.Open();

        SendCommandToDevice(command);
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
            logger.LogError($"Error sending command '{command}' to port '{_serialPort?.PortName}': {ex.Message}");
        }
    }

    private void SerialPortDataReceived(object sender, SerialDataReceivedEventArgs e)
    {
        if (_serialPort is { IsOpen: true })
        {
            var response = _serialPort.ReadExisting();
            logger.LogInformation($"Response from device: {response}");

            if (response.Contains("SERNUM"))
            {
                _connectedDevices[_serialPort.PortName] = response;
                logger.LogInformation($"Device connected: {response}");
                OnEventReceived?.Invoke(this, new TagDeviceEventModel
                {
                    EventType = TagDeviceEventEnum.OnConnected,
                    Data = response.Replace("SERNUM=", string.Empty)
                });
            }
            else if (response.Contains("GBATTCHARGE"))
            {
                OnEventReceived?.Invoke(this, new TagDeviceEventModel
                {
                    EventType = TagDeviceEventEnum.OnChargeReceived,
                    Data = response.Replace("GBATTCHARGE=", string.Empty)
                });
            }

            _serialPort.Close();
        }
    }

    // FileSystemWatcher при считывании файлов, являющихся символьными ссылками
    // (которыми являются в том числе и записи о подключенных метках), добавляет в начало имени
    // файла .# и в конец имени суффикс из 16 символов.
    // Для дальнейшей работы с файлом ненужные символы обрезаются
    private string TrimTemSymbols(string str, int positionFromEnd)
    {
        if (string.IsNullOrEmpty(str) || str.Length < positionFromEnd)
        {
            return str;
        }

        if (str.Contains(".#"))
        {
            str = str.Replace(".#", string.Empty);
            var indexFromEnd = positionFromEnd - 1;
            var index = str.Length - 1 - indexFromEnd;

            if (index >= 0)
            {
                return str.Substring(0, index);
            }
        }

        return str;
    }
}