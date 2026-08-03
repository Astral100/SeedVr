namespace SeedVr.Remote.Models
{
    /// <summary>One upscale job: its ids and the instance-side paths derived from the job id, so its upload
    /// and output are namespaced under the job's folder and milestone 6 can remove them by that prefix.</summary>
    public class JobRequest
    {
        // Names this job's folder under the instance's input and output roots. No dashes: it is a path segment.
        public string JobId { get; set; }

        // Ties the /prompt submission to the progress broadcast a milestone 4 WebSocket attaches to. Raw path only.
        public string ClientId { get; set; }

        // The resolved local source video, uploaded to the instance.
        public string LocalVideoPath { get; set; }

        // ComfyUI input subfolder for this job's upload: jobs/<job-id>.
        public string UploadSubfolder { get; set; }

        // SaveVideo.filename_prefix: jobs/<job-id>/<input base name>, so outputs land under output/jobs/<job-id>/.
        public string OutputFilenamePrefix { get; set; }
    }
}
