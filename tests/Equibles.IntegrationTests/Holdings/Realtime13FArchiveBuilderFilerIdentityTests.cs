using Equibles.Holdings.HostedService.Models;
using Equibles.Holdings.HostedService.Services;

namespace Equibles.IntegrationTests.Holdings;

public class Realtime13FArchiveBuilderFilerIdentityTests
{
    [Fact]
    public async Task Build_CoverPage_CarriesTheFilersOwnIdentityNumbers()
    {
        // The realtime archive feeds the same importer as SEC's data set, so a cover-page column
        // the builder omits is silently absent from every realtime-imported holder.
        var filing = new Parsed13FFiling
        {
            AccessionNumber = "0001765380-26-000004",
            Cik = "1765380",
            FilingDate = new DateOnly(2026, 5, 15),
            PeriodOfReport = new DateOnly(2026, 3, 31),
            FilingManagerName = "ADVISER LLC",
            Form13FFileNumber = "028-16741",
            CrdNumber = "000171004",
            SecFileNumber = "801-80534",
        };

        using var archive = new Realtime13FArchiveBuilder().Build([filing]);

        var rows = new List<Dictionary<string, string>>();
        await foreach (var row in new TsvParser().ParseEntry(archive.GetEntry("COVERPAGE.tsv")))
            rows.Add(row);

        var cover = rows.Should().ContainSingle().Subject;
        cover["FORM13FFILENUMBER"].Should().Be("028-16741");
        cover["CRDNUMBER"].Should().Be("000171004");
        cover["SECFILENUMBER"].Should().Be("801-80534");
    }
}
