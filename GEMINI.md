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
