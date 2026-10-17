namespace Equibles.IntegrationTests.Sec;

/// <summary>
/// Real N-CEN submissions trimmed to the elements <c>NCenFilingProcessor</c> reads. N-CEN states a
/// US firm's country as a <c>*StateCountry</c> attribute and a foreign firm's as an element, and a
/// "Y" family answer as an attribute of <c>registrantFamilyInvComp</c>.
/// </summary>
internal static class NCenSubmissionFixtures
{
    // Vanguard Windsor Funds, accession 0000107606-26-000003: a fund-family member whose US
    // providers use the attribute form; one pricing service and one custodian are foreign.
    internal const string VanguardWindsor = """
        <SEC-DOCUMENT>0000107606-26-000003.txt
        <DOCUMENT>
        <TYPE>N-CEN
        <TEXT>
        <XML>
        <?xml version="1.0" encoding="UTF-8"?>
        <edgarSubmission xmlns="http://www.sec.gov/edgar/ncen">
          <headerData>
            <submissionType>N-CEN</submissionType>
            <filerInfo>
              <investmentCompanyType>N-1A</investmentCompanyType>
            </filerInfo>
          </headerData>
          <formData>
            <generalInfo reportEndingPeriod="2025-10-31" isReportPeriodLt12="N" />
            <registrantInfo>
              <registrantFullName>VANGUARD WINDSOR FUNDS</registrantFullName>
              <investmentCompFileNo>811-00834</investmentCompFileNo>
              <registrantLei>5493006MMNJGHJDZOD60</registrantLei>
              <registrantstate>US-PA</registrantstate>
              <registrantcountry>US</registrantcountry>
              <isRegistrantFirstFiling>N</isRegistrantFirstFiling>
              <isRegistrantLastFiling>N</isRegistrantLastFiling>
              <registrantFamilyInvComp isRegistrantFamilyInvComp="Y" familyInvCompFullName="The Vanguard Group, Inc." />
              <principalUnderwriters>
                <principalUnderwriter>
                  <principalUnderwriterName>VANGUARD MARKETING CORP.</principalUnderwriterName>
                  <principalUnderWriterStateCountry principalUnderWriterCountry="US" principalUnderWriterState="US-PA" />
                  <isPrincipalUnderwriterAffiliatedWithRegistrant>Y</isPrincipalUnderwriterAffiliatedWithRegistrant>
                </principalUnderwriter>
              </principalUnderwriters>
              <publicAccountants>
                <publicAccountant>
                  <publicAccountantName>PRICEWATERHOUSECOOPERS LLP</publicAccountantName>
                  <publicAccountantStateCountry publicAccountantCountry="US" publicAccountantState="US-PA" />
                </publicAccountant>
              </publicAccountants>
            </registrantInfo>
            <managementInvestmentQuestionSeriesInfo>
              <managementInvestmentQuestion>
                <mgmtInvFundName>VANGUARD WINDSOR FUND</mgmtInvFundName>
                <mgmtInvSeriesId>S000004417</mgmtInvSeriesId>
                <investmentAdvisers>
                  <investmentAdviser>
                    <investmentAdviserName>Wellington Management Company LLP</investmentAdviserName>
                    <investmentAdviserStateCountry investmentAdviserState="US-MA" investmentAdviserCountry="US" />
                  </investmentAdviser>
                </investmentAdvisers>
                <transferAgents>
                  <transferAgent>
                    <transferAgentName>THE VANGUARD GROUP, INC.</transferAgentName>
                    <transferAgentStateCountry transferAgentState="US-PA" transferAgentCountry="US" />
                    <isTransferAgentAffiliated>Y</isTransferAgentAffiliated>
                  </transferAgent>
                </transferAgents>
                <pricingServices>
                  <pricingService>
                    <pricingServiceName>Bloomberg Finance LP</pricingServiceName>
                    <pricingServiceStateCountry pricingServiceState="US-NY" pricingServiceCountry="US" />
                    <isPricingServiceAffiliated>N</isPricingServiceAffiliated>
                  </pricingService>
                  <pricingService>
                    <pricingServiceName>S&amp;P Global Limited</pricingServiceName>
                    <pricingServiceCountry>GB</pricingServiceCountry>
                    <isPricingServiceAffiliated>N</isPricingServiceAffiliated>
                  </pricingService>
                </pricingServices>
                <custodians>
                  <custodian>
                    <custodianName>Mizuho Bank, Limited</custodianName>
                    <custodianCountry>JP</custodianCountry>
                    <isCustodianAffiliated>N</isCustodianAffiliated>
                  </custodian>
                  <custodian>
                    <custodianName>State Street Bank and Trust Company</custodianName>
                    <custodianStateCountry custodianState="US-MA" custodianCountry="US" />
                    <isCustodianAffiliated>N</isCustodianAffiliated>
                  </custodian>
                </custodians>
                <shareholderServicingAgents>
                  <shareholderServicingAgent>
                    <shareholderServiceAgentName>The Vanguard Group, Inc.</shareholderServiceAgentName>
                    <shareholderServiceAgentStateCountry shareholderServiceAgentState="US-PA" shareholderServiceAgentCountry="US" />
                    <isShareholderServiceAgentAffiliated>Y</isShareholderServiceAgentAffiliated>
                  </shareholderServicingAgent>
                </shareholderServicingAgents>
                <admins>
                  <admin>
                    <adminName>The Vanguard Group, Inc.</adminName>
                    <adminStateCountry adminState="US-PA" adminCountry="US" />
                    <isAdminAffiliated>Y</isAdminAffiliated>
                  </admin>
                </admins>
              </managementInvestmentQuestion>
            </managementInvestmentQuestionSeriesInfo>
          </formData>
        </edgarSubmission>
        </XML>
        </TEXT>
        </DOCUMENT>
        </SEC-DOCUMENT>
        """;

    // ETF Series Solutions, accession 0001193125-26-421574: not a family member, with a US
    // sub-adviser in the attribute form.
    internal const string EtfSeriesSolutions = """
        <SEC-DOCUMENT>0001193125-26-421574.txt
        <DOCUMENT>
        <TYPE>N-CEN
        <TEXT>
        <XML>
        <?xml version="1.0" encoding="UTF-8"?>
        <edgarSubmission xmlns="http://www.sec.gov/edgar/ncen">
          <headerData>
            <submissionType>N-CEN</submissionType>
            <filerInfo>
              <investmentCompanyType>N-1A</investmentCompanyType>
            </filerInfo>
          </headerData>
          <formData>
            <generalInfo reportEndingPeriod="2026-07-31" isReportPeriodLt12="N" />
            <registrantInfo>
              <registrantFullName>ETF Series Solutions</registrantFullName>
              <investmentCompFileNo>811-22668</investmentCompFileNo>
              <registrantLei>5493008BB0X0GYS3UI50</registrantLei>
              <registrantstate>US-WI</registrantstate>
              <registrantcountry>US</registrantcountry>
              <isRegistrantFirstFiling>N</isRegistrantFirstFiling>
              <isRegistrantLastFiling>N</isRegistrantLastFiling>
              <isRegistrantFamilyInvComp>N</isRegistrantFamilyInvComp>
            </registrantInfo>
            <managementInvestmentQuestionSeriesInfo>
              <managementInvestmentQuestion>
                <mgmtInvFundName>US Vegan Climate ETF</mgmtInvFundName>
                <mgmtInvSeriesId>S000063769</mgmtInvSeriesId>
                <investmentAdvisers>
                  <investmentAdviser>
                    <investmentAdviserName>Beyond Investing LLC</investmentAdviserName>
                    <investmentAdviserStateCountry investmentAdviserState="US-FL" investmentAdviserCountry="US" />
                  </investmentAdviser>
                </investmentAdvisers>
                <subAdvisers>
                  <subAdviser>
                    <subAdviserName>Penserra Capital Management LLC</subAdviserName>
                    <isSubAdviserAffiliated>N</isSubAdviserAffiliated>
                    <subAdviserStateCountry subAdviserState="US-CA" subAdviserCountry="US" />
                  </subAdviser>
                </subAdvisers>
              </managementInvestmentQuestion>
            </managementInvestmentQuestionSeriesInfo>
          </formData>
        </edgarSubmission>
        </XML>
        </TEXT>
        </DOCUMENT>
        </SEC-DOCUMENT>
        """;
}
