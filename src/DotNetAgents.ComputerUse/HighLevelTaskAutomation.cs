namespace DotNetAgents.ComputerUse;

/// <summary>Executes high-level tasks from a plan with error recovery. CU-3.6.</summary>
public sealed class HighLevelTaskAutomation : IHighLevelTaskAutomation
{
    private readonly IBrowserAgent _browser;
    private readonly Func<string, CancellationToken, Task<AutomationPlan>> _planProvider;

    /// <summary>Creates automation that uses the given browser agent and plan provider.</summary>
    /// <param name="browser">Browser agent for navigation, click, fill.</param>
    /// <param name="planProvider">Async delegate that returns a plan for a task description (e.g. from an LLM).</param>
    public HighLevelTaskAutomation(IBrowserAgent browser, Func<string, CancellationToken, Task<AutomationPlan>> planProvider)
    {
        _browser = browser ?? throw new ArgumentNullException(nameof(browser));
        _planProvider = planProvider ?? throw new ArgumentNullException(nameof(planProvider));
    }

    /// <inheritdoc />
    public async Task<HighLevelTaskResult> ExecuteTaskAsync(string taskDescription, HighLevelTaskOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(taskDescription);
        var opts = options ?? new HighLevelTaskOptions();
        var recoveries = 0;
        try
        {
            var plan = await _planProvider(taskDescription, cancellationToken).ConfigureAwait(false);
            if (plan.Steps.Count == 0)
                return new HighLevelTaskResult { Success = false, Error = "Plan contained no steps.", StepsExecuted = 0 };

            var executed = 0;
            for (var i = 0; i < plan.Steps.Count; i++)
            {
                var step = plan.Steps[i];
                var retries = 0;
                while (retries <= opts.MaxRetriesPerStep)
                {
                    try
                    {
                        await ExecuteStepAsync(step, cancellationToken).ConfigureAwait(false);
                        executed++;
                        break;
                    }
                    catch (Exception) when (retries < opts.MaxRetriesPerStep)
                    {
                        retries++;
                        recoveries++;
                    }
                    catch (Exception ex)
                    {
                        return new HighLevelTaskResult
                        {
                            Success = false,
                            Summary = $"Failed at step {i + 1} ({step.Kind}) after {opts.MaxRetriesPerStep} retries.",
                            StepsExecuted = executed,
                            RecoveriesApplied = recoveries,
                            Error = ex.Message
                        };
                    }
                }
            }
            return new HighLevelTaskResult
            {
                Success = true,
                Summary = plan.Goal,
                StepsExecuted = executed,
                RecoveriesApplied = recoveries
            };
        }
        catch (Exception e)
        {
            return new HighLevelTaskResult { Success = false, Error = e.Message, RecoveriesApplied = recoveries };
        }
    }

    private async Task ExecuteStepAsync(AutomationStep step, CancellationToken cancellationToken)
    {
        var kind = step.Kind.Trim().ToUpperInvariant();
        var p = step.Parameters;

        if (kind == "NAVIGATE" && p.TryGetValue("url", out var url) && !string.IsNullOrEmpty(url))
        {
            _ = await _browser.NavigateAsync(url!, cancellationToken).ConfigureAwait(false);
            return;
        }
        if (kind == "CLICK")
        {
            IBrowserElement? el = null;
            if (p.TryGetValue("selector", out var sel) && !string.IsNullOrEmpty(sel))
                el = await _browser.QuerySelectorAsync(sel!, cancellationToken).ConfigureAwait(false);
            if (el == null && p.TryGetValue("description", out var desc) && !string.IsNullOrEmpty(desc))
                el = await _browser.FindByDescriptionAsync(desc!, cancellationToken).ConfigureAwait(false);
            if (el == null)
                throw new InvalidOperationException("Click step: no element found for selector/description.");
            await _browser.ClickAsync(el, cancellationToken).ConfigureAwait(false);
            return;
        }
        if (kind == "FILL" && p.TryGetValue("selector", out var fillSel) && p.TryGetValue("value", out var value))
        {
            var fillEl = await _browser.QuerySelectorAsync(fillSel ?? "", cancellationToken).ConfigureAwait(false)
                ?? await _browser.FindByDescriptionAsync(fillSel ?? "", cancellationToken).ConfigureAwait(false);
            if (fillEl == null)
                throw new InvalidOperationException("Fill step: element not found.");
            await _browser.FillAsync(fillEl, value ?? "", cancellationToken).ConfigureAwait(false);
            return;
        }
        throw new NotSupportedException($"Step kind '{step.Kind}' is not supported.");
    }
}
