namespace Equibles.Sec.BusinessLogic;

/// <summary>Source-stated metadata and content for one named SGML document block.</summary>
public sealed class SecFilingArtifactDescriptor
{
    public SecFilingArtifactDescriptor(
        string fileName,
        string type,
        string sequence,
        int? sequenceNumber,
        string description,
        string body,
        string rawBlock,
        bool isPrimary
    )
    {
        FileName = fileName;
        Type = type;
        Sequence = sequence;
        SequenceNumber = sequenceNumber;
        Description = description;
        Body = body;
        RawBlock = rawBlock;
        IsPrimary = isPrimary;
    }

    public string FileName { get; }
    public string Type { get; }
    public string Sequence { get; }
    public int? SequenceNumber { get; }
    public string Description { get; }
    public string Body { get; }
    public string RawBlock { get; }
    public bool IsPrimary { get; }
}
