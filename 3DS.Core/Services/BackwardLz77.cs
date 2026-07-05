namespace _3DS.Core.Services;

public static class BackwardLz77
{
    private const int FooterSize = 8;
    private const int MinMatchLength = 3;
    private const int MaxMatchLength = 18;   // (0xF) + 3
    private const int MinOffset = 3;
    private const int MaxOffset = 0x1002;    // (0xFFF) + 3
    private const int HashChainMaxSteps = 128;

    public static byte[] Decompress(byte[] compressed)
    {
        int compressedSize = compressed.Length;
        if (compressedSize < FooterSize)
            throw new InvalidDataException("압축된 code 데이터가 너무 작아 BackwardLZ77 푸터를 읽을 수 없습니다.");
        uint bufferTopAndBottom = BitConverter.ToUInt32(compressed, compressedSize - 8);
        uint originalBottom = BitConverter.ToUInt32(compressed, compressedSize - 4);
        uint top = bufferTopAndBottom & 0xFFFFFF;
        uint bottom = (bufferTopAndBottom >> 24) & 0xFF;
        if (bottom < FooterSize || bottom > FooterSize + 3 || top < bottom || top > compressedSize)
            throw new InvalidDataException("BackwardLZ77 푸터 값이 유효하지 않습니다. 압축 플래그 오판이거나 데이터가 손상됐을 수 있습니다.");
        uint uncompressedSize = (uint)compressedSize + originalBottom;
        byte[] output = new byte[uncompressedSize];
        Array.Copy(compressed, output, compressedSize);
        int destPos = (int)uncompressedSize;
        int srcPos = compressedSize - (int)bottom;
        int endPos = compressedSize - (int)top;
        while (srcPos - endPos > 0)
        {
            byte flag = output[--srcPos];
            for (int i = 0; i < 8; i++)
            {
                if ((flag << i & 0x80) == 0)
                {
                    if (destPos - endPos < 1 || srcPos - endPos < 1)
                        throw new InvalidDataException("BackwardLZ77 압축 해제 중 데이터 범위를 벗어났습니다.");
                    output[--destPos] = output[--srcPos];
                }
                else
                {
                    if (srcPos - endPos < 2)
                        throw new InvalidDataException("BackwardLZ77 압축 해제 중 데이터 범위를 벗어났습니다.");
                    int a = output[--srcPos];
                    int b = output[--srcPos];
                    int offset = (((a & 0x0F) << 8) | b) + 3;
                    int size = ((a >> 4) & 0x0F) + 3;
                    if (size > destPos - endPos)
                        throw new InvalidDataException("BackwardLZ77 압축 해제 중 데이터 범위를 벗어났습니다.");
                    int dataPos = destPos + offset;
                    if (dataPos > uncompressedSize)
                        throw new InvalidDataException("BackwardLZ77 압축 해제 중 데이터 범위를 벗어났습니다.");
                    for (int j = 0; j < size; j++)
                        output[--destPos] = output[--dataPos];
                }
                if (srcPos - endPos <= 0)
                    break;
            }
        }
        return output;
    }

    /// <summary>
    /// 원본 데이터를 BackwardLZ77 형식으로 압축합니다.
    /// 압축해도 이득이 없는 경우(랜덤 데이터, 너무 작은 데이터 등)에는
    /// 원본과 동일한 바이트 배열을 그대로 반환합니다 - 호출하는 쪽에서
    /// 반환값의 길이가 원본과 같은지 확인해서 "압축 안 함"으로 처리하면 됩니다.
    /// </summary>
    public static byte[] Compress(byte[] raw)
    {
        int n = raw.Length;
        if (n == 0)
            return raw;

        // Decompress는 압축 스트림을 뒤에서부터 읽어(srcPos) 결과 버퍼를 끝에서부터
        // 채운다(destPos). 이는 "원본을 뒤집은 배열을 정방향으로 일반적인 LZSS로
        // 압축한 뒤, 그 토큰 스트림을 다시 뒤집어 저장한 것"과 수학적으로 동일하다.
        byte[] rev = new byte[n];
        for (int i = 0; i < n; i++)
            rev[i] = raw[n - 1 - i];

        var lastPos = new Dictionary<int, int>();
        int[] prevPos = new int[n];

        // ---- 1단계: 전체 토큰을 먼저 찾아서 목록으로만 기록한다 ----
        // srcPos(읽기 포인터)와 destPos(쓰기 포인터)는 같은 버퍼에서 동시에
        // 줄어드는데, 매치 토큰은 그 간격(destPos-srcPos)을 줄이고 flag byte는
        // 아주 조금 늘린다. 리터럴이 많이 몰린 구간을 만나면 이 간격이 음수가
        // 될 수 있고, 그러면 아직 읽지 않은 압축 스트림을 압축 해제 도중 결과가
        // 먼저 덮어써서 데이터가 깨진다. 그래서 D(j) = j - k(j) (j=처리한 원본
        // 바이트 수, k=지금까지 생성된 스트림 바이트 수)를 추적해서, 이 값이
        // "지금까지 나온 최댓값"보다 떨어지는 지점 이후는 압축하지 않고 그냥
        // raw prefix로 남겨 안전을 보장한다.
        var tokenIsMatch = new List<bool>();
        var tokenLen = new List<int>();      // 매치 길이 또는 1(리터럴)
        var tokenOff = new List<int>();      // 매치 offset 또는 리터럴 바이트값
        var dValues = new List<int> { 0 };
        var boundaryJ = new List<int> { 0 };

        int j = 0;
        int k = 0;
        int flagBitCountPass1 = 0;

        while (j < n)
        {
            if (flagBitCountPass1 == 0)
                k++; // 새 flag byte

            int bestLen = 0;
            int bestOff = 0;
            int maxOff = Math.Min(j, MaxOffset);
            int maxLen = Math.Min(MaxMatchLength, n - j);

            if (maxOff >= MinOffset && maxLen >= MinMatchLength)
            {
                int hash = Hash3(rev, j);
                int cand;
                if (lastPos.TryGetValue(hash, out cand))
                {
                    int steps = 0;
                    while (cand >= 0 && steps < HashChainMaxSteps)
                    {
                        int off = j - cand;
                        if (off < MinOffset)
                        {
                            cand = prevPos[cand];
                            steps++;
                            continue;
                        }
                        if (off > MaxOffset)
                            break;

                        int len = MatchLength(rev, cand, j, maxLen);
                        if (len > bestLen)
                        {
                            bestLen = len;
                            bestOff = off;
                            if (len >= maxLen)
                                break;
                        }
                        cand = prevPos[cand];
                        steps++;
                    }
                }
            }

            if (bestLen >= MinMatchLength)
            {
                tokenIsMatch.Add(true);
                tokenLen.Add(bestLen);
                tokenOff.Add(bestOff);
                k += 2;

                int end = j + bestLen;
                int insertEnd = Math.Min(end, n - 2);
                for (int p = j; p < insertEnd; p++)
                {
                    int h = Hash3(rev, p);
                    prevPos[p] = lastPos.TryGetValue(h, out int pv) ? pv : -1;
                    lastPos[h] = p;
                }
                j = end;
            }
            else
            {
                tokenIsMatch.Add(false);
                tokenLen.Add(1);
                tokenOff.Add(rev[j]);
                k += 1;

                if (j < n - 2)
                {
                    int h = Hash3(rev, j);
                    prevPos[j] = lastPos.TryGetValue(h, out int pv2) ? pv2 : -1;
                    lastPos[h] = j;
                }
                j++;
            }

            flagBitCountPass1 = (flagBitCountPass1 + 1) % 8;
            dValues.Add(j - k);
            boundaryJ.Add(j);
        }

        // ---- 2단계: 안전하게 유지할 수 있는 마지막 지점을 찾는다 ----
        int maxD = int.MinValue;
        for (int i = 0; i < dValues.Count; i++)
            if (dValues[i] > maxD)
                maxD = dValues[i];

        int cutoffIndex = 0;
        for (int i = 0; i < dValues.Count; i++)
            if (dValues[i] == maxD)
                cutoffIndex = i; // 마지막으로 최댓값을 찍은 지점을 채택(압축 범위 최대화)

        int jFinal = boundaryJ[cutoffIndex];
        int keptTokenCount = cutoffIndex; // dValues/boundaryJ는 토큰 0개 처리 상태도 포함하므로 인덱스 = 토큰 수

        // ---- 3단계: 채택된 토큰들만으로 실제 스트림을 만든다 ----
        var stream = new List<byte>(keptTokenCount + keptTokenCount / 8 + 4);
        int flagIndex = -1;
        byte currentFlag = 0;
        int flagBitCount = 0;

        for (int t = 0; t < keptTokenCount; t++)
        {
            if (flagIndex == -1)
            {
                flagIndex = stream.Count;
                stream.Add(0);
                currentFlag = 0;
                flagBitCount = 0;
            }

            if (tokenIsMatch[t])
            {
                int lengthField = tokenLen[t] - 3;
                int offsetField = tokenOff[t] - 3;
                byte a = (byte)(((lengthField & 0xF) << 4) | ((offsetField >> 8) & 0xF));
                byte b = (byte)(offsetField & 0xFF);
                stream.Add(a);
                stream.Add(b);
                currentFlag |= (byte)(1 << (7 - flagBitCount));
            }
            else
            {
                stream.Add((byte)tokenOff[t]);
            }

            flagBitCount++;
            if (flagBitCount == 8)
            {
                stream[flagIndex] = currentFlag;
                flagIndex = -1;
            }
        }

        if (flagIndex != -1)
            stream[flagIndex] = currentFlag;

        byte[] s = stream.ToArray();
        Array.Reverse(s);

        int rawPrefixLen = n - jFinal;
        int top = s.Length + FooterSize;
        const int bottom = FooterSize;
        int compressedSize = rawPrefixLen + s.Length + FooterSize;

        if (compressedSize >= n)
            return raw; // 압축해도 이득이 없음 - 호출부에서 길이 비교로 판단

        uint originalBottom = (uint)(n - compressedSize);

        byte[] result = new byte[compressedSize];
        Array.Copy(raw, 0, result, 0, rawPrefixLen);
        Array.Copy(s, 0, result, rawPrefixLen, s.Length);
        uint bufferTopAndBottom = (uint)top | ((uint)bottom << 24);
        BitConverter.GetBytes(bufferTopAndBottom).CopyTo(result, rawPrefixLen + s.Length);
        BitConverter.GetBytes(originalBottom).CopyTo(result, rawPrefixLen + s.Length + 4);

        // 3DS 부팅에 직결되는 데이터이므로, 자체적으로 왕복 검증을 해서
        // 혹시 모를 인코딩 버그가 조용히 깨진 code.bin을 만들지 않도록 한다.
        byte[] verify = Decompress(result);
        if (!verify.AsSpan().SequenceEqual(raw))
            throw new InvalidOperationException("BackwardLZ77 압축 결과 검증에 실패했습니다. 압축 로직에 문제가 있습니다.");

        return result;
    }

    private static int Hash3(byte[] data, int pos)
        => (data[pos] << 16) | (data[pos + 1] << 8) | data[pos + 2];

    private static int MatchLength(byte[] data, int candidate, int current, int maxLen)
    {
        int len = 0;
        while (len < maxLen && data[candidate + len] == data[current + len])
            len++;
        return len;
    }
}