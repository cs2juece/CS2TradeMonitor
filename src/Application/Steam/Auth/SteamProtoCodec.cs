using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;

namespace CS2TradeMonitor.Application.Steam.Auth
{
    internal sealed class SteamProtoWriter
    {
        private readonly MemoryStream _stream = new();

        public byte[] ToArray() => _stream.ToArray();

        public void WriteString(int field, string value)
        {
            if (string.IsNullOrEmpty(value)) return;
            WriteTag(field, 2);
            byte[] bytes = Encoding.UTF8.GetBytes(value);
            WriteVarint((ulong)bytes.Length);
            _stream.Write(bytes, 0, bytes.Length);
        }

        public void WriteBytes(int field, byte[] value)
        {
            if (value.Length == 0) return;
            WriteTag(field, 2);
            WriteVarint((ulong)value.Length);
            _stream.Write(value, 0, value.Length);
        }

        public void WriteMessage(int field, byte[] value) => WriteBytes(field, value);

        public void WriteBool(int field, bool value)
        {
            WriteTag(field, 0);
            WriteVarint(value ? 1UL : 0UL);
        }

        public void WriteUInt64(int field, ulong value)
        {
            WriteTag(field, 0);
            WriteVarint(value);
        }

        public void WriteFixed64(int field, ulong value)
        {
            WriteTag(field, 1);
            Span<byte> buffer = stackalloc byte[8];
            BinaryPrimitives.WriteUInt64LittleEndian(buffer, value);
            _stream.Write(buffer);
        }

        private void WriteTag(int field, int wireType) => WriteVarint((ulong)((field << 3) | wireType));

        private void WriteVarint(ulong value)
        {
            while (value >= 0x80)
            {
                _stream.WriteByte((byte)(value | 0x80));
                value >>= 7;
            }
            _stream.WriteByte((byte)value);
        }
    }

    internal sealed class SteamProtoReader
    {
        private readonly ReadOnlyMemory<byte> _data;
        private int _pos;

        public SteamProtoReader(byte[] data)
        {
            _data = data ?? Array.Empty<byte>();
        }

        public bool TryReadField(out int field, out int wireType)
        {
            field = 0;
            wireType = 0;
            if (_pos >= _data.Length) return false;
            ulong tag = ReadVarint();
            field = (int)(tag >> 3);
            wireType = (int)(tag & 7);
            return field > 0;
        }

        public string ReadString(int wireType) => Encoding.UTF8.GetString(ReadBytes(wireType));

        public byte[] ReadBytes(int wireType)
        {
            if (wireType != 2) throw new InvalidDataException("Unexpected protobuf wire type.");
            int length = checked((int)ReadVarint());
            if (length < 0 || _pos + length > _data.Length) throw new InvalidDataException("Invalid protobuf length.");
            byte[] result = _data.Slice(_pos, length).ToArray();
            _pos += length;
            return result;
        }

        public bool ReadBool(int wireType) => ReadUInt64(wireType) != 0;

        public ulong ReadUInt64(int wireType)
        {
            return wireType switch
            {
                0 => ReadVarint(),
                1 => ReadFixed64(),
                _ => throw new InvalidDataException("Unexpected protobuf wire type.")
            };
        }

        public float ReadFloat(int wireType)
        {
            if (wireType != 5) throw new InvalidDataException("Unexpected protobuf wire type.");
            if (_pos + 4 > _data.Length) throw new InvalidDataException("Invalid protobuf fixed32.");
            float value = BitConverter.ToSingle(_data.Slice(_pos, 4).ToArray(), 0);
            _pos += 4;
            return value;
        }

        public void Skip(int wireType)
        {
            switch (wireType)
            {
                case 0:
                    ReadVarint();
                    break;
                case 1:
                    _pos += 8;
                    break;
                case 2:
                    int length = checked((int)ReadVarint());
                    _pos += length;
                    break;
                case 5:
                    _pos += 4;
                    break;
                default:
                    throw new InvalidDataException("Unsupported protobuf wire type.");
            }

            if (_pos > _data.Length)
                throw new InvalidDataException("Invalid protobuf skip.");
        }

        private ulong ReadFixed64()
        {
            if (_pos + 8 > _data.Length) throw new InvalidDataException("Invalid protobuf fixed64.");
            ulong value = BinaryPrimitives.ReadUInt64LittleEndian(_data.Span.Slice(_pos, 8));
            _pos += 8;
            return value;
        }

        private ulong ReadVarint()
        {
            ulong result = 0;
            int shift = 0;
            while (_pos < _data.Length && shift < 64)
            {
                byte b = _data.Span[_pos++];
                result |= (ulong)(b & 0x7F) << shift;
                if ((b & 0x80) == 0)
                    return result;
                shift += 7;
            }
            throw new InvalidDataException("Invalid protobuf varint.");
        }
    }
}
