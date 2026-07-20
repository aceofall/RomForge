using System.Security.Cryptography;
using NUSPacker;
using NUSPacker.Nuspackage;
using NUSPacker.Nuspackage.Contents;
using NUSPacker.Nuspackage.Crypto;
using NUSPacker.Nuspackage.Fst;
using NUSPacker.Nuspackage.Packaging;
using NUSPacker.Utils;
using WiiU.Core.Models;

namespace WiiU.Core.Services;

/// <summary>
/// Packs a title into WUP format (.app/.h3/title.tmd/title.cert/title.tik).
///
/// This used to be a hand-rolled FST/TMD/hash-tree/ticket writer. It's now a thin adapter over
/// NUSPackerSharp (a verified line-for-line C# port of the original NUSPacker.jar - its output was
/// diffed byte-for-byte against the real jar's output and matches exactly, aside from the two
/// genuinely-random padding regions in title.tik). Same public signature as before, so callers
/// don't need to change.
///
/// IMPORTANT: the FST tree (directory/file names and structure) is built entirely in memory,
/// keyed by exact case-sensitive path strings. The local filesystem is only ever used to hold raw
/// file bytes, under generated collision-proof names that have nothing to do with the real path -
/// so this never depends on the host OS's filesystem being case-sensitive (Wii U paths are
/// case-sensitive; Windows/NTFS is not, and silently collapses paths that differ only by case).
/// </summary>
public static class WupPacker
{
    public static void Pack(
        string outputFolder,
        ulong titleId,
        ushort titleVersion,
        IReadOnlyList<WupContentGroup> groups,
        Action<long, long, string>? onProgress = null,
        CancellationToken ct = default,
        ulong osVersion = 0x000500101000400AUL,
        int appType = unchecked((int)0x80000000))
    {
        Directory.CreateDirectory(outputFolder);

        string scratchRoot = Path.Combine(Path.GetTempPath(), "romforge_pack_" + Guid.NewGuid().ToString("N"));
        string stagingDir = Path.Combine(scratchRoot, "files");
        string prevTmpDir = Settings.tmpDir;
        Settings.tmpDir = Path.Combine(scratchRoot, "tmp");

        try
        {
            Directory.CreateDirectory(stagingDir);
            Directory.CreateDirectory(Settings.tmpDir);

            long totalBytes = 0;
            foreach (var g in groups)
                foreach (var f in g.Files)
                    totalBytes += f.Data.LongLength;

            var contents = new Contents();
            var fst = new FST(contents);
            FSTEntry root = fst.GetFSTEntries().GetRootEntry()!;
            root.SetContent(contents.GetFSTContent());

            // path ("" = root, "code", "content/sub", ...) -> that directory's FSTEntry.
            // Built purely in memory and keyed with ordinal (case-sensitive) string comparison,
            // so "Content" and "content" are correctly treated as two different entries if the
            // source data actually has both - never silently merged the way a case-insensitive
            // filesystem would merge them.
            var dirsByPath = new Dictionary<string, FSTEntry>(StringComparer.Ordinal) { [""] = root };
            var seenFilePaths = new HashSet<string>(StringComparer.Ordinal);

            FSTEntry GetOrCreateDir(string dirPath)
            {
                if (dirsByPath.TryGetValue(dirPath, out var existing))
                    return existing;

                int slash = dirPath.LastIndexOf('/');
                string parentPath = slash < 0 ? "" : dirPath[..slash];
                string name = slash < 0 ? dirPath : dirPath[(slash + 1)..];

                FSTEntry parent = GetOrCreateDir(parentPath);
                var dir = new FSTEntry(false);
                dir.SetDir(true);
                dir.SetFileName(name);
                parent.AddChildren(dir);
                dirsByPath[dirPath] = dir;
                return dir;
            }

            ulong parentTitleIdForContent = titleId & ~0x0000000F00000000UL;
            uint contentGroupId = (uint)((titleId >> 8) & 0xFFFF);

            long processedBytes = 0;
            int fileIndex = 0;

            foreach (var group in groups)
            {
                ct.ThrowIfCancellationRequested();

                if (group.Files.Count == 0)
                    continue;

                ulong parentTitleId = group.FstFlags == 0x0400 ? parentTitleIdForContent : 0UL;
                uint groupId = group.FstFlags switch { 0x0400 => contentGroupId, 0x0040 => 0x0400u, _ => 0u };

                var details = new ContentDetails(group.Hashed, (short)groupId, (long)parentTitleId, (short)group.FstFlags);
                Content content = contents.GetNewContent(details);

                long groupBytes = 0;

                foreach (var file in group.Files)
                {
                    ct.ThrowIfCancellationRequested();

                    string relPath = file.RelativePath.Trim('/');

                    if (!seenFilePaths.Add(relPath))
                        throw new InvalidOperationException($"같은 경로가 두 번 등장했습니다 (대소문자까지 포함해서 동일함): {relPath}");

                    int lastSlash = relPath.LastIndexOf('/');
                    string dirPath = lastSlash < 0 ? "" : relPath[..lastSlash];
                    string leafName = lastSlash < 0 ? relPath : relPath[(lastSlash + 1)..];

                    FSTEntry parentDir = GetOrCreateDir(dirPath);

                    string stagedPath = Path.Combine(stagingDir, (fileIndex++).ToString());
                    File.WriteAllBytes(stagedPath, file.Data);

                    var entry = new FSTEntry(stagedPath);
                    entry.SetFileName(leafName); // override the staged (index-based) disk name with the real, exact-case name
                    entry.SetContent(content);
                    parentDir.AddChildren(entry);

                    groupBytes += file.Data.LongLength;
                }

                processedBytes += groupBytes;
                onProgress?.Invoke(processedBytes, totalBytes, $"content #{content.GetID():x8}");
            }

            // Every directory needs a Content reference too (NUSPackerSharp aborts otherwise) -
            // propagate bottom-up from whichever content its files ended up in.
            PropagateDirectoryContent(root);

            var appInfo = new AppXMLInfo();
            appInfo.SetTitleID((long)titleId);
            appInfo.SetGroupID((short)contentGroupId);
            appInfo.SetOSVersion((long)osVersion);
            appInfo.SetTitleVersion((short)titleVersion);
            appInfo.SetAppType(appType);

            byte[] titleKeyPlain = RandomNumberGenerator.GetBytes(16);
            var encryptionKey = new Key(titleKeyPlain);
            var encryptWithKey = new Key(Constants.WiiUCommonKey);

            ct.ThrowIfCancellationRequested();

            NUSPackage nusPackage = NUSPackageFactory.CreatePackageFromBuiltTree(contents, fst, appInfo, encryptionKey, encryptWithKey);

            onProgress?.Invoke(processedBytes, totalBytes, "패킹 중");

            nusPackage.PackContents(outputFolder);

            // NUSPackerSharp writes content filenames in uppercase hex (ported from the original
            // Java CLI's %08X convention). The rest of this codebase (WupTitleSource etc.) expects
            // lowercase hex ({cid:x8}.app / .h3). Normalize here so nothing downstream has to care.
            foreach (var file in Directory.GetFiles(outputFolder, "*.app").Concat(Directory.GetFiles(outputFolder, "*.h3")))
            {
                string fileName = Path.GetFileName(file);
                string lower = fileName.ToLowerInvariant();
                if (fileName != lower)
                {
                    string dest = Path.Combine(outputFolder, lower);
                    if (File.Exists(dest)) File.Delete(dest);
                    File.Move(file, dest);
                }
            }

            onProgress?.Invoke(totalBytes, totalBytes, "완료");
        }
        finally
        {
            Settings.tmpDir = prevTmpDir;
            try { if (Directory.Exists(scratchRoot)) Directory.Delete(scratchRoot, true); } catch { /* best effort */ }
        }
    }

    /// <summary>Sets Content bottom-up on every directory entry to one of its descendants' Content.</summary>
    private static Content PropagateDirectoryContent(FSTEntry entry)
    {
        if (!entry.IsDir())
        {
            return entry.GetContent() ?? throw new InvalidOperationException($"'{entry.GetFilename()}' 파일에 콘텐츠가 할당되지 않았습니다.");
        }

        Content? chosen = null;

        foreach (var child in entry.GetChildren())
        {
            Content childContent = PropagateDirectoryContent(child);
            chosen ??= childContent;
        }

        if (chosen is null)
            throw new InvalidOperationException($"'{entry.GetFilename()}' 폴더가 비어 있어 콘텐츠를 할당할 수 없습니다. 더미 파일을 넣어주세요.");

        entry.SetContent(chosen);
        return chosen;
    }
}