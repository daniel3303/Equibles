-- Read-only completion query for the finite pre-international U.S. directory cohort.
BEGIN TRANSACTION READ ONLY;
SELECT count(*) AS mapped_listings,
       count(*) FILTER (WHERE listing."Id" IS NULL) AS missing_listings,
       count(*) FILTER (WHERE listing."MarketCountryCode" IS DISTINCT FROM 'US') AS wrong_market
FROM "LegacyEquityListing" mapping
LEFT JOIN "EquityListing" listing ON listing."Id" = mapping."EquityListingId";
SELECT "MarketCountryCode", count(*) FROM "EquityListing" GROUP BY "MarketCountryCode" ORDER BY 1;
ROLLBACK;
