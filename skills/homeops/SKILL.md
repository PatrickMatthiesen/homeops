---
name: homeops
description: "Use when Codex needs to operate this homelab through the local `homeops` CLI: inspect Proxmox, run Terraform fmt/validate/plan/apply, run Ansible syntax/check/apply through WSL, diagnose credential/tool readiness with doctor, or help an agent perform authenticated infrastructure work without reading raw credentials. Trigger for requests involving homeops, Proxmox inventory, homelab infrastructure operations, Terraform or Ansible runs mediated by the homeops credential broker, plan/apply workflows, or safe agent infrastructure automation."
---

# homeops

Use `homeops` as the authenticated operations harness for this homelab. It lets an agent use infrastructure credentials as capabilities without exposing the underlying secret values.

## Safety Model

- Treat `homeops` as the only credential-backed path for Proxmox, Terraform, and Ansible operations in this repository.
- Never ask the user for raw Proxmox tokens, Terraform provider secrets, Ansible vault passwords, SSH passphrases, or private key material.
- Never try to read Windows Credential Manager entries, temporary vault files, environment dumps, Terraform state, or other secret-bearing files unless the user explicitly asks for a security investigation.
- Do not invent forbidden commands. `homeops secret get`, `homeops env`, `homeops shell`, `homeops exec`, `homeops terraform raw`, and `homeops ansible raw` are intentionally unavailable.
- Prefer JSON output because `homeops` defaults to agent-friendly JSON. Add `--text` only when the user wants human-readable terminal output.

## First Checks

Run this before authenticated operations or when the user reports setup trouble:

```powershell
homeops doctor
```

Use the result to report:

- Whether Terraform is available on Windows.
- Whether WSL is configured for Ansible.
- Whether required credentials are present, without exposing values.
- Which repo roots and audit paths `homeops` is using.

If credentials are missing, tell the user to run `homeops login` from a trusted local shell. Do not ask them to paste secrets into chat.

## Proxmox Inspection

Use read-only Proxmox commands for inventory and capacity checks:

```powershell
homeops proxmox status
homeops proxmox nodes
homeops proxmox vms
homeops proxmox storage
```

Summarize the JSON result for the user. Do not fall back to direct Proxmox API calls unless `homeops` is broken and the user explicitly approves another approach.

## Terraform Workflow

Use Terraform through `homeops`, not raw `terraform`, when provider credentials are needed:

```powershell
homeops terraform fmt --check
homeops terraform validate <target>
homeops terraform plan <target> --out
homeops terraform apply <target> --plan-id <plan-id> --yes
```

Guidelines:

- Prefer `plan --out` before apply so there is a controlled plan artifact.
- Use `--yes` only when the user has asked the agent to complete the operation autonomously or when continuing an already approved workflow.
- Read the returned `riskLevel`, `summary`, `stdout`, `stderr`, and `auditEventId`.
- If `riskLevel` is `high`, call that out plainly in the user update or final response.
- If the command returns a plan id in the subject or summary, reuse that exact id for apply.

## Ansible Workflow

Use Ansible through `homeops`, which runs `ansible-playbook` in the configured WSL distro and manages the vault password temp file:

```powershell
homeops ansible syntax <playbook>
homeops ansible check <playbook> --limit <host-or-group>
homeops ansible apply <playbook> --limit <host-or-group> --yes
```

Guidelines:

- Prefer `syntax` then `check` before `apply`.
- Use `--limit` for targeted fixes unless the user explicitly wants a broad run.
- If WSL or Ansible is unavailable, use `homeops doctor` and report the setup gap.
- Do not run raw `ansible-playbook` for credential-backed playbooks.

## Reporting

When reporting results, include:

- Command category and target/playbook.
- Exit code.
- Risk level.
- Short redacted summary.
- Audit event id when present.

Do not include raw secrets. If output contains `[REDACTED]`, preserve that marker and do not try to reconstruct what was hidden.
