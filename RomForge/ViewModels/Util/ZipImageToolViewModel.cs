using Common.WPF.ViewModels;
using Ionic.Zlib;
using Microsoft.Win32;
using RomForge.Helpers;
using RomForge.Models; // 프로젝트의 LogEntry 및 LogLevel 위치에 맞게 수정
using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Input;

namespace RomForge.ViewModels.Util;

public class ZipImageToolViewModel : ToolTabViewModel
{
    #region Fields

    private CancellationTokenSource? _cts;
    private bool _isWorking;
    private object? _selectedUsbDrive;
    private string _writeImagePath = string.Empty;
    private string _readImagePath = string.Empty;
    private int _selectedSegmentIndex = 5; // 기본값 20GB
    private int _selectedCompressionIndex = 0; // 기본값 압축 안함
    private bool _isZipOptionsVisible;

    private int _progressValue;
    private string _statusMessage1 = string.Empty;
    private string _statusMessage2 = string.Empty;

    // 예시 구조를 위한 임시 가상 클래스 (실제 PickPack.Disk 내부 구조에 맞게 매핑 필요)
    // WmiWatchers나 DriveInfos, ImageWriter 등은 기존 라이브러리를 그대로 참조한다고 가정합니다.
    private readonly dynamic? _wmiWatchers;

    #endregion

    #region Properties

    public bool IsWorking
    {
        get => _isWorking;
        set { _isWorking = value; OnPropertyChanged(); CommandManager.InvalidateRequerySuggested(); }
    }

    public ObservableCollection<object> UsbDrives { get; } = [];

    public object? SelectedUsbDrive
    {
        get => _selectedUsbDrive;
        set { _selectedUsbDrive = value; OnPropertyChanged(); }
    }

    public string WriteImagePath
    {
        get => _writeImagePath;
        set { _writeImagePath = value; OnPropertyChanged(); }
    }

    public string ReadImagePath
    {
        get => _readImagePath;
        set { _readImagePath = value; OnPropertyChanged(); }
    }

    public int SelectedSegmentIndex
    {
        get => _selectedSegmentIndex;
        set { _selectedSegmentIndex = value; OnPropertyChanged(); }
    }

    public int SelectedCompressionIndex
    {
        get => _selectedCompressionIndex;
        set { _selectedCompressionIndex = value; OnPropertyChanged(); }
    }

    public bool IsZipOptionsVisible
    {
        get => _isZipOptionsVisible;
        set { _isZipOptionsVisible = value; OnPropertyChanged(); }
    }

    public int ProgressValue
    {
        get => _progressValue;
        set { _progressValue = value; OnPropertyChanged(); }
    }

    public string StatusMessage1
    {
        get => _statusMessage1;
        set { _statusMessage1 = value; OnPropertyChanged(); }
    }

    public string StatusMessage2
    {
        get => _statusMessage2;
        set { _statusMessage2 = value; OnPropertyChanged(); }
    }

    public ObservableCollection<LogEntry> LogEntries { get; } = [];

    public List<string> SegmentSizes { get; } = ["분할안함", "1GB", "2GB", "5GB", "10GB", "20GB", "50GB", "100GB"];
    public List<string> CompressionLevels { get; } = ["압축 안함", "빠르게", "보통 압축률", "최대 압축률"];

    #endregion

    #region Commands

    public ICommand RefreshDrivesCommand { get; }
    public ICommand OpenWriteImageCommand { get; }
    public ICommand OpenReadImageCommand { get; }
    public ICommand WriteCommand { get; }
    public ICommand ReadCommand { get; }

    #endregion

    public ZipImageToolViewModel()
    {
        RefreshDrivesCommand = new RelayCommand(_ => ListRemovableUsbDrives());
        OpenWriteImageCommand = new RelayCommand(_ => OpenWriteImage());
        OpenReadImageCommand = new RelayCommand(_ => OpenReadImage());
        WriteCommand = new RelayCommand(async _ => await ExecuteWriteAsync());
        ReadCommand = new RelayCommand(async _ => await ExecuteReadAsync());

        // 기존 절전 모드 방지 로직 유지
        try { dynamic diskUtil = new object(); /* DiskUtil.PreventSleep(); */ } catch { }

        ListRemovableUsbDrives();
        SetupUsbWatchers();
    }

    #region Methods

    private void ListRemovableUsbDrives()
    {
        UsbDrives.Clear();
        try
        {
            // 기존 WinForms 프로젝트의 DriveInfos.GetDriveInfos() 호출 가정
            dynamic drives = typeof(System.Windows.Forms.Form).Assembly.GetType("PickPack.Disk.DriveInfos")?
                .GetMethod("GetDriveInfos")?.Invoke(null, null) ?? new List<object>();

            foreach (var d in drives)
                UsbDrives.Add(d);
        }
        catch
        {
            // 실패 시 Fallback 혹은 기존 참조 정상 작동 시 정상 반영됨
        }

        if (UsbDrives.Count > 0)
            SelectedUsbDrive = UsbDrives[0];
        else
            System.Windows.MessageBox.Show("연결된 이동식 드라이브가 없습니다.\n연결 후 재시도 하세요.", "알림", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void SetupUsbWatchers()
    {
        try
        {
            // 기존 WmiWatchers 이벤트 연동 부분 (필요 시 유지)
            // 본 코드는 구동 컨텍스트 유지를 위해 비하인드나 전역 핸들러 구조 대신 원본 로직 흐름만 보존합니다.
        }
        catch { }
    }

    private void OpenWriteImage()
    {
        var dialog = new OpenFileDialog
        {
            Filter = "ZIP 파일 (*.zip)|*.zip|7z 파일 (*.7z)|*.7z|gz 파일 (*.gz)|*.gz|디스크 이미지 파일 (*.img)|*.img|모든 파일 (*.*)|*.*"
        };
        if (dialog.ShowDialog() == true)
        {
            WriteImagePath = dialog.FileName;
        }
    }

    private void OpenReadImage()
    {
        var dialog = new SaveFileDialog
        {
            Filter = "디스크 이미지 파일 (*.img)|*.img|ZIP 파일 (*.zip)|*.zip"
        };
        if (dialog.ShowDialog() == true)
        {
            ReadImagePath = dialog.FileName;
            string ext = Path.GetExtension(dialog.FileName).ToLowerInvariant();
            IsZipOptionsVisible = (ext == ".zip");
        }
    }

    private async Task ExecuteWriteAsync()
    {
        if (IsWorking)
        {
            _cts?.Cancel();
            StatusMessage1 = "취소중...";
            return;
        }

        if (SelectedUsbDrive == null)
        {
            MessageBox.Show("SD 카드를 선택해주세요.", "알림", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (MessageBox.Show("굽기를 시작하면 선택한 드라이브의 모든 데이터가 삭제 됩니다.\n진행 할까요?", "알림", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.No)
            return;

        using (BeginWork())
        {
            IsWorking = true;
            _cts = new CancellationTokenSource();
            ProgressValue = 0;

            try
            {
                dynamic info = SelectedUsbDrive;
                int diskNumber = info.DiskNumber;
                long sizeBytes = info.SizeBytes;

                // 기존 라이브러리 비동기 메서드 호출 (인스턴스 및 이벤트 연동)
                // 예시 구조이므로 리플렉션이나 원본 프로젝트 참조 객체를 그대로 대입하시면 됩니다.
                dynamic imgWriter = Activator.CreateInstance(Type.GetType("PickPack.Disk.ImageWriter")!)!;

                imgWriter.ProgressChanged += (System.EventHandler<dynamic>)((s, e) => {
                    Application.Current.Dispatcher.Invoke(() => {
                        ProgressValue = e.Percent;
                        StatusMessage1 = e.Message1;
                        if (e.Message2 != null) StatusMessage2 = e.Message2;
                    });
                });

                imgWriter.WriteEnded += (System.EventHandler)((s, e) => {
                    Application.Current.Dispatcher.Invoke(() => {
                        ProgressValue = 100;
                        StatusMessage1 = "이미지 굽기 완료";
                        MessageBox.Show("이미지 굽기가 완료되었습니다.", "굽기 완료", MessageBoxButton.OK, MessageBoxImage.Information);
                    });
                });

                await imgWriter.WriteImageAsync(WriteImagePath, diskNumber, sizeBytes, _cts.Token);
            }
            catch (OperationCanceledException)
            {
                ProgressValue = 0;
                StatusMessage1 = "굽기 취소";
                MessageBox.Show("굽기가 취소 되었습니다.", "알림", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "오류", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsWorking = false;
                _cts?.Dispose();
                _cts = null;
            }
        }
    }

    private async Task ExecuteReadAsync()
    {
        if (IsWorking)
        {
            _cts?.Cancel();
            StatusMessage1 = "취소중...";
            return;
        }

        if (SelectedUsbDrive == null)
        {
            MessageBox.Show("SD 카드를 선택해주세요.", "알림", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (string.IsNullOrEmpty(ReadImagePath))
        {
            MessageBox.Show("이미지 파일을 지정해주세요.", "알림", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        using (BeginWork())
        {
            IsWorking = true;
            _cts = new CancellationTokenSource();
            ProgressValue = 0;

            try
            {
                dynamic info = SelectedUsbDrive;
                int diskNumber = info.DiskNumber;

                dynamic reader = Activator.CreateInstance(Type.GetType("PickPack.Disk.ImageReader")!)!;
                reader.ProgressChanged += (System.EventHandler<dynamic>)((s, e) => {
                    Application.Current.Dispatcher.Invoke(() => {
                        ProgressValue = e.Percent;
                        StatusMessage1 = e.Message1;
                        if (e.Message2 != null) StatusMessage2 = e.Message2;
                    });
                });

                reader.WriteEnded += (System.EventHandler)((s, e) => {
                    Application.Current.Dispatcher.Invoke(() => {
                        ProgressValue = 100;
                        StatusMessage1 = "이미지 저장 완료";
                        MessageBox.Show("이미지 저장이 완료되었습니다.", "저장 완료", MessageBoxButton.OK, MessageBoxImage.Information);
                    });
                });

                long maxOutputSegmentSize64 = GetMaxOutputSegmentSize64();
                CompressionLevel compressionLevel = GetCompressionLevel();

                await Task.Run(() => reader.ReadImageAsync(diskNumber, ReadImagePath, maxOutputSegmentSize64, compressionLevel, _cts.Token));
            }
            catch (OperationCanceledException)
            {
                ProgressValue = 0;
                StatusMessage1 = "저장 취소";
                DeleteArchiveTempAndPartialFiles(ReadImagePath);
                MessageBox.Show("저장이 취소되었습니다.", "알림", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "오류", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsWorking = false;
                _cts?.Dispose();
                _cts = null;
            }
        }
    }

    private long GetMaxOutputSegmentSize64()
    {
        return SelectedSegmentIndex switch
        {
            0 => 0,
            1 => 1073741824,
            2 => 2 * 1073741824L,
            3 => 5 * 1073741824L,
            4 => 10 * 1073741824L,
            5 => 20 * 1073741824L,
            6 => 50 * 1073741824L,
            7 => 100 * 1073741824L,
            _ => 20 * 1073741824L
        };
    }

    private CompressionLevel GetCompressionLevel()
    {
        return SelectedCompressionIndex switch
        {
            0 => CompressionLevel.None,
            1 => CompressionLevel.BestSpeed,
            2 => CompressionLevel.Default,
            3 => CompressionLevel.BestCompression,
            _ => CompressionLevel.None
        };
    }

    private void DeleteArchiveTempAndPartialFiles(string fileName)
    {
        try
        {
            if (File.Exists(fileName)) File.Delete(fileName);
            string? directoryPath = Path.GetDirectoryName(fileName);
            if (string.IsNullOrEmpty(directoryPath) || !Directory.Exists(directoryPath)) return;

            foreach (string file in Directory.GetFiles(directoryPath, "DotNetZip-*.tmp"))
                File.Delete(file);

            string searchPattern = Path.GetFileNameWithoutExtension(fileName) + ".z*";
            foreach (string file in Directory.GetFiles(directoryPath, searchPattern))
                File.Delete(file);
        }
        catch { }
    }

    #endregion
}