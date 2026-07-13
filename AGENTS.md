# Working on HomeOps

HomeOps is a Windows-first .NET CLI that gives humans and agents constrained,
audited access to this homelab. Keep the credential boundary intact: improve the
tool rather than bypassing it.

## Safety rules

- Use `homeops` for authenticated Proxmox, Terraform, and Ansible operations.
- Never print, inspect, log, or commit credentials from Windows Credential
  Manager, environment variables, Terraform state, vault files, or private keys.
- Never add general-purpose secret, environment, shell, or raw execution
  commands. Commands such as `secret get`, `env`, `shell`, `exec`, and provider
  passthroughs are intentionally out of scope.
- Preserve redaction in command output and audit records. Tests must use
  synthetic credentials only.
- Proxmox inspection commands must remain read-only. Treat Terraform and Ansible
  apply operations as high risk and retain their confirmation controls.
- Do not weaken TLS verification. Fix endpoint names or certificate trust instead.

## Development workflow

1. Run `git status --short` and preserve unrelated worktree changes.
2. Read `skills/homeops/SKILL.md` before credential-backed operations.
3. Run `homeops doctor` before authenticated validation.
4. Make focused changes under `src/HomeOps.Cli` and add regression coverage under
   `tests/HomeOps.Tests`.
5. Run `dotnet test` and `git diff --check` before committing.
6. For Proxmox changes, validate with a read-only command such as
   `homeops proxmox nodes`. Do not replace it with direct API calls.
7. For Terraform, prefer `plan --out` followed by applying the saved plan. For
   Ansible, prefer `syntax`, then `check`, then a narrowly limited `apply`.

Configuration files are safe to commit but must never contain secrets. Credentials
are entered with `homeops login` from a trusted local terminal and stored in
Windows Credential Manager.

## Build and release

Keep the version in `src/HomeOps.Cli/HomeOps.Cli.csproj` and `Program.cs` aligned.
After making changes, run `dotnet test` and `dotnet publish` to publish to the users environment.
For a local Windows release:

```powershell
dotnet test
dotnet publish src/HomeOps.Cli -c Release -r win-x64 --self-contained false /p:PublishSingleFile=true
Copy-Item src/HomeOps.Cli/bin/Release/net10.0/win-x64/publish/homeops.exe `
  $env:LOCALAPPDATA/Programs/homeops/homeops.exe -Force
homeops --version
homeops doctor
```

After installation, run the relevant safe command through the installed binary to
verify that the published artifact—not merely the development build—works.
