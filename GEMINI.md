# Agent working guidelines for SmartCopy2026
@AGENTS.md

# Gemini-specific rules

## Handling `dotnet build` compilation output
When building the project using `dotnet build` in a console session, the MSBuild Terminal Logger uses ANSI escape sequences and carriage return `\r` characters to overdraw its progress bar. This mangles the captured output for the agent pipeline, hiding compilation errors and producing gibberish text.

### The Solution:
To cleanly read build errors directly in the console output, force the environment into non-interactive mode or explicitly turn off the terminal logger. Try the following command:

   ```powershell
   dotnet build /tl:off /clp:ErrorsOnly | Out-String
   ```
     
**Rule:** Always attempt to gather dotnet build output natively using the command above. Do not write temporary files to the workspace. If you are still unable to read the console output, ask the user for help by providing the command you ran and requesting they provide the output.

## Committing Changes and Temporary Files

When working on tasks, it is common to create temporary files for logs, API responses (e.g. from `gh pr view`), or diagnostics.

**Rule:** 
1. **Never** commit temporary diagnostic/log files to the repository. Always review `git status` or `git diff` carefully before running `git commit -a` or `git add .`.
2. **Require User Validation:** Before committing any changes to the repository, you **must** ask for the user's validation and explicit approval. Do not auto-commit changes without prior consent.
