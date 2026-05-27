using System.Reflection;
using System.Text.RegularExpressions;
using Equibles.Sec.Data.Models;
using Equibles.Sec.Mcp.Tools;

namespace Equibles.UnitTests.Sec;

public class RagSearchToolsParseDocumentTypeContractTests
{
    // RagSearchTools.DocumentTypeDescription is the [Description] attribute the
    // MCP framework hands to the LLM as the contract for the `documentType`
    // argument on SearchDocuments / SearchCompanyDocuments / SearchDocument /
    // ListCompanyDocuments. Every value quoted in that string must round-trip
    // through ParseDocumentType to a non-null DocumentType — otherwise the LLM
    // follows the documented contract and the server silently drops the filter
    // (null parsedType = no document-type filter), returning results from every
    // filing type instead.
    public static IEnumerable<object[]> AdvertisedDocumentTypes()
    {
        var description = (string)
            typeof(RagSearchTools)
                .GetField("DocumentTypeDescription", BindingFlags.NonPublic | BindingFlags.Static)
                .GetRawConstantValue();

        foreach (Match match in Regex.Matches(description, @"'([^']+)'"))
        {
            yield return new object[] { match.Groups[1].Value };
        }
    }

    [Theory]
    [MemberData(nameof(AdvertisedDocumentTypes))]
    public void ParseDocumentType_AdvertisedValue_ResolvesToKnownDocumentType(string advertised)
    {
        var method = typeof(RagSearchTools).GetMethod(
            "ParseDocumentType",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        var result = (DocumentType)method.Invoke(null, [advertised]);

        result
            .Should()
            .NotBeNull(
                $"the MCP tool description advertises '{advertised}' as an allowed value — every advertised value must round-trip through ParseDocumentType so the LLM-visible contract matches the parser"
            );
    }
}
