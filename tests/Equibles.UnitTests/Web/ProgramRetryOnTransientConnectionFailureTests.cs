using System.Net.Sockets;
using Equibles.Web;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Npgsql;
using NSubstitute;

namespace Equibles.UnitTests.Web;

public class ProgramRetryOnTransientConnectionFailureTests
{
    private readonly ILogger _logger = Substitute.For<ILogger>();
    private static readonly TimeSpan ZeroDelay = TimeSpan.Zero;

    [Fact]
    public async Task SuccessOnFirstAttempt_InvokesOperationOnce()
    {
        var calls = 0;
        Task Operation()
        {
            calls++;
            return Task.CompletedTask;
        }

        await Program.RetryOnTransientConnectionFailure(
            Operation,
            _logger,
            maxAttempts: 3,
            ZeroDelay
        );

        calls.Should().Be(1);
    }

    [Fact]
    public async Task TransientThenSuccess_RetriesUntilOperationSucceeds()
    {
        var calls = 0;
        Task Operation()
        {
            calls++;
            if (calls < 3)
                throw new NpgsqlException("refused", new SocketException());
            return Task.CompletedTask;
        }

        await Program.RetryOnTransientConnectionFailure(
            Operation,
            _logger,
            maxAttempts: 5,
            ZeroDelay
        );

        calls.Should().Be(3);
    }

    [Fact]
    public async Task NonTransientException_PropagatesImmediately()
    {
        var calls = 0;
        Task Operation()
        {
            calls++;
            throw new InvalidOperationException("hard failure");
        }

        var act = () =>
            Program.RetryOnTransientConnectionFailure(
                Operation,
                _logger,
                maxAttempts: 5,
                ZeroDelay
            );

        await act.Should().ThrowAsync<InvalidOperationException>();
        calls.Should().Be(1);
    }

    [Fact]
    public async Task TransientFailureOnFinalAttempt_PropagatesException()
    {
        var calls = 0;
        Task Operation()
        {
            calls++;
            throw new NpgsqlException("refused", new SocketException());
        }

        var act = () =>
            Program.RetryOnTransientConnectionFailure(
                Operation,
                _logger,
                maxAttempts: 3,
                ZeroDelay
            );

        await act.Should().ThrowAsync<NpgsqlException>();
        // First two attempts are caught by the retry filter; the 3rd one re-throws.
        calls.Should().Be(3);
    }
}
