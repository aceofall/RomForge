namespace PickPack.Disk
{
    public class UrlWriteHandler : IImageWriteHandler
    {
        readonly Action<int, string, string?> _progressCallback;
        readonly HttpClient _httpClient;

        public UrlWriteHandler(Action<int, string, string?> progressCallback)
        {
            _progressCallback = progressCallback;
            _httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromMinutes(180)
            };
        }

        public async Task<(Stream stream, long length)> OpenStreamAsync(string url, CancellationToken cancellationToken)
        {
            _progressCallback(0, "파일 정보 확인 중...", url);

            using var headRequest = new HttpRequestMessage(HttpMethod.Head, url);
            var headResponse = await _httpClient.SendAsync(headRequest, cancellationToken);

            headResponse.EnsureSuccessStatusCode();

            long? contentLength = headResponse.Content.Headers.ContentLength;

            if (!contentLength.HasValue)
                throw new InvalidOperationException("서버에서 파일 크기 정보를 제공하지 않습니다.");

            _progressCallback(0, "다운로드 시작 중...", $"파일 크기: {contentLength.Value / (1024 * 1024)} MB");

            var downloadStream = await CreateDownloadStreamAsync(url, contentLength.Value, cancellationToken);

            return (downloadStream, contentLength.Value);
        }

        private async Task<Stream> CreateDownloadStreamAsync(string url, long contentLength, CancellationToken cancellationToken)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, url);
            var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

            response.EnsureSuccessStatusCode();

            var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var progressStream = new ProgressTrackingStream(responseStream, contentLength, _progressCallback, response);

            return progressStream;
        }

        public void Dispose() => _httpClient?.Dispose();
    }

    public class ProgressTrackingStream(Stream baseStream, long totalLength, Action<int, string, string?> progressCallback, HttpResponseMessage response) : Stream
    {
        long _bytesRead = 0;
        DateTime _lastProgressReport = DateTime.MinValue;

        public override bool CanRead => baseStream.CanRead;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => totalLength;

        public override long Position
        {
            get => _bytesRead;
            set => throw new NotSupportedException();
        }

        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            int read = await baseStream.ReadAsync(buffer.AsMemory(offset, count), cancellationToken);

            if (read > 0)
            {
                _bytesRead += read;

                var now = DateTime.Now;
                if (now - _lastProgressReport > TimeSpan.FromMilliseconds(500))
                {
                    int percent = (int)((double)_bytesRead / totalLength * 100);
                    string speedInfo = CalculateSpeed();
                    progressCallback(percent, "다운로드 중...", speedInfo);
                    _lastProgressReport = now;
                }
            }

            return read;
        }

        public override int Read(byte[] buffer, int offset, int count) => ReadAsync(buffer, offset, count, CancellationToken.None).GetAwaiter().GetResult();

        private string CalculateSpeed()
        {
            var elapsed = DateTime.Now - _lastProgressReport;

            if (elapsed.TotalSeconds > 0)
            {
                double mbRead = _bytesRead / (1024.0 * 1024.0);
                double mbTotal = totalLength / (1024.0 * 1024.0);

                return $"{mbRead:F1}/{mbTotal:F1} MB";
            }

            return string.Empty;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                baseStream?.Dispose();
                response?.Dispose();
            }

            base.Dispose(disposing);
        }

        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}