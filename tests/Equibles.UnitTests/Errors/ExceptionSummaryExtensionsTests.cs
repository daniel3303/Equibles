using Equibles.Errors.BusinessLogic.Extensions;

namespace Equibles.UnitTests.Errors;

public class ExceptionSummaryExtensionsTests
{
    [Fact]
    public void ToSummaryMessage_NoInner_ReturnsOwnMessage()
    {
        var ex = new InvalidOperationException("plain failure");

        ex.ToSummaryMessage().Should().Be("plain failure");
    }

    [Fact]
    public void ToSummaryMessage_WrappedCause_JoinsOuterAndInner()
    {
        // The whole point: a wrapper whose message defers to "the inner exception" must surface
        // that inner cause on one line.
        var ex = new InvalidOperationException(
            "See the inner exception for details.",
            new Exception("23505: duplicate key")
        );

        var summary = ex.ToSummaryMessage();

        summary.Should().Be("See the inner exception for details. -> 23505: duplicate key");
    }

    [Fact]
    public void ToSummaryMessage_RepeatedMessagesInChain_Deduplicated()
    {
        // A rethrow chain often repeats the same message; padding the line with duplicates adds no
        // signal, so consecutive/repeated identical messages collapse to one.
        var ex = new Exception("boom", new Exception("boom", new Exception("root cause")));

        ex.ToSummaryMessage().Should().Be("boom -> root cause");
    }

    [Fact]
    public void ToSummaryMessage_Null_ReturnsNull()
    {
        Exception ex = null;

        ex.ToSummaryMessage().Should().BeNull();
    }
}
