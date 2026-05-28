using System.Net;
using Equibles.Integrations.Cboe;
using Equibles.Integrations.Cboe.Models;
using Equibles.Integrations.Common.RateLimiter;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Equibles.UnitTests.Cboe;

/// <summary>
/// Adversarial Lane A. <see cref="CboePutCallRecord"/>'s volume properties
/// are <c>long?</c> and the ratio is <c>decimal?</c> — the record type
/// itself encodes the partial-data contract. So when CBOE's payload has a
/// product's ratio in the <c>ratios</c> array but is missing the per-product
/// volume category key entirely (a realistic outcome of a CBOE front-end
/// rename), the record must still surface with the ratio and null volumes.
/// Dropping it would force the dashboard's "no data" branch and lose the
/// ratio we still have.
/// </summary>
public class CboeClientPartialVolumeTests
{
    [Fact]
    public async Task DownloadDailyPutCallRatios_RatioPresentButVolumeCategoryMissing_KeepsRecordWithNullVolumes()
    {
        var date = new DateOnly(2026, 5, 26);

        // Only the TOTAL PUT/CALL RATIO entry is present; the matching
        // "SUM OF ALL PRODUCTS" category — where the parser looks for the
        // VOLUME row — is intentionally absent. No other products appear.
        var optionsDataJson =
            "{"
            + "\"ratios\":["
            + "{\"name\":\"TOTAL PUT/CALL RATIO\",\"value\":\"0.82\"}"
            + "]"
            + "}";
        var escaped = optionsDataJson.Replace("\\", "\\\\").Replace("\"", "\\\"");
        var html =
            "<html><body><script>"
            + "self.__next_f.push([1,\"...\\\"optionsData\\\":"
            + escaped
            + "...\"])"
            + "</script></body></html>";

        var sut = CreateSut(new SingleResponseHandler(HttpStatusCode.OK, html));

        var result = await sut.DownloadDailyPutCallRatios(date);

        result
            .Should()
            .ContainKey(
                CboePutCallProductType.Total,
                "ratio is present — the partial-data contract requires the record to survive a missing volume category"
            );
        var record = result[CboePutCallProductType.Total];
        record.PutCallRatio.Should().Be(0.82m);
        record.CallVolume.Should().BeNull("the volume category key is absent");
        record.PutVolume.Should().BeNull();
        record.TotalVolume.Should().BeNull();
        record.Date.Should().Be(date);
    }

    private static CboeClient CreateSut(HttpMessageHandler handler) =>
        new(
            new HttpClient(handler),
            Substitute.For<ILogger<CboeClient>>(),
            Substitute.For<IRateLimiter>()
        );

    private sealed class SingleResponseHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;
        private readonly string _body;

        public SingleResponseHandler(HttpStatusCode status, string body)
        {
            _status = status;
            _body = body;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        ) =>
            Task.FromResult(
                new HttpResponseMessage(_status) { Content = new StringContent(_body) }
            );
    }
}
