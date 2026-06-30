namespace _3DS.Core.Save.Models;

public class DifiPartitionInfo
{
    public DifiHeader DifiHeader;
    public IvfcDescriptor IvfcDescriptor;
    public DpfsDescriptor DpfsDescriptor;
    public int DescriptorLen;
    public int PartitionLen;
}