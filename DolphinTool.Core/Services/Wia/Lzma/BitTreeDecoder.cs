namespace DolphinTool.Core.Services.Wia.Lzma;

struct BitTreeDecoder(int numBitLevels)
{
    readonly BitDecoder[] Models = new BitDecoder[1 << numBitLevels];

    public readonly void Init()
	{
		for (uint i = 1; i < (1 << numBitLevels); i++)
			Models[i].Init();
	}

	public readonly uint Decode(RangeDecoder rangeDecoder)
	{
		uint m = 1;

		for (int bitIndex = numBitLevels; bitIndex > 0; bitIndex--)
			m = (m << 1) + Models[m].Decode(rangeDecoder);

		return m - ((uint)1 << numBitLevels);
	}

	public readonly uint ReverseDecode(RangeDecoder rangeDecoder)
	{
		uint m = 1;
		uint symbol = 0;

		for (int bitIndex = 0; bitIndex < numBitLevels; bitIndex++)
		{
			uint bit = Models[m].Decode(rangeDecoder);

			m <<= 1;
			m += bit;
			symbol |= (bit << bitIndex);
		}

		return symbol;
	}

	public static uint ReverseDecode(BitDecoder[] Models, UInt32 startIndex, RangeDecoder rangeDecoder, int NumBitLevels)
	{
		uint m = 1;
		uint symbol = 0;

		for (int bitIndex = 0; bitIndex < NumBitLevels; bitIndex++)
		{
			uint bit = Models[startIndex + m].Decode(rangeDecoder);

			m <<= 1;
			m += bit;
			symbol |= (bit << bitIndex);
		}

		return symbol;
	}
}