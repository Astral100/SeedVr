# SeedVr - plan

## Goal

Upscale a local video with the SeedVR2 model on a rented Vast.ai ComfyUI instance, driven end to end from
the console app: find a ready instance, upload the video, patch and submit the workflow, track progress,
download the result, and clean up the instance afterwards.

`JobRequest` carries the job id, client id and the namespaced instance paths; its prompt id, status and progress
are added when milestones 4 and 5 need them.

## Current state

Milestone 3's raw path is verified live (01/08/2026). `JobOrchestrator` orchestrates: `InstanceSelector` returns a ready
instance, from which `JobOrchestrator` derives the ComfyUI and wrapper addresses, then `JobRunner` runs the job. `JobRunner` shares
`GetJobRequest` (resolve input, build the `JobRequest`), `UploadInputVideo` (via `ComfyUiClient.UploadVideo`) and
the `WorkflowBuilder` patch. `StartRawJob` submits over `ComfyUiClient.SubmitPrompt` with the request's `client_id`
and reports `node_errors` on rejection; `StartWrapperJob` submits over `ComfyWrapperClient.Generate` to the wrapper
address derived from the instance's `8288/tcp` mapping. After submitting, each path waits for the job to finish:
`ComfyProgressClient.TrackRawJobCompletion` watches the raw progress WebSocket and confirms over `/history`, while
`WrapperProgressClient.TrackWrapperJobCompletion` polls the wrapper's `/result`, so both `StartRawJob` and `StartWrapperJob`
return only once the job has finished. Both trackers run the same ETA estimators: the shared `PhaseLinePoller` feeds
phase/batch lines to each, while percent comes from the socket on the raw path and from `/result`'s message on the wrapper path. `StartJob` calls the raw path; switch to the wrapper by toggling the commented line in `StartJob`. The two
tracking methods return the completed `/history` entry / final `/result` (null on failure), and
`JobRunner.DownloadJobOutputs` then streams each output over raw `/view` into `videos/output/`, mirroring the remote
`jobs/<job-id>/` subfolder so runs never collide, via a `.part` file renamed only on success. After a successful
download `JobRunner.CleanupRemoteJobFolders` removes the instance's `jobs/<job-id>/` input and output folders over
the instance's Jupyter contents API. `Main` wires `Console.CancelKeyPress` to a `CancellationTokenSource`, so Ctrl+C unwinds
the run; a cancellation during tracking first sends a best-effort raw `POST /interrupt` / wrapper `POST /cancel/{request_id}`.
Both trackers run under a progress-re-armed stall deadline (`ProcessingStallTimeoutSeconds`); a stalled job is stopped and recovered like a cancelled one, then the run fails. The
workflow is a typed `SeedVrWorkflow`, not raw JSON, namespaced under `jobs/<job-id>/`. Node IDs and the wrapper
contract are confirmed in `docs/comfyui-wrapper-openapi.json`.

`SeedVr.Remote` owns its HTTP-client and orchestration registrations through `AddSeedVrRemote`; the console composition root owns configuration and calls that extension. Remote protocol/path constants and estimator calibration constants live in their owning projects, leaving `SeedVr.Core` for shared configuration. The ComfyUI health check deserializes `/system_stats` and logs a compact version, RAM and GPU/VRAM summary rather than the full response.

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
| 4 | Live progress | Done, both paths verified live 03/08/2026 |
| 5 | Download the result | Done, both paths verified live 03/08/2026 |
| 6 | Remote cleanup and cancellation | Done, both paths verified live 04/08/2026 |
| 7 | Timeouts and progress reporting | Done, stall paths verified live 04/08/2026 |
| 8 | SeedVR parameter surface | Next |
| 9 | Parameter-aware ETA estimation | Planned, after 8 |
| 10 | Output encoding exploration | Potential, research needed |
| 11 | Workflow experimentation | Planned, last |

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
- Wrapper: poll `GET /result/{request_id}` (confirmed live: `status` goes `pending` -> `generating` -> `completed`, with the percent inside the human-readable `message`, e.g. "Progress: 70.0% (70/100)", parsed by `WrapperMessageParser` into the estimators). Verified live 03/08/2026 (request `0f06f4c9-dcc4-40cf-bd03-cbdaa6be622e`): 4.6s mean ETA error, matching the raw path. The `completed` status already includes remote finalization, so the completion metrics report the finalization split as zero on this path. `/generate/stream` was probed live 03/08/2026 and ruled out for progress; see Decisions taken.

## Milestone 4b - precise live ETA (experimental)

Goal: a precise progress bar / ETA to remote job completion. Adaptive-hybrid is the sole live estimator behind `IProgressEstimator` (`Update(ProgressSample) -> EtaEstimate`); `ProgressTracker` feeds it each sample, reports compact progress at 10-point intervals, records a replayable JSON trace and scores the completed run. Estimators are stateful (each IS a run model, the noted exception to the stateless rule).

The comparison implementations remain available for historical evaluation and focused tests; adaptive-hybrid composes the phase and percent models internally. The evaluated approaches were:
- `NaiveLinearEstimator` - `total = elapsed / fractionDone`. Percent only. The baseline to beat.
- `PercentDemaEstimator` - clipped double-EMA of percent/sec, extrapolated. Percent only; abrupt phase-rate changes are bounded.
- `PhaseBatchEstimator` - the phase model below; needs video metadata + stdout phase/batch lines. Completed-phase speed cautiously scales unseen phase priors within bounded limits.
- `AdaptiveHybridEstimator` - starts from phase/batch priors, then progressively blends in the host's percent-implied completion time; phase-only events retain the latest percent evidence.

Model (PhaseBatchEstimator):
- `N = ceil(frameCount / batchSize)`. SeedVR2 pads every batch to a uniform size (`Sequence of 32 frames` -> `Padding batch: 1 frame added (32 -> 33)`), so cost scales with padded batches, not raw frames. A raw-frame model badly mis-estimates short clips: a barely-filled last batch still costs a full batch (34 frames = 2 full batches = same cost as 64).
- `total ~= finalization + sum(setup_phase + N x batchCost_phase x workloadScale)`; workload scale includes output area and batch size relative to the reference run.
- Self-correct at every phase boundary; refine from batch 2 onward with clipped, confidence-weighted evidence because the first batch can carry phase-specific warmup cost.

Signals (wired into both paths; batch_size on node 10 is 33):
- Frame count, width and height up front via ffprobe's indexed `nb_frames` metadata, without scanning or decoding the video. If a container does not expose usable metadata, SeedVR2's startup `Input: N frames, WxHpx` line supplies the same workload context after submission.
- SeedVR2 stdout via `GET /internal/logs/raw`, polled every `LogPollSeconds` with its own short `LogPollTimeoutSeconds` deadline (not the control timeout), parsed by `ProgressLogParser` into video metadata and phase/batch events. Phase measurements use each log entry's UTC source timestamp, not its delayed polling receipt time, clamped so host/instance clock skew cannot date a signal into the future or before the run began. Polling is anchored to tracking start expressed in the instance's own clock, so the first response consumes current-job startup lines without replaying the pre-run buffer whatever the host/instance clock offset. `/internal/*` is "frontend use only" - fragile across ComfyUI/SeedVR2 versions, and observed to stop accepting connections mid-run under GPU load, so treat the phase-batch feed as best-effort.
- WebSocket `value/max` (scaled to 0-100) fed to the tracker via `RecordPercent`, driving the percent-derived estimators and hybrid correction. On the wrapper path the socket does not broadcast to this client, so the percent comes from `/result`'s message at the poll cadence instead.

Phase structure is phase-major (all N batches per phase, then the next phase). Priors below are the current reference defaults, retuned from the three 03/08/2026 RTX 3090 runs (3B model, output ~1080x1962px, 33-frame batches):

| Phase | WS % band | Setup prior | Per-batch prior |
| ----- | --------- | ----------- | --------------- |
| 1 Encoding | 0-20% | ~3.2s | ~20.5s |
| 2 DiT upscaling | 20-45% | ~1s | ~23.75s |
| 3 VAE decoding | 45-95% | ~0.3s | ~48s (dominant) |
| 4 Post-processing | 95-100% | 0s | ~5s |

Every estimator adds a remote-finalization allowance after SeedVR2 reaches 100%. Five live traces fit this as ~3.5s fixed coordination overhead plus ~0.1885s per frame scaled by output area. `Average FPS` prints only at the end, so it cannot seed a live ETA.

The 03/08 runs are materially variable at the same workload (~200s, ~217s and ~227s to remote completion), so fixed priors use the stable middle and completed-phase measurements learn a strongly shrunk, bounded run-speed factor. Two parser false positives were fixed: only `Phase N:` headers and `batch k/N` lines are treated as markers, so prose like `Starting upscaling generation`, `Upscaling completed successfully!` and the `...VideoUpscaler` URL no longer trigger a phase change.

Run 2 completed in ~185s from estimator anchor to SeedVR2 100% and ~200s to remote history success. Its direct timestamps showed encoding as ~2s setup + two ~19.5s batches, not the old ~25s setup + 6s/batch decomposition; DiT measured ~21.4s/batch, VAE ~46.0s/batch and post-processing ~5.1s/batch.

Instrumentation: the tracker clock anchors to the first live signal (excludes queue wait), keeps phase measurements and every prediction in the trace, and warns on percent regression. The console shows only human-readable elapsed/remaining/estimated-total progress and a completion summary; raw socket values, phase lines and estimator refinements stay silent. A completed run writes `logs/RunSnapshot <yyyy.MM.dd HH-mm-ss>.json` (same-second collisions overwrite) carrying the run id, receipt and source timing, every signal and prediction. `EstimatorEvaluator` scores adaptive-hybrid at every unique nonterminal percent checkpoint, reporting the mean and worst ETA error over those checkpoints. Historical prediction dictionaries remain in the trace schema so saved comparison traces still replay. Replay without Vast.ai: `dotnet run --project SeedVr.Console -- --score-estimator-trace <path>`.

Live verification 03/08/2026: prompt `7cc1db2f-a0d2-488a-9353-5db1edcd18ae` completed successfully in ~216.6s from the estimator anchor. Its first DiT batch took ~38.7s but its second took ~18.1s, confirming that a lone completed batch must not replace the phase rate or strongly scale unrelated phases. After clipping/confidence weighting that signal and bounding hybrid disagreement, final offline replay ranks adaptive-hybrid first on both saved full traces: 2.1s/2.4s common-checkpoint MAE and 6.1s/5.4s all-event maximum jump; phase-batch follows at 4.4s/4.0s MAE.

Final live validation 03/08/2026: prompt `72adcfd2-ccba-4d27-8aea-48a6405b952f` completed successfully in ~217.3s from the estimator anchor, including 14.8s remote finalization. Adaptive-hybrid ranked first live with 1.3s common-checkpoint MAE, +0.2s bias, 2.6s worst error and a 6.4s all-event maximum jump. Phase-batch followed at 2.5s MAE and a 4.2s maximum jump. The clipped batch-2 refinement held the repeated ~38.6s first DiT batch to a ~4s total-prediction adjustment instead of the previous ~50s jump. The back-to-back same-instance runs completed in ~216.6s and ~217.3s, establishing stable warm-instance throughput.

One-batch validation 03/08/2026: `[01s] cat finger shooting.mp4` produced 32 frames and prompt `16951c75-d890-4f3a-911c-adad7f5bdb12` completed successfully in ~111.8s from the estimator anchor. Remote finalization was 7.9s versus ~15.8s for 62 frames, confirming frame-proportional output assembly. With the five-trace finalization fit, offline replay gives phase-batch 3.4s, frame-linear 3.5s and adaptive-hybrid 3.8s common-checkpoint MAE; the short trace has only two common checkpoints.

Many-batch validation 03/08/2026: `[10s] cat finger shooting.mp4` produced 300 frames / 10 padded batches and prompt `d4cd2660-4682-48c6-b480-fc7a0c1d8a3d` completed successfully in ~1005.6s (15:45.8 processing + 59.9s remote finalization). With the live run's original zero-intercept finalization model, adaptive-hybrid and phase-batch ranked first at 22.0s/22.2s common-checkpoint MAE over 33 checkpoints; naive-linear scored 30.9s, frame-linear 33.9s and percent-DEMA 48.4s. Encoding measured 3.8s + 19.6s/batch, DiT 0.1s + 22.6s/batch, VAE ~47.0s/batch and post-processing ~5.0s/batch, validating the setup/per-batch decomposition and batch 3+ refinement. The prior finalization model predicted 75.1s; fitting a fixed overhead plus per-frame cost across all five traces predicts 60.1s for this run while retaining ~9.5s/15.2s for 32/62 frames. Offline replay with that fit reduces phase-batch/hybrid MAE to 10.0s/10.4s; hybrid remains the aggregate winner across all 53 common checkpoints, while phase-batch narrowly wins this long trace.

Ten-second validation 04/08/2026: `[10s] cat finger shooting.mp4` (300 frames, batch 33, 704x1280 -> 1080) completed in 12:20, prompt `0af7f009`. The estimate opened at 1036s vs 726s actual to SeedVR2 100% (+43%), still read 853s at 50% and 799s at completion; average ETA error 172.3s, worst 294.7s over 34 checkpoints. The priors overestimate on this faster host and the blend leans on them too long - the strongest data point yet for the lengths/hosts validation.

Host-adaptation tuning 04/08/2026, replay-validated against all 21 traces:
- The hybrid deviation band is relative (`HybridDeviationFraction` 0.35 of the phase total, 15s floor) instead of an absolute 15s that silenced live evidence on long runs; maximum live weight raised to 0.9.
- Per-batch evidence clips widened to 0.5-2.0 (from 0.75-1.35); run-speed learning scales with the number of measured phases (0.35 each, capped 0.8), so agreeing phases describe the host.
- Finalization is host-dependent (59.9s vs 14.7s for the same 300-frame clip on two hosts; the reference-host frame-linear fit stays exact there). The phase estimator scales the prior by the squared run-speed factor, and the hybrid swaps the live estimators' fixed allowance for that adapted value.
- Replay: the 10s fast-host trace improved 172.3s -> 110.0s average, today's fast-host short runs ~20s -> ~14s, reference-host traces held at 3-5s (d4cd2660, the same clip on the reference host, 11.0s -> 18.0s). Two tests updated to the widened guardrail magnitudes; 32/32 pass.
- The remaining error is structural: early checkpoints have only the reference priors, and a 40% faster host cannot be predicted before evidence arrives. The fix is cross-run host memory - persisting each instance's learned speed factor and finalization to seed the next run's priors.

Trace regression net 04/08/2026: recorded run snapshots live in `SeedVr.Estimators.Tests/Traces/` (tracked, copied to test output). `EstimatorTraceRegressionTests` replays each through adaptive-hybrid and asserts a per-trace MAE bound set above its score at the time it was added; a completeness test fails when a trace file lacks a bound. New runs still save to `logs/`; promoting one is moving the file plus adding a bound. Bounds are regression alarms - a deliberate retune refreshes them as part of the change. The corpus is curated to distinct situations only (host x clip length x speed regime x submit path): 8 fixtures kept of the 21 recorded - reference host 62f slow/fast clusters plus the outlier-first-batch run, 32f one-batch, 300f percent-only; fast host 32f raw and wrapper at production settings, 300f. Near-duplicate runs are not promoted; a superior recording of an existing cell replaces it (300f fast host is now `e675c4a0`, 04/08 13:06 - the first host-stamped snapshot, recorded under the retuned estimator, live average 110.9s matching replay).

Host fingerprint 04/08/2026: `VastAiInstance` reads `machine_id`, `gpu_name`, `cpu_name`, `cpu_cores_effective`, `cpu_cores`, `cpu_ram`, `disk_bw`, `pcie_bw` and `dlperf`; `JobProgressContext` carries the `HostProfile` and every new trace records it. RAM is recorded as the rental's allotment (machine RAM x effective/total cores, matching the instance card), not the machine total. The fingerprint is logged at instance selection. Old traces deserialize with a null host. Verified against the live instance card: every field matches; `disk_bw` drifts between reads as Vast re-measures it.

To do:
- Validate adaptive-hybrid across a range of lengths, aspect ratios and hosts.
- Cross-run host memory: seed priors from the instance's previous completed runs, keyed by `machine_id`.

## Milestone 5 - download the result

Verified live 03/08/2026 on both paths (raw prompt `91d818ff-fbfb-4121-a301-ddbbf703da68`, wrapper request
`0f06f4c9-dcc4-40cf-bd03-cbdaa6be622e`): the finished output downloaded to `videos/output/jobs/<job-id>/` with the
server filename verbatim, byte count matching Content-Length. Local placement: `videos/output/<subfolder>/<filename>`
with the server-returned values verbatim.

- Raw: `GET /view?filename=&subfolder=&type=output`, built from the server-returned filename and subfolder, streamed to a `.part` file, renamed only on success. Confirmed live: `outputs["23"].images[0]` carries `filename`, `subfolder` (`jobs/<job-id>`) and `type` (`output`); ComfyUI appends its own counter and extension (`..._00001_.mp4`), so use the returned filename verbatim.
- Wrapper: `GET /result/{request_id}` once `status == "completed"`. Confirmed live: `Result.output[]` carries `filename`, `subfolder` (`jobs/<job-id>`), `type` (`output`), `node_id` (`23`), `output_type` (`images`) and a worker `local_path`. Without an `s3` config the wrapper returns these references, not the bytes, so the download still goes through raw `/view` - or pass an `s3` config so the wrapper uploads and the URL comes back in the output (the path production needs, since a routed ephemeral worker's raw `/view` is unreachable after the fact).

## Milestone 6 - remote cleanup and cancellation

- Cleanup: after a successful download, `JobRunner.CleanupRemoteJobFolders` deletes `workspace/ComfyUI/input/jobs/<job-id>` and `workspace/ComfyUI/output/jobs/<job-id>` through the instance's Jupyter contents API (`DELETE /api/contents/<path>`, `JupyterClient`), which removes a non-empty folder in one call (204). Auth is `Authorization: token <jupyter_token>`, with the token and the `8080/tcp` port mapping read from the same Vast.ai account API call that resolves the other addresses; Jupyter is served over HTTPS with a self-signed certificate, so its client alone skips certificate validation. A 404 counts as already clean; any cleanup failure warns and never fails a job whose download succeeded.
- Cancellation: Ctrl+C during tracking sends a best-effort remote cancel on `CancellationToken.None` under the control timeout - raw `POST /interrupt`, wrapper `POST /cancel/{request_id}` - then rethrows so the run unwinds as cancelled.
- Post-cancellation recovery (`GpuRecovery.RecoverLatchedGpuMemory`, called by `JobRunner` on both paths, bounded by an overall deadline): wait for the queue to drain (the interrupt lands at a phase boundary), read the GPU's free-VRAM fraction from `/system_stats` with settle retries, and when it stays under `MinimumFreeVramFraction` restart the ComfyUI process via `JupyterClient.RestartComfyUi` (`supervisorctl restart comfyui` through Jupyter's terminal API: `POST /api/terminals`, the command over its WebSocket, then delete the terminal), then poll until VRAM reports healthy. Dry run verified live 04/08/2026: ComfyUI back with full VRAM in 18s.
- Readiness gate: `InstanceSelector` refuses an instance (Busy) whose free-VRAM fraction is under `MinimumFreeVramFraction`, so a latched instance can never receive a job. Verified live 04/08/2026 against a real latch (12.4/23.6 GiB free after a decode-phase cancel): the instance was refused, then recovered by a ComfyUI restart.
- A cancelled transfer's connection abort surfaces as an IOException inside the copy loops. `ProgressFileDownload` rethrows it as an OperationCanceledException on the caller's token (its exceptions reach user code, so the cancel unwinds as a cancellation, not a logged failure). `ProgressStreamContent` instead ends the serialization quietly on cancellation: nothing observes an exception thrown from a SerializeToStreamAsync frame (the HttpClient machinery swallows it and PostAsync surfaces the cancellation itself either way), so throwing there is dead weight. The instance logs a ConnectionResetError for the aborted upload; that is the server's normal view of a client abort and nothing is stored.
- Raw cancellation verified live 04/08/2026 (prompt `c5b8288c-6090-4428-a89e-4b62ba135edd`): Ctrl+C at ~20% sent `/interrupt`, the instance logged "Processing interrupted", and `/history` recorded `status_str "error"`, `completed false`, with an `execution_interrupted` message naming the node it stopped on. The cancelled run's leftover input folder was removed via Jupyter afterwards.
- In-app cleanup verified live 04/08/2026 on both paths (raw job `e106e92cb5fc4b9daff072db9ca92cdb`, wrapper job `9fe400fcf4fa4c9eba5b4078ca0559fe`): after the download both job folders were removed through `JobRunner`, confirmed empty by follow-up listings.
- Wrapper cancellation verified live 04/08/2026 (requests `644cfafa` at ~20% and `8235a35a` at ~45%): `/cancel` marks the request cancelled, aborts the worker's WebSocket and interrupts the underlying ComfyUI prompt itself, so no raw `/interrupt` chaining is needed. The interrupt takes effect at a phase boundary, so a cancelled job can keep the instance busy for a short wind-down; the queue-length readiness gate covers that window (observed refusing a run).

## Milestone 7 - timeouts and progress

The control calls (`GetSystemStats`, `GetComfyUiQueueLength`, `GetInstalledModels`) carry a linked-`CancellationTokenSource` deadline; `ComfyUiClient` sets `Timeout.InfiniteTimeSpan`. Upload and download stream in chunks, re-arm a configurable idle deadline after each successful write and report progress on that same tick; the download requests with `HttpCompletionOption.ResponseHeadersRead` and takes its total from `Content-Length`.

- Processing stall deadline: `ProcessingStallTimeoutSeconds` (default 600) bounds every tracking wait, linked to the run token; firing throws a `TimeoutException` (thrown nowhere else, so it is accurately catchable). Raw: armed across the socket connect and every receive, re-armed only by a progress frame that records a percent. Wrapper: armed across the `/result` polls, re-armed by a status transition or a changed parsed percent - which also bounds a permanently failing or unparseable `/result` (the former unbounded-retry case), since those polls never re-arm. The value must outlast the longest legitimately quiet stretch, chiefly remote finalization (~0.2s per frame of silence).
- `JobRunner` treats a stall like a cancellation: best-effort raw `/interrupt` / wrapper `/cancel`, then `GpuRecovery`, then the run fails - otherwise a hung job would hold the instance and the readiness gate would refuse every later run.
- `/history` completion poll: bounded by the stall deadline when the socket saw the run end (the entry lands within seconds); left unbounded when the socket dropped mid-run, because the poll is then the only tracker for a possibly healthy long job with no percent signal left to re-arm on.
- The courtesy socket close runs on its own 5s deadline (`SocketCloseTimeoutSeconds`), so a hung or already-cancelled connection cannot stall the unwind.
- The wrapper `/result` percent is recorded only when it changes (closes the 4b to-do): the 3s poll no longer replays identical percents into the rate-based estimators as slowdowns.
- Both stall paths verified live 04/08/2026 by freezing the instance's ComfyUI python process (`pkill -STOP`) mid-generation at a 60s test timeout. Raw (prompt `f1bb3893`): the deadline fired exactly 60s after the last progress frame, the `/interrupt` timed out with a warning against the frozen server, recovery gave up at its 120s deadline and the run failed cleanly. Wrapper (request `0e1a3e10`): `/result` kept answering with an unmoving percent, the deadline fired at 60s, `/cancel` succeeded (the wrapper stayed alive) and the run failed cleanly. A frozen server makes each failed queue ping cost the full 30s control timeout, so the recovery's 120s deadline bails before the 5-failed-pings streak - same outcome, expected. A healthy 60s-armed run also completed with no false fire.

## Milestone 8 - SeedVR parameter surface

One typed, grouped parameter model — the future serverless request payload. Spec: issue #1; tickets #2 (skeleton), #3 (value parameters, blocked by #2), #4 (compile node, blocked by #2). Decisions settled 07/08/2026:

- Sources, most general first: built-in defaults (today's template values) -> job JSON file -> appsettings overrides. Appsettings wins; every applied override is logged at job start; override properties are nullable value types so "not set" is distinguishable.
- An omitted parameter means today's template value; defaults live in one place.
- Exposure criterion (trim settled 07/08/2026): a parameter is exposed only if its best value is genuinely unknown (content-, taste- or instance-dependent); README-settled values stay frozen. 11 fields.
- Structural groups (nested blocks in the job file, one class per group):
  - Quality: DiT model, `resolution`.
  - Performance: `batch_size`, torch compile `enabled` (the compile settings node is wired into the graph only when enabled; mode stays default).
  - MemoryFit: `blocks_to_swap`, `offload_device` (one field, `cpu`/`none`, applied to the DiT/VAE/tensor offload points), VAE `encode_tiled`/`decode_tiled` toggles.
  - Output: `bit_depth` (default: matched to the source via the existing ffprobe), `crf` (default 16; today's runs encode at the implicit CRF 23).
  - ExperimentControl: `seed`, `enable_debug`.
- Frozen (not exposed, current or README-recommended values): `temporal_overlap` (3), `uniform_batch_size` (true), `color_correction` (lab), `attention_mode` (sdpa; auto-selection per GPU is a possible follow-up), `swap_io_components` (false), tile sizes/overlaps, `prepend_frames`, `input_noise_scale`/`latent_noise_scale`, `max_resolution`, `tile_debug`, `cache_model` (false; one job per instance), compile mode/backend/dynamo/`fullgraph` knobs, VAE model, container/codec (core nodes are mp4/h264 only). Promoting a frozen parameter later is a small routine change.
- Validation rejects before submit, with a logged reason: `batch_size` must be 4n+1; `blocks_to_swap` <= 32 (3B) / 36 (7B), nonzero requires `offload_device` = `cpu`; `resolution` capped at 4K; `crf` 0-51; `bit_depth` 8 or 10; enum fields among accepted options.
- The effective parameter set is recorded into the run snapshot/trace. Experiment runs are not promoted into the trace regression corpus.
- Presets are saved job files; the request only ever carries raw parameters (see CONTEXT.md).
- ETA impact accepted for now: estimator margins widen under non-reference settings; tightening is milestone 9.

## Milestone 9 - parameter-aware ETA estimation

- Extend the estimators to take the recorded effective parameter set into account explicitly (model variant, block swap, tiling, compile), tightening margins as recorded runs accumulate.

## Milestone 10 - output encoding exploration (potential)

- Explore smaller/faster 4K delivery: VideoHelperSuite's Video Combine with NVENC h265, or software h265. Needs research; requires a Vast template update to install the custom node.

## Milestone 11 - workflow experimentation

- Experiment with the workflow graph itself — alternative nodes and wiring, not just parameter values.

## Open decisions

- None right now.

## Unverified paths

- The post-cancellation recovery has not caught a real latch in the wild (7 cancels since produced none; the one observed latch predates the recovery). Both branches are verified live 04/08/2026: the no-latch path on four cancelled runs, and the restart branch via a forced test (latch check temporarily forced true, since reverted) - ComfyUI restarted through `JupyterClient.RestartComfyUi` and reported the memory released 18s later.
- The 404 branch in `InstanceSelector.ValidateModelsDownloaded`, for a missing `seedvr2` models folder.
- Healthy runs on both paths completed at the production 600s stall value with no false fire (04/08/2026, raw `e32cb5da`, wrapper `d4eb9b81`, with the percent dedup active). A 10s/300-frame clip (raw `0af7f009`, 12:20 total) finalized in 14s - far lighter than the earlier ~0.2s/frame estimate - so 600s covers finalization for clips well beyond 10 minutes of footage; only multi-minute clips remain unexercised.
- Both failure strings are confirmed live 04/08/2026: the raw path's interrupted run recorded `status_str "error"` with `completed false`, and the wrapper's OOM run reported `status "failed"`.

## Decisions taken

- `AuthToken` is the instance `WEB_PASSWORD`, set at instance creation and kept in `appsettings.Development.json`, rather than a token read from the instance.
- Milestone 2 unit tests were written and then reverted before commit, on request.
- Both the raw ComfyUI and wrapper paths are built and kept, to compare them; the wrapper contract lives in `docs/comfyui-wrapper-openapi.json`.
- ComfyUI requires the mixed `[string, int]` link form: an all-string array fails on the output index ("list indices must be integers"), an all-integer array fails on the node id, so `NodeLink` and its converter stay.
- `ComfyUiSubmitResult`'s `node_errors` shape (`class_type`, `errors[].message`/`details`) is confirmed against a live 400.
- The wrapper returns output as file references (`filename`/`subfolder`/`type`/`local_path`), not bytes, unless an `s3` config is passed. Production on serverless will need `s3` for both input and output delivery, since a routed ephemeral worker's raw `/view` and local disk are unreachable after the fact.
- The API wrapper runs on container port `8288/tcp` (baked into the SeedVR2 Vast template, alongside ComfyUI's `8188/tcp`) and its proxy accepts `Bearer AuthToken`; its address resolves live from the instance's port mapping via `GetWrapperAddress`, so no wrapper URL is configured.
- Every progress loop (raw socket, `/internal/logs/raw` poll, `/history` poll, wrapper `/result` poll) self-heals: a transient read failure or per-request timeout is logged and retried, and an unparseable socket frame is skipped. Only run cancellation stops a loop, so a mid-run proxy blip does not abandon a job still running remotely.
- Wrapper tracking stays on `/result` polling. A one-off stream probe (run 03/08/2026, removed since; capture kept in `logs/wrapper-stream-74af0990b28347208b3447221cfe35a8.log`) pinned `/generate/stream` as SSE (`text/event-stream`, `data: {json}` events with `request_id`/`status`/`message`/`timestamp` plus queue info) that emits lifecycle transitions only: after "queued" and "processing" it stayed silent through 30s of generation while `/result` already reported 20%, so it adds nothing for progress cadence.
- Remote cleanup goes through the instance's Jupyter server, the one instance service that can delete files over HTTP: ComfyUI and the wrapper expose no deletion, and Vast.ai's remote-execute API (`PUT api/v0/instances/command/{id}/`) refuses running instances ("Execute command only avail on stopped instances", confirmed live 04/08/2026), pointing at SSH instead. Jupyter's `DELETE /api/contents/<path>` deletes a non-empty folder in one call (confirmed live 04/08/2026, removing the run's leftover folders).
- The wrapper reports a failed generation as HTTP 500 on `/result`, with the JSON body still carrying the full result (`status "failed"`, the error inside `message`; confirmed live 04/08/2026 via an OOM run). `GetResult` reads that body instead of throwing, mirroring `SubmitPrompt`'s 400 `node_errors` handling, so the poll ends on the failed status instead of retrying forever.
- A raw `/interrupt` mid-run leaves the SeedVR2 runner's VRAM allocated (~11 GiB observed 04/08/2026); `/free` does not reclaim it immediately, and the next job can fail with `torch.OutOfMemoryError`. The latch is occasional, not systematic: both wrapper-path cancels released their VRAM immediately. Failed jobs are prevented by the post-cancellation recovery (detect the latch, restart the ComfyUI process via Jupyter) and the free-VRAM readiness gate; a possible refinement is skipping the interrupt when the remaining ETA is short.
- The wrapper honors a self-assigned `input.request_id` (confirmed live: events echo it and `/result/<our-id>` answered mid-run), and its "Generation started" message exposes the underlying ComfyUI prompt id - both useful for serverless correlation later; the model property was removed with the probe and can be re-added when needed.

## Deferred

- Housekeeping pass at the end, after the raw-vs-wrapper decision lands: split `HttpClients/` into `HttpClients` (protocol clients), `Progress` (trackers, `PhaseLinePoller`, parsers) and `Transfer` (`ProgressStreamContent`, `ProgressFileDownload`); extract `JobRunner`'s remaining pipeline stages (download + local-path logic, cleanup/cancel pair) into their own classes. The GPU recovery subsystem is already extracted into `GpuRecovery`.
- The first port binding wins when an instance returns several.
