# Ask your AI assistant about a company's exempt offerings (Form D)

Equibles imports SEC Form D notices — the filings companies submit when they raise private capital through a Regulation D exempt offering — and exposes them through the MCP server, so you can ask your AI assistant how a company is raising money privately, alongside its public filings. To browse the same notices on the web portal, see [View a company's exempt offerings](how-to-view-exempt-offerings.md).

## Before you start

- Connect an AI assistant to the MCP server first — see [Connect an AI assistant](tutorial-connect-ai-assistant.md).
- Let the worker run after startup so Form D notices have been imported from SEC EDGAR.

## Ask about exempt offerings

Name a company and ask about its private fundraising:

- "Has AAPL filed any Form D offerings?"
- "Show me Stripe's recent private placements."
- "How much has this company raised privately, and from how many investors?"

The assistant picks the `GetExemptOfferings` tool and returns the company's recent Form D notices.

## What you should see

A table of recent Form D offerings, each showing the issuer, the total offering amount and the amount sold so far (a dollar figure, or "Indefinite" when the issuer doesn't cap it), the minimum investment, the number of investors, the claimed Regulation D exemptions, and whether the notice is an amendment.

If the reply says there are no exempt offerings, the company may simply have no Form D filings — confirm the worker has run, then try a company known to raise private capital.
