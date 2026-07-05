# HomeOps

HomeOps is a Windows-first .NET CLI for running homelab operations through a
small, auditable command surface instead of handing raw infrastructure
credentials to a shell, script, or agent. It also makes Ansible practical from
Windows by running playbooks through WSL behind a normal PowerShell-friendly
`homeops ansible ...` command.

It is built for the awkward but common situation where a human wants help with a
home lab: inspect Proxmox, plan Terraform, run Ansible, maybe let an AI agent do
some of that work, but still keep the credential boundary intact. HomeOps gives
the operator useful capabilities without becoming a generic secret reader,
environment dumper, or remote shell.

## The problem

Infrastructure tools usually expect credentials to be available as environment
variables, config files, prompt input, private keys, or vault passwords. That is
fine for a trusted human at a terminal, but it is a poor fit for agent-assisted
operations:

- credentials can leak into chat, logs, command output, shell history, or state
  files;
- broad shell access makes it hard to prove what an agent could or could not
  read;
- Ansible does not run natively as a supported control node on Windows, which
  leaves Windows-first operators juggling WSL commands, path translation, vault
  password files, and inventory paths by hand;
- Terraform and Ansible are powerful enough that "just run the tool" is often
  too large a permission boundary;
- post-run audit trails are usually scattered across terminal scrollback,
  tool-specific files, and memory.

HomeOps narrows the interface. Credentials live in Windows Credential Manager,
and the CLI exposes only specific operations that are useful for this homelab.
Outputs and audit records are redacted before they leave the tool.

## What it can do

HomeOps currently supports:

- local credential bootstrap with `homeops login` and cleanup with
  `homeops logout`;
- readiness checks with `homeops doctor`;
- read-only Proxmox inspection:
  - `homeops proxmox status`
  - `homeops proxmox nodes`
  - `homeops proxmox vms`
  - `homeops proxmox storage`
  - `homeops proxmox storage-content`
- Terraform workflows on Windows:
  - `homeops terraform fmt --check`
  - `homeops terraform validate <target>`
  - `homeops terraform plan <target> --out`
  - `homeops terraform apply <target> --plan-id <plan-id> --yes`
- Ansible workflows from Windows through WSL:
  - `homeops ansible syntax <playbook>`
  - `homeops ansible check <playbook> --limit <host-or-group>`
  - `homeops ansible apply <playbook> --limit <host-or-group> --yes`
  - `homeops ansible vault edit`
- JSON-first output for agents, plus `--text` for compact human output;
- redacted audit events containing command category, subject, exit code, risk
  level, git state, and summaries.

## What it intentionally cannot do

HomeOps is deliberately not a general-purpose credential broker. It does not
provide commands such as:

- `secret get`
- `env`
- `shell`
- `exec`
- raw Terraform provider passthroughs
- raw Ansible passthroughs

Those commands would make it easy to extract credentials or escape the audited
operation model. If a new workflow is needed, the preferred approach is to add a
narrow HomeOps command for that workflow, with redaction and audit behavior
built in.

## How it works

HomeOps keeps three ideas separate:

1. **Configuration is safe to commit.** `homeops.json`, `homeops.yaml`, and
   `homeops.yml` contain paths, target catalogs, runner settings, and feature
   flags. They must not contain secrets.
2. **Credentials stay local.** `homeops login` stores credentials in Windows
   Credential Manager under internal `homeops:*` keys.
3. **Commands expose capabilities, not raw secrets.** When a tool needs a
   credential, HomeOps injects it only into the child process or temporary file
   required for that operation, then redacts output and writes an audit record.

For Proxmox, HomeOps uses two separate API tokens:

- an inspection token for read-only inventory and status commands;
- a Terraform token for VM/storage operations assigned to Terraform.

For Terraform, HomeOps runs Terraform on Windows and injects provider
credentials into the Terraform process environment.

For Ansible, HomeOps turns a Windows command into a WSL-backed
`ansible-playbook` run. It resolves Windows paths, calls the configured WSL
distro, stages vault and become passwords as temporary files for the child
process, and removes those files during cleanup. `homeops ansible vault edit`
keeps the edit flow on the encrypted vault file and verifies that the result
remains Ansible Vault encrypted before replacing the original.

## Quick start

Build and run from source:

```powershell
dotnet build
dotnet run --project src/HomeOps.Cli -- doctor
```

Publish a local Windows binary:

```powershell
dotnet publish src/HomeOps.Cli -c Release -r win-x64 --self-contained false /p:PublishSingleFile=true
```

Install it somewhere on `PATH`, then run:

```powershell
homeops login
homeops doctor
```

`homeops doctor` checks local tool readiness, configured paths, required
credentials, read-only Proxmox access, and Terraform token permissions. It does
not run Terraform plans or apply infrastructure changes.

For a fuller setup walkthrough, see [docs/install.md](docs/install.md).

## Configuration

The default config file is [homeops.json](homeops.json). A config can describe:

- the infrastructure repository root;
- Proxmox feature checks such as cloud-image download permissions;
- Terraform target directories and named targets;
- where saved Terraform plan artifacts should be stored;
- the WSL distro and inventory path used for Ansible;
- named Ansible playbooks and default limits;
- where redacted audit events are written.

Example:

```json
{
  "schemaVersion": 1,
  "infrastructureRepo": ".",
  "proxmox": {
    "features": {
      "cloudImageDownloads": false
    }
  },
  "terraform": {
    "targetsRoot": "terraform",
    "planArtifactDir": ".homeops/plans"
  },
  "ansible": {
    "playbooksRoot": "ansible",
    "wslDistro": "Ubuntu",
    "inventoryPath": "ansible/inventory"
  },
  "audit": {
    "logDir": ".homeops/audit"
  }
}
```

The `.homeops/` directory is ignored by Git because it contains local audit
records and saved plan artifacts.

## Typical workflows

Inspect before changing:

```powershell
homeops doctor
homeops proxmox nodes
homeops proxmox vms
homeops proxmox storage
```

Plan Terraform before applying:

```powershell
homeops terraform fmt --check
homeops terraform validate sample-app
homeops terraform plan sample-app --out
homeops terraform apply sample-app --plan-id 20260705213000-sample-app --yes
```

Check Ansible before applying:

```powershell
homeops ansible syntax site.yml
homeops ansible check site.yml --limit new-host
homeops ansible apply site.yml --limit new-host --yes
```

Use privilege escalation only when the playbook needs it:

```powershell
homeops ansible check site.yml --limit new-host --become
homeops ansible apply site.yml --limit new-host --become --yes
```

## Ansible from Windows

Ansible is not supported as a native Windows control node. The usual workaround
is to install Ansible in WSL and then remember the right `wsl.exe` incantation,
Linux paths, inventory path, vault password file, and cleanup steps every time
you run a playbook.

HomeOps wraps that pattern in a Windows-native command surface:

```powershell
homeops ansible syntax site.yml
homeops ansible check site.yml --limit new-host
homeops ansible apply site.yml --limit new-host --yes
```

Under the hood, HomeOps runs Ansible inside the configured WSL distro, translates
the relevant Windows paths to WSL paths, stages vault and optional become
passwords safely, and keeps the audit/redaction behavior consistent with the
rest of the CLI. From PowerShell, it feels like a normal Windows tool; inside
WSL, Ansible still runs in its supported Linux environment.

## Proxmox setup

Create Proxmox API identities from a root shell on a Proxmox VE node. The
examples use token privilege separation, so the owning user's ACL is the ceiling
and the token must also have its own ACL. Grant both principals the intended
role; the token remains independently constrained.

### Read-only inspection user

```bash
pveum user add homeops-inspect@pve --comment "HomeOps read-only inspection"
pveum user token add homeops-inspect@pve homeops --privsep 1
pveum acl modify / --users 'homeops-inspect@pve' --roles PVEAuditor --propagate 1
pveum acl modify / --tokens 'homeops-inspect@pve!homeops' --roles PVEAuditor --propagate 1
```

The token creation command prints its secret once. Save the complete token in
this form for `homeops login`:

```text
homeops-inspect@pve!homeops=TOKEN_SECRET
```

`PVEAuditor` is sufficient for the read-only `homeops proxmox` commands. Do not
grant this identity a write-capable role.

### Terraform user

Create a second identity so Terraform access is not shared with inspection:

```bash
pveum user add homeops-terraform@pve --comment "HomeOps Terraform automation"
pveum user token add homeops-terraform@pve terraform --privsep 1
```

Grant only the resource paths Terraform manages:

```bash
VM_STORAGE='vm-storage'

pveum acl modify / --users 'homeops-terraform@pve' --roles PVEAuditor --propagate 1
pveum acl modify / --tokens 'homeops-terraform@pve!terraform' --roles PVEAuditor --propagate 1
pveum acl modify /vms --users 'homeops-terraform@pve' --roles PVEVMAdmin --propagate 1
pveum acl modify /vms --tokens 'homeops-terraform@pve!terraform' --roles PVEVMAdmin --propagate 1
pveum acl modify "/storage/$VM_STORAGE" --users 'homeops-terraform@pve' --roles PVEDatastoreAdmin --propagate 1
pveum acl modify "/storage/$VM_STORAGE" --tokens 'homeops-terraform@pve!terraform' --roles PVEDatastoreAdmin --propagate 1
```

For multiple VM storage targets, add one storage ACL per target. Prefer narrow
ACLs for pools, SDN zones, and other resources instead of granting
`Administrator` at `/`.

Save the Terraform token in this form for `homeops login`:

```text
homeops-terraform@pve!terraform=TOKEN_SECRET
```

Never put either token in this repository, a configuration file, shell history,
or chat. Enter both directly when `homeops login` prompts from a trusted Windows
terminal.

### Optional Proxmox permissions

Only grant these when the Terraform configuration needs them.

Cloud-image downloads require `Sys.AccessNetwork` on the target node and
`Datastore.AllocateTemplate` on the image storage. Enable the feature explicitly
so Doctor checks those permissions:

```json
{
  "proxmox": {
    "node": "example-node",
    "imageStorage": "image-store",
    "features": {
      "cloudImageDownloads": true
    }
  }
}
```

Then create narrowly scoped roles:

```bash
IMAGE_STORAGE='image-store'
PROXMOX_NODE='example-node'

pveum role add HomeOpsImageStore --privs 'Datastore.AllocateSpace Datastore.AllocateTemplate Datastore.Audit'
pveum role add HomeOpsImageDownload --privs 'Sys.AccessNetwork'

pveum acl modify "/storage/$IMAGE_STORAGE" --users 'homeops-terraform@pve' --roles HomeOpsImageStore --propagate 0
pveum acl modify "/storage/$IMAGE_STORAGE" --tokens 'homeops-terraform@pve!terraform' --roles HomeOpsImageStore --propagate 0
pveum acl modify "/nodes/$PROXMOX_NODE" --users 'homeops-terraform@pve' --roles HomeOpsImageDownload --propagate 0
pveum acl modify "/nodes/$PROXMOX_NODE" --tokens 'homeops-terraform@pve!terraform' --roles HomeOpsImageDownload --propagate 0
```

If Terraform uses Proxmox SDN, grant `PVESDNUser` only on the specific zone:

```bash
SDN_ZONE='example-zone'
pveum acl modify "/sdn/zones/$SDN_ZONE" --users 'homeops-terraform@pve' --roles PVESDNUser --propagate 1
pveum acl modify "/sdn/zones/$SDN_ZONE" --tokens 'homeops-terraform@pve!terraform' --roles PVESDNUser --propagate 1
```

## Agent usage

HomeOps defaults to JSON because agents can consume structured output reliably:

```powershell
homeops proxmox nodes
homeops terraform plan sample-app --out
homeops ansible check site.yml --limit new-host
```

Use `--text` when a human wants compact terminal output.

For agent workflows, the important rule is simple: improve HomeOps instead of
bypassing it. If an operation needs credentials, add or use a constrained
HomeOps command rather than reading credential stores, exporting environment
variables, opening raw shells, or calling provider APIs directly.

## Development

Run the test suite:

```powershell
dotnet test
```

Before publishing or committing release changes, also run:

```powershell
git diff --check
```

For a local Windows release:

```powershell
dotnet test
dotnet publish src/HomeOps.Cli -c Release -r win-x64 --self-contained false /p:PublishSingleFile=true
Copy-Item src/HomeOps.Cli/bin/Release/net10.0/win-x64/publish/homeops.exe `
  $env:LOCALAPPDATA/Programs/homeops/homeops.exe -Force
homeops --version
homeops doctor
```

After installation, verify the installed binary with a safe command such as:

```powershell
homeops proxmox nodes
```
