namespace DolphinTool.Core.Services.Wia.Lzma;

struct BitDecoder
{
    public const int kNumBitModelTotalBits = 11;

    public const uint kBitModelTotal = (1 << kNumBitModelTotalBits);

    const int kNumMoveBits = 5;
    uint Prob;

    public void UpdateModel(int numMoveBits, uint symbol)
    {
        if (symbol == 0)
            Prob += (kBitModelTotal - Prob) >> numMoveBits;
        else
            Prob -= (Prob) >> numMoveBits;
    }

    public void Init() { Prob = kBitModelTotal >> 1; }

    public uint Decode(RangeDecoder rangeDecoder)
    {
        uint newBound = (uint)(rangeDecoder.Range >> kNumBitModelTotalBits) * (uint)Prob;

        if (rangeDecoder.Code < newBound)
        {
            rangeDecoder.Range = newBound;
            Prob += (kBitModelTotal - Prob) >> kNumMoveBits;

            rangeDecoder.Normalize();

            return 0;
        }
        else
        {
            rangeDecoder.Range -= newBound;
            rangeDecoder.Code -= newBound;
            Prob -= (Prob) >> kNumMoveBits;

            rangeDecoder.Normalize();

            return 1;
        }
    }
}