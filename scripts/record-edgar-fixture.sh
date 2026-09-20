#!/usr/bin/env bash
#
# Records one company's SEC EDGAR `companyfacts` document as a test fixture for the EDGAR
# normaliser (ADR 0010, issue #72), and refreshes the recorded copy of the SEC ticker file.
#
# A fixture is the real document, cut down and never edited: a whole `companyfacts` runs to
# several megabytes, so only the concepts the normaliser reads are kept (the revenue candidates,
# net income, diluted shares), plus every `dei` cover-page concept and the IFRS revenue and profit
# concepts, so that what a fixture *lacks* (a cover-page share count, any us-gaap fact at all) is
# as true of the fixture as of the filer. Facts for periods ending before 2019 are dropped. Every
# value, date, form and accession number that remains is exactly as the SEC served it.
#
# The expected figures in EdgarNormaliserTests were derived by hand from these files. Re-recording
# a fixture after the company has filed again moves them, and the test must be re-derived with it.
#
# Needs: bash, curl, jq, perl. EDGAR refuses requests without a contact address in the User-Agent;
# it is read from Edgar__ContactEmail, the same variable the API reads (infra/README.md).
#
# Usage: Edgar__ContactEmail=you@example.com scripts/record-edgar-fixture.sh TICKER [TICKER...]
set -Eeuo pipefail

: "${Edgar__ContactEmail:?set Edgar__ContactEmail to the contact address EDGAR should see}"
[ $# -gt 0 ] || { echo "usage: $0 TICKER [TICKER...]" >&2; exit 2; }

fixtures="$(cd "$(dirname "$0")/.." && pwd)/api/Compass.Api.Tests/Fixtures/Edgar"
user_agent="Compass fixture recording $Edgar__ContactEmail"
mkdir -p "$fixtures"

fetch() { curl --silent --show-error --fail --compressed -H "User-Agent: $user_agent" "$1"; }

# The ticker file is recorded whole: it is the ticker map's fixture, and it is one line per company.
fetch https://www.sec.gov/files/company_tickers.json \
  | jq -c . | perl -pe 's/("\d+":\{)/\n$1/g; s/\}$/\n}/' > "$fixtures/company_tickers.json"

for ticker in "$@"; do
  symbol="$(tr '[:lower:]' '[:upper:]' <<<"$ticker")"
  cik="$(jq -r --arg t "$symbol" '[.[] | select(.ticker == $t)][0].cik_str // empty' "$fixtures/company_tickers.json")"
  [ -n "$cik" ] || { echo "$symbol is not in the SEC ticker file" >&2; exit 1; }
  sleep 0.2 # EDGAR asks for fewer than ten requests a second

  fetch "https://data.sec.gov/api/xbrl/companyfacts/CIK$(printf '%010d' "$cik").json" | jq --indent 1 '
    ["Revenues",
     "RevenueFromContractWithCustomerExcludingAssessedTax",
     "RevenueFromContractWithCustomerIncludingAssessedTax",
     "SalesRevenueNet",
     "RevenuesNetOfInterestExpense",
     "SalesRevenueGoodsNet",
     "NetIncomeLoss",
     "WeightedAverageNumberOfDilutedSharesOutstanding",
     "Revenue", "ProfitLoss", "ProfitLossAttributableToOwnersOfParent"] as $read
    | .facts |= with_entries(
        (.key == "dei") as $cover
        | .value |= with_entries(
            select($cover or (.key | IN($read[])))
            | .value |= {units: (.units
                | map_values(map(select(.end >= "2019-01-01")))
                | with_entries(select(.value | length > 0)))}
            | select(.value.units | length > 0)))' \
    | perl -0pe 's/\{\s+("(?:start|end)":[^{}]*?)\s+\}/"{ " . ($1 =~ s{\s*\n\s*}{ }gr) . " }"/ge' \
    > "$fixtures/$symbol.json"

  echo "recorded $symbol (CIK $cik): $(wc -c < "$fixtures/$symbol.json" | tr -d ' ') bytes"
done
