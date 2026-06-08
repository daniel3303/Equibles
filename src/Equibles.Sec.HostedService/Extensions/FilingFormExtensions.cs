using Equibles.Integrations.Sec.Models;

namespace Equibles.Sec.HostedService.Extensions;

public static class FilingFormExtensions
{
    // SEC amendment submissions carry "/A" in their form type (e.g. "D/A", "N-PORT/A", "N-CEN/A").
    public static bool IsAmendmentForm(this FilingData filing) => filing.Form.IsAmendmentFormType();

    public static bool IsAmendmentFormType(this string formType) =>
        formType?.Contains("/A", StringComparison.OrdinalIgnoreCase) == true;
}
