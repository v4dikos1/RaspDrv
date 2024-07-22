namespace Device.RaspDrv
{
    public class RaspDrvConfig
    {
        public required string PortName { get; set; }
        public required int BaudRate { get; set; }
    }
}
