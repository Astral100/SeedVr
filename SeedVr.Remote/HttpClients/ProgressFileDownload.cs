using SeedVr.Logger;

namespace SeedVr.Remote.HttpClients
{
    /// <summary>Writes a response body to a local file the way ProgressStreamContent reads one from disk: chunked, re-arming
    /// the idle deadline after each successful write and reporting percent milestones against Content-Length.</summary>
    public class ProgressFileDownload
    {
        private readonly string _path;
        private readonly TimeSpan _idleTimeout;
        private readonly CancellationToken _callerToken;
        private int _lastReportedPercent;

        public ProgressFileDownload(string path, TimeSpan idleTimeout, CancellationToken callerToken)
        {
            _path = path;
            _idleTimeout = idleTimeout;
            _callerToken = callerToken;
        }

        /// <summary>Streams the response body into the target path through a .part file that is renamed only on success.</summary>
        public async Task Save(HttpResponseMessage response)
        {
            var directory = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var partPath = _path + Constants.Transfer.PartFileSuffix;
            try
            {
                await CopyToFile(response, partPath);
                File.Move(partPath, _path, true);
            }
            finally
            {
                DeleteLeftoverPartFile(partPath);
            }
        }

        private async Task CopyToFile(HttpResponseMessage response, string partPath)
        {
            var totalLength = response.Content.Headers.ContentLength ?? 0;
            await using var source = await response.Content.ReadAsStreamAsync(_callerToken);
            await using var destination = File.Create(partPath);
            using var idleDeadline = CancellationTokenSource.CreateLinkedTokenSource(_callerToken);
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
                    ReportProgress(transferred, totalLength);
                }
            }
            catch (OperationCanceledException ex) when (!_callerToken.IsCancellationRequested)
            {
                throw new IOException($"The transfer made no progress for {_idleTimeout.TotalSeconds:F0} seconds.", ex);
            }

            // Belt and braces: a truncated body normally surfaces as a read exception, but never rename a short file into place.
            if (totalLength > 0 && transferred != totalLength)
            {
                throw new IOException($"The download ended early: received {transferred} of {totalLength} bytes.");
            }

            // Likewise never rename an empty file into place: a proxy can answer 200 with no body when the real file is gone.
            if (transferred == 0)
            {
                throw new IOException("The download contained no data.");
            }
        }

        /// <summary>Logs the transfer's byte progress only when it crosses the next percent milestone or completes.
        /// A response without a Content-Length reports nothing; the caller logs the finished file instead.</summary>
        private void ReportProgress(long transferred, long totalLength)
        {
            if (totalLength <= 0)
            {
                return;
            }

            var percent = (int)Math.Floor(100.0 * transferred / totalLength);
            var shouldReport = percent >= _lastReportedPercent + Constants.Transfer.ProgressPercentInterval || transferred == totalLength;
            if (!shouldReport)
            {
                return;
            }

            _lastReportedPercent = percent;
            Log.Information("Download progress: {Transferred}/{Total} bytes ({Percent}%).", [transferred, totalLength, percent]);
        }

        /// <summary>Removes an aborted transfer's .part file; after a successful move it no longer exists. A failed
        /// delete only warns, so cleanup cannot mask the download's own exception.</summary>
        private void DeleteLeftoverPartFile(string partPath)
        {
            try
            {
                if (File.Exists(partPath))
                {
                    File.Delete(partPath);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Log.Warning(ex, "Could not delete the leftover partial download '{PartPath}'.", [partPath]);
            }
        }
    }
}
