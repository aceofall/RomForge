namespace DolphinTool.Core.Services.Wia.Lzma;

struct BitEncoder
{
	public const int kNumBitModelTotalBits = 11;

	public const uint kBitModelTotal = (1 << kNumBitModelTotalBits);

	const int kNumMoveBits = 5;
	const int kNumMoveReducingBits = 2;

	public const int kNumBitPriceShiftBits = 6;

	uint Prob;

	public void Init() { Prob = kBitModelTotal >> 1; }

	public void UpdateModel(uint symbol)
	{
		if (symbol == 0)
			Prob += (kBitModelTotal - Prob) >> kNumMoveBits;
		else
			Prob -= (Prob) >> kNumMoveBits;
	}

	public void Encode(RangeEncoder encoder, uint symbol)
	{
		uint newBound = (encoder.Range >> kNumBitModelTotalBits) * Prob;

		if (symbol == 0)
		{
			encoder.Range = newBound;
			Prob += (kBitModelTotal - Prob) >> kNumMoveBits;
		}
		else
		{
			encoder.Low += newBound;
			encoder.Range -= newBound;
			Prob -= (Prob) >> kNumMoveBits;
		}

		if (encoder.Range < RangeEncoder.kTopValue)
		{
			encoder.Range <<= 8;

			encoder.ShiftLow();
		}
	}

	private static readonly UInt32[] ProbPrices = new UInt32[kBitModelTotal >> kNumMoveReducingBits];

	static BitEncoder()
	{
		const int kNumBits = (kNumBitModelTotalBits - kNumMoveReducingBits);

		for (int i = kNumBits - 1; i >= 0; i--)
		{
			UInt32 start = (UInt32)1 << (kNumBits - i - 1);
			UInt32 end = (UInt32)1 << (kNumBits - i);

			for (UInt32 j = start; j < end; j++)
				ProbPrices[j] = ((UInt32)i << kNumBitPriceShiftBits) + (((end - j) << kNumBitPriceShiftBits) >> (kNumBits - i - 1));
		}
	}

	public readonly uint GetPrice(uint symbol) => ProbPrices[(((Prob - symbol) ^ ((-(int)symbol))) & (kBitModelTotal - 1)) >> kNumMoveReducingBits];
	
	public readonly uint GetPrice0() => ProbPrices[Prob >> kNumMoveReducingBits];
	
	public readonly uint GetPrice1() => ProbPrices[(kBitModelTotal - Prob) >> kNumMoveReducingBits];
}