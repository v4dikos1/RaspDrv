using System.IO.Ports;
using System.Text;
using Device.RaspDrv.Helpers;
using Device.RaspDrv.Helpers.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Device.RaspDrv
{
    public class RaspDrvController : IDeviceController, IDisposable
    {
        private readonly IOptions<RaspDrvConfig> _options;
        private readonly ILogger<RaspDrvController> _logger;

        private SerialPort _serialPort;

        // Стартовая и конечная последовательности кадра
        private const string _frameStart = "POST:";
        private readonly byte[] _frameStartBytes = Encoding.ASCII.GetBytes(_frameStart);
        private byte[] _startLkp = new byte[5];

        private const string _frameEnd = "\r\n";
        private readonly byte[] _frameEndBytes = Encoding.ASCII.GetBytes(_frameEnd);
        private byte[] _endLkp = new byte[2];

        // Флаг, указывающий на то, что идет сборка кадра
        private bool _gatheringFrame = false;

        // Собранный кадр
        private List<byte> _frame = new();
        private List<byte> _rawBuffer = new();

        public event EventHandler<DeviceCommandModel>? OnCommandReceived;

        public RaspDrvController(IOptions<RaspDrvConfig> options, ILogger<RaspDrvController> logger)
        {
            _options = options;
            _logger = logger;

            _serialPort = new SerialPort(
                _options.Value.PortName,
                _options.Value.BaudRate,
                Parity.None,
                8,
                StopBits.One);

            _serialPort.DataReceived += SerialPort_DataReceived;

            try
            {
                _serialPort.Open();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error while opening RaspDrv port! PortName={_options.Value.PortName};");
                throw;
            }

            if(_serialPort.IsOpen)
            {
                _logger.LogInformation("RaspDrv serial port is open!");
            }
            else
            {
                _logger.LogError("RaspDrv serial port not open!");
            }
        }

        public async Task SendCommand(DeviceCommandModel command)
        {
            _logger.LogInformation($"Sending command to RaspDrv: {command}");

            byte[] bytes = Encoding.ASCII.GetBytes(command.ToString());
            _logger.LogTrace($"Bytes to send: {string.Join(" ", bytes.Select(b => b.ToString("X2")))}");

            try
            {
                await _serialPort.BaseStream.WriteAsync(bytes, 0, bytes.Length);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while sending command to RaspDrv");
                throw;
            }
        }

        private void SerialPort_DataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            try
            {
                int byteRead = -1;
                _rawBuffer.Clear();

                while (_serialPort.BytesToRead > 0)
                {
                    byteRead = _serialPort.ReadByte();
                    _rawBuffer.Add((byte)byteRead);

                    ShiftAndAddValue(_startLkp, (byte)byteRead);
                    ShiftAndAddValue(_endLkp, (byte)byteRead);

                    if (!_gatheringFrame && SequenceEqual(_startLkp, _frameStartBytes))
                    {
                        _frame.Clear();
                        _gatheringFrame = true;

                        // Добавим в кадр стартовую последовательность
                        _frame.AddRange(_frameStartBytes);
                        continue;
                    }

                    if (_gatheringFrame)
                    {
                        _frame.Add((byte)byteRead);
                    }

                    if (_gatheringFrame && SequenceEqual(_endLkp, _frameEndBytes))
                    {
                        _gatheringFrame = false;

                        _logger.LogTrace($"Frame found!");
                    }
                }

                _logger.LogTrace($"Raw bytes received: {string.Join(" ", _rawBuffer.Select(b => b.ToString("X2")))}");
                _logger.LogTrace($"Raw message:{Encoding.ASCII.GetString(_rawBuffer.ToArray())}");

                if(!_gatheringFrame && _frame.Count > 0)
                {
                    _logger.LogTrace($"Processing frame: {Encoding.ASCII.GetString(_frame.ToArray())}");

                    var command = new DeviceCommandResponseBuilder()
                        .SetCommand(_frame.ToArray())
                        .Build();

                    _frame.Clear();

                    if (command != null)
                    {
                        _logger.LogDebug($"Command received: {command}");
                        OnCommandReceived?.Invoke(this, command);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while reading answer from RaspDrv");
                throw;
            }
        }

        private static void ShiftAndAddValue(byte[] array, byte newValue)
        {
            // Сдвигаем все элементы массива на одну позицию влево
            Buffer.BlockCopy(array, 1, array, 0, array.Length - 1);

            // Добавляем новое значение в конец массива
            array[^1] = newValue;
        }

        private static bool SequenceEqual(byte[] firstArray, byte[] secondArray)
        {
            return firstArray.AsSpan().SequenceEqual(secondArray);
        }

        public void Dispose()
        {
            _serialPort.Close();
        }
    }
}
