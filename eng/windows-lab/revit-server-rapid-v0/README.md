# Revit Server 2027 rapid-setup lab configuration

This repository-owned Dockur configuration is exclusively for the disposable Windows Server 2022
Desktop Experience lab in issues #114-#116. Read `docs/windows-development-lab.md` before use.

`compose.bootstrap.yaml` supplies the temporary outbound-only preparation network.
`compose.acceptance.yaml` supplies the final Docker-internal network and is the only overlay allowed
for #114 Ready/Blocked evidence. Use `manage.sh`; it prevents attaching both networks together and
preserves the bind-mounted system and data disks across container recreation.

Provide `BALLS_REVIT_SERVER_PASSWORD` only in the invoking shell or another owner-controlled secret
source at `/home/scwlkr/.config/balls-labs/revit-server-2027/private.env`, mode `0600`. Never place it
in this directory, command output, evidence, or Git.

The configuration mounts only its isolated system and data storage. It intentionally has no
`/shared` host mount, so no Linux shared folder can reach `D:\RevitServer`.
