# homeops v1

`homeops` is a Windows-first operations CLI for letting humans and agents run
authenticated homelab infrastructure actions without exposing raw credentials.

## Install

Build from the repository:

```powershell
dotnet build
dotnet run --project src/HomeOps.Cli -- doctor
```

Publish a local single-file binary:

```powershell
dotnet publish src/HomeOps.Cli -c Release -r win-x64 --self-contained false /p:PublishSingleFile=true
```

## Configuration

`homeops.json`, `homeops.yaml`, and `homeops.yml` are intentionally safe to
commit. They contain paths, catalog metadata, and runner settings only:

- `infrastructureRepo`: root of the infrastructure repository.
- `proxmox.features.cloudImageDownloads`: opt-in check for Proxmox URL-download
  permissions; defaults to `false`.
- `proxmox.node`: target node when cloud-image downloads are enabled.
- `proxmox.imageStorage`: image storage when cloud-image downloads are enabled.
- `terraform.targetsRoot`: directory containing Terraform targets.
- `terraform.planArtifactDir`: controlled Terraform plan output directory.
- `terraform.targets`: optional named targets with `path`, `description`, and
  `host`.
- `ansible.playbooksRoot`: directory containing playbooks.
- `ansible.wslDistro`: WSL distro used to run Ansible.
- `ansible.inventoryPath` or `ansible.inventory`: Ansible inventory path.
- `ansible.playbooks`: optional named playbooks with `path`, `description`, and
  `default_limit`.
- `audit.logDir`: redacted audit-record directory.

Do not put secrets in the homeops config file.

## Credentials

Run once from a trusted local shell:

```powershell
homeops login
```

Credentials are stored in Windows Credential Manager under internal `homeops:*`
keys. The CLI never implements `secret get`, `env`, `shell`, or arbitrary
`exec`, because those would turn the tool into a credential extraction path.

## Terraform

Terraform runs directly on Windows:

```powershell
homeops terraform fmt --check
homeops terraform validate sample-app
homeops terraform plan sample-app --out
homeops terraform apply sample-app --plan-id 20260602194500-sample-app --yes
```

Terraform credentials are injected only into the Terraform child process.
Output and audit records are redacted before they are printed or persisted.

## Ansible and WSL

Ansible runs through WSL:

```powershell
wsl.exe --install Ubuntu
```

Install Ansible inside the configured distro, then verify:

```powershell
wsl.exe -d Ubuntu ansible --version
homeops doctor
```

When running Ansible, `homeops` writes the vault password to a temporary file,
passes that file only to the child process, and deletes it in cleanup.

## Agent Usage

The CLI defaults to JSON for agents:

```powershell
homeops proxmox nodes
homeops terraform plan project-vm --out
homeops ansible check site.yml --limit new-vm
```

Use `--text` for compact human output.

High-risk applies are allowed with `--yes` so agents can complete useful work
while the human is away. Risk level, dirty-worktree status, command category,
subject, exit code, and redacted summary are written to the audit log.
