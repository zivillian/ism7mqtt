namespace ism7mqtt
{
    public enum PayloadType:short
    {
        PortalLogonReq = 0x0,
        PortalLogonResp = 0x1,
        SystemconfigReq = 0x2,
        SystemconfigResp = 0x3,
        TgrBundleReq = 0x4,
        TgrBundleResp = 0x5,
        ISMConfigReq = 0x6,
        ISMConfigResp = 0x7,
        DirectLogonReq = 0x8,
        DirectLogonResp = 0x9,
        ISMProtocolConfigReq,
        ISMProtocolConfigResp,
        ISMProtocolValueReq,
        ISMProtocolValueResp,
        BinaryData,
        KeepAlive = 0xf,
        PortalLogonReqEncrypted,
        StartSequence,
        Unknown
    }
}