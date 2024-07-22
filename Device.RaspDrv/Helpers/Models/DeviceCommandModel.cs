namespace Device.RaspDrv.Helpers.Models;

public class DeviceCommandModel
{
    public DeviceCommandModel(DeviceCommandTypeEnum commandType, string? payload, string? command,
        string? checkSum, int? lockerPartNumber, List<ushort>? cellNumberList)
    {
        CommandType = commandType;
        Payload = payload;
        Command = command;
        CheckSum = checkSum;
        LockerPartNumber = lockerPartNumber;
        CellNumberList = cellNumberList;
    }

    /// <summary>
    /// Тип команды
    /// </summary>
    public DeviceCommandTypeEnum CommandType { get; }

    /// <summary>
    /// Доп полезная нагрузка
    /// </summary>
    public string? Payload { get; }

    /// <summary>
    /// Сформированная команда на устройство
    /// прим.: POST:1,128,8,4,0001=2269
    /// </summary>
    public string? Command { get; }

    /// <summary>
    /// CRC16 контрольная сумма (в HEX представлении)
    /// </summary>
    public string? CheckSum { get; }

    /// <summary>
    /// Номер шкафа (от 0 до 128)
    /// </summary>
    public int? LockerPartNumber { get; }

    /// <summary>
    /// Номера ячейки (от 0 до 15)
    /// </summary>
    public IEnumerable<ushort>? CellNumberList { get; }

    public override string ToString() => Command ?? string.Empty;
}
