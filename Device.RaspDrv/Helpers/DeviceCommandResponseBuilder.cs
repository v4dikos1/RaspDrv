using System.Collections;
using System.Text;
using System.Text.RegularExpressions;
using Device.RaspDrv.Helpers.Models;

namespace Device.RaspDrv.Helpers;
public class DeviceCommandResponseBuilder
{
    private string? _command;

    private const string CommandPattern =
        @"^POST:(?<sender>\d{1,3}),(?<receiver>\d{1,3}),(?<command>\d{1,3}),(?<payload_size>\d{1,3}),(?<payload>.+?)=(?<crc>[0-9A-Fa-f]{4})\r\n$";

    public DeviceCommandResponseBuilder SetCommand(byte[] commandBytes)
    {
        _command = Encoding.ASCII.GetString(commandBytes);
        return this;
    }

    public DeviceCommandModel? Build()
    {
        if (_command == null)
        {
            throw new ArgumentNullException(nameof(_command));
        }

        var match = Regex.Match(_command, CommandPattern);
        if (match.Success)
        {
            var commandType = MapCommand(int.Parse(match.Groups["command"].Value));
            var sender = int.Parse(match.Groups["sender"].Value);
            var payload = match.Groups["payload"].Value;
            var crc = match.Groups["crc"].Value;
            int? lockerNumber = null;
            List<ushort>? cellNumberList = null;
            if (commandType == DeviceCommandTypeEnum.Status)
            {
                lockerNumber = sender;
                cellNumberList = MapCellNumber(payload);
            }

            return new DeviceCommandModel(commandType, payload, _command, crc, lockerNumber, cellNumberList);
        }

        return null;
    }


    private List<ushort> MapCellNumber(string payload)
    {
        if (string.IsNullOrEmpty(payload) || payload.Length != 4)
        {
            throw new ArgumentOutOfRangeException("Invalid payload format!");
        }

        var ur = Convert.ToUInt16(payload, 16);
        var bytes = BitConverter.GetBytes(ur);
        BitArray bits = new BitArray(bytes);

        var cells_list = new List<ushort>();

        for (int i = 0; i < bits.Count; i++)
        {
            if (bits[i])
            {
                cells_list.Add((ushort)(i + 1));
            }
        }

        return cells_list;
    }


    private DeviceCommandTypeEnum MapCommand(int command)
    {
        return command switch
        {
            2 => DeviceCommandTypeEnum.OnBarcodeRead,
            3 => DeviceCommandTypeEnum.OnCardRead,
            10 => DeviceCommandTypeEnum.Status,
            _ => throw new ArgumentOutOfRangeException()
        };
    }
}
