using System.Reflection;
using Equibles.Sec.Data.Models;
using Equibles.Sec.Mcp.Tools;

namespace Equibles.UnitTests.Sec;

public class RagSearchToolsParseDocumentTypeEarningsCallTranscriptTests
{
    [Fact(
        Skip = "GH-2118 — ParseDocumentType rejects 'EarningsCallTranscript' advertised by the MCP tool description"
    )]
    public void ParseDocumentType_EarningsCallTranscriptFromMcpDescription_ResolvesToKnownDocumentType()
    {
        // RagSearchTools.DocumentTypeDescription is the [Description] attribute the
        // MCP framework hands to the LLM as the contract for the `documentType`
        // argument on SearchDocuments / SearchCompanyDocuments / SearchDocument /
        // ListCompanyDocuments. It enumerates the allowed values verbatim:
        //   "Document type filter. Allowed values: 'TenK', 'TenQ', 'EightK',
        //    'TenKa', 'TenQa', 'EightKa', 'TwentyF', 'SixK', 'FortyF',
        //    'EarningsCallTranscript'"
        //
        // ParseDocumentType is what consumes that argument. Every value listed in
        // the description must round-trip through ParseDocumentType to a non-null
        // DocumentType — otherwise the LLM follows the documented contract and the
        // server silently drops the filter (null parsedType = no document-type
        // filter), returning results from every filing type instead.
        //
        // Pin the contract on "EarningsCallTranscript" — there is no defined
        // DocumentType for it, so the description and the parser disagree.
        var method = typeof(RagSearchTools).GetMethod(
            "ParseDocumentType",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        var result = (DocumentType)method.Invoke(null, ["EarningsCallTranscript"]);

        result
            .Should()
            .NotBeNull(
                "the MCP tool description advertises 'EarningsCallTranscript' as an allowed value"
            );
    }
}
