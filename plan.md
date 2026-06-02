# homeops CLI Implementation Plan

`homeops` is a local-first operations CLI for homelab infrastructure work. Its
job is to let humans and coding agents run authenticated infrastructure actions
without exposing raw credentials to the agent, the chat transcript, logs, or
ordinary command output.

The goal is not to make the agent powerless. The agent should be able to help
with real infrastructure work: inspect Proxmox, edit infrastructure code, run
Terraform, run Ansible, debug failures, and deploy new VMs or services while the
human is away. `homeops` exists because the normal CLI authentication model
usually requires secrets in environment variables, prompt input, config files,
or temporary files that are too easy for an agent to read.

## Landscape

Several adjacent tools already exist:

- General AI credential brokers and gateways, such as Hermetic and OneCLI,
  broker API requests so agents can call services without seeing raw keys.
- Agent-aware secret managers, such as PassBox and Tene, store secrets in an
  encrypted vault and inject them into processes at runtime.
- Enterprise automation systems, such as AWX or Ansible Automation Platform,
  attach credentials to job templates and run automation without exposing the
  credential values to ordinary users.
- Terraform Cloud and similar runners can store sensitive variables, but
  Terraform configurations and provisioners can still leak secrets through
  shell output if they execute arbitrary commands.

`homeops` is intentionally more specific than these tools. It is not a generic
secret manager, generic API proxy, or enterprise automation platform. It is a
small local operations harness for this homelab: Proxmox inspection, Terraform,
Ansible, SSH-based deployment, redacted output, and an audit trail.

## Product Contract

`homeops` provides commands for:

- Reading Proxmox inventory and capacity.
- Running Terraform formatting, validation, plan, and apply workflows.
- Running Ansible syntax, check, and apply workflows.
- Managing local credential bootstrap through a login/logout flow.
- Returning machine-readable output suitable for agents.
- Recording redacted audit events for privileged operations.

`homeops` does not manage application deployments directly unless those
deployments are represented by the infrastructure repository's Terraform or
Ansible workflows.

## Core Security Model

`homeops` is a credential-hiding capability broker.

The caller may be a human or an agent. The caller can choose supported
operations, see redacted command output, inspect exit codes, and reason over
machine-readable results. The caller must not be able to directly read:

- Proxmox API tokens.
- Terraform provider tokens.
- Ansible vault passwords.
- SSH private key material or passphrases.
- Environment dumps containing secrets.
- Temporary credential files.

The CLI runs on a trusted local machine under the authenticated user account.
Credentials are available to `homeops`, but are not returned as plaintext. When
Terraform or Ansible requires credentials, `homeops` injects them only into the
child process for the duration of the command and redacts known secret material
from all captured output.

This model reduces secret disclosure. It does not make Terraform or Ansible
safe to run against untrusted code. Infrastructure-as-code can execute local
commands, invoke providers, change remote systems, and store values in state.
That is acceptable for this project because the agent is meant to perform real
infrastructure work. The boundary is "the agent may use the capability" rather
than "the agent may read the credential."

## Design Principles

- Keep the agent useful. Do not over-constrain infrastructure changes up front.
- Hide credentials as data. A command may use a credential, but must not print,
  return, list, or export it.
- Prefer operational capabilities over raw secret access.
- Keep destructive actions visible and auditable.
- Use confirmations for high-risk actions, not for every ordinary operation.
- Treat redaction as defense-in-depth, not as the only protection.
- Fail closed on credential-store, redaction, or temp-file cleanup errors.

## Credential Storage

Initial backend:

- Windows Credential Manager, protected by the current Windows user.

Planned abstraction:

```text
CredentialStore
  get(name) -> secret bytes
  set(name, secret bytes)
  delete(name)
  listMetadata()
```

The abstraction should allow later support for:

- Linux Secret Service.
- macOS Keychain.
- Explicit file-backed development backend for tests only.

The CLI must not store long-lived secrets inside the binary, repository, shell
profile, Terraform files, Ansible files, or agent-readable config.

## Credential Names

Initial logical credential keys:

```text
proxmox.endpoint
proxmox.inspect.token
proxmox.terraform.token
ansible.vault_password
ssh.deploy_key_path
ssh.deploy_key_passphrase
```

Credential keys are internal identifiers. The CLI should not provide a generic
`secret get` command for these values.

## Commands

Bootstrap:

```text
homeops login
homeops logout
homeops doctor
```

Proxmox inspection:

```text
homeops proxmox status [--json]
homeops proxmox nodes [--json]
homeops proxmox vms [--json]
homeops proxmox storage [--json]
```

Terraform:

```text
homeops terraform fmt [--check]
homeops terraform validate <target>
homeops terraform plan <target> [--json] [--out]
homeops terraform apply <target> [--plan-id id] [--yes]
```

Ansible:

```text
homeops ansible syntax <playbook>
homeops ansible check <playbook> [--limit host-or-group]
homeops ansible apply <playbook> [--limit host-or-group] [--yes]
```

## Commands Not Allowed

Do not implement:

```text
homeops secret get <name>
homeops env
homeops shell
homeops exec <arbitrary command>
homeops terraform raw ...
homeops ansible raw ...
```

These turn `homeops` into a credential extraction or privileged shell tool.

## Proxmox Client

Initial API needs:

- Node list.
- Node CPU and memory status.
- Storage list and usage.
- VM/container list and status.
- Cluster/resource summary if available.

Output should support JSON for agents and compact human tables for humans.

The inspection token should be read-only in Proxmox where possible. Terraform
and mutation-capable tokens should be separate from inspection credentials.

## Terraform Runner

The Terraform runner should:

- Run in the configured infrastructure repo path.
- Inject provider credentials only into the Terraform subprocess.
- Avoid passing secrets through command-line arguments.
- Redact known secret patterns from stdout, stderr, summaries, and JSON output.
- Support broad targets initially, while still constraining execution to
  Terraform subcommands.
- Keep plan artifacts in a controlled state directory.
- Record the repo commit and dirty-worktree status for each plan and apply.
- Prefer saved plan artifacts for apply when available.

The first implementation may be permissive about targets because usefulness
matters. It must not accept arbitrary shell commands.

For high-risk applies, `homeops` should require confirmation unless `--yes` is
explicitly provided by a trusted human workflow. High-risk signals include:

- Destroy actions.
- Resource replacements.
- Large numbers of changes.
- Dirty worktree at apply time.
- Applying without a saved plan artifact.

## Ansible Runner

The Ansible runner should:

- Run in the configured infrastructure repo path.
- Provide the vault password through a temporary vault-password file.
- Remove the temporary vault-password file after use.
- Avoid exposing the temp file path in agent-facing output when possible.
- Redact known secret patterns from stdout, stderr, summaries, and JSON output.
- Support syntax-check, check mode, and apply mode.
- Record the repo commit and dirty-worktree status for each check and apply.

The first implementation may accept any playbook path under the repo. It must
not accept arbitrary shell commands.

For high-risk applies, `homeops` should require confirmation unless `--yes` is
explicitly provided by a trusted human workflow. High-risk signals include:

- Applying against all hosts.
- Running with no limit for broad inventories.
- Dirty worktree at apply time.
- Playbooks outside expected infrastructure directories.

## Redaction

All subprocess output should pass through a redaction layer before being printed
or serialized.

Minimum redaction sources:

- Exact secrets loaded from the credential store.
- Terraform token-like values.
- Ansible vault password.
- SSH private key paths if considered sensitive.
- Common private key block markers.
- Environment-variable assignment patterns for known secret names.

Redaction should replace matches with `[REDACTED]`.

Redaction is a safety net. The implementation should also avoid putting secrets
in places that are easy to print, such as process arguments, persistent files,
debug logs, or generic environment dumps.

## Agent-Friendly Output

Inspection commands should support stable JSON.

Plan/check/apply commands should return:

- Exit code.
- Command category.
- Target or playbook.
- Risk level.
- Confirmation requirement, when applicable.
- Short summary.
- Full redacted stdout/stderr.
- Audit event id.

This lets an agent reason without scraping fragile terminal tables or asking for
raw credentials.

## Audit Trail

Privileged commands should write a local redacted audit record containing:

- Timestamp.
- User account.
- Command category.
- Target or playbook.
- Repo path.
- Repo commit.
- Dirty-worktree status.
- Plan artifact id, when applicable.
- Exit code.
- Risk level.
- Confirmation decision.
- Redacted summary.

The audit trail should never contain raw secrets or unredacted subprocess
output.

## Confirmation Model

Routine read-only and validation commands should run without confirmation.

Apply commands should be possible from an agent workflow, but high-risk applies
should pause with a concise machine-readable confirmation request. The human can
approve from the phone by sending the agent the required confirmation token or
by rerunning with an explicit trusted flag.

The confirmation model should be light enough that the tool remains pleasant for
homelab use.

## Implementation Milestones

1. Choose runtime and scaffold CLI project.
2. Add command parser and top-level help.
3. Implement credential store abstraction.
4. Implement Windows Credential Manager backend.
5. Implement `login`, `logout`, and `doctor`.
6. Implement Proxmox API client and JSON inspection commands.
7. Implement redaction pipeline.
8. Implement audit trail writer.
9. Implement Terraform runner.
10. Implement Terraform plan artifact handling.
11. Implement Ansible runner.
12. Add lightweight risk detection and confirmation prompts.
13. Add tests for command parsing, credential backend, redaction, and audit
    records.
14. Add install docs for Windows host and dev-container usage.
15. Add an agent skill that teaches the safe command workflow.

## Security Concerns

- Repo-controlled Terraform and Ansible can execute code. `homeops` reduces
  accidental secret exposure but cannot make arbitrary IaC code harmless.
- A local agent with full filesystem and process access may still abuse
  capabilities available to the authenticated user.
- Terraform state may contain sensitive values even when CLI output is redacted.
- Terraform provisioners or Ansible tasks can intentionally print environment
  variables unless the runner blocks, detects, or redacts them.
- The CLI should never log secrets, even in verbose/debug modes.
- Apply commands should be visibly intentional and harder to run accidentally
  than read-only inspection or check commands.
- Credential-store errors should fail closed.

## Open Decisions

- Runtime: Go, Rust, or .NET single-file binary.
- How dev containers call the Windows credential-backed CLI.
- How much Terraform target validation is useful before it becomes annoying.
- Whether `--yes` should be allowed for agents or only for trusted human shells.
- Where audit records and plan artifacts should live.
- Whether SSH should be handled through an agent protocol, temp key material, or
  direct subprocess integration.
