# SeedVr - plan

## Goal

Upscale a local video with the SeedVR2 model on a rented Vast.ai ComfyUI instance, driven end to end from
the console app: find a ready instance, upload the video, patch and submit the workflow, track progress,
download the result, and clean up the instance afterwards.

Still to be built: `ComfyProgressClient` (WebSocket monitor). `JobContext` carries the job id, client id and
the namespaced instance paths; its prompt id, status and progress are added when milestones 4 and 5 need them.

## Current state

Milestone 3 is code-complete and unverified live. `JobRunner` orchestrates: `InstanceSelector` returns a ready
instance's ComfyUI address, then `JobSubmitter` runs the job. `JobSubmitter` shares `PrepareJob` (resolve input,
build the `JobContext`) and `BuildWorkflow` (upload via `ComfyUiClient.UploadVideo`, patch via `WorkflowBuilder`).
`SubmitRawJob` submits over `ComfyUiClient.SubmitPrompt` with the context's `client_id` and reports `node_errors`
on rejection; `SubmitWrapperJob` submits over `ComfyWrapperClient.Generate` to `AppSettings.WrapperBaseUrl`. `Run`
calls the raw path; switch to the wrapper by toggling the commented line in `Run`. The workflow is a typed
`SeedVrWorkflow`, not raw JSON, namespaced under `jobs/<job-id>/`. Node IDs and the wrapper contract are confirmed
in `docs/comfyui-wrapper-openapi.json`.

Next, a single live session to close out milestone 3:
- Run the raw path end-to-end against a rented instance: upload, submit, queued `prompt_id`.
- Confirm the `jobs/<job-id>/` namespacing holds live (see Open decisions).
- Confirm the `node_errors` body shape against a real 400, so `ComfyUiSubmitResult` matches (its field names are from the ComfyUI source, not yet seen live).
- Confirm the wrapper is deployed, on what port, and its auth shape.
- Resolve the `NodeLink` uniform-array question (see Open decisions).

## Milestones

| # | Milestone | Status |
| - | --------- | ------ |
| 1 | Config and health check | Done, verified live |
| 2 | Instance readiness check | Done, verified live 28/07/2026 |
| 3 | Patch workflow, upload, submit | Code-complete, unverified live |
| 4 | Live progress | Not started |
| 5 | Download the result | Not started |
| 6 | Remote cleanup and cancellation | Not started |
| 7 | Timeouts and progress reporting | Deferred until the phase boundaries settle |

## Milestone 3 - patch, upload, submit

Two submit paths are built in parallel, raw ComfyUI and the wrapper, to compare them. Node IDs are
confirmed against the `Seedvr2 Hd Video Upscale` example in `docs/comfyui-wrapper-openapi.json`:
21 `LoadVideo`, 22 `GetVideoComponents`, 24 `CreateVideo`, 23 `SaveVideo`, 10 `SeedVR2VideoUpscaler`,
13/14 the VAE/DiT loaders.

Shared:
- `WorkflowBuilder` patches node 21 `LoadVideo.file`, node 23 `SaveVideo.filename_prefix`, node 14 DiT model and node 13 VAE model. `PrepareJob` builds the `JobContext`: `LoadVideo.file` is the subfoldered upload, `filename_prefix` is `jobs/<job-id>/<input base name>`.
- Upload stays raw `POST /upload/image`, multipart, streamed, into subfolder `jobs/<job-id>`; the wrapper has no upload endpoint, so a local file uploads through raw ComfyUI or S3.
- Escape query values with `Uri.EscapeDataString`; `InputVideoPath` contains `[`, `]` and spaces.
- Resolve `videos/` at runtime; unlike `workflows/`, it is not copied to the output directory.

Raw submit:
- `POST /prompt` with `{prompt, client_id}`; generate the `client_id` so milestone 4 can attach its WebSocket.

Wrapper submit:
- `POST /generate` with `{input:{workflow_json}}`; the returned `request_id` is the correlation key, no `client_id`.
- Address comes from `AppSettings.WrapperBaseUrl` until port discovery is settled; the two paths are chosen by editing `Run`, not a runtime switch.

## Milestone 4 - live progress

- Raw: WebSocket `/ws?clientId=...`, with `/history/<prompt_id>` polling as the fallback; `/history` is the authoritative completion source when the socket drops.
- Wrapper: `POST /generate/stream` streamed status, or poll `GET /result/{request_id}`. Chunk format unverified, see Open decisions.

## Milestone 5 - download the result

- Raw: `GET /view?filename=&subfolder=&type=output`, built from the server-returned filename and subfolder, streamed to a `.part` file, renamed only on success.
- Wrapper: `GET /result/{request_id}`; the output arrives in `Result.output` as a URL or base64, per `return_outputs_as_base64` and the `s3` config.

## Milestone 6 - remote cleanup and cancellation

- Remove `ComfyUI/input/jobs/<job-id>/` and `ComfyUI/output/jobs/<job-id>/` after a successful download.
- Cancel: raw `POST /interrupt` or queue removal; wrapper `POST /cancel/{request_id}`.

## Milestone 7 - timeouts and progress

Only `GetSystemStats` and `GetComfyUiQueueLength` carry a deadline; `ComfyUiClient` sets `Timeout.InfiniteTimeSpan`.

- Give the remaining control calls the same linked-`CancellationTokenSource` deadline.
- Uncomment the timeout catch in `JobRunner.ValidateModelsDownloaded` once `GetInstalledModels` has one.
- Give transfers an idle deadline: re-arm `CancelAfter` on each chunk, so a stall fails fast while a long transfer does not.
- Report progress from the same tick that re-arms the timer.
- Upload: wrap the `FileStream` in a counting `Stream`; the total comes from the file length.
- Download: request with `HttpCompletionOption.ResponseHeadersRead` and copy in chunks; the total comes from `Content-Length`.
- Processing: re-arm on each WebSocket `progress` message, which carries `value` and `max`.

## Open decisions

- The wrapper's address is supplied by `AppSettings.WrapperBaseUrl` for now. Whether the wrapper is deployed on the Vast.ai instances, on what port, and its auth shape (`ComfyWrapperClient` assumes `Bearer AuthToken`) all need a live check; auto-discovery from the port mapping is deferred until its container port is known.
- `/generate/stream`'s chunk format is unspecified in the openapi (empty response schema); milestone 4's wrapper path needs it pinned down against a live instance.
- `JobContext` namespaces the upload (`/upload/image` `subfolder`) and output (`filename_prefix`) under `jobs/<job-id>/`. Confirm live that ComfyUI files the upload there, that `LoadVideo.file` reads it as `<subfolder>/<name>`, and that `SaveVideo` writes under `output/jobs/<job-id>/`, so milestone 6 cleanup can remove by that prefix.
- Node 23 is the native `SaveVideo` (chained `CreateVideo` 24 -> `SaveVideo` 23). The `/history` output key it writes the file under (`images` vs a video-specific key) is unknown; milestone 5's download parses it to find the filename, so confirm it on the first live run.
- The `node_errors` body shape on a rejected `/prompt`: `ComfyUiClient.SubmitPrompt` reads the 400 body into `ComfyUiSubmitResult`, whose `node_errors` field names are taken from the ComfyUI source, not a live response. Confirm them against a real 400.
- Whether `NodeLink` and its converter are needed at all: the template links are `[string, int]` (`["22", 0]`), a mixed-type array. Test against a live `/prompt` whether ComfyUI also accepts a uniform array (both strings or both ints); if it does, drop `NodeLink` for a plain `List<string>`/`int[]` and delete the converter.

## Unverified paths

- The 404 branch in `JobRunner.ValidateModelsDownloaded`, for a missing `seedvr2` models folder.

## Decisions taken

- `AuthToken` is the instance `WEB_PASSWORD`, set at instance creation and kept in `appsettings.Development.json`, rather than a token read from the instance.
- Milestone 2 unit tests were written and then reverted before commit, on request.
- Both the raw ComfyUI and wrapper paths are built and kept, to compare them; the wrapper contract lives in `docs/comfyui-wrapper-openapi.json`.

## Deferred

- Silence the `IHttpClientFactory` info logs: `"System.Net.Http.HttpClient": "Warning"` under `Logging:LogLevel`.
- The first port binding wins when an instance returns several.
- `/system_stats` logs its full body at Information.
- `SeedVr.Remote.Tests` is inert: it has no reference to `SeedVr.Remote`, only an empty `Test1`.
