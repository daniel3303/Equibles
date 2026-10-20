namespace Equibles.Sec.HostedService.Services;

/// <summary>One Form ADV filing from SEC's filing archive: its adviser and submission date.</summary>
internal readonly record struct FormAdvFilingRow(int FilingId, int Crd, DateOnly FiledOn);
