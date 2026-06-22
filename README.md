# HomeOps

HomeOps is a Windows-first .NET CLI for constrained, audited access to a
homelab. See [the installation guide](docs/install.md) for build, installation,
and login instructions.

## Create the Proxmox users

HomeOps uses two separate Proxmox API tokens:

- `homeops-inspect@pve` can only read inventory and status.
- `homeops-terraform@pve` can manage the VM and storage resources assigned to
  Terraform.

Create them from a root shell on a Proxmox VE node. The examples use privilege
separation, so the owning user's ACL is the ceiling and the token must also have
its own ACL. Grant both principals the intended role; the token remains
independently constrained.

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

Grant only the resource paths Terraform manages. Set these values to match
the storage used for VM disks; this default matches this repository:

```bash
VM_STORAGE='vm-storage'

pveum acl modify / --users 'homeops-terraform@pve' --roles PVEAuditor --propagate 1
pveum acl modify / --tokens 'homeops-terraform@pve!terraform' --roles PVEAuditor --propagate 1
pveum acl modify /vms --users 'homeops-terraform@pve' --roles PVEVMAdmin --propagate 1
pveum acl modify /vms --tokens 'homeops-terraform@pve!terraform' --roles PVEVMAdmin --propagate 1
pveum acl modify "/storage/$VM_STORAGE" --users 'homeops-terraform@pve' --roles PVEDatastoreAdmin --propagate 1
pveum acl modify "/storage/$VM_STORAGE" --tokens 'homeops-terraform@pve!terraform' --roles PVEDatastoreAdmin --propagate 1
```

For multiple VM storage targets, add one storage ACL per target. Prefer
similarly narrow ACLs for pools or other resources rather than granting
`Administrator` at `/`.

Save the Terraform token in this form for `homeops login`:

```text
homeops-terraform@pve!terraform=TOKEN_SECRET
```

### Additional permissions (optional)

Do not grant these unless the corresponding Terraform configuration needs
them.

#### Cloud-image downloads

Terraform only needs these permissions when Proxmox itself downloads cloud
images from URLs. Enable the feature explicitly so Doctor includes the cloud
checks:

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

This feature requires `Sys.AccessNetwork` on the target node and
`Datastore.AllocateTemplate` on the image storage. Create narrowly scoped roles
and assign them to both the user and token:

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

When `cloudImageDownloads` is false or omitted, Doctor does not list or require
these permissions.

#### Software-defined networking

If the Terraform configuration uses Proxmox SDN, also grant `PVESDNUser` on the
specific zone it uses; do not add this permission otherwise:

```bash
SDN_ZONE='localnetwork'
pveum acl modify "/sdn/zones/$SDN_ZONE" --users 'homeops-terraform@pve' --roles PVESDNUser --propagate 1
pveum acl modify "/sdn/zones/$SDN_ZONE" --tokens 'homeops-terraform@pve!terraform' --roles PVESDNUser --propagate 1
```

Never put either token in this repository, a configuration file, shell history,
or chat. Enter both directly when `homeops login` prompts from a trusted Windows
terminal; HomeOps stores them in Windows Credential Manager.

### Verify the setup

After entering the Proxmox endpoint and both tokens, verify the local setup and
the read-only identity:

```powershell
homeops login
homeops doctor
homeops proxmox nodes
homeops proxmox vms
homeops proxmox storage
homeops proxmox storage-content
homeops proxmox storage-content --content iso
```

As part of its diagnostics, `homeops doctor` only sends read-only `GET` requests
to Proxmox. It checks cluster resources, nodes, storage, and the QEMU and
container and storage-content inventory on every visible node with the
inspection token. It also reads the Terraform token's effective permissions and
verifies the baseline VM and storage privileges described above. Optional
feature permissions are only checked when their feature is enabled. Doctor
returns a non-zero exit code if either token is missing required access; it
never runs a plan or mutation.

`storage-content` lists the volumes visible to the read-only identity, including
ISO files, templates, VM disks, and other storage content, with their node and
storage context. Use `--content` for one Proxmox content type, such as `iso`,
`vztmpl`, `images`, `rootdir`, or `backup`. Results and object properties are
sorted for stable JSON output.

Validate Terraform access with a plan before any apply:

```powershell
$Target = "name-from-homeops-config"
homeops terraform plan $Target --out
```

If a plan reports a permission error, add only the missing privilege at the
narrowest applicable Proxmox path. Keep the inspection and Terraform users and
tokens separate.
