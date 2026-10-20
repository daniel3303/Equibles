using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Equibles.Sec.Data.Models;

namespace Equibles.Sec.HostedService.Services;

/// <summary>
/// Builds an adviser's version row from the parsed roster entity and hashes its tracked content.
/// The hash reads typed values, never raw cells, so a CSV "1,000.00" and a workbook 1000, or a
/// column one layout lacks and another leaves blank, hash alike.
/// </summary>
internal static class FormAdvVersionContent
{
    internal static FormAdvAdviserVersion From(
        FormAdvAdviser adviser,
        IEnumerable<FormAdvAdviserClientType> clientTypes,
        FormAdvRegistration registration,
        DateOnly reportDate
    )
    {
        var version = new FormAdvAdviserVersion
        {
            Crd = adviser.Crd,
            Registration = registration,
            ReportDate = reportDate,
            LastSeenReportDate = reportDate,
            SecNumber = adviser.SecNumber,
            LegalName = adviser.LegalName,
            Cik = adviser.Cik,
            Lei = adviser.Lei,
            OrganizationForm = adviser.OrganizationForm,
            NumberOfEmployees = adviser.NumberOfEmployees,
            TotalRegulatoryAum = adviser.TotalRegulatoryAum,
            DiscretionaryAum = adviser.DiscretionaryAum,
            NonDiscretionaryAum = adviser.NonDiscretionaryAum,
            RegistrationBasis = adviser.RegistrationBasis,
            ExemptionBasis = adviser.ExemptionBasis,
            OtherBusinesses = adviser.OtherBusinesses,
            RelatedPersons = adviser.RelatedPersons,
            AdvisesPrivateFunds = adviser.AdvisesPrivateFunds,
            PrivateFundCount = adviser.PrivateFundCount,
            HedgeFundCount = adviser.HedgeFundCount,
            PrivateEquityFundCount = adviser.PrivateEquityFundCount,
            VentureCapitalFundCount = adviser.VentureCapitalFundCount,
            RealEstateFundCount = adviser.RealEstateFundCount,
            LiquidityFundCount = adviser.LiquidityFundCount,
            SecuritizedAssetFundCount = adviser.SecuritizedAssetFundCount,
            OtherPrivateFundCount = adviser.OtherPrivateFundCount,
            PrivateFundGrossAssets = adviser.PrivateFundGrossAssets,
            LatestFilingDate = adviser.LatestFilingDate,
        };
        version.ClientTypes = clientTypes
            .GroupBy(clientType => clientType.ClientType)
            .Select(group => group.First())
            .OrderBy(clientType => clientType.ClientType)
            .Select(clientType => new FormAdvClientTypeAum
            {
                FormAdvAdviserVersionId = version.Id,
                ClientType = clientType.ClientType,
                ClientCount = clientType.ClientCount,
                FewerThanFiveClients = clientType.FewerThanFiveClients,
                RegulatoryAum = clientType.RegulatoryAum,
            })
            .ToList();
        version.ContentHash = Hash(version);
        return version;
    }

    /// <summary>SHA-256 of every tracked field and client type, lowercase hex.</summary>
    internal static string Hash(FormAdvAdviserVersion version)
    {
        var text = new StringBuilder();
        void Add(object value) =>
            text.Append(
                    value switch
                    {
                        null => "∅",
                        Enum member => Convert
                            .ToInt64(member, CultureInfo.InvariantCulture)
                            .ToString(CultureInfo.InvariantCulture),
                        bool flag => flag ? "1" : "0",
                        IFormattable formattable => formattable.ToString(
                            null,
                            CultureInfo.InvariantCulture
                        ),
                        _ => value.ToString(),
                    }
                )
                .Append('\u001f');

        Add(version.Registration);
        Add(version.SecNumber);
        Add(version.LegalName);
        Add(version.Cik);
        Add(version.Lei);
        Add(version.OrganizationForm);
        Add(version.NumberOfEmployees);
        Add(version.TotalRegulatoryAum);
        Add(version.DiscretionaryAum);
        Add(version.NonDiscretionaryAum);
        Add(version.RegistrationBasis);
        Add(version.ExemptionBasis);
        Add(version.OtherBusinesses);
        Add(version.RelatedPersons);
        Add(version.AdvisesPrivateFunds);
        Add(version.PrivateFundCount);
        Add(version.HedgeFundCount);
        Add(version.PrivateEquityFundCount);
        Add(version.VentureCapitalFundCount);
        Add(version.RealEstateFundCount);
        Add(version.LiquidityFundCount);
        Add(version.SecuritizedAssetFundCount);
        Add(version.OtherPrivateFundCount);
        Add(version.PrivateFundGrossAssets);
        foreach (var clientType in version.ClientTypes.OrderBy(row => row.ClientType))
        {
            Add(clientType.ClientType);
            Add(clientType.ClientCount);
            Add(clientType.FewerThanFiveClients);
            Add(clientType.RegulatoryAum);
        }
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())));
    }
}
