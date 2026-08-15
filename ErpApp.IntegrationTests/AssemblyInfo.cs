using Xunit;

// Each collection fixture points the static AppConfig.ConnectionString at its
// own scratch database; parallel collections would race on that shared state.
// Sequential execution lets each fixture set it up and tear it down cleanly.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
