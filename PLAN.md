# SeedVr - plan

## Goal

Upscale a local video with the SeedVR2 model on a rented Vast.ai ComfyUI instance, driven end to end from
the console app: find a ready instance, upload the video, patch and submit the workflow, track progress,
download the result, and clean up the instance afterwards.

Still to be built: `SeedVrWorkflowBuilder` (clones and patches the API workflow per job), `ComfyProgressClient`
(WebSocket monitor) and `JobContext` (job id, prompt id, filenames, status, last progress).

## Current state

Milestone 2 is complete and verified live against a real instance on 28/07/2026. Milestone 3 is next,
starting with a check of the node IDs in the workflow JSON.

Uncommitted: `InstanceState` moved into `SeedVr.Remote/Models/`.

## Milestones

| # | Milestone | Status |
| - | --------- | ------ |
| 1 | Config and health check | Done, verified live |
| 2 | Instance readiness check | Done, verified live 28/07/2026 |
| 3 | Patch workflow, upload, submit | Next |
| 4 | Live progress | Not started |
| 5 | Download the result | Not started |
| 6 | Remote cleanup and cancellation | Not started |
| 7 | Timeouts and progress reporting | Deferred until the phase boundaries settle |

## Milestone 3 - patch, upload, submit

- Check the node IDs in the workflow JSON before writing any code.
- Patch node 21 `LoadVideo.file`, node 23 `SaveVideo.filename_prefix`, node 10 `SeedVR2VideoUpscaler.inputs.*`.
- Upload via `POST /upload/image`, multipart, streamed.
- Submit the workflow inline via `POST /prompt`.
- Escape query values with `Uri.EscapeDataString`; `InputVideoPath` contains `[`, `]` and spaces.
- Build `/view` from the server-returned filename and subfolder, not the local path.
- Generate a `client_id` and send it with `/prompt`, so milestone 4 can attach its WebSocket.
- Verify the node `class_types` exist via `/object_info/{node}`: `LoadVideo`, `GetVideoComponents`, `CreateVideo`, `SaveVideo`.
- Resolve `videos/` at runtime; unlike `workflows/`, it is not copied to the output directory.

## Milestone 4 - live progress

- WebSocket `/ws?clientId=...`, with `/history/<prompt_id>` polling as the fallback.
- `/history` is the authoritative completion source when the socket drops.

## Milestone 5 - download the result

- `GET /view?filename=&subfolder=&type=output`, streamed to a `.part` file, renamed only on success.

## Milestone 6 - remote cleanup and cancellation

- Remove `ComfyUI/input/jobs/<job-id>/` and `ComfyUI/output/jobs/<job-id>/` after a successful download.
- Cancel via `POST /interrupt` or queue removal.

## Milestone 7 - timeouts and progress

Only `GetSystemStats` and `GetComfyUiQueueLength` carry a deadline; `ComfyUiClient` sets `Timeout.InfiniteTimeSpan`.

- Give the remaining control calls the same linked-`CancellationTokenSource` deadline.
- Uncomment the timeout catch in `SeedVrJobRunner.ValidateModelsDownloaded` once `GetInstalledModels` has one.
- Give transfers an idle deadline: re-arm `CancelAfter` on each chunk, so a stall fails fast while a long transfer does not.
- Report progress from the same tick that re-arms the timer.
- Upload: wrap the `FileStream` in a counting `Stream`; the total comes from the file length.
- Download: request with `HttpCompletionOption.ResponseHeadersRead` and copy in chunks; the total comes from `Content-Length`.
- Processing: re-arm on each WebSocket `progress` message, which carries `value` and `max`.

## Open decisions

- Milestones 4-5: use the on-instance API wrapper (`/generate/stream`, `/result/{request_id}`, `/cancel/{request_id}`) or the raw ComfyUI protocol. Blocked on interpreting the `/generate/stream` disconnect. Choosing the wrapper reduces milestone 4 to parsing chunks, makes milestone 6 cancellation a `POST /cancel/{request_id}`, and makes milestone 3's `client_id` step unnecessary. Milestone 3 can start before this is settled.

## Unverified paths

- The 404 branch in `SeedVrJobRunner.ValidateModelsDownloaded`, for a missing `seedvr2` models folder.

## Decisions taken

- `AuthToken` is the instance `WEB_PASSWORD`, set at instance creation and kept in `appsettings.Development.json`, rather than a token read from the instance.
- Milestone 2 unit tests were written and then reverted before commit, on request.

## Deferred

- Silence the `IHttpClientFactory` info logs: `"System.Net.Http.HttpClient": "Warning"` under `Logging:LogLevel`.
- The first port binding wins when an instance returns several.
- `/system_stats` logs its full body at Information.
- `SeedVr.Remote.Tests` is inert: it has no reference to `SeedVr.Remote`, only an empty `Test1`.
