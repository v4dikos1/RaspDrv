using System.ComponentModel;

namespace RaspDrv.Com;

public enum TagDeviceCommandsEnum
{
    [Description("Получение версии прошивки метки")]
    GetVersion = 0,

    [Description("Получение серийного номера метки")]
    GetSerialNumber = 1,

    [Description("Получение уровня заряда метки")]
    GetBatteryCharge = 2
}