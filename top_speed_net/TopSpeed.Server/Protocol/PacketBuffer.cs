using System;
using System.Buffers.Binary;
using System.Text;

namespace TopSpeed.Server.Protocol
{
    internal ref struct PacketReader
    {
        private readonly ReadOnlySpan<byte> _data;
        private int _offset;

        public PacketReader(ReadOnlySpan<byte> data)
        {
            _data = data;
            _offset = 0;
        }

        public bool HasRemaining(int count) => _offset + count <= _data.Length;

        public byte ReadByte() => _data[_offset++];

        public bool ReadBool() => ReadByte() != 0;

        public ushort ReadUInt16()
        {
            var value = BinaryPrimitives.ReadUInt16LittleEndian(_data.Slice(_offset, 2));
            _offset += 2;
            return value;
        }

        public uint ReadUInt32()
        {
            var value = BinaryPrimitives.ReadUInt32LittleEndian(_data.Slice(_offset, 4));
            _offset += 4;
            return value;
        }

        public int ReadInt32()
        {
            var value = BinaryPrimitives.ReadInt32LittleEndian(_data.Slice(_offset, 4));
            _offset += 4;
            return value;
        }

        public string ReadFixedString(int length)
        {
            var span = _data.Slice(_offset, length);
            _offset += length;
            var value = Encoding.ASCII.GetString(span);
            var nullIndex = value.IndexOf('\0');
            return nullIndex >= 0 ? value.Substring(0, nullIndex) : value.Trim();
        }
    }

    internal ref struct PacketWriter
    {
        private Span<byte> _buffer;
        private int _offset;

        public PacketWriter(Span<byte> buffer)
        {
            _buffer = buffer;
            _offset = 0;
        }

        public int Written => _offset;

        public void WriteByte(byte value) => _buffer[_offset++] = value;

        public void WriteBool(bool value) => WriteByte((byte)(value ? 1 : 0));

        public void WriteUInt16(ushort value)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(_buffer.Slice(_offset, 2), value);
            _offset += 2;
        }

        public void WriteUInt32(uint value)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(_buffer.Slice(_offset, 4), value);
            _offset += 4;
        }

        public void WriteInt32(int value)
        {
            BinaryPrimitives.WriteInt32LittleEndian(_buffer.Slice(_offset, 4), value);
            _offset += 4;
        }

        public void WriteFixedString(string value, int length)
        {
            var bytes = Encoding.ASCII.GetBytes(value ?? string.Empty);
            var count = Math.Min(length, bytes.Length);
            bytes.AsSpan(0, count).CopyTo(_buffer.Slice(_offset, count));
            for (var i = count; i < length; i++)
                _buffer[_offset + i] = 0;
            _offset += length;
        }
    }
}
