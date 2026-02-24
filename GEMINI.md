# Agent working guidelines for SmartCopy2026
@AGENTS.md

# Gemini-specific rules

## Handling `dotnet build` compilation output
When building the project using `dotnet build` in a console session, the MSBuild Terminal Logger (and sometimes the default console logger) uses ANSI escape sequences and carriage return `\r` characters to maintain and overdraw its progress bar. This behaviour severely mangles the captured output string for the agent pipeline, hiding the actual compilation errors and causing gibberish text overlapping (e.g., `0 Warning(s)ithub\SmartCopy...`).

Previously, this was worked around by redirecting output into log files (`dotnet build > build.log`) because redirection forces MSBuild into non-interactive mode which produces readable text. However, **this pollutes the workspace with stray log files**.

### The Solution:
To cleanly read build errors directly in the console output without leaving files behind, force the environment into non-interactive mode or explicitly turn off the terminal logger. Use one of the following approaches:

1. **Pipe to `Out-String`** (forces MSBuild into redirected mode):
   ```powershell
   dotnet build | Out-String
   ```
   
2. **Use minimal/error-only logger flags**:
   ```powershell
   dotnet build /tl:off /clp:ErrorsOnly
   ```
   
**Rule:** Always use one of these commands to gather `dotnet build` errors natively rather than writing temporary `.log` files into the source directory.
