using Equibles.Sec.Data.Models;
using Equibles.Sec.Repositories;

namespace Equibles.UnitTests.Sec;

public class SecFilingArtifactRepositoryOrderTests
{
    [Fact]
    public void Order_NumericSequence_OrdersOneTwoTenBeforeUnknown()
    {
        var artifacts = new[]
        {
            Artifact("10", 10),
            Artifact("unknown", null),
            Artifact("2", 2),
            Artifact("1", 1),
        }.AsQueryable();

        var ordered = SecFilingArtifactRepository.Order(artifacts).Select(x => x.Sequence);

        ordered.Should().Equal("1", "2", "10", "unknown");
    }

    private static SecFilingArtifact Artifact(string sequence, int? sequenceNumber)
    {
        return new SecFilingArtifact
        {
            FileName = sequence + ".htm",
            Type = "EX-4.1",
            SourceUrl = "https://www.sec.gov/" + sequence,
            Sequence = sequence,
            SequenceNumber = sequenceNumber,
            CaptureStatus = SecFilingArtifactCaptureStatus.MetadataOnly,
        };
    }
}
