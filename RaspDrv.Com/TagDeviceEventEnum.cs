using System.ComponentModel;

namespace RaspDrv.Com;

public enum TagDeviceEventEnum
{
    [Description("Метка подключена")]
    OnConnected = 0,

    [Description("Метка отключена")]
    OnDisconnected = 1,

    [Description("Получен уровень заряда")]
    OnChargeReceived = 2
}