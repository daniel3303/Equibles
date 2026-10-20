# OpenFIGI mapping captures

- `tokyo-request.json` and `tokyo-response.json` were captured unchanged on 2026-10-20 by POSTing the request body to `https://api.openfigi.com/v3/mapping` without an API key.
  - The jobs ask the Tokyo exchange code `JT` for local codes 7203 (Toyota Motor), 8951 (Nippon Building Fund), 130A (Veritas In Silico) and 0000, and for Toyota's ISIN `JP3633400001`.
  - OpenFIGI answered 7203 and 130A as `Common Stock`, 8951 as `REIT`, the ISIN with the same 7203 line, and 0000 with the warning "No identifier found.".
- OpenFIGI's API documentation: https://www.openfigi.com/api/documentation.
