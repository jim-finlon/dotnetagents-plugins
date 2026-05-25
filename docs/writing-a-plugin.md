# Writing A Plugin

A good DotNetAgents plugin is a thin, explicit adapter. It should make one
external capability available to agents without smuggling in unrelated product
policy.

## Shape

Use normal .NET conventions:

- typed options
- dependency injection registration
- interface-first public contracts
- cancellation tokens
- structured logging
- clear exceptions or result types
- tests that do not require production credentials

## Registration

Prefer an extension method:

```csharp
public static IServiceCollection AddExamplePlugin(
    this IServiceCollection services,
    Action<ExamplePluginOptions>? configure = null)
{
    if (configure is not null)
    {
        services.Configure(configure);
    }

    services.AddSingleton<IExampleClient, ExampleClient>();
    return services;
}
```

## Options Validation

Validate configuration before the first live call:

- endpoint is present
- timeout is bounded
- credential reference is present when live calls are enabled
- dangerous capabilities are off by default

## Tests

Include:

- options validation tests
- offline client tests with a fake transport
- argument validation tests
- redaction tests
- timeout/failure tests

Do not make contributors need production credentials to run the basic test
suite.

## Public Boundary

Keep private implementation details out of public plugins. A public plugin can
advertise that premium products use the same contract for managed operations,
but the premium implementation, private workflows, and proprietary scoring
systems stay outside this repository.
