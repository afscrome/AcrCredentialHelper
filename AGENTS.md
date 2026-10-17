This project is a Docker credential helper for Azure Container Registry (ACR), using Microsoft Entra broker authentication.
The purpose is to streamline docker authentication by making it work silently, without users needing to re-run `az acr login` for multiple registries / whenever tokens expire.

## Layout

- `app/`: Main Executable
- `test/unit/`: TUnit unit tests
- `.github/workflows/`: shared build pipeline and cross-platform CI matrix.

## Build

Run from the repository root:

```bash
dotnet restore AcrCredentialHelper.slnx
dotnet build AcrCredentialHelper.slnx --no-restore
dotnet test --project test/unit/Docker.Credential.Acr.Tests.csproj
```

## Tests

- Tests use TUnit on Microsoft.Testing.Platform (MTP).
  Do not use legacy VSTest commands.
- Build and intermediate output uses the modern .NET Artifacts Path Layout.
- Unit test MUST remain in memory - do not touch disk or network.
- Tests run in parallel, do not modify global state such as env vars.

## Publish

The app publishes with Native AOT.

## Security

Never log or store tokens, passwords or other sensitive credentials.
Rely on `Azure.Identity` for all entra tokens.

## Documentation

- Use British English 
- Do not add internal implementation details to the readme - the readme is for end users.

## References

For protocol or authentication changes, read the relevant reference:

- [Credential helper protocol](docs/credential-helper-protocol.md)
- [ACR authentication](docs/acr-authentication.md)
