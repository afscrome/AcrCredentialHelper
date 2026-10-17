# ACR authentication

ACR's Microsoft Entra authentication flow uses three distinct credentials: an Entra access token, an ACR refresh token, and a scoped ACR access token.
The Entra access token establishes the caller's identity and permissions; the ACR refresh token obtains registry access tokens; a scoped ACR access token authorises registry operations.

## Get Entra Access token

First get an Entra access token to authenticate with.  Currently we use `Azure.Identity` for this.

## Exchange Entra access token for ACR Refresh token

Send an HTTPS `POST` request to the registry's `/oauth2/exchange` endpoint with an `application/x-www-form-urlencoded` body:

| Field | Value |
| --- | --- |
| `grant_type` | `access_token` |
| `service` | Registry service name, such as `contosoregistry.azurecr.io` |
| `tenant` | Microsoft Entra tenant associated with the credentials |
| `access_token` | Valid Microsoft Entra access token with access to the registry |

A successful response contains the ACR refresh token in the JSON `refresh_token` field.
This is a registry credential, distinct from a Microsoft Entra refresh token.

## Supply credentials to Docker

For this flow, Docker uses `00000000-0000-0000-0000-000000000000` (the all-zero GUID) as the username and the **ACR refresh token** as the password.
The all-zero GUID identifies the password as an ACR refresh token.
In the [Docker credential helper protocol](credential-helper-protocol.md), these values correspond to the `get` response's `Username` and `Secret` fields.

Docker transparently uses the ACR refresh token to obtain scoped access tokens for registry operations.

Use HTTPS for token exchanges and registry requests, and treat all three token types as secrets.

## References

- [ACR Microsoft Entra OAuth token exchange](https://azure.github.io/acr/AAD-OAuth.html)
- [Azure Container Registry authentication](https://learn.microsoft.com/en-gb/azure/container-registry/container-registry-authentication)
