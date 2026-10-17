# Credential helper protocol

Docker invokes `docker-credential-acr` with the command name as its first argument and sends any command payload through stdin, not command-line arguments.
At the time of writing, required commands are:

| Command | Input via stdin | Successful output via stdout |
| --- | --- | --- |
| `get` | Registry server URL as plain text | JSON object with `Username` and `Secret` properties |
| `store` | JSON object with `ServerURL`, `Username`, and `Secret` properties | No output; this helper intentionally ignores the payload and does not persist credentials |
| `erase` | Registry server URL as plain text | No output; this helper removes the registry's saved account mapping |
| `list` | No input | JSON object mapping registry server URLs to usernames |

`install` is a helper-specific setup command, not part of Docker's credential helper protocol.
Keep stdout reserved for protocol responses, preserving JSON property names and exit-code behaviour.
Write diagnostic data to stderr through the existing logging infrastructure, never alongside JSON on stdout.
Logging defaults to `Warning`.

Docker locates credential helpers on its `PATH`, so the helper executable must be available in the environment where Docker runs.
The executable must be named `docker-credential-{name}` (with `.exe` on Windows), where `{name}` is the helper name configured in Docker's `config.json`, not a full path or executable filename.
The `credHelpers` object maps individual registry hostnames, including explicit ports when present, to helper names.
For example, this configuration routes credentials for `myregistry.azurecr.io` to `docker-credential-acr`:

```json
{
  "credHelpers": {
    "myregistry.azurecr.io": "acr"
  }
}
```

Per-registry `credHelpers` entries take precedence over the default `credsStore` and credentials in `auths` for those registries.
Docker reads `config.json` from its configuration directory, normally `~/.docker`, or the directory selected by `DOCKER_CONFIG`.

- [Docker credential helper protocol](https://docs.docker.com/reference/cli/docker/login/#credential-helper-protocol)
- [docker/docker-credential-helpers](https://github.com/docker/docker-credential-helpers)
