using Xunit;

namespace Intervals.Api.Tests;

[CollectionDefinition(nameof(AuthCollection))]
public sealed class AuthCollection : ICollectionFixture<AuthWebFactory> { }
