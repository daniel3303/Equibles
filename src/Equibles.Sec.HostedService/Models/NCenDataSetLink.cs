namespace Equibles.Sec.HostedService.Models;

/// <summary>One quarterly Form N-CEN data set archive listed on SEC's catalogue page.</summary>
public record NCenDataSetLink(int Year, int Quarter, string FileName, string Url);
