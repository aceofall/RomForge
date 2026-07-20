using Common.WPF.ViewModels;
using LibHac.Ns;
using NSW.Core.Enums;
using NSW.HacPack.Models;
using NSW.M1.Core.Services;
using NSW.WPF.Services;
using NSW.WPF.UI;
using NSW.WPF.ViewModels;
using RomForge.Core.Models;
using RomForge.Core.Services.Switch;
using RomForge.Core.UI.Command;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace RomForge.ViewModels.Switch
{
    public class ConvertSaturnMainViewModel : ToolTabViewModel
    {
        // romfs 하위 고정 cue 파일명 (더미 스위치 타이틀 내부에 박혀있는 이름)
        private const string TargetCueFileName = "Cotton2.cue";

        private string _gameTitle = string.Empty;
        private string _gameId = string.Empty;
        private string _gameVersion = string.Empty;
        private string _cuePath = string.Empty;
        private string _nspPath = string.Empty;
        private string _coverImagePath = string.Empty;
        private bool _isConverting;
        private string _progressLabel = "대기 중...";
        private string _progressPercent = "0%";
        private string _progressSpeed = string.Empty;
        private string _progressTime = "00:00 경과";
        private double _progressPct;

        private CancellationTokenSource? _cts;

        public string GameTitle { get => _gameTitle; set { _gameTitle = value; OnPropertyChanged(); } }
        public string GameId { get => _gameId; set { _gameId = value; OnPropertyChanged(); } }
        public string GameVersion { get => _gameVersion; set { _gameVersion = value; OnPropertyChanged(); } }

        public string CuePath
        {
            get => _cuePath;
            set { _cuePath = value; OnPropertyChanged(); OnPropertyChanged(nameof(CueHintVisibility)); _ = ParseSaturnDataAsync(value); }
        }

        public string NspPath
        {
            get => _nspPath;
            set { _nspPath = value; OnPropertyChanged(); OnPropertyChanged(nameof(NspHintVisibility)); }
        }

        public string CoverImagePath
        {
            get => _coverImagePath;
            set { _coverImagePath = value; OnPropertyChanged(); OnPropertyChanged(nameof(CoverHintVisibility)); }
        }

        public bool IsConverting { get => _isConverting; set { _isConverting = value; OnPropertyChanged(); } }
        public string ProgressLabel { get => _progressLabel; set { _progressLabel = value; OnPropertyChanged(); } }
        public string ProgressPercent { get => _progressPercent; set { _progressPercent = value; OnPropertyChanged(); } }
        public string ProgressSpeed { get => _progressSpeed; set { _progressSpeed = value; OnPropertyChanged(); } }
        public string ProgressTime { get => _progressTime; set { _progressTime = value; OnPropertyChanged(); } }
        public double ProgressPct { get => _progressPct; set { _progressPct = value; OnPropertyChanged(); } }

        public Visibility CueHintVisibility => string.IsNullOrEmpty(CuePath) ? Visibility.Visible : Visibility.Collapsed;
        public Visibility NspHintVisibility => string.IsNullOrEmpty(NspPath) ? Visibility.Visible : Visibility.Collapsed;
        public Visibility CoverHintVisibility => string.IsNullOrEmpty(CoverImagePath) ? Visibility.Visible : Visibility.Collapsed;

        public ICommand BrowseCueCommand { get; }
        public ICommand BrowseNspCommand { get; }

        public ConvertSaturnMainViewModel()
        {
            BrowseCueCommand = new RelayCommand(_ => BrowseCue());
            BrowseNspCommand = new RelayCommand(_ => BrowseNsp());
        }

        private void BrowseCue()
        {
            var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "CUE 파일|*.cue" };
            if (dlg.ShowDialog() == true) CuePath = dlg.FileName;
        }

        private void BrowseNsp()
        {
            var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "NSP/NSZ 파일|*.nsp;*.nsz" };
            if (dlg.ShowDialog() == true) NspPath = dlg.FileName;
        }

        private async Task ParseSaturnDataAsync(string cuePath)
        {
            if (string.IsNullOrEmpty(cuePath) || !File.Exists(cuePath)) return;
            try
            {
                string binPath = string.Empty;
                var lines = await File.ReadAllLinesAsync(cuePath);
                foreach (var line in lines)
                {
                    if (line.StartsWith("FILE", System.StringComparison.OrdinalIgnoreCase))
                    {
                        var parts = line.Split('"');
                        if (parts.Length > 1)
                        {
                            binPath = Path.Combine(Path.GetDirectoryName(cuePath)!, parts[1]);
                            break;
                        }
                    }
                }
                if (string.IsNullOrEmpty(binPath) || !File.Exists(binPath)) return;
                using var fs = new FileStream(binPath, FileMode.Open, FileAccess.Read);
                if (fs.Length < 0x90) return;
                fs.Seek(0x30, SeekOrigin.Begin);
                var buffer = new byte[10];
                await fs.ReadAsync(buffer.AsMemory(0, 10));
                GameId = Encoding.ASCII.GetString(buffer).Trim();
                buffer = new byte[5];
                fs.Seek(0x3B, SeekOrigin.Begin);
                await fs.ReadAsync(buffer.AsMemory(0, 5));
                GameVersion = Encoding.ASCII.GetString(buffer);
                fs.Seek(0x70, SeekOrigin.Begin);
                var titleBuffer = new byte[32];
                await fs.ReadAsync(titleBuffer.AsMemory(0, 32));
                GameTitle = Encoding.ASCII.GetString(titleBuffer).Trim();
            }
            catch
            {
                GameTitle = string.Empty;
                GameId = string.Empty;
                GameVersion = string.Empty;
            }
        }

        public async Task ConvertAsync()
        {
            if (!ValidateInputs(out string err))
            {
                MessageBoxHelper.ShowError(err);
                return;
            }

            IsConverting = true;
            _cts = new CancellationTokenSource();
            var token = _cts.Token;

            // Progress<T>는 생성 시점(지금, UI 스레드)의 SynchronizationContext를 캡처해서
            // Report() 콜백을 자동으로 UI 스레드로 마샬링해줌. 그래서 이건 그대로 둬도 됨.
            var progress = new Progress<(int pct, string label)>(p =>
            {
                ProgressPct = p.pct >= 0 ? p.pct : 0;
                ProgressPercent = p.pct >= 0 ? $"{p.pct}%" : string.Empty;
                ProgressLabel = p.label;
            });

            try
            {
                // 실제 무거운 작업(언팩/파일IO/리빌드)은 백그라운드 스레드에서 실행.
                // 이게 없으면 NspBuildService.Run 내부의 동기 처리 구간(NPDM/NCA 생성 등)이
                // UI 스레드를 막아버려서 Dispatcher가 progress 콜백을 처리 못 하고 화면이 멈춰있음.
                await Task.Run(async () =>
                {
                    string outputDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "output", Path.GetFileNameWithoutExtension(NspPath));
                    Directory.CreateDirectory(outputDir);

                    // 1) 더미 스위치 NSP 언팩
                    SetLabel("더미 NSP 언팩 중...");
                    var unpackReq = new BuildRequest(NspPath, string.Empty, [], string.Empty, outputDir);
                    await NspBuildService.Run(unpackReq, BuildMode.UnpackOnly, progress, (msg, lvl) => SetLabel(msg), token);

                    string unpackedDir = Path.Combine(outputDir, "unpacked");
                    string romfsDir = Path.Combine(unpackedDir, "romfs");

                    // 2) 기존 cue 위치 탐색 (romfs 하위 어딘가의 Cotton2.cue)
                    string? existingCuePath = Directory.Exists(romfsDir)
                        ? Directory.GetFiles(romfsDir, TargetCueFileName, SearchOption.AllDirectories).FirstOrDefault()
                        : null;

                    if (existingCuePath == null)
                        throw new FileNotFoundException($"romfs 안에서 {TargetCueFileName}를 찾을 수 없습니다.");

                    string targetDir = Path.GetDirectoryName(existingCuePath)!;

                    // 3) 기존 cue가 참조하는 bin들 삭제
                    SetLabel("기존 CUE/BIN 삭제 중...");
                    var oldBins = CHD.Core.Services.ConversionSource.ParseBinsFromCue(existingCuePath);

                    foreach (var bin in oldBins)
                    {
                        if (File.Exists(bin))
                            File.Delete(bin);
                    }

                    File.Delete(existingCuePath);

                    // 4) 새로 입력받은 cue가 참조하는 bin들을 같은 폴더로 복사, cue는 Cotton2.cue로 이름 변경
                    SetLabel("새 CUE/BIN 복사 중...");
                    var newBins = CHD.Core.Services.ConversionSource.ParseBinsFromCue(CuePath);

                    if (newBins.Count == 0)
                        throw new FileNotFoundException("입력한 CUE에서 참조하는 BIN 파일을 찾을 수 없습니다.");

                    foreach (var bin in newBins)
                    {
                        if (!File.Exists(bin))
                            throw new FileNotFoundException($"BIN 파일이 존재하지 않습니다: {bin}");

                        string destBin = Path.Combine(targetDir, Path.GetFileName(bin));
                        File.Copy(bin, destBin, true);
                    }

                    string newCuePath = Path.Combine(targetDir, TargetCueFileName);
                    File.Copy(CuePath, newCuePath, true);

                    // 5) Title ID 계산 (첫번째 bin 기준 CRC32) → 010XXXXXXXX000
                    SetLabel("Title ID 계산 중...");
                    uint crc = Crc32Helper.ComputeFile(newBins[0]);
                    string titleIdStr = Crc32Helper.BuildTitleId(crc);
                    ulong titleId = ulong.Parse(titleIdStr, System.Globalization.NumberStyles.HexNumber);

                    // 6) 메타데이터 로드 후 한국어 슬롯에 게임명/커버 반영
                    SetLabel("메타데이터 반영 중...");
                    var metadata = MetadataService.GetGameMetadataFromUnpacked(unpackedDir);

                    // TODO: LibHac ApplicationControlProperty.Language 열거형에서 한국어 값 이름 확인 필요 (Korean으로 가정)
                    var koLang = metadata.Languages.FirstOrDefault(l => l.Language == ApplicationControlProperty.Language.Korean)
                                 ?? metadata.Languages.First();

                    koLang.TitleName = GameTitle;
                    koLang.Flag = true;

                    if (!string.IsNullOrEmpty(CoverImagePath) && File.Exists(CoverImagePath))
                        koLang.LogoData = BuildCoverBytes(CoverImagePath);

                    // 7) 리빌드 (Title ID는 BuildRequest.OverrideTitleId로 주입 → NspBuildService에서 처리)
                    // TODO: BuildRequest에 `public ulong? OverrideTitleId { get; set; }` 프로퍼티 추가 필요
                    //       (OverrideSdkVersion / OverrideKeyGeneration과 동일한 패턴)
                    SetLabel("리팩 중...");
                    var rebuildReq = new BuildRequest(string.Empty, string.Empty, [], string.Empty, outputDir)
                    {
                        UserMetadata = metadata,
                        OverrideTitleId = titleId
                    };

                    string finalNspPath = await NspBuildService.Run(rebuildReq, BuildMode.RebuildOnly, progress, (msg, lvl) => SetLabel(msg), token);

                    // 8) 결과 파일명을 우리가 계산한 GameTitle/titleIdStr 기준으로 리네임.
                    //    NspBuildService 내부(StepPackage)는 건드리지 않고 결과물만 후처리.
                    SetLabel("파일명 정리 중...");
                    string finalRenamedPath = RenameOutputFile(finalNspPath, GameTitle, titleIdStr);

                    SetLabel("완료!");
                    Path.GetDirectoryName(finalRenamedPath)!.OpenFolder();
                }, token);
            }
            catch (OperationCanceledException)
            {
                SetLabel("취소됨");
            }
            catch (Exception ex)
            {
                SetLabel("오류 발생");
                MessageBoxHelper.ShowError($"변환 실패: {ex.Message}");
            }
            finally
            {
                _cts?.Dispose();
                _cts = null;
                IsConverting = false;
                ProgressPct = 0;
            }
        }

        /// <summary>
        /// 백그라운드 스레드에서 안전하게 ProgressLabel을 갱신하기 위한 헬퍼.
        /// (Progress&lt;T&gt;를 거치지 않는 직접 호출들은 전부 이걸 통해야 함 - 안 그러면
        /// 크로스 스레드 예외 남)
        /// </summary>
        private void SetLabel(string msg) => Application.Current.Dispatcher.Invoke(() => ProgressLabel = msg);

        /// <summary>
        /// StepPackage가 만든 결과 NSP 파일명을 게임명 + 계산된 Title ID 기준으로 리네임.
        /// </summary>
        private static string RenameOutputFile(string currentPath, string gameTitle, string titleIdStr)
        {
            if (!File.Exists(currentPath))
                return currentPath;

            string safeTitle = gameTitle;

            foreach (char c in Path.GetInvalidFileNameChars())
                safeTitle = safeTitle.Replace(c, '_');

            string dir = Path.GetDirectoryName(currentPath)!;
            string newFileName = $"{safeTitle} [{titleIdStr.ToUpperInvariant()}][Saturn].nsp";
            string newPath = Common.Utils.GetUniqueFilePath(Path.Combine(dir, newFileName));

            File.Move(currentPath, newPath);

            return newPath;
        }

        public void Cancel() => _cts?.Cancel();

        private bool ValidateInputs(out string errorMsg)
        {
            errorMsg = string.Empty;

            if (string.IsNullOrEmpty(NspPath) || !File.Exists(NspPath))
            {
                errorMsg = "원본 스위치 파일(NSP/NSZ)을 선택하세요.";
                return false;
            }

            if (string.IsNullOrEmpty(CuePath) || !File.Exists(CuePath))
            {
                errorMsg = "CUE 파일을 선택하세요.";
                return false;
            }

            if (string.IsNullOrEmpty(GameTitle))
            {
                errorMsg = "게임명을 입력하세요.";
                return false;
            }

            return true;
        }

        /// <summary>
        /// LanguageTabControl.ImgGame_Drop 로직과 동일하게 256x256 jpg로 정규화.
        /// </summary>
        private static byte[] BuildCoverBytes(string imagePath)
        {
            using var image = Image.Load<Bgra32>(imagePath);
            image.Mutate(x => x.Resize(256, 256));

            using var ms = new MemoryStream();
            image.SaveAsJpeg(ms);
            return ms.ToArray();
        }
    }
}