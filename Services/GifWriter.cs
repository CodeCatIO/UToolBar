using System.IO;
using System.Runtime.InteropServices;

namespace EdgeBar.Services;

/// <summary>流式 GIF89a 编码器：固定 256 色调色板 + 有序抖动，逐帧写入不缓存整段录屏</summary>
public sealed class GifWriter : IDisposable
{
    private static readonly int[] Bayer4 =
    {
        0, 8, 2, 10,
        12, 4, 14, 6,
        3, 11, 1, 9,
        15, 7, 13, 5
    };

    private readonly Stream _stream;
    private readonly int _width;
    private readonly int _height;
    private readonly byte[] _palette = new byte[768];
    private readonly byte[] _indices;
    private readonly byte[] _row;
    private readonly Dictionary<int, int> _dictionary = new(4096);
    private readonly byte[] _block = new byte[255];
    private int _blockCount;
    private int _bitBuffer;
    private int _bitCount;
    private bool _finished;
    private bool _growPending;

    public GifWriter(Stream stream, int width, int height)
    {
        _stream = stream;
        _width = width;
        _height = height;
        _indices = new byte[width * height];
        _row = new byte[width * 4];
        BuildPalette();
        WriteHeader();
    }

    private void BuildPalette()
    {
        int index = 0;
        for (int r = 0; r < 6; r++)
        {
            for (int g = 0; g < 6; g++)
            {
                for (int b = 0; b < 6; b++)
                {
                    _palette[index * 3] = (byte)(r * 51);
                    _palette[index * 3 + 1] = (byte)(g * 51);
                    _palette[index * 3 + 2] = (byte)(b * 51);
                    index++;
                }
            }
        }

        for (int i = 0; i < 40; i++)
        {
            byte level = (byte)(i * 255 / 39);
            _palette[index * 3] = level;
            _palette[index * 3 + 1] = level;
            _palette[index * 3 + 2] = level;
            index++;
        }
    }

    private void WriteHeader()
    {
        Write("GIF89a"u8);
        WriteShort(_width);
        WriteShort(_height);
        _stream.WriteByte(0xF7); // 全局色表, 256 色, 8 位色深
        _stream.WriteByte(0);
        _stream.WriteByte(0);
        _stream.Write(_palette, 0, _palette.Length);

        // NETSCAPE2.0 无限循环
        _stream.WriteByte(0x21);
        _stream.WriteByte(0xFF);
        _stream.WriteByte(11);
        Write("NETSCAPE2.0"u8);
        _stream.WriteByte(3);
        _stream.WriteByte(1);
        WriteShort(0);
        _stream.WriteByte(0);
    }

    /// <summary>写入一帧，bgra 为 32 位 BGRA 像素（自上而下）</summary>
    public void WriteFrame(IntPtr bgra, int stride, int delayCentiseconds)
    {
        int width = _width;
        int height = _height;
        for (int y = 0; y < height; y++)
        {
            Marshal.Copy(IntPtr.Add(bgra, y * stride), _row, 0, width * 4);
            int rowOffset = y * width;
            int ditherRow = (y & 3) * 4;

            for (int x = 0; x < width; x++)
            {
                int offset = x * 4;
                byte b = _row[offset];
                byte g = _row[offset + 1];
                byte r = _row[offset + 2];
                int dither = (Bayer4[ditherRow + (x & 3)] - 8) * 4;
                _indices[rowOffset + x] = MapColor(r + dither, g + dither, b + dither);
            }
        }

        // 图形控制扩展
        _stream.WriteByte(0x21);
        _stream.WriteByte(0xF9);
        _stream.WriteByte(0x04);
        _stream.WriteByte(0x04); // disposal 1, 不使用透明色
        WriteShort(Math.Max(2, delayCentiseconds));
        _stream.WriteByte(0);
        _stream.WriteByte(0);

        // 图像描述符
        _stream.WriteByte(0x2C);
        WriteShort(0);
        WriteShort(0);
        WriteShort(width);
        WriteShort(height);
        _stream.WriteByte(0); // 无本地色表

        _stream.WriteByte(8); // LZW 最小码长
        WriteLzw(_indices);
    }

    private static byte MapColor(int r, int g, int b)
    {
        if (r < 0) r = 0;
        else if (r > 255) r = 255;
        if (g < 0) g = 0;
        else if (g > 255) g = 255;
        if (b < 0) b = 0;
        else if (b > 255) b = 255;

        int max = Math.Max(r, Math.Max(g, b));
        int min = Math.Min(r, Math.Min(g, b));
        if (max - min <= 16)
        {
            return (byte)(216 + r * 39 / 255);
        }

        int ri = r * 6 / 256;
        int gi = g * 6 / 256;
        int bi = b * 6 / 256;
        return (byte)(ri * 36 + gi * 6 + bi);
    }

    private void WriteLzw(byte[] indices)
    {
        int clearCode = 256;
        int endCode = 257;
        int nextCode = 258;

        _codeSize = 9;
        _growPending = false;
        _dictionary.Clear();
        _bitBuffer = 0;
        _bitCount = 0;
        _blockCount = 0;

        Emit(clearCode);

        int prefix = indices[0];
        for (int i = 1; i < indices.Length; i++)
        {
            int value = indices[i];
            int key = (prefix << 8) | value;

            if (_dictionary.TryGetValue(key, out int code))
            {
                prefix = code;
                continue;
            }

            Emit(prefix);
            if (nextCode < 4096)
            {
                _dictionary[key] = nextCode++;
                // 码长增长要延后一个码，解码器的字典总比编码器晚一项
                if (nextCode == (1 << _codeSize) && _codeSize < 12) _growPending = true;
            }
            else
            {
                Emit(clearCode);
                _dictionary.Clear();
                nextCode = 258;
                _codeSize = 9;
                _growPending = false;
            }
            prefix = value;
        }

        Emit(prefix);
        Emit(endCode);

        if (_bitCount > 0)
        {
            AddByte((byte)(_bitBuffer & 0xFF));
            _bitBuffer = 0;
            _bitCount = 0;
        }

        FlushBlock();
        _stream.WriteByte(0); // 块结束
    }

    private void Emit(int code)
    {
        _bitBuffer |= code << _bitCount;
        _bitCount += _codeSize;
        while (_bitCount >= 8)
        {
            AddByte((byte)(_bitBuffer & 0xFF));
            _bitBuffer >>= 8;
            _bitCount -= 8;
        }

        if (_growPending)
        {
            _growPending = false;
            if (_codeSize < 12) _codeSize++;
        }
    }

    private int _codeSize = 9;

    private void AddByte(byte value)
    {
        _block[_blockCount++] = value;
        if (_blockCount == 255) FlushBlock();
    }

    private void FlushBlock()
    {
        if (_blockCount == 0) return;
        _stream.WriteByte((byte)_blockCount);
        _stream.Write(_block, 0, _blockCount);
        _blockCount = 0;
    }

    private void WriteShort(int value)
    {
        _stream.WriteByte((byte)(value & 0xFF));
        _stream.WriteByte((byte)((value >> 8) & 0xFF));
    }

    private void Write(ReadOnlySpan<byte> bytes) => _stream.Write(bytes);

    public void Finish()
    {
        if (_finished) return;
        _finished = true;
        _stream.WriteByte(0x3B);
        _stream.Flush();
    }

    public void Dispose()
    {
        Finish();
    }
}
