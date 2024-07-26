using System.Text;
using Device.RaspDrv.Helpers.Models;

namespace Device.RaspDrv.Helpers
{
    public class DeviceCommandRequestBuilder
    {
        private int? _partLockerNumber;
        private readonly List<ushort> _cellNumberList = new();
        private string? _payload = "";
        private DeviceCommandTypeEnum _type;

        public DeviceCommandModel Build()
        {
            _payload = ConvertCellNumberToHex(_cellNumberList);
            var commandType = _type switch
            {
                DeviceCommandTypeEnum.OpenDoor => "8",
                DeviceCommandTypeEnum.Status => "10",
                _ => throw new ArgumentOutOfRangeException()
            };
            var receiver = _partLockerNumber;
            var payloadCommand = $"POST:1,{receiver},{commandType},{_payload.Length},{_payload}=";
            var payloadCommandBytes = Encoding.ASCII.GetBytes(payloadCommand);
            var crc = Crc16.ComputeChecksum(payloadCommandBytes);
            var crcBytes = BitConverter.GetBytes(crc);
            var crcString = crcBytes[1].ToString("X2") + crcBytes[0].ToString("X2");
            var command = payloadCommand + crcString + "\r\n";
            return new DeviceCommandModel(_type, _payload, command, crcString, _partLockerNumber, _cellNumberList);
        }

        /// <summary>
        /// Установить глобальный номер ячейки
        /// </summary>
        /// <param name="cellNumber">Значение вида [part-locker]_[cell-number]
        /// где part-locker - номер части постамата, cell-number - номер ячейки
        /// </param>
        /// <returns></returns>
        public DeviceCommandRequestBuilder SetCellNumber(string cellNumber)
        {
            var splitStr = cellNumber.Split('_');
            var partLocker = ushort.Parse(splitStr[0]);
            var cell = ushort.Parse(splitStr[1]);
            if (_partLockerNumber != null && _partLockerNumber != partLocker)
            {
                throw new ApplicationException("Ячейки должны быть от одной части постамата");
            }
            _partLockerNumber = partLocker;
            _cellNumberList.Add(cell);
            return this;
        }

        /// <summary>
        /// Установить тип события
        /// </summary>
        /// <param name="type"></param>
        /// <returns></returns>
        public DeviceCommandRequestBuilder SetEventType(DeviceCommandTypeEnum type)
        {
            _type = type;
            return this;
        }

        private string ConvertCellNumberToHex(List<ushort> cellNumberList)
        {
            var u_mask = (ushort)0b1111_1111_1111_1111;

            //foreach (var cellNumber in cellNumberList)
            //{
            //    u_mask |= (ushort)(1 << cellNumber - 1);
            //}

            var result_bytes = BitConverter.GetBytes(u_mask);

            return $"{result_bytes[1]:X2}{result_bytes[0]:X2}";
        }
    }

    public static class Crc16
    {
        private const ushort Polynomial = 0xA001;
        private static readonly ushort[] Table = new ushort[256];

        public static ushort ComputeChecksum(byte[] bytes)
        {
            ushort crc = 0;
            for (var i = 0; i < bytes.Length; ++i)
            {
                var index = (byte)(crc ^ bytes[i]);
                crc = (ushort)(crc >> 8 ^ Table[index]);
            }
            return crc;
        }

        static Crc16()
        {
            for (ushort i = 0; i < Table.Length; ++i)
            {
                ushort value = 0;
                var temp = i;
                for (byte j = 0; j < 8; ++j)
                {
                    if (((value ^ temp) & 0x0001) != 0)
                    {
                        value = (ushort)(value >> 1 ^ Polynomial);
                    }
                    else
                    {
                        value >>= 1;
                    }
                    temp >>= 1;
                }
                Table[i] = value;
            }
        }
    }
}
