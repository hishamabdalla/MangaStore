using Xunit;

// Every class here boots its own WebApplicationFactory<Program>. Two hosts starting concurrently
// race inside xUnit's HostFactoryResolver, which resolves the entry point through a process-wide
// diagnostic listener — the loser fails with "The entry point exited without ever building an
// IHost". Integration tests are I/O-bound against in-memory SQLite, so serialising them costs
// little and removes a flake that only appears once a second test class exists.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
