using System.Buffers;

namespace PickPack.Disk.ETC
{
    internal static class Optimal
    {
        public static readonly byte[] ZeroBuffer;
        public static readonly ArrayPool<byte> ArrayPool = ArrayPool<byte>.Shared;
        public static readonly int BufferSize;
        public static readonly int ChannelCapacity;

        static Optimal()
        {
            BufferSize = GetOptimalBufferSize();
            ChannelCapacity = GetOptimalChannelCapacity();
            ZeroBuffer = new byte[BufferSize];
        }

        private static int GetOptimalBufferSize() => 2 * 1024 * 1024;

        private static int GetOptimalChannelCapacity() => 2;
    }
}