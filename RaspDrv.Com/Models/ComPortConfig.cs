namespace RaspDrv.Com.Models;

public class ComPortConfig
{
    public required string PortName { get; set; }
    public required int BaudRate { get; set; }
}