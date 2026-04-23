using Xunit;

namespace CasinoShiz.Tests;

/// <summary>Serializes tests that use static <c>BotMiniGameSession</c> (cross-game lock).</summary>
[CollectionDefinition("MiniGameSession", DisableParallelization = true)]
public sealed class MiniGameSessionCollection
{
}
