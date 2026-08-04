## Code

- No `Async` suffix on async method names. `GetInstalledModels`, not `GetInstalledModelsAsync`.
- Every method has a braced body. Never use expression-bodied methods (`=>`).
- Keep statements on one line, unless the line is genuinely long (roughly over 150-170 characters); past that point, split it structurally. Log calls and method signatures stay on one line no matter the length.
- Compare strings with `==` and `!=`. Never `string.Equals`, and no `ToLower`/`ToUpper` unless genuinely necessary.
- Don't use `string.Compare`/`string.CompareOrdinal` unless a case genuinely requires it.
- Every enum declares `Unknown` first, so the default value is never a real state.
- Don't inline a non-trivial or `await`ed call into a `return` — assign it to a variable first. Do return a simple synchronous expression (member/null-conditional chain, interpolation, comparison, trivial call like `FirstOrDefault()`) directly, never through a throwaway variable.
- Only 9.x NuGet packages.
- Prefer `GetFromJsonAsync`/`ReadFromJsonAsync` over reading streams and calling `JsonSerializer` by hand.
- Deserialize JSON into typed models (`GetFromJsonAsync`/`JsonSerializer.Deserialize<T>`); never navigate a `JsonDocument`/`JsonNode`/`JsonElement` by hand.
- Keep methods stateless: return a value rather than mutating another object, and pass what a method needs explicitly.
- Private helpers stay instance methods, never `static`, unless the whole class is `static`.
- Every hardcoded line or constant lives in the project's `Constants` file, except genuinely configurable values, which live in `appsettings.json`.
- Do not enable nullable reference types (`<Nullable>enable</Nullable>`). Leave it off in every project.
- Catch only the exceptions you can describe accurately. No catch-all handlers outside `Program.Main`.

## Commit messages

- Past tense: "Added", "Removed", "Moved" - not "Add"/"Remove"/"Move".
- Each `-` bullet on a single line. Shorten the wording rather than wrapping it.
- For new files, state the end result, not the steps that built it. Narrate steps only for edits to tracked files.

## Workflow

- Explain the intended approach before starting any implementation; no waiting for approval, but the explanation always comes first.
- Read `PLAN.md` before answering what is done or what is next.
- Update `PLAN.md` with the latest project state on every change that affects the plan.
- Do not run `dotnet build`/`run`/`test` after every small change. Batch small edits and verify once, or not at all when the change is trivially safe.
- Build for substantial changes only: a new class, a refactor across several methods or files, or a behaviour change worth verifying.
- Never commit or push without an explicit request in that message ("commit"/"push"). Reverting, editing, building, verifying, or agreeing on what a commit would contain is not a request to commit it — wait for the direct instruction.
- Commit all pending changes in one commit, even unrelated ones, unless told otherwise.
- Always push after committing. Do not wait to be asked. This applies only to commits already asked for; it is not licence to create one.
- Do not prefix shell commands with `cd` or `Set-Location`. Use absolute paths where a path is needed.

## Documents

- Plan and design documents hold decisions and actions only. Rationale belongs in the conversation.
- Do not describe things that can be read from the code. Descriptions go stale; conventions do not.
- Promote anything durable from a plan-mode plan into `PLAN.md`.
