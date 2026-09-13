namespace Equibles.Migrations.Infrastructure;

internal static class EquityStorageRetirementSql
{
    internal static string Read(string fileName)
    {
        using var stream =
            typeof(EquityStorageRetirementSql).Assembly.GetManifestResourceStream(
                "Equibles.Migrations.Infrastructure." + fileName
            )
            ?? throw new InvalidOperationException(
                $"Missing equity retirement migration resource: {fileName}"
            );
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
