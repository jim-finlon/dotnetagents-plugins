# DotNetAgents Plugins Documentation

Plugins connect the DotNetAgents public core to real systems: databases, vector
stores, messaging, UI shells, media providers, browser/computer-use surfaces,
storage, and framework interop.

Start here:

1. [Choosing Plugins](choosing-plugins.md) - pick adapters by product need.
2. [Installation And Configuration](installation-and-configuration.md) - add
   packages, bind options, and keep secrets out of source.
3. [Adapter Families](adapter-families.md) - understand what each plugin family
   does.
4. [Credential And Safety Patterns](credential-and-safety.md) - configure
   providers without leaking tokens or over-granting tools.
5. [Writing A Plugin](writing-a-plugin.md) - build an adapter that fits the
   public extension model.

Feature guides:

- [Vector Stores](features/vector-stores.md)
- [Messaging](features/messaging.md)
- [Storage And Artifacts](features/storage-artifacts.md)
- [Database Tooling](features/database-tooling.md)
- [Browser And Computer Use](features/browser-computer-use.md)
- [UI](features/ui.md)
- [Multimodal And Media](features/multimodal-media.md)

The plugin repository should stay technical. It explains package selection,
configuration shape, validation, and integration patterns. Product philosophy
belongs mostly in the core docs; premium implementation details stay private.
