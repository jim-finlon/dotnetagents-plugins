# Choosing Plugins

Start with the core repository. Add plugins only when your agent needs an
external integration.

## Selection Checklist

Ask these questions before installing a plugin:

- What system does the agent need to touch?
- Is the operation read-only or mutating?
- Does the plugin need credentials?
- Can you test the integration locally or with a disposable environment?
- What failure should the agent return when the dependency is unavailable?
- Does the integration need preview/confirm before mutation?

## Common Choices

| Need | Plugin Family |
| --- | --- |
| Persist artifacts or run outputs | Storage plugins |
| Search embeddings or documents | Vector store plugins |
| Publish events or agent messages | Messaging plugins |
| Inspect or manipulate databases | Database plugins |
| Drive a browser or computer-use flow | Browser/computer-use plugins |
| Add an agent-facing UI | UI plugins |
| Process multimodal provider inputs | Multimodal/media plugins |
| Bridge another agent framework | Interop plugins |

## Keep The Agent Boundary Clear

The plugin should adapt an external system. It should not hide product policy
inside a connector. Keep these concerns separate:

- plugin: connection, validation, provider API shape
- agent: goal, plan, tool selection
- governance: whether the action is allowed
- workflow: when the action happens
- observability: how the action is recorded

That separation lets the same plugin serve a prototype, a production service,
and a premium managed workflow without changing the public adapter contract.
