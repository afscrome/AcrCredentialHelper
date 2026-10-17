# Azure Container Registry credential helper for Docker

`docker-credential-acr` lets Docker authenticate to private Azure Container Registry
(ACR) images using your Microsoft Entra account. Sign in when prompted, then use
ordinary Docker commands such as `docker pull`—you do not need to run `docker login`
for each registry.

## Installation

Installation instructions are not available yet. Build and release pipelines,
published release artifacts, and step-by-step installation instructions are still
being prepared and will be added here.

## Get started

Once the helper is installed and available to Docker on your `PATH`, run:

```sh
docker-credential-acr install
```

The helper signs you in through the platform authentication broker, finds the ACR
registries you can access in the selected Entra tenant, and configures Docker to use
the helper for those registries. Your existing Docker configuration is preserved:
registries are only added to `credHelpers`. A registry that Docker already routes to
a different credential helper is left unchanged, and a warning is logged. To switch
it to this helper, edit or remove its entry in your Docker `config.json`. Registry
names keep any explicit port, such as `registry.example.com:5000`, because Docker
matches `credHelpers` entries by host and port.
The first sign-in may ask you to choose an account or complete your organization's
sign-in requirements, such as multifactor authentication.

If you need to select a particular tenant, provide its tenant ID or verified domain:

```sh
docker-credential-acr install --tenant contoso.onmicrosoft.com
```

Without `--tenant`, the helper uses `ACR_CREDENTIAL_HELPER_TenantId`, then
`AZURE_TENANT_ID`. When Docker requests credentials for a registry, the tenant of the
account remembered for that registry takes precedence over both settings.

Sign-in waits up to five minutes before failing. To change this, set
`ACR_CREDENTIAL_HELPER_AuthenticationTimeout` to a time span such as `00:10:00`.

After setup, pull an image as usual:

```sh
docker pull <registry>/<repository>:<tag>
```

For example:

```sh
docker pull myregistry.azurecr.io/my-app:latest
```

Docker will request credentials from the helper when it needs them. The helper first
tries to use the remembered account silently and prompts you to sign in again if
required. Run `docker-credential-acr install` again to discover registries created
since your last setup.

## Access and sign-in

- Your account needs permission to discover registries and permission to pull the
  images you use. Registry discovery does not grant image access; an appropriate ACR
  pull or repository-reader role is still required.
- The helper uses interactive Microsoft Entra broker sign-in. Azure CLI, PowerShell,
  workload identity, and unattended CI authentication are not supported.
- The helper remembers the selected account for each discovered registry. This is
  account information, not a saved password or access token.
- `docker logout <registry>` removes the helper's remembered account for that
  registry. It does not sign you out of the platform authentication broker or revoke
  tokens managed by the broker.
- Registries explicitly configured to use this helper are treated as ACR endpoints.
  Custom hostnames, including private-link names and CNAMEs, can be used if Docker is
  configured to route them to this helper.

## Troubleshooting

When troubleshooting a Docker command, increase the helper's log level through its
environment variable. Docker starts the credential helper for you, so you cannot
add helper command-line options to a `docker pull` or other Docker command. Set the
variable in the environment from which Docker runs:

```powershell
$env:ACR_CREDENTIAL_HELPER_Logging__LogLevel__Default = "Information"
docker pull <registry>/<repository>:<tag>
```

The helper reads `ACR_CREDENTIAL_HELPER_Logging__LogLevel__Default` and writes
diagnostic messages to stderr. The default level is `Warning`; use `Debug` for
detailed Azure SDK diagnostics.

For commands that invoke the helper directly, you can instead pass `--log-level`.
For example, to see more information while setting up Docker:

```sh
docker-credential-acr install --log-level Information
```

Use `docker-credential-acr --help` or `docker-credential-acr install --help` to see
available command options.

The helper discovers registries in the Azure public cloud. It can only find
registries that your account is allowed to read in the selected tenant.

## References

- [Docker credential helper protocol](https://docs.docker.com/reference/cli/docker/login/#credential-helper-protocol)
- [docker/docker-credential-helpers](https://github.com/docker/docker-credential-helpers)
