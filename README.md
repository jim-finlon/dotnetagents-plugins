# DotNetAgents Public Plugins

This repository contains optional integration packages for the public
DotNetAgents platform.

DotNetAgents core gives you the agent runtime: hosting, workflows, tools,
protocols, model routing, governance, observability, memory, and agent patterns.
This repository extends that core into the systems agents need to touch:
databases, vector stores, messaging buses, browser automation, user interfaces,
media generation, computer-use surfaces, storage, and framework interop.

Use this repository when your agent needs to leave the core runtime and operate
against real infrastructure.

These public plugins are the extension surface. Premium packages use the same
contracts for deeper enterprise adapters, managed governance, lab/evaluation
packs, and private customer integrations without exposing premium code here.

## What Lives Here

| Area | Packages |
| --- | --- |
| Messaging | Kafka, RabbitMQ, Redis, and SignalR agent messaging adapters |
| Storage | artifact storage and task-knowledge stores for PostgreSQL and SQL Server |
| Vector stores | Chroma, Pinecone, PostgreSQL, Qdrant, Weaviate, and conformance helpers |
| Databases | database abstractions, dialects, validation, analysis, security, orchestration, and tooling |
| Browser and computer use | browser tools, Playwright, vision, Windows computer-use, and code-action packages |
| UI | Blazor, Avalonia, and shared UI core packages for agent-facing tools |
| Multimodal and media | OpenAI multimodal adapters and media-generation adapter packages |
| Interop | Microsoft Agent Framework interop and related bridge packages |

The packages are versioned with the public DotNetAgents package train.

## How This Fits With DotNetAgents

- Start with `dotnetagents` when you need the runtime and agent patterns.
- Add `dotnetagents-plugins` when the runtime needs production integrations.
- Use `dotnetagents-examples` when you want runnable starter applications.

## Documentation

The `/docs` tree is the technical guide for plugin users and authors:

- [Choosing Plugins](docs/choosing-plugins.md)
- [Installation And Configuration](docs/installation-and-configuration.md)
- [Adapter Families](docs/adapter-families.md)
- [Credential And Safety Patterns](docs/credential-and-safety.md)
- [Writing A Plugin](docs/writing-a-plugin.md)

The main platform README is in the core repository:

https://github.com/jim-finlon/dotnetagents

The comparison guide is here:

https://github.com/jim-finlon/dotnetagents/blob/main/COMPARISON.md

## Install

Packages are currently preview packages targeting .NET 10. Install only the
adapters you need:

```bash
dotnet add package DotNetAgents.VectorStores.PostgreSQL --version 1.0.0-preview.1
dotnet add package DotNetAgents.Agents.Messaging.Redis --version 1.0.0-preview.1
dotnet add package DotNetAgents.Ui.Blazor --version 1.0.0-preview.1
```

Use package versions that match the core DotNetAgents package train.

## Public Core Boundary

This repository is part of the public open-core surface. It should not contain
private hosted-service implementation, private infrastructure configuration,
customer code, secrets, proprietary prompts, or internal operations runbooks.

Premium or private systems may add managed services, commercial adapters,
enterprise governance packs, and hosted operations on top of these packages. The
public plugins remain useful without those private systems.

Roadmap themes include more public-safe adapters, richer sample configuration,
and integration points for the upcoming public Arena experience. Commercial
packages may reference these contracts for advanced routing, certification, and
managed operations, but their implementation details stay private.

## Status

The plugin packages are in preview while APIs, docs, and examples are hardened.
Expect useful integrations now and some API movement before stable 1.0.

## License

DotNetAgents public plugins are licensed under Apache-2.0. See `LICENSE` and
`NOTICE`.
