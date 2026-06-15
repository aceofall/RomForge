using System.Management;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Microsoft.Win32.SafeHandles;

namespace RomForge.Core.Services
{
    public class DriveInfos
    {
        #region Win32

        private const uint GENERIC_READ = 0x80000000;
        private const uint FILE_SHARE_READ = 0x00000001;
        private const uint FILE_SHARE_WRITE = 0x00000002;
        private const uint OPEN_EXISTING = 3;
        private const uint FILE_ATTRIBUTE_NORMAL = 0x80;

        private const uint IOCTL_DISK_GET_LENGTH_INFO = 0x0007405C;

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern SafeFileHandle CreateFile(string lpFileName, uint dwDesiredAccess, uint dwShareMode, IntPtr lpSecurityAttributes, uint dwCreationDisposition, uint dwFlagsAndAttributes, IntPtr hTemplateFile);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool DeviceIoControl(SafeFileHandle hDevice, uint dwIoControlCode, IntPtr lpInBuffer, uint nInBufferSize, out GET_LENGTH_INFORMATION lpOutBuffer, uint nOutBufferSize, out uint lpBytesReturned, IntPtr lpOverlapped);

        [StructLayout(LayoutKind.Sequential)]
        private struct GET_LENGTH_INFORMATION
        {
            public long Length;
        }

        #endregion

        #region Field

        internal static List<DriveInfos> Infos = [];

        #endregion

        #region Property

        public string DevicePath { get; set; }

        public string? DriveLetter { get; private set; }

        public string Model { get; set; }

        public string DeviceId { get; set; }

        public long SizeBytes { get; set; }

        public int DiskNumber { get; set; }

        #endregion

        public DriveInfos(string devicePath, string model, long sizeBytes)
        {
            DevicePath = devicePath;
            Model = model;
            SizeBytes = sizeBytes;

            DiskNumber = int.Parse(Regex.Match(devicePath, @"\d+$").Value);
            DriveLetter = GetDriveLetterFromDiskNumber(this.DiskNumber);
        }

        #region Override

        public override string ToString()
        {
            string letter = string.IsNullOrEmpty(this.DriveLetter) ? "?" : this.DriveLetter;

            return $"[{letter}] ({FormatSize((long)SizeBytes)}) {Model}";
        }

        #endregion

        #region Public

        public static string FormatSize(long bytes)
        {
            const long KB = 1024;
            const long MB = KB * 1024;
            const long GB = MB * 1024;
            const long TB = GB * 1024;

            if (bytes >= TB) return $"{(bytes / (double)TB):N2} TB";
            if (bytes >= GB) return $"{(bytes / (double)GB):N2} GB";
            if (bytes >= MB) return $"{(bytes / (double)MB):N2} MB";
            if (bytes >= KB) return $"{(bytes / (double)KB):N2} KB";

            return $"{bytes} Bytes";
        }

        public static string? GetDriveLetterFromDiskNumber(int diskNumber)
        {
            using var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_DiskDrive");

            foreach (ManagementObject disk in searcher.Get().Cast<ManagementObject>())
            {
                if (Convert.ToInt32(disk["Index"]) == diskNumber)
                {
                    foreach (var logical in from ManagementObject partition in disk.GetRelated("Win32_DiskPartition")
                                            from ManagementObject logical in partition.GetRelated("Win32_LogicalDisk")
                                            select logical)
                    {
                        return logical["DeviceID"]?.ToString();
                    }
                }
            }

            return null;
        }

        public static string? GetDriveLetterFromDiskNumber(int diskNumber, int index)
        {
            using var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_DiskDrive");

            foreach (ManagementObject drive in searcher.Get().Cast<ManagementObject>())
            {
                if (Convert.ToInt32(drive["Index"]) != diskNumber)
                    continue;

                foreach (ManagementObject partition in drive.GetRelated("Win32_DiskPartition").Cast<ManagementObject>())
                {
                    int i = 0;

                    foreach (ManagementObject logical in partition.GetRelated("Win32_LogicalDisk").Cast<ManagementObject>())
                    {
                        if (i == index)
                            return logical["DeviceID"]?.ToString();
                        i++;
                    }
                }
            }

            return null;
        }

        public static List<DriveInfos> GetDriveInfos()
        {
            var infos = new List<DriveInfos>();

            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_DiskDrive");

                foreach (ManagementObject wmi_drive in searcher.Get().Cast<ManagementObject>())
                {
                    using (wmi_drive)
                    {
                        string? deviceId = wmi_drive["DeviceID"]?.ToString();
                        string? model = wmi_drive[nameof(Model)]?.ToString();
                        string? mediaType = wmi_drive["MediaType"]?.ToString();
                        bool isRemovable = mediaType?.Contains("Removable", StringComparison.OrdinalIgnoreCase) == true || string.Equals(mediaType, "External hard disk media", StringComparison.OrdinalIgnoreCase);

                        if (isRemovable && deviceId != null && model != null)
                        {
                            try
                            {
                                long sizeBytes = Convert.ToInt64(wmi_drive["Size"] ?? 0);

                                if (sizeBytes > 0)
                                    infos.Add(new DriveInfos(deviceId, model, sizeBytes));
                            }
                            catch { }
                        }
                    }
                }
            }
            catch
            {
                throw;
            }

            return infos;
        }

        #endregion
    }
}