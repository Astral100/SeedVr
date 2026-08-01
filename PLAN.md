# SeedVr - plan

## Goal

Upscale a local video with the SeedVR2 model on a rented Vast.ai ComfyUI instance, driven end to end from
the console app: find a ready instance, upload the video, patch and submit the workflow, track progress,
download the result, and clean up the instance afterwards.

Still to be built: `ComfyProgressClient` (WebSocket monitor). `JobRequest` carries the job id, client id and the
namespaced instance paths; its prompt id, status and progress are added when milestones 4 and 5 need them.

## Current state

Milestone 3's raw path is verified live (01/08/2026). `JobOrchestrator` orchestrates: `InstanceSelector` returns a ready
instance, from which `JobOrchestrator` derives the ComfyUI and wrapper addresses, then `JobRunner` runs the job. `JobRunner` shares
`GetJobRequest` (resolve input, build the `JobRequest`), `UploadInputVideo` (via `ComfyUiClient.UploadVideo`) and
the `WorkflowBuilder` patch. `StartRawJob` submits over `ComfyUiClient.SubmitPrompt` with the request's `client_id`
and reports `node_errors` on rejection; `StartWrapperJob` submits over `ComfyWrapperClient.Generate` to the wrapper
address derived from the instance's `8288/tcp` mapping. `StartJob` calls the raw path; switch to the wrapper by
toggling the commented line in `StartJob`. `Main` wires `Console.CancelKeyPress` to a `CancellationTokenSource`, so Ctrl+C unwinds the run. The
workflow is a typed `SeedVrWorkflow`, not raw JSON, namespaced under `jobs/<job-id>/`. Node IDs and the wrapper
contract are confirmed in `docs/comfyui-wrapper-openapi.json`.

The raw path ran end-to-end against a rented instance: instance gating, upload into `jobs/<job-id>/`, patch and
`POST /prompt` to a queued `prompt_id`, then the job ran to `status_str: success`. Both sides of the `jobs/<job-id>/`
namespacing, the mixed `[string, int]` `NodeLink` form and the `node_errors` 400 shape are confirmed live; a uniform
all-string or all-integer link is rejected. The wrapper path is verified live too: `Bearer AuthToken` passes the
wrapper's proxy, `/generate` returns a `request_id`, and `/result/{request_id}` reports progress then a `completed`
output. Without an `s3` config the wrapper returns file references, not the bytes.

## Milestones

| # | Milestone | Status |
| - | --------- | ------ |
| 1 | Config and health check | Done, verified live |
| 2 | Instance readiness check | Done, verified live 28/07/2026 |
| 3 | Patch workflow, upload, submit | Done, both raw and wrapper paths verified live 01/08/2026 |
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
- `WorkflowBuilder` patches node 21 `LoadVideo.file`, node 23 `SaveVideo.filename_prefix`, node 14 DiT model and node 13 VAE model. `GetJobRequest` builds the `JobRequest`: `LoadVideo.file` is the subfoldered upload, `filename_prefix` is `jobs/<job-id>/<input base name>`.
- Upload stays raw `POST /upload/image`, multipart, streamed, into subfolder `jobs/<job-id>`; the wrapper has no upload endpoint, so a local file uploads through raw ComfyUI or S3.
- Escape query values with `Uri.EscapeDataString`; `InputVideoPath` contains `[`, `]` and spaces.
- Resolve `videos/` at runtime; unlike `workflows/`, it is not copied to the output directory.

Raw submit:
- `POST /prompt` with `{prompt, client_id}`; generate the `client_id` so milestone 4 can attach its WebSocket.

Wrapper submit:
- `POST /generate` with `{input:{workflow_json}}`; the returned `request_id` is the correlation key, no `client_id`.
- The address is derived from the instance's `8288/tcp` port mapping via `GetWrapperAddress`; the two paths are chosen by editing `StartJob`, not a runtime switch.

## Milestone 4 - live progress

- Raw: WebSocket `/ws?clientId=...`, with `/history/<prompt_id>` polling as the fallback; `/history` is the authoritative completion source when the socket drops. Completion is `status.completed == true` with `status.status_str == "success"` (confirmed live).
- Wrapper: poll `GET /result/{request_id}` (confirmed live: `status` goes `pending` -> `generating` -> `completed`, with the percent inside the human-readable `message`, e.g. "Progress: 70.0% (70/100)"), or `POST /generate/stream` for structured status (chunk format still unverified, see Open decisions).

## Milestone 5 - download the result

- Raw: `GET /view?filename=&subfolder=&type=output`, built from the server-returned filename and subfolder, streamed to a `.part` file, renamed only on success. Confirmed live: `outputs["23"].images[0]` carries `filename`, `subfolder` (`jobs/<job-id>`) and `type` (`output`); ComfyUI appends its own counter and extension (`..._00001_.mp4`), so use the returned filename verbatim.
- Wrapper: `GET /result/{request_id}` once `status == "completed"`. Confirmed live: `Result.output[]` carries `filename`, `subfolder` (`jobs/<job-id>`), `type` (`output`), `node_id` (`23`), `output_type` (`images`) and a worker `local_path`. Without an `s3` config the wrapper returns these references, not the bytes, so the download still goes through raw `/view` - or pass an `s3` config so the wrapper uploads and the URL comes back in the output (the path production needs, since a routed ephemeral worker's raw `/view` is unreachable after the fact).

## Milestone 6 - remote cleanup and cancellation

- Remove `ComfyUI/input/jobs/<job-id>/` and `ComfyUI/output/jobs/<job-id>/` after a successful download.
- Cancel: raw `POST /interrupt` or queue removal; wrapper `POST /cancel/{request_id}`.

## Milestone 7 - timeouts and progress

The control calls (`GetSystemStats`, `GetComfyUiQueueLength`, `GetInstalledModels`) carry a linked-`CancellationTokenSource` deadline; `ComfyUiClient` sets `Timeout.InfiniteTimeSpan`. The transfer calls still need theirs.

- Give transfers an idle deadline: re-arm `CancelAfter` on each chunk, so a stall fails fast while a long transfer does not.
- Report progress from the same tick that re-arms the timer.
- Upload: wrap the `FileStream` in a counting `Stream`; the total comes from the file length.
- Download: request with `HttpCompletionOption.ResponseHeadersRead` and copy in chunks; the total comes from `Content-Length`.
- Processing: re-arm on each WebSocket `progress` message, which carries `value` and `max`.

## Open decisions

- `/generate/stream`'s chunk format is unspecified in the openapi (empty response schema); milestone 4's wrapper path needs it pinned down against a live instance.

## Unverified paths

- The 404 branch in `InstanceSelector.ValidateModelsDownloaded`, for a missing `seedvr2` models folder.

## Decisions taken

- `AuthToken` is the instance `WEB_PASSWORD`, set at instance creation and kept in `appsettings.Development.json`, rather than a token read from the instance.
- Milestone 2 unit tests were written and then reverted before commit, on request.
- Both the raw ComfyUI and wrapper paths are built and kept, to compare them; the wrapper contract lives in `docs/comfyui-wrapper-openapi.json`.
- ComfyUI requires the mixed `[string, int]` link form: an all-string array fails on the output index ("list indices must be integers"), an all-integer array fails on the node id, so `NodeLink` and its converter stay.
- `ComfyUiSubmitResult`'s `node_errors` shape (`class_type`, `errors[].message`/`details`) is confirmed against a live 400.
- The wrapper returns output as file references (`filename`/`subfolder`/`type`/`local_path`), not bytes, unless an `s3` config is passed. Production on serverless will need `s3` for both input and output delivery, since a routed ephemeral worker's raw `/view` and local disk are unreachable after the fact.
- The API wrapper runs on container port `8288/tcp` (baked into the SeedVR2 Vast template, alongside ComfyUI's `8188/tcp`) and its proxy accepts `Bearer AuthToken`; its address resolves live from the instance's port mapping via `GetWrapperAddress`, so no wrapper URL is configured.

## Deferred

- Silence the `IHttpClientFactory` info logs: `"System.Net.Http.HttpClient": "Warning"` under `Logging:LogLevel`.
- The first port binding wins when an instance returns several.
- `/system_stats` logs its full body at Information.
- `SeedVr.Remote.Tests` is inert: it has no reference to `SeedVr.Remote`, only an empty `Test1`.
