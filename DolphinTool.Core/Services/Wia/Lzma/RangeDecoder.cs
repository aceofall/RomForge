namespace DolphinTool.Core.Services.Wia.Lzma;

class RangeDecoder
{
    public const uint kTopValue = (1 << 24);

    public uint Range;

    public uint Code;

    byte[] _buf = null!;
    int _pos;

    public void Init(byte[] buf, int offset)
    {
        _buf = buf;
        _pos = offset;

        Code = 0;
        Range = 0xFFFFFFFF;

        for (int i = 0; i < 5; i++)
            Code = (Code << 8) | _buf[_pos++];
    }

    public void ReleaseStream()
    {
        _buf = null!;
    }

    public void Normalize()
    {
        while (Range < kTopValue)
        {
            Code = (Code << 8) | _buf[_pos++];
            Range <<= 8;
        }
    }

    public void Normalize2()
    {
        if (Range < kTopValue)
        {
            Code = (Code << 8) | _buf[_pos++];
            Range <<= 8;
        }
    }

    public uint GetThreshold(uint total) => Code / (Range /= total);

    public void Decode(uint start, uint size)
    {
        Code -= start * Range;
        Range *= size;

        Normalize();
    }

    public uint DecodeDirectBits(int numTotalBits)
    {
        uint range = Range;
        uint code = Code;
        uint result = 0;

        for (int i = numTotalBits; i > 0; i--)
        {
            range >>= 1;

            uint t = (code - range) >> 31;

            code -= range & (t - 1);
            result = (result << 1) | (1 - t);

            if (range < kTopValue)
            {
                code = (code << 8) | _buf[_pos++];
                range <<= 8;
            }
        }

        Range = range;
        Code = code;

        return result;
    }

    public uint DecodeBit(uint size0, int numTotalBits)
    {
        uint newBound = (Range >> numTotalBits) * size0;
        uint symbol;

        if (Code < newBound)
        {
            symbol = 0;
            Range = newBound;
        }
        else
        {
            symbol = 1;
            Code -= newBound;
            Range -= newBound;
        }

        Normalize();

        return symbol;
    }
}