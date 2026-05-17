# DotNetAgents Public Plugins

This repository contains optional DotNetAgents plugin packages that extend the
public core with messaging, browser automation, computer-use, database,
vector-store, storage, multimodal, UI primitives, and interoperability
integrations.

These packages are split from the internal development workspace through an
audited open-core staging process. Private factory, premium product,
commercially licensed UI assets, and operator-specific integrations are
intentionally not included here.

## Build

Restore and build the solution with the matching public DotNetAgents core
packages available from your NuGet sources:

```bash
dotnet restore DotNetAgents.Plugins.sln
dotnet build DotNetAgents.Plugins.sln --no-restore
```

## Package List

`PUBLIC-PLUGIN-PACKAGES.txt` records the packages included in this public
plugin snapshot.
