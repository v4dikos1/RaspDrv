using System.IO.Ports;
using System.Text;

namespace RaspDrv.Com
{
    internal class Program
    {
        private static SerialPort? _serialPort;

        static void Main(string[] args)
        {
            Console.WriteLine("Hello, World!");

            // parse menu options

            while (true)
            {
                ShowMenu();

                var option = Console.ReadLine();

                switch (option)
                {
                    case "1":
                        Console.Write("Enter message: ");
                        while (true)
                        {
                            var message1 = Console.ReadLine();
                            if (message1 == "qq")
                                break;

                            Task.Run(() => { Send(message1!); });
                        }
                        break;
                    case "2":
                        Console.Write("Enter raw message: ");
                        while (true)
                        {
                            var message2 = Console.ReadLine();

                            if (message2 == "qq")
                                break;

                            Task.Run(() => { SendRaw(message2!); });
                        }
                        break;
                    case "3":
                        Console.Write("Enter COM port: ");
                        var port = Console.ReadLine();

                        if (_serialPort != null)
                            _serialPort.Close();
                        _serialPort = new SerialPort(port, 115200, Parity.None, 8, StopBits.One);
                        _serialPort.DataReceived += SerialPort_DataReceived;
                        _serialPort.Open();
                        break;
                    case "4":
                        if (_serialPort != null)
                        {
                            _serialPort.Close();
                            _serialPort.Dispose();
                        }
                        Environment.Exit(0);

                        break;
                    default:
                        Console.WriteLine("Invalid option");
                        break;
                }
            }
        }

        private static void SerialPort_DataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            var bytesToRead = _serialPort.BytesToRead;
            var buffer = new byte[bytesToRead];
            _serialPort.Read(buffer, 0, bytesToRead);

            var readResult = Encoding.ASCII.GetString(buffer);

            Console.Write($"{readResult}");
        }

        private static void ShowMenu()
        {
            Console.WriteLine("\nMenu:\n");
            Console.WriteLine("1. Send message");
            Console.WriteLine("2. Send raw message");
            Console.WriteLine("3. Set COM port");
            Console.WriteLine("4. Exit");
            Console.Write("\nChoose an option: ");
        }

        public static bool Send(string request)
        {
            var bdata = Encoding.ASCII.GetBytes(request);

            var crc = CalculateCrc16(bdata);

            var crcBytes = BitConverter.GetBytes(crc);

            request = request + crcBytes[1].ToString("X2") + crcBytes[0].ToString("X2");

            request += "\r\n";

            try
            {
                if (_serialPort.IsOpen)
                {
                    Console.WriteLine($"Sending >>> {request}");
                    _serialPort.Write(request);
                }
                else
                {
                    return false;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
                throw;
            }

            return true;
        }

        public static bool SendRaw(string request)
        {
            try
            {
                if (_serialPort.IsOpen)
                {
                    Console.WriteLine($"Sending >>>: {request}");
                    _serialPort.Write(request);
                }
                else
                {
                    return false;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
                throw;
            }

            return true;
        }

        //CRC16 calculation
        private static ushort CalculateCrc16(byte[] data)
        {
            ushort crc = 0;

            for (int i = 0; i < data.Length; i++)
            {
                crc ^= data[i];

                for (int j = 0; j < 8; j++)
                {
                    if ((crc & 0x0001) != 0)
                    {
                        crc >>= 1;
                        crc ^= 0xA001;
                    }
                    else
                    {
                        crc >>= 1;
                    }
                }
            }

            return crc;
        }
    }
}
