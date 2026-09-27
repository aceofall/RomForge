using DolphinTool.Core.Models;

namespace DolphinTool.Core.Rvz;

internal interface IWiiPartitionSource
{
    bool TryReadDecryptedHashGroup(long readOffset, int blocksInThisGroup, byte[] decrypted, List<HashException> exceptions);
}