using Xunit;

// Disable parallel test execution across test classes because integration tests share LocalDB state
[assembly: CollectionBehavior(DisableTestParallelization = true)]
