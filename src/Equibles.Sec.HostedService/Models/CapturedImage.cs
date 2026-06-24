namespace Equibles.Sec.HostedService.Models;

/// <summary>
/// An image referenced by a filing's as-filed HTML, downloaded from EDGAR during capture and
/// handed to the persistence layer to store as a <c>DocumentImage</c>. <see cref="FileName"/> is
/// the bare EDGAR filename the as-filed HTML references it by (the viewer's lookup key).
/// </summary>
public record CapturedImage(string FileName, byte[] Bytes, string ContentType);
