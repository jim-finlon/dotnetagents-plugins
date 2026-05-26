# Feature: Browser And Computer Use

Browser and computer-use plugins let agents interact with UI surfaces when an
API is unavailable or insufficient.

## Use Cases

- inspect a web page
- operate a test browser
- capture screenshots for review
- automate a bounded UI workflow
- use vision to identify UI state

## Safety Position

Prefer APIs when available. UI automation is powerful but brittle, so keep it
bounded and observable.

## Technical Pattern

```csharp
public interface IBrowserDriver
{
    Task<string> NavigateAndCaptureAsync(string url, CancellationToken ct = default);
}
```

For a complete example, see the [Plugin Showcase Pack](../../dotnetagents-examples/examples/plugin-showcase/README.md).

For mutations, use preview/confirm and capture evidence before and after the action.

## Implementation Checklist

- isolate browser profiles
- restrict domains
- capture screenshots or traces
- set timeouts
- avoid storing credentials in browser state
- require approval for irreversible actions
