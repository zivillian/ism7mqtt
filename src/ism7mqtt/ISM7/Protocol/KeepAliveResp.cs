namespace ism7mqtt.ISM7.Protocol
{
    public class KeepAliveResp : IResponse
    {
        public KeepAliveResp(short seq)
        {
            Seq = seq;
        }

        public short Seq { get; }

        public PayloadType MessageType => PayloadType.KeepAlive;
    }
}