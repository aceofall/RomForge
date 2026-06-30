using _3DS.Core.Save.Models;

namespace _3DS.Core.Save.Interfaces;

public interface IFileSystem
{
    IFileSystemFile OpenFile(uint ino);
    IFileSystemDir OpenDir(uint ino);
    IFileSystemDir OpenRoot() => OpenDir(1);
    void Commit();
    FsStat Stat();
}