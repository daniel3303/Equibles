using Equibles.Sec.Data.Models;

namespace Equibles.UnitTests.Sec;

public class SecFormsTests
{
    // The scraper asks EDGAR for a form by the type's display name and maps a filing back by the
    // same string, so each must resolve to exactly one catalog entry.
    [Fact]
    public void EveryForm_RoundTripsThroughItsEdgarFormString()
    {
        SecForms.All.Select(f => f.EdgarForm).Should().OnlyHaveUniqueItems();
        SecForms
            .All.Should()
            .OnlyContain(f =>
                SecForms.ForEdgarForm(f.EdgarForm) == f && SecForms.Find(f.Type) == f
            );
    }

    // An amendment stores as its own type; its original must be a catalog form of the same family,
    // spelled as EDGAR spells amendments.
    [Fact]
    public void EveryAmendment_NamesAnOriginalOfItsFamily()
    {
        foreach (var amendment in SecForms.All.Where(f => f.AmendmentOf != null))
        {
            var original = SecForms.Find(amendment.AmendmentOf);
            original.Should().NotBeNull(amendment.EdgarForm);
            original!.Family.Should().Be(amendment.Family, amendment.EdgarForm);
            amendment.EdgarForm.Should().Be(original.EdgarForm + "/A");
        }
    }

    // Every "/A" form is wired to its original, so a correction is never synced without lineage.
    [Fact]
    public void EveryAmendmentSpelling_DeclaresItsOriginal()
    {
        SecForms
            .All.Where(f => f.EdgarForm.EndsWith("/A", StringComparison.Ordinal))
            .Should()
            .OnlyContain(f => f.AmendmentOf != null);
    }

    // Syncing an amendment without its original leaves the correction with nothing to supersede.
    [Fact]
    public void SyncedByDefault_IncludesTheOriginalOfEverySyncedAmendment()
    {
        var synced = SecForms.SyncedByDefault.ToHashSet();
        SecForms
            .All.Where(f => f.AmendmentOf != null && synced.Contains(f.Type))
            .Should()
            .OnlyContain(f => synced.Contains(f.AmendmentOf));
    }

    [Theory]
    [InlineData("10-K")]
    [InlineData("SC TO-T/A")]
    [InlineData("DEFA14A")]
    [InlineData("NT 10-Q")]
    [InlineData("ARS")]
    public void ForEdgarForm_ResolvesRealEdgarSpellings(string edgarForm)
    {
        SecForms.ForEdgarForm(edgarForm)!.EdgarForm.Should().Be(edgarForm);
    }

    // Registered non-filing types (ESEF reports, commercial transcripts) are not EDGAR forms.
    [Fact]
    public void NonFilingTypes_AreNotEdgarForms()
    {
        SecForms.IsEdgar(DocumentType.EsefAnnualReport).Should().BeFalse();
        SecForms.IsEdgar(DocumentType.Other).Should().BeFalse();
        SecForms
            .IsEdgar(new DocumentType("EarningsCallTranscript", "Earnings Call"))
            .Should()
            .BeFalse();
        SecForms.ForEdgarForm("424B4").Should().BeNull();
    }
}
