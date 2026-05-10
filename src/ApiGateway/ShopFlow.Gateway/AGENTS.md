# ShopFlow.Gateway — module deltas

This module owns: the YARP reverse-proxy boundary in front of every module API. Validates JWT bearer tokens at the edge, then forwards `/api/{module}/*` to the corresponding module API cluster. No domain logic, no DbContext.

Deltas from root [`AGENTS.md`](../../../AGENTS.md):

1. **No quartet.** The gateway is a single project (root rule 6 explicit exception). No Domain / Application / Infrastructure split — there is no domain to model, only routing and cross-cutting auth.
2. **Routes live in configuration**, not code. New module → new route in `appsettings.json` `ReverseProxy:Routes` + matching cluster in `ReverseProxy:Clusters`. Code changes only when introducing a new cross-cutting concern (rate limit, circuit breaker, request transform).
3. **JWT validation at the boundary** — downstream module APIs trust the gateway-validated principal via the propagated `Authorization` header. Module APIs still re-validate (defense-in-depth: someone may hit the module directly during dev), but the gateway is the canonical enforcement point.

## Lifecycle invariants
- Cluster destinations match Aspire service-discovery names (`http://inventory-api`, etc.). Production deployments swap them via configuration; the gateway code is environment-agnostic.

## Phase-0 status
Routes for all 6 module APIs are wired even though only Inventory has substantive endpoints today. The other modules answer `/api/{module}/healthz` once they boot under the AppHost (deferred to U11+).
