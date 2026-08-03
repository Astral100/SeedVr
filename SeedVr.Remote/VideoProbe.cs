using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using SeedVr.Estimators.Jobs;
using SeedVr.Logger;
using SeedVr.Remote.Models.Ffprobe;

namespace SeedVr.Remote
{
    /// <summary>Reads frame count and dimensions with ffprobe so estimators can scale padded frame and pixel work.</summary>
    public class VideoProbe
    {
        public async Task<VideoMetadata> GetVideoMetadata(string localVideoPath, CancellationToken cancellationToken = default)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = Constants.Video.FfprobeExecutable,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            foreach (var argument in GetArguments(localVideoPath))
            {
                startInfo.ArgumentList.Add(argument);
            }

            try
            {
                using var process = Process.Start(startInfo);
                if (process == null)
                {
                    Log.Warning("ffprobe did not start; waiting for SeedVR2 to report the video metadata.");
                    return null;
                }

                // Drain both streams before waiting, so a full stderr buffer cannot deadlock the process.
                var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
                var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
                await process.WaitForExitAsync(cancellationToken);
                var output = await outputTask;
                var error = await errorTask;

                if (process.ExitCode != 0)
                {
                    Log.Warning("ffprobe exited with code {ExitCode}: {Error}", [process.ExitCode, error.Trim()]);
                    return null;
                }

                return GetParsedVideoMetadata(output);
            }
            catch (Win32Exception ex)
            {
                Log.Warning(ex, "ffprobe was not found on PATH; waiting for SeedVR2 to report the video metadata.");
                return null;
            }
            catch (JsonException ex)
            {
                Log.Warning(ex, "ffprobe returned malformed JSON; waiting for SeedVR2 to report the video metadata.");
                return null;
            }
        }

        private IEnumerable<string> GetArguments(string localVideoPath)
        {
            return
            [
                "-v", "error",
                "-select_streams", "v:0",
                "-show_entries", "stream=width,height,nb_frames",
                "-of", "json",
                localVideoPath
            ];
        }

        private VideoMetadata GetParsedVideoMetadata(string output)
        {
            var probe = JsonSerializer.Deserialize<FfprobeOutput>(output);
            var stream = probe?.Streams?.FirstOrDefault();
            if (stream != null && int.TryParse(stream.FrameCount, out var frameCount) && frameCount > 0 && stream.Width > 0 && stream.Height > 0)
            {
                return new VideoMetadata(frameCount, stream.Width, stream.Height);
            }

            Log.Warning("The video container does not report usable frame metadata; waiting for SeedVR2 to report it.");
            return null;
        }
    }
}
