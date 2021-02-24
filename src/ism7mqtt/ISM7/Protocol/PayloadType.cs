namespace ism7mqtt
{
    public enum PayloadType:short
    {
        PortalLogonReq = 0,
        PortalLogonResp = 1,
        SystemconfigReq = 2,
        SystemconfigResp = 3,
        TgrBundleReq = 4,
        TgrBundleResp = 5,
        ISMConfigReq = 6,
        ISMConfigResp = 7,
        DirectLogonReq = 8,
        DirectLogonResp = 9,
        ISMProtocolConfigReq,
        ISMProtocolConfigResp,
        ISMProtocolValueReq,
        ISMProtocolValueResp,
        BinaryData,
        KeepAlive,
        PortalLogonReqEncrypted,
        StartSequence,
        Unknown
    }
}