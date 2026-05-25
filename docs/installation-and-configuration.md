# Installation And Configuration

Install only the packages your agent needs.

```bash
dotnet add package DotNetAgents.VectorStores.PostgreSQL --version 1.0.0-preview.1
dotnet add package DotNetAgents.Agents.Messaging.Redis --version 1.0.0-preview.1
dotnet add package DotNetAgents.Storage.ArtifactStore --version 1.0.0-preview.1
```

Use versions that match the core DotNetAgents package train.

## Configuration Pattern

Prefer typed options and dependency injection:

```csharp
builder.Services.Configure<MyPluginOptions>(
    builder.Configuration.GetSection("DotNetAgents:Plugins:MyPlugin"));

builder.Services.AddMyDotNetAgentsPlugin();
```

Good options objects:

- validate required fields at startup
- use timeouts and retry limits
- distinguish read-only and mutating capabilities
- accept credential references or environment-variable names
- avoid raw secret values in logs and exception messages

## Secrets

Do not commit provider keys, database passwords, tokens, connection strings, or
private endpoints.

For public examples:

- use placeholders
- use environment variables
- document the variable names
- make live calls opt-in
- provide an offline smoke path when possible

For production:

- resolve secrets from your organization's approved secret store
- inject them at runtime
- redact them before logging
- rotate them outside the application code

## Failure Behavior

Every plugin should define useful failure behavior:

- missing configuration fails at startup or returns clear guidance
- provider timeout returns a bounded error
- auth failure does not print the secret
- unavailable dependency does not crash unrelated tools
- mutating calls can report whether work happened before failure

Agents become easier to operate when adapters fail explicitly.
