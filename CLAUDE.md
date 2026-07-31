## Code

- No `Async` suffix on async method names. `GetInstalledModels`, not `GetInstalledModelsAsync`.
- Every method has a braced body. Never use expression-bodied methods (`=>`).
- Keep statements and signatures on one line, unless the line is genuinely long or the signature has many parameters.
- Compare strings with `==` and `!=`. Never `string.Equals`, and no `ToLower`/`ToUpper` unless genuinely necessary.
- Every enum declares `Unknown` first, so the default value is never a real state.
- A `return` statement never contains a method call. Assign the result to a variable first, then return it.
- Only 9.x NuGet packages.
- Prefer `GetFromJsonAsync`/`ReadFromJsonAsync` over reading streams and calling `JsonSerializer` by hand.
- Keep methods stateless: return a value rather than mutating another object, and pass what a method needs explicitly.
- Values that are not genuinely configurable belong in `Constants`, not `AppSettings`.
- Catch only the exceptions you can describe accurately. No catch-all handlers outside `Program.Main`.

## Commit messages

- Past tense: "Added", "Removed", "Moved" - not "Add"/"Remove"/"Move".
- Each `-` bullet on a single line. Shorten the wording rather than wrapping it.

## Workflow

- Read `PLAN.md` before answering what is done or what is next.
- Update `PLAN.md` with the latest project state on every change that affects the plan.
- Do not run `dotnet build`/`run`/`test` after every small change. Batch small edits and verify once, or not at all when the change is trivially safe.
- Build for substantial changes only: a new class, a refactor across several methods or files, or a behaviour change worth verifying.
- Never commit without confirming first. Reverting, editing, or building a change is not a request to commit it.
- Commit all pending changes in one commit, even unrelated ones, unless told otherwise.
- Always push after committing. Do not wait to be asked. This applies only to commits already asked for; it is not licence to create one.
- Do not prefix shell commands with `cd` or `Set-Location`. Use absolute paths where a path is needed.

## Documents

- Plan and design documents hold decisions and actions only. Rationale belongs in the conversation.
- Do not describe things that can be read from the code. Descriptions go stale; conventions do not.
- Promote anything durable from a plan-mode plan into `PLAN.md`.
