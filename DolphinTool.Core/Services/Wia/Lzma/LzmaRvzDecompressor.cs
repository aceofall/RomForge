using DolphinTool.Core.Rvz;

namespace DolphinTool.Core.Services.Wia.Lzma;

internal sealed class LzmaRvzDecompressor : RvzDecompressor
{
    private readonly LzmaDecoder _decoder = new();
    private byte[] _inputBuffer = [];
    private byte[] _outputBuffer = [];

    public LzmaRvzDecompressor(ReadOnlySpan<byte> compressorData)
    {
        if (compressorData.Length != 5)
            throw new InvalidDataException("LZMA1 속성 데이터 크기가 올바르지 않습니다.");

        _decoder.SetDecoderProperties(compressorData.ToArray());
    }

    public override int Decompress(ReadOnlySpan<byte> source, Span<byte> destination)
    {
        if (_inputBuffer.Length < source.Length)
            _inputBuffer = new byte[source.Length];

        source.CopyTo(_inputBuffer);

        if (_outputBuffer.Length < destination.Length)
            _outputBuffer = new byte[destination.Length];

        using var output = new MemoryStream(_outputBuffer, 0, destination.Length, true, true);

        _decoder.Code(_inputBuffer, 0, output, destination.Length);

        int total = (int)output.Position;

        _outputBuffer.AsSpan(0, total).CopyTo(destination);

        return total;
    }
}