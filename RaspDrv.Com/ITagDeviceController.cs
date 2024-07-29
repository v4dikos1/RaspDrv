using RaspDrv.Com.Models;

namespace RaspDrv.Com;

public interface ITagDeviceController
{
    /// <summary>
    /// Событие метки
    /// </summary>
    event EventHandler<TagDeviceEventModel>? OnEventReceived;
    
    /// <summary>
    /// Получение уровня заряда метки
    /// </summary>
    /// <param name="serialNumber">Серийный номер метки</param>
    public Task GetChargeLevel(string serialNumber);
}