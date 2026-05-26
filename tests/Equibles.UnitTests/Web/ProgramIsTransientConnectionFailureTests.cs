using System.Net.Sockets;
using Equibles.Web;
using FluentAssertions;
using Npgsql;

namespace Equibles.UnitTests.Web;

public class ProgramIsTransientConnectionFailureTests
{
    [Fact]
    public void NpgsqlException_WithSocketInner_IsTransient()
    {
        var ex = new NpgsqlException("conn refused", new SocketException());

        Program.IsTransientConnectionFailure(ex).Should().BeTrue();
    }

    [Fact]
    public void NpgsqlException_WithNonSocketInner_IsNotTransient()
    {
        var ex = new NpgsqlException("boom", new InvalidOperationException());

        Program.IsTransientConnectionFailure(ex).Should().BeFalse();
    }

    [Fact]
    public void NpgsqlException_WithNoInner_IsNotTransient()
    {
        var ex = new NpgsqlException("boom");

        Program.IsTransientConnectionFailure(ex).Should().BeFalse();
    }

    [Fact]
    public void PostgresException_CannotConnectNow_IsTransient()
    {
        // "57P03" = cannot_connect_now (server in startup).
        var ex = new PostgresException(
            messageText: "the database system is starting up",
            severity: "FATAL",
            invariantSeverity: "FATAL",
            sqlState: "57P03"
        );

        Program.IsTransientConnectionFailure(ex).Should().BeTrue();
    }

    [Fact]
    public void PostgresException_OtherSqlState_IsNotTransient()
    {
        var ex = new PostgresException(
            messageText: "duplicate key",
            severity: "ERROR",
            invariantSeverity: "ERROR",
            sqlState: "23505"
        );

        Program.IsTransientConnectionFailure(ex).Should().BeFalse();
    }

    [Fact]
    public void UnrelatedException_IsNotTransient()
    {
        var ex = new InvalidOperationException("nope");

        Program.IsTransientConnectionFailure(ex).Should().BeFalse();
    }
}
