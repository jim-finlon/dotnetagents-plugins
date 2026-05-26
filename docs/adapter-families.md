# Adapter Families

This page summarizes the major plugin families and what each one is for.

## Messaging

Messaging adapters connect agents to event streams, queues, pub/sub systems,
and real-time notifications.

Use them for:

- fan-out work
- async status updates
- agent-to-service events
- UI notifications
- durable integration messages

Start with the simplest transport that fits your reliability needs. Do not use
a distributed broker when an in-process queue or local channel is enough.

## Storage

Storage adapters persist artifacts, task knowledge, run outputs, and other
agent-produced data.

Good storage integration includes:

- stable artifact ids
- content hashes when useful
- redaction before persistence
- clear retention policy
- read/write separation for high-impact data

## Vector Stores

Vector store adapters support semantic retrieval over documents, chunks,
memories, and other indexed content.

Use vector stores when keyword search is not enough. Keep the retrieval layer
observable: record which sources were considered, which were selected, and how
the result was used.

## Databases

Database plugins provide abstractions, dialect helpers, validation, security
checks, and tooling.

Agents that inspect databases should default to read-only access. Mutating
database tools should require explicit policy, tests, and preview/confirm
behavior.

## Browser And Computer Use

Browser/computer-use plugins let agents interact with UI surfaces that do not
have a direct API.

Use them carefully:

- prefer APIs when available
- isolate browser state
- make tests deterministic
- capture screenshots or artifacts for debugging
- require confirmation for irreversible actions

## UI

UI plugins help build agent-facing tools and operator surfaces. Use them when
humans need to inspect, approve, correct, or steer agent work.

## Multimodal And Media

Multimodal/media plugins connect to providers that process images, audio, video,
or generated media. Keep provider-specific details at the adapter edge and keep
agent workflows provider-neutral where practical.

## Interop

Interop plugins bridge DotNetAgents with other agent frameworks. Use them when a
team already has an ecosystem investment but wants a .NET runtime, governance,
or protocol surface around it.

## Plugin Showcase

For a complete, runnable console example demonstrating the configuration and usage of all 7 public plugin families with offline fakes and configuration options, see the [Plugin Showcase Pack](../../dotnetagents-examples/examples/plugin-showcase/README.md) in the `dotnetagents-examples` repository.

