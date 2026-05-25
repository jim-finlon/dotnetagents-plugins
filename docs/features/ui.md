# Feature: UI

UI plugins help teams build surfaces where humans inspect, approve, correct, and
steer agent work.

## Use Cases

- approval dashboards
- agent run viewers
- workflow status panels
- chat or command surfaces
- artifact review tools

## Technical Pattern

UI should display the agent's evidence, not only the final answer:

- current state
- pending action
- preview payload
- warnings
- validation result
- audit or run id

## Implementation Checklist

- show before/after for mutations
- make approvals explicit
- render structured errors clearly
- avoid hiding policy denials
- do not expose raw secrets
- test small screens and long text

## Public And Premium Boundary

Public UI packages provide reusable building blocks. Premium products may add
managed dashboards, certification views, or enterprise governance workflows
without changing the public UI principles.
