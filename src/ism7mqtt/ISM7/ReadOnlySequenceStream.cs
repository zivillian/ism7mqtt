using System;
using System.Buffers;
using System.IO;

namespace ism7mqtt
{
    public class ReadOnlySequenceStream:Stream
    {
        private readonly ReadOnlySequence<byte> _sequence;
        private SequencePosition _position;

        public ReadOnlySequenceStream(ReadOnlySequence<byte> sequence)
        {
            _sequence = sequence;
            _position = sequence.Start;
        }
        public override void Flush()
        {
            throw new System.NotImplementedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            return Read(buffer.AsSpan(0, count));
        }

        public override int Read(Span<byte> buffer)
        {
            if (_sequence.End.Equals(_position)) return 0;
            var part = _sequence.Slice(_position);
            if (part.Length > buffer.Length)
                part = part.Slice(part.Start, buffer.Length);
            part.CopyTo(buffer);
            _position = part.End;
            return (int) part.Length;
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new System.NotImplementedException();
        }

        public override void SetLength(long value)
        {
            throw new System.NotImplementedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new System.NotImplementedException();
        }

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => _sequence.Length;

        public override long Position
        {
            get => _sequence.GetOffset(_position);
            set => _position = _sequence.GetPosition(value);
        }
    }
}