using System.Net;
using Equibles.Integrations.GovernmentContracts;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Equibles.UnitTests.GovernmentContracts;

/// <summary>
/// Pins <see cref="UsaSpendingClient.GetContractAwards"/> against a well-formed response whose
/// <c>results</c> field is JSON null. The contract is to return the awards in the window — a page
/// that carries no results array contributes nothing, so the call must yield an empty list, not
/// throw. The model defaults Results to an empty list, but a deserialized explicit null overwrites
/// that default, so reading Results.Count without a guard would NRE.
/// </summary>
public class UsaSpendingClientGetContractAwardsNullResultsTests
{
    [Fact]
    public async Task GetContractAwards_ResultsFieldIsNull_ReturnsEmptyWithoutThrowing()
    {
        const string body = "{\"results\":null,\"page_metadata\":{\"page\":1,\"hasNext\":false}}";
        var sut = new UsaSpendingClient(
            new HttpClient(new SingleResponseHandler(body)),
            Substitute.For<ILogger<UsaSpendingClient>>()
        );

        var awards = await sut.GetContractAwards(
            new DateOnly(2024, 1, 1),
            new DateOnly(2024, 12, 31),
            minimumAmount: 0m
        );

        awards.Should().BeEmpty();
    }

    private sealed class SingleResponseHandler : HttpMessageHandler
    {
        private readonly string _body;

        public SingleResponseHandler(string body) => _body = body;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        ) =>
            Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(_body) }
            );
    }
}
