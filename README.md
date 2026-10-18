# Azure Container Registry credential helper for Docker

Are you sick and tired of having to run `az acr login` or `docker login` multiple times per day -  for separate registries or every 4 hours when your token expires?

The ACR Credential Helper streamlines this by:

1. Integrating with Entra Id, which in many cases can silently authenticate you.
2. Prompting you to authenticate inside the `docker` command, instead of docker giving you an access denied, and then you having to remember how to re-authenticate.


## Installation

### macOS and Linux

Install the latest build from the `main` branch with:

```sh
curl -fsSL https://raw.githubusercontent.com/afscrome/AcrCredentialHelper/main/scripts/install.sh | bash
```

The script installs `docker-credential-acr` into `~/.acrcredhelper` and adds that directory to your `PATH` if it is missing.
For now it requires the [GitHub CLI](https://cli.github.com) (`gh`), signed in with `gh auth login`.

### Windows

Windows installation instructions are not available yet.
Published release artefacts and a Windows installer are still being prepared.

## Get started

Once the helper is installed and available to Docker on your `PATH`, run:

```sh
docker-credential-acr install
```

The helper signs you in through the platform authentication broker, finds the ACR registries you can access in the selected Entra tenant, and configures Docker to use the helper for those registries.
Your existing Docker configuration is preserved: registries are only added to `credHelpers`.
A registry that Docker already routes to a different credential helper is left unchanged, and a warning is logged.
To switch it to this helper, edit or remove its entry in your Docker `config.json`.
Registry names keep any explicit port, such as `registry.example.com:5000`, because Docker matches `credHelpers` entries by host and port.
The first sign-in may ask you to choose an account or complete your organisation's sign-in requirements, such as multifactor authentication.

If you need to select a particular tenant, provide its tenant ID or verified domain:

```sh
docker-credential-acr install --tenant contoso.onmicrosoft.com
```

Without `--tenant`, the helper uses `ACR_CREDENTIAL_HELPER_TenantId`, then `AZURE_TENANT_ID`.
When Docker requests credentials for a registry, the tenant of the account remembered for that registry takes precedence over both settings.

Sign-in waits up to five minutes before failing.
To change this, set `ACR_CREDENTIAL_HELPER_AuthenticationTimeout` to a time span such as `00:10:00`.

After setup, pull an image as usual:

```sh
docker pull <registry>/<repository>:<tag>
```

For example:

```sh
docker pull myregistry.azurecr.io/my-app:latest
```

Docker will request credentials from the helper when it needs them.
The helper first tries to use the remembered account silently and prompts you to sign in again if required.
Run `docker-credential-acr install` again to discover registries created since your last setup.

## Access and sign-in

- Your account needs permission to discover registries and permission to pull the images you use.
  Registry discovery does not grant image access; an appropriate ACR pull or repository-reader role is still required.
- The helper uses interactive Microsoft Entra broker sign-in.
  Azure CLI, PowerShell, workload identity, and unattended CI authentication are not supported.
- The helper remembers the selected account for each discovered registry.
  This is account information, not a saved password or access token.
- `docker logout <registry>` removes the helper's remembered account for that registry.
  It does not sign you out of the platform authentication broker or revoke tokens managed by the broker.
- Registries explicitly configured to use this helper are treated as ACR endpoints.
  Custom hostnames, including private-link names and CNAMEs, can be used if Docker is configured to route them to this helper.

## Troubleshooting

When troubleshooting a Docker command, increase the helper's log level through its environment variable.
Docker starts the credential helper for you, so you cannot add helper command-line options to a `docker pull` or other Docker command.
Set the variable in the environment from which Docker runs:

```powershell
$env:ACR_CREDENTIAL_HELPER_Logging__LogLevel__Default = "Information"
docker pull <registry>/<repository>:<tag>
```

The helper reads `ACR_CREDENTIAL_HELPER_Logging__LogLevel__Default` and writes diagnostic messages to stderr.
The default level is `Warning`; use `Debug` for detailed Azure SDK diagnostics.

For commands that invoke the helper directly, you can instead pass `--log-level`.
For example, to see more information while setting up Docker:

```sh
docker-credential-acr install --log-level Information
```

Use `docker-credential-acr --help` or `docker-credential-acr install --help` to see available command options.

The helper discovers registries in the Azure public cloud.
It can only find registries that your account is allowed to read in the selected tenant.
