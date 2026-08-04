using System.Net;
using SeedVr.Logger;

namespace SeedVr.Remote.HttpClients
{
    public class ProgressStreamContent : HttpContent
    {
        private readonly string _path;
        private readonly long _length;
        private readonly TimeSpan _idleTimeout;
        private readonly CancellationToken _callerToken;
        private int _lastReportedPercent;

        public ProgressStreamContent(string path, TimeSpan idleTimeout, CancellationToken callerToken)
        {
            _path = path;
            _length = new FileInfo(path).Length;
            _idleTimeout = idleTimeout;
            _callerToken = callerToken;
        }

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext context)
        {
            return CopyTo(stream, CancellationToken.None);
        }

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext context, CancellationToken cancellationToken)
        {
            return CopyTo(stream, cancellationToken);
        }

        protected override bool TryComputeLength(out long length)
        {
            length = _length;
            return true;
        }

        private async Task CopyTo(Stream destination, CancellationToken serializationToken)
        {
            await using var source = File.OpenRead(_path);
            using var idleDeadline = CancellationTokenSource.CreateLinkedTokenSource(_callerToken, serializationToken);
            idleDeadline.CancelAfter(_idleTimeout);

            var buffer = new byte[Constants.Transfer.BufferSize];
            long transferred = 0;

            try
            {
                while (true)
                {
                    var read = await source.ReadAsync(buffer, idleDeadline.Token);
                    if (read == 0)
                    {
                        break;
                    }

                    await destination.WriteAsync(buffer.AsMemory(0, read), idleDeadline.Token);
                    transferred += read;
                    idleDeadline.CancelAfter(_idleTimeout);
                    ReportProgress(transferred);
                }
            }
            catch (OperationCanceledException ex) when (!_callerToken.IsCancellationRequested && !serializationToken.IsCancellationRequested)
            {
                throw new IOException($"The transfer made no progress for {_idleTimeout.TotalSeconds:F0} seconds.", ex);
            }
            catch (Exception ex) when (ex is OperationCanceledException or IOException && (_callerToken.IsCancellationRequested || serializationToken.IsCancellationRequested))
            {
                // Cancellation lands here either as the linked token's cancellation or as the abort of the already-
                // cancelled connection. Nothing observes an exception thrown from this frame - the HttpClient
                // machinery swallows it and PostAsync surfaces the cancellation itself either way - so end the
                // serialization quietly instead of throwing into a void.
            }
        }

        /// <summary>Logs the transfer's byte progress only when it crosses the next percent milestone or completes.</summary>
        private void ReportProgress(long transferred)
        {
            var percent = _length > 0 ? (int)Math.Floor(100.0 * transferred / _length) : 100;
            var shouldReport = percent >= _lastReportedPercent + Constants.Transfer.ProgressPercentInterval || transferred == _length;
            if (!shouldReport)
            {
                return;
            }

            _lastReportedPercent = percent;
            Log.Information("Upload progress: {Transferred}/{Total} bytes ({Percent}%).", [transferred, _length, percent]);
        }
    }
}
