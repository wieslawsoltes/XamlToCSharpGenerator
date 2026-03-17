using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace XamlToCSharpGenerator.PreviewerHost.Protocol;

internal static class MinimalBson
{
    private const byte TypeDouble = 0x01;
    private const byte TypeString = 0x02;
    private const byte TypeDocument = 0x03;
    private const byte TypeArray = 0x04;
    private const byte TypeBoolean = 0x08;
    private const byte TypeNull = 0x0A;
    private const byte TypeInt32 = 0x10;
    private const byte TypeInt64 = 0x12;

    public static byte[] SerializeDocument(IReadOnlyDictionary<string, object?> document)
    {
        ArgumentNullException.ThrowIfNull(document);

        using var stream = new MemoryStream();
        stream.Write(stackalloc byte[4]);

        foreach (var pair in document)
        {
            WriteElement(stream, pair.Key, pair.Value);
        }

        stream.WriteByte(0);
        var buffer = stream.GetBuffer();
        BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(0, 4), checked((int)stream.Length));
        return stream.ToArray();
    }

    public static IReadOnlyDictionary<string, object?> DeserializeDocument(ReadOnlySpan<byte> payload)
    {
        var offset = 0;
        return ReadDocument(payload, ref offset);
    }

    private static Dictionary<string, object?> ReadDocument(ReadOnlySpan<byte> payload, ref int offset)
    {
        if (offset + 4 > payload.Length)
        {
            throw new InvalidDataException("BSON payload is truncated.");
        }

        var documentStart = offset;
        var documentLength = BinaryPrimitives.ReadInt32LittleEndian(payload.Slice(offset, 4));
        if (documentLength < 5 || documentStart + documentLength > payload.Length)
        {
            throw new InvalidDataException("BSON payload length is invalid.");
        }

        offset += 4;
        var documentEnd = documentStart + documentLength - 1;
        var document = new Dictionary<string, object?>(StringComparer.Ordinal);

        while (offset < documentEnd)
        {
            var elementType = payload[offset++];
            var name = ReadCString(payload, ref offset);
            document[name] = ReadValue(elementType, payload, ref offset);
        }

        if (offset >= payload.Length || payload[offset] != 0)
        {
            throw new InvalidDataException("BSON document terminator is missing.");
        }

        offset++;
        return document;
    }

    private static object?[] ReadArray(ReadOnlySpan<byte> payload, ref int offset)
    {
        Dictionary<string, object?> indexedValues = ReadDocument(payload, ref offset);
        object?[] values = new object?[indexedValues.Count];
        foreach (KeyValuePair<string, object?> pair in indexedValues)
        {
            if (!int.TryParse(pair.Key, NumberStyles.None, CultureInfo.InvariantCulture, out int index) ||
                index < 0 ||
                index >= values.Length)
            {
                throw new InvalidDataException("BSON array index is invalid.");
            }

            values[index] = pair.Value;
        }

        return values;
    }

    private static object? ReadValue(byte elementType, ReadOnlySpan<byte> payload, ref int offset)
    {
        return elementType switch
        {
            TypeDouble => ReadDouble(payload, ref offset),
            TypeString => ReadString(payload, ref offset),
            TypeDocument => ReadDocument(payload, ref offset),
            TypeArray => ReadArray(payload, ref offset),
            TypeBoolean => ReadBoolean(payload, ref offset),
            TypeNull => null,
            TypeInt32 => ReadInt32(payload, ref offset),
            TypeInt64 => ReadInt64(payload, ref offset),
            _ => throw new InvalidDataException("Unsupported BSON element type: 0x" + elementType.ToString("X2"))
        };
    }

    private static string ReadCString(ReadOnlySpan<byte> payload, ref int offset)
    {
        var start = offset;
        while (offset < payload.Length && payload[offset] != 0)
        {
            offset++;
        }

        if (offset >= payload.Length)
        {
            throw new InvalidDataException("BSON cstring terminator is missing.");
        }

        var value = Encoding.UTF8.GetString(payload.Slice(start, offset - start));
        offset++;
        return value;
    }

    private static string ReadString(ReadOnlySpan<byte> payload, ref int offset)
    {
        var byteLength = ReadInt32(payload, ref offset);
        if (byteLength <= 0 || offset + byteLength > payload.Length)
        {
            throw new InvalidDataException("BSON string length is invalid.");
        }

        var contentLength = byteLength - 1;
        var value = Encoding.UTF8.GetString(payload.Slice(offset, contentLength));
        if (payload[offset + contentLength] != 0)
        {
            throw new InvalidDataException("BSON string terminator is missing.");
        }

        offset += byteLength;
        return value;
    }

    private static bool ReadBoolean(ReadOnlySpan<byte> payload, ref int offset)
    {
        if (offset >= payload.Length)
        {
            throw new InvalidDataException("BSON boolean payload is truncated.");
        }

        return payload[offset++] != 0;
    }

    private static int ReadInt32(ReadOnlySpan<byte> payload, ref int offset)
    {
        if (offset + 4 > payload.Length)
        {
            throw new InvalidDataException("BSON int32 payload is truncated.");
        }

        var value = BinaryPrimitives.ReadInt32LittleEndian(payload.Slice(offset, 4));
        offset += 4;
        return value;
    }

    private static double ReadDouble(ReadOnlySpan<byte> payload, ref int offset)
    {
        if (offset + 8 > payload.Length)
        {
            throw new InvalidDataException("BSON double payload is truncated.");
        }

        var bits = BinaryPrimitives.ReadInt64LittleEndian(payload.Slice(offset, 8));
        offset += 8;
        return BitConverter.Int64BitsToDouble(bits);
    }

    private static long ReadInt64(ReadOnlySpan<byte> payload, ref int offset)
    {
        if (offset + 8 > payload.Length)
        {
            throw new InvalidDataException("BSON int64 payload is truncated.");
        }

        var value = BinaryPrimitives.ReadInt64LittleEndian(payload.Slice(offset, 8));
        offset += 8;
        return value;
    }

    private static void WriteElement(Stream stream, string name, object? value)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);

        switch (value)
        {
            case null:
                stream.WriteByte(TypeNull);
                WriteCString(stream, name);
                return;

            case double doubleValue:
                stream.WriteByte(TypeDouble);
                WriteCString(stream, name);
                WriteDouble(stream, doubleValue);
                return;

            case string stringValue:
                stream.WriteByte(TypeString);
                WriteCString(stream, name);
                WriteString(stream, stringValue);
                return;

            case bool booleanValue:
                stream.WriteByte(TypeBoolean);
                WriteCString(stream, name);
                stream.WriteByte(booleanValue ? (byte)1 : (byte)0);
                return;

            case int intValue:
                stream.WriteByte(TypeInt32);
                WriteCString(stream, name);
                WriteInt32(stream, intValue);
                return;

            case long longValue:
                stream.WriteByte(TypeInt64);
                WriteCString(stream, name);
                WriteInt64(stream, longValue);
                return;

            case IReadOnlyDictionary<string, object?> nestedDocument:
                stream.WriteByte(TypeDocument);
                WriteCString(stream, name);
                WriteDocument(stream, nestedDocument);
                return;

            case IReadOnlyList<object?> arrayValue:
                stream.WriteByte(TypeArray);
                WriteCString(stream, name);
                WriteArray(stream, arrayValue);
                return;

            default:
                throw new InvalidOperationException("Unsupported BSON value type: " + value.GetType().FullName);
        }
    }

    private static void WriteDocument(Stream stream, IReadOnlyDictionary<string, object?> document)
    {
        var bytes = SerializeDocument(document);
        stream.Write(bytes, 0, bytes.Length);
    }

    private static void WriteArray(Stream stream, IReadOnlyList<object?> values)
    {
        var indexedValues = new Dictionary<string, object?>(values.Count, StringComparer.Ordinal);
        for (int index = 0; index < values.Count; index++)
        {
            indexedValues[index.ToString(CultureInfo.InvariantCulture)] = values[index];
        }

        WriteDocument(stream, indexedValues);
    }

    private static void WriteCString(Stream stream, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        stream.Write(bytes, 0, bytes.Length);
        stream.WriteByte(0);
    }

    private static void WriteString(Stream stream, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        WriteInt32(stream, bytes.Length + 1);
        stream.Write(bytes, 0, bytes.Length);
        stream.WriteByte(0);
    }

    private static void WriteInt32(Stream stream, int value)
    {
        Span<byte> buffer = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(buffer, value);
        stream.Write(buffer);
    }

    private static void WriteInt64(Stream stream, long value)
    {
        Span<byte> buffer = stackalloc byte[8];
        BinaryPrimitives.WriteInt64LittleEndian(buffer, value);
        stream.Write(buffer);
    }

    private static void WriteDouble(Stream stream, double value)
    {
        Span<byte> buffer = stackalloc byte[8];
        BinaryPrimitives.WriteInt64LittleEndian(buffer, BitConverter.DoubleToInt64Bits(value));
        stream.Write(buffer);
    }
}
