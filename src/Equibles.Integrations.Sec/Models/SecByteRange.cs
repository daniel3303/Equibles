namespace Equibles.Integrations.Sec.Models;

/// <summary>A byte range of a sec.gov file and the file's total length.</summary>
public sealed record SecByteRange(byte[] Bytes, long TotalLength);
