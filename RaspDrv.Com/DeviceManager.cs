using Microsoft.Extensions.Logging;
using RaspDrv.Com.Helpers;
using RaspDrv.Com.Models;
using System.Collections.Concurrent;
using System.IO.Ports;

namespace RaspDrv.Com;


internal class DeviceManager(ILogger<TagDeviceController> logger, ComPortConfig config,
    EventHandler<TagDeviceEventModel>? onEventReceived) : IDisposable
{
    // Подключенные метки: ключ - реальный путь до файла, значение - серийный номер метки
    private readonly ConcurrentDictionary<string, string> _connectedDevices = new();

    // Словарь ссылок. Ключ - символьная ссылка на файл метки, значение - реальный путь до файла метки
    private readonly ConcurrentDictionary<string, string> _symLinksDictionary = new();

    // Словарь портов. Ключ - реальный путь до файла, значение - порт
    private readonly ConcurrentDictionary<string, SerialPort> _serialPorts = new();

    /// <summary>
    /// Первичная инициализация меток при старте приложения.
    /// </summary>
    public void InitializeDevices()
    {
        try
        {
            var devices = Directory.GetFiles(config.PortName);
            foreach (var path in devices)
            {
                var devicePath = SymlinkResolver.TrimTempSymbols(path, 16);
                if (File.Exists(devicePath))
                {
                    HandleNewDevice(devicePath);
                }
                else
                {
                    logger.LogError($"Real device path '{devicePath}' does not exist.");
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogError($"Error initializing devices: {ex.Message}");
        }
    }

    public void SetEventHandler(EventHandler<TagDeviceEventModel>? eventHandler)
    {
        onEventReceived = eventHandler;
    }

    /// <summary>
    ///  Получение уровня заряда метки
    /// </summary>
    /// <param name="serialNumber">Серийный номер</param>
    public Task GetChargeLevel(string serialNumber)
    {
        var path = _connectedDevices.SingleOrDefault(x => x.Value == serialNumber).Key;
        if (path == null)
        {
            logger.LogError($"Device with serial number {serialNumber} not found.");
            return Task.CompletedTask;
        }

        if (!_serialPorts.TryGetValue(path, out var existingPort))
        {
            existingPort = new SerialPort(path, config.BaudRate, Parity.None, 8, StopBits.One)
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

    /// <summary>
    /// Обработка новой метки
    /// </summary>
    /// <param name="devicePath"></param>
    public void HandleNewDevice(string devicePath)
    {
        try
        {
            // Получение реальной ссылки на файл по символьной ссылке
            var realDevicePath = SymlinkResolver.GetRealPath(devicePath);

            if (!File.Exists(realDevicePath))
            {
                logger.LogError($"Real device path '{realDevicePath}' does not exist.");
                return;
            }

            if (!_serialPorts.TryGetValue(realDevicePath, out var existingPort))
            {
                existingPort = new SerialPort(realDevicePath, config.BaudRate, Parity.None, 8, StopBits.One)
                {
                    ReadTimeout = 10000,
                    WriteTimeout = 10000
                };
                existingPort.DataReceived += SerialPortDataReceived;
                existingPort.Open();
                _serialPorts[realDevicePath] = existingPort;
            }

            logger.LogInformation($"Port opened successfully: {realDevicePath}");

            _symLinksDictionary[devicePath] = realDevicePath;
            SendCommandToDevice(TagDeviceCommandsEnum.GetSerialNumber, existingPort);
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

    public void OnDeviceDisconnected(string filePath)
    {
        _symLinksDictionary.TryGetValue(filePath, out var realPath);
        if (realPath != null && _connectedDevices.TryRemove(realPath, out var serialNumber))
        {
            _serialPorts.TryRemove(realPath, out var port);
            port?.Dispose();

            onEventReceived?.Invoke(this, new TagDeviceEventModel
            {
                EventType = TagDeviceEventEnum.OnDisconnected,
                Data = serialNumber
            });
            logger.LogInformation($"Device disconnected: {serialNumber}");
        }
    }

    /// <summary>
    /// Отправка команды на метку
    /// </summary>
    /// <param name="command">Команда</param>
    /// <param name="serialPort">COM порт</param>
    /// <exception cref="ArgumentOutOfRangeException"></exception>
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
            logger.LogError($"Error sending command '{command}' to port '{serialPort.PortName}': {ex.Message}");
        }
    }

    /// <summary>
    /// Обработчик события ответа метки на команду
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    private void SerialPortDataReceived(object sender, SerialDataReceivedEventArgs e)
    {
        var serialPort = (SerialPort)sender;
        if (!serialPort.IsOpen) return;

        var response = serialPort.ReadExisting();
        logger.LogInformation($"Response from device: {response}");

        if (response.Contains("SERNUM"))
        {
            var serialNumber = response.Replace("SERNUM=", string.Empty).Replace(";", string.Empty);
            _connectedDevices[serialPort.PortName] = serialNumber;
            logger.LogInformation($"Device connected: {response}");
            onEventReceived?.Invoke(this, new TagDeviceEventModel
            {
                EventType = TagDeviceEventEnum.OnConnected,
                Data = serialNumber
            });
        }
        else if (response.Contains("BATTCHARGE"))
        {
            var charge = response.Replace("BATTCHARGE=", string.Empty).Replace(";", string.Empty);
            logger.LogInformation($"Device {_connectedDevices[serialPort.PortName]} charge level: {charge}");
            onEventReceived?.Invoke(this, new TagDeviceEventModel
            {
                EventType = TagDeviceEventEnum.OnChargeReceived,
                Data = charge
            });
        }
    }

    public void Dispose()
    {
        foreach (var port in _serialPorts.Values)
        {
            port.Dispose();
        }
    }
}