# SeedVr

Driving SeedVR2 video-upscaling jobs end to end on rented Vast.ai ComfyUI instances: select an instance, submit a patched workflow, track progress, download the result.

## Language

### Parameters

**Job file**:
A JSON file holding one job's parameter set; the request payload of the future serverless interface.
_Avoid_: config file, experiment file, settings file

**Parameter group**:
One of the five structural blocks a job file's parameters belong to: Quality, Performance, MemoryFit, Output, ExperimentControl. A parameter lives in the group matching the reason you'd reach for it.

**Quality parameters**:
Parameters that change what the upscaled frames look like.

**Performance parameters**:
Parameters that make a run faster on a capable instance.

**MemoryFit parameters**:
Parameters that make a run fit on a memory-constrained instance.

**Output parameters**:
Parameters that control how the finished frames are encoded into the delivered file.

**Experiment controls**:
Parameters for reproducibility and diagnosis rather than the result itself.

**Override**:
An appsettings value applied on top of the job file; it wins, and its application is logged.
_Avoid_: pin, local setting

**Effective parameter set**:
The values a run actually used after defaults, job file and overrides are layered; recorded with the run.

**Frozen parameter**:
A workflow value deliberately not exposed; it changes only by editing the workflow template.

**Preset**:
A convenience that pre-fills parameters before submission — today a saved job file, later a UI choice. The request itself only ever carries raw parameters, never a preset name.
_Avoid_: profile, tier
