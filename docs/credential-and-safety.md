# Credential And Safety Patterns

Plugins often touch real systems. Treat configuration and credentials as part
of the product design, not an afterthought.

## Public Examples

Public samples should use this pattern:

```json
{
  "DotNetAgents": {
    "Plugins": {
      "ExampleProvider": {
        "Endpoint": "https://api.example.com",
        "ApiKeyEnvironmentVariable": "EXAMPLE_PROVIDER_API_KEY",
        "TimeoutSeconds": 30
      }
    }
  }
}
```

The sample names the environment variable but does not include the secret.

## Capability Flags

Separate capabilities in configuration:

```json
{
  "ReadEnabled": true,
  "WriteEnabled": false,
  "AllowDeletes": false
}
```

This helps operators and tests prove that a tool cannot accidentally perform a
higher-impact action.

## Redaction

Redact before logging:

- API keys
- bearer tokens
- connection strings
- passwords
- signed URLs
- customer identifiers when not needed for diagnostics

Failures should include enough guidance to fix configuration without echoing the
secret value.

## Preview/Confirm For Mutations

When a plugin can mutate external state, expose a preview path:

1. validate arguments
2. summarize the intended mutation
3. show target resources
4. estimate side effects
5. require confirmation
6. record the result

This pattern is useful in open-source apps and becomes essential in managed or
premium operating environments.
