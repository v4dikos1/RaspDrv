using Device.RaspDrv.Helpers.Models;

namespace Device.RaspDrv
{
    public interface IDeviceController
    {
        event EventHandler<DeviceCommandModel>? OnCommandReceived;

        public Task SendCommand(DeviceCommandModel command);
    }
}
