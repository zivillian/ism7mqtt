using System.Buffers.Binary;

namespace ism7mqtt.ISM7.Protocol
{
    public class KeepAliveReq : IPayload
    {
        private readonly short _seq;

        public KeepAliveReq(short seq)
        {
            _seq = seq;
        }

        public PayloadType Type => PayloadType.KeepAlive;

        public byte[] Serialize()
        {
            var result = new byte[2];
            BinaryPrimitives.WriteInt16BigEndian(result, _seq);
            return result;
        }
    }
}