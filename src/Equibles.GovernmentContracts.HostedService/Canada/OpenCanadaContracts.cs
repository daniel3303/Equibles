using System.Globalization;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Equibles.GovernmentContracts.HostedService.Canada;

/// <summary>A Government of Canada fiscal quarter as the proactive publication names it ("2026-2027-Q2" is July to September 2026).</summary>
public readonly record struct CanadianReportingPeriod(int FiscalYear, int Quarter)
    : IComparable<CanadianReportingPeriod>
{
    /// <summary>
    /// The first quarter read. From fiscal 2017-2018 every report states the contract's total including
    /// amendments; most earlier history carries no reporting period and states each transaction's own value.
    /// </summary>
    public static readonly CanadianReportingPeriod First = new(2017, 1);

    public string Code => $"{FiscalYear}-{FiscalYear + 1}-Q{Quarter}";

    // The federal fiscal year starts on April 1.
    public DateOnly Start => new DateOnly(FiscalYear, 4, 1).AddMonths(3 * (Quarter - 1));

    public DateOnly End => Start.AddMonths(3).AddDays(-1);

    public CanadianReportingPeriod Next =>
        Quarter == 4 ? new(FiscalYear + 1, 1) : new(FiscalYear, Quarter + 1);

    public CanadianReportingPeriod Previous =>
        Quarter == 1 ? new(FiscalYear - 1, 4) : new(FiscalYear, Quarter - 1);

    public static CanadianReportingPeriod Containing(DateOnly day) =>
        new(day.Month >= 4 ? day.Year : day.Year - 1, (day.Month + 8) % 12 / 3 + 1);

    public int CompareTo(CanadianReportingPeriod other) => Start.CompareTo(other.Start);

    public static bool operator <(CanadianReportingPeriod left, CanadianReportingPeriod right) =>
        left.CompareTo(right) < 0;

    public static bool operator >(CanadianReportingPeriod left, CanadianReportingPeriod right) =>
        left.CompareTo(right) > 0;

    public static bool operator <=(CanadianReportingPeriod left, CanadianReportingPeriod right) =>
        left.CompareTo(right) <= 0;

    public static bool operator >=(CanadianReportingPeriod left, CanadianReportingPeriod right) =>
        left.CompareTo(right) >= 0;
}

/// <summary>One row of the "Contracts over $10,000" resource, as the open.canada.ca datastore returns it.</summary>
public sealed record CanadianContractRow(
    string Department,
    string DepartmentTitle,
    string ReferenceNumber,
    string ProcurementId,
    string VendorName,
    string ContractDate,
    string DeliveryDate,
    decimal? ContractValue,
    string InstrumentType,
    string Description,
    string ReportingPeriod
)
{
    /// <summary>
    /// One contract's identity across its reports: an amendment repeats the department's
    /// procurement id with the amended total, so the department and that id name the contract;
    /// a row without one falls back to its own reference number.
    /// </summary>
    public string AwardUniqueKey =>
        string.IsNullOrWhiteSpace(ProcurementId)
        || ProcurementId.Trim().Equals("NA", StringComparison.OrdinalIgnoreCase)
            ? $"CA:{Department}:ref:{ReferenceNumber?.Trim()}"
            : $"CA:{Department}:{ProcurementId.Trim()}";

    /// <summary>The sequence a department numbers its reports with inside one quarter; a later amendment carries a higher number.</summary>
    public int ReferenceSequence =>
        ReferenceNumber != null
        && OpenCanadaContracts.TrailingNumber().Match(ReferenceNumber) is { Success: true } match
        && int.TryParse(
            match.Value,
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out var sequence
        )
            ? sequence
            : 0;
}

/// <summary>
/// Reads the Government of Canada's proactive publication of contracts over $10,000 from the
/// open.canada.ca CKAN datastore, one fiscal quarter at a time.
/// </summary>
public static partial class OpenCanadaContracts
{
    public const string ResourceId = "fac950c0-00d5-4ec1-a4d3-9cbebf98a305";

    /// <summary>The portal returns at most this many records per request, whatever limit is asked for.</summary>
    public const int PageSize = 1000;

    private const string Fields =
        "owner_org,owner_org_title,reference_number,procurement_id,vendor_name,contract_date,delivery_date,contract_value,instrument_type,description_en,reporting_period";

    public static string PageAddress(CanadianReportingPeriod period, int offset) =>
        "https://open.canada.ca/data/api/3/action/datastore_search"
        + $"?resource_id={ResourceId}"
        + "&filters="
        + Uri.EscapeDataString(JsonConvert.SerializeObject(new { reporting_period = period.Code }))
        + $"&limit={PageSize}&offset={offset.ToString(CultureInfo.InvariantCulture)}"
        + "&sort=_id"
        + "&fields="
        + Uri.EscapeDataString(Fields);

    /// <summary>
    /// Parses one datastore page. The portal's firewall answers a request it refuses with an HTML
    /// page and status 200, so anything but a successful JSON answer is refused rather than read as an empty quarter.
    /// </summary>
    public static IReadOnlyList<CanadianContractRow> ParsePage(string body)
    {
        JObject answer;
        try
        {
            using var reader = new JsonTextReader(new StringReader(body ?? ""))
            {
                DateParseHandling = DateParseHandling.None,
                FloatParseHandling = FloatParseHandling.Decimal,
            };
            answer = JObject.Load(reader);
        }
        catch (JsonReaderException exception)
        {
            throw new InvalidDataException(
                "open.canada.ca did not answer with JSON (the request was likely refused).",
                exception
            );
        }
        if (
            answer.Value<bool?>("success") != true
            || answer["result"]?["records"] is not JArray records
        )
            throw new InvalidDataException(
                $"open.canada.ca reported a failed datastore search: {answer["error"]}"
            );
        return records.OfType<JObject>().Select(Row).ToList();
    }

    /// <summary>The contract date as a signing date, or null for a placeholder (the publication carries 1899 and 1900 dates) or a date after today.</summary>
    public static DateOnly? AwardDate(string value, DateOnly today) =>
        Date(value) is { } date && date.Year >= 1950 && date <= today ? date : null;

    /// <summary>The delivery date, or null when it is a placeholder.</summary>
    public static DateOnly? EndDate(string value) =>
        Date(value) is { } date && date.Year is >= 1950 and <= 2100 ? date : null;

    /// <summary>The English half of a department's bilingual title ("National Defence | Défense nationale").</summary>
    public static string EnglishTitle(string title)
    {
        if (string.IsNullOrWhiteSpace(title))
            return null;
        var separator = title.IndexOf(" | ", StringComparison.Ordinal);
        return (separator < 0 ? title : title[..separator]).Trim();
    }

    [GeneratedRegex(@"\d+$")]
    internal static partial Regex TrailingNumber();

    private static DateOnly? Date(string value) =>
        DateOnly.TryParseExact(
            value?.Trim(),
            "yyyy-MM-dd",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var date
        )
            ? date
            : null;

    private static CanadianContractRow Row(JObject record) =>
        new(
            Text(record, "owner_org"),
            Text(record, "owner_org_title"),
            Text(record, "reference_number"),
            Text(record, "procurement_id"),
            Text(record, "vendor_name"),
            Text(record, "contract_date"),
            Text(record, "delivery_date"),
            decimal.TryParse(
                Text(record, "contract_value"),
                NumberStyles.Number,
                CultureInfo.InvariantCulture,
                out var value
            )
                ? value
                : null,
            Text(record, "instrument_type"),
            Text(record, "description_en"),
            Text(record, "reporting_period")
        );

    private static string Text(JObject record, string field) =>
        record[field] is JValue { Value: not null } value
            ? Convert.ToString(value.Value, CultureInfo.InvariantCulture)?.Trim()
            : null;
}
