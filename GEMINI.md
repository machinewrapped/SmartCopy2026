# Agent working guidelines for SmartCopy2026
@AGENTS.md

# Gemini-specific rules

## Handling `dotnet build` compilation output
When building the project using `dotnet build` in a console session, the MSBuild Terminal Logger uses ANSI escape sequences and carriage return `\r` characters to overdraw its progress bar. This mangles the captured output for the agent pipeline, hiding compilation errors and producing gibberish text.

### The Solution:
To cleanly read build errors directly in the console output, force the environment into non-interactive mode or explicitly turn off the terminal logger. 

Use one of the following approaches:

1. **Claude Code's solution**
   ```powershell
   dotnet build SmartCopy.UI/SmartCopy.UI.csproj 2>&1 | tail -40
   ```

2. **Use minimal/error-only logger flags**:
   ```powershell
   dotnet build /tl:off /clp:ErrorsOnly
   ```

3. **Pipe to `Out-String`** (forces MSBuild into redirected mode):
   ```powershell
   dotnet build | Out-String
   ```
     
**Rule:** Always use one of these approaches to gather `dotnet build` output natively rather than writing temporary files in the workspace.

## Committing Changes and Temporary Files

When working on tasks, it is common to create temporary files for logs, API responses (e.g. from `gh pr view`), or diagnostics.

**Rule:** 
1. **Never** commit temporary diagnostic/log files to the repository. Always review `git status` or `git diff` carefully before running `git commit -a` or `git add .`.
2. **Require User Validation:** Before committing any changes to the repository, you **must** ask for the user's validation and explicit approval. Do not auto-commit changes without prior consent.
