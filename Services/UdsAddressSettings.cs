namespace BOOTWR.Services
{
    /// <summary>当前 UDS 通信地址的单一来源。所有服务的发送ID/接收ID 均委托到此，
    /// 界面修改物理地址/响应地址后由 ViewModel 同步更新，全局立即生效。</summary>
    public class UdsAddressSettings
    {
        public uint PhysicalAddress { get; set; } = 0x7E0;
        public uint ResponseAddress { get; set; } = 0x7E8;
    }
}
