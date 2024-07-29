namespace RaspDrv.Com.Models;

public class TagDeviceEventModel
{
    /// <summary>
    /// Данные события
    /// </summary>
    public string Data { get; set; } = string.Empty;

    /// <summary>
    /// Тип события
    /// </summary>
    public TagDeviceEventEnum EventType { get; set; }
}