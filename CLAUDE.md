# Conventions

This file is authoritative. If anything else - stored memory, an earlier instruction, a habit from
another project - disagrees with it, follow this file and say so rather than resolving it silently.

## Code

- No `Async` suffix on async method names. `GetInstalledModels`, not `GetInstalledModelsAsync`.
- Every method has a braced body. Never use expression-bodied methods (`=>`).
- Keep statements and signatures on one line, unless the line is genuinely long or the signature has many parameters.
- Compare strings with `==` and `!=`. Never `string.Equals`, and no `ToLower`/`ToUpper` unless genuinely necessary.
- Only 9.x NuGet packages.
- Prefer `GetFromJsonAsync`/`ReadFromJsonAsync` over reading streams and calling `JsonSerializer` by hand.
- Keep methods stateless: return a value rather than mutating another object, and pass what a method needs explicitly.
- Values that are not genuinely configurable belong in `Constants`, not `AppSettings`.
- Catch only the exceptions you can describe accurately. No catch-all handlers outside `Program.Main`.

## Commit messages

- Past tense: "Added", "Removed", "Moved" - not "Add"/"Remove"/"Move".
- Each `-` bullet on a single line. Shorten the wording rather than wrapping it.

## Workflow

- Do not run `dotnet build`/`run`/`test` after every small change. Batch small edits and verify once, or not at all when the change is trivially safe.
- Build for substantial changes only: a new class, a refactor across several methods or files, or a behaviour change worth verifying.

## Documents

- Plan and design documents hold decisions and actions only. Rationale belongs in the conversation.
- Do not describe things that can be read from the code. Descriptions go stale; conventions do not.
