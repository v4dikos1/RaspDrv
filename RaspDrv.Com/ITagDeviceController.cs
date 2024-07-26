namespace RaspDrv.Com;

public interface ITagDeviceController
{
    event EventHandler<TagDeviceEventModel>? OnEventReceived;

    public Task SendCommand(TagDeviceCommandsEnum command, string serialNumber);
}