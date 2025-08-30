AGENTS

Purpose
- Document how automated coding agents interact with this codebase and the conventions they must follow.

Scope
- This repository is a .NET 8 Razor Pages app plus a supporting library. Agents must prioritize Razor Pages patterns over MVC/Blazor.
- Only software-development tasks are in scope (coding, refactoring, tests, docs). No secrets handling or non-dev tasks.

Agent responsibilities
- Implement small, scoped changes with clear rationale and minimal blast radius.
- Use existing abstractions first (PeopleService, FolkRawClient, SafeSoapLogger, configuration/options, DI).
- Prefer configuration over code for tunables (e.g., Retry:Http:Attempts/BaseDelayMs/JitterMs).
- Keep user-facing strings localizable and reuse existing resources where possible.

Conventions to enforce
- Logging
  - Use LoggerMessage source generators (partial methods or LoggerMessage.Define) instead of direct logger.LogX calls in hot paths.
  - Place Exception as the last parameter in LoggerMessage methods.
- Razor Pages
  - Do not migrate to MVC controllers or Blazor. Keep page handlers (OnGet/OnPost) and validation attributes.
- Options/config
  - Bind options from IConfiguration (AddAppOptions) and expose operational knobs in appsettings.
  - Respect existing keys:
    - Retry:Http:{Attempts,BaseDelayMs,JitterMs}
    - Http:BypassServerCertificateValidation
    - XRoad:* and Operations:* sections
- SOAP templates
  - FolkRawClient falls back to built-in templates. Missing XML files should at most log a warning (already implemented in ConfigurationLoader).
- Token handling
  - Token insertion is configurable. Default behavior is validated; avoid hard-coding.
- Style
  - Keep methods small, early-return on validation errors, and prefer immutable locals.
  - Use sealed partial classes when adding LoggerMessage methods in page models/services.

Typical tasks for agents
- Add or adjust LoggerMessage delegates to satisfy analyzers (e.g., CA1848/CA2254).
- Surface tunables via configuration (retries, timeouts, feature flags) and consume them via DI.
- Improve resiliency and diagnostics without breaking Razor Pages UX.
- Add minimal docs and guardrails for ops (e.g., describing config keys).

Safety and guardrails
- Never log sensitive values (tokens, passwords). SafeSoapLogger and SoapSanitizer are available; masking is on by default via Logging:MaskTokens.
- Do not introduce secrets into source. Read credentials only from configuration/user secrets/ENV.
- Preserve localization (IStringLocalizer/IViewLocalizer) and validation behavior.

Workflow expectations
- Analyze the workspace structure before editing. Prefer targeted edits.
- Build after edits and address compiler/analyzer warnings introduced by the change.
- Keep PRs small with clear descriptions. Include a brief changelog of files touched and why.

Operational knobs (reference)
- Retry:Http
  - Attempts (int): number of HTTP retry attempts.
  - BaseDelayMs (int): base backoff in milliseconds.
  - JitterMs (int): max jitter in milliseconds.
- Operations:GetPeoplePublicInfo:XmlPath / Operations:GetPerson:XmlPath
  - Optional custom SOAP templates; if missing, built-ins are used.
- XRoad:TokenInsert:*
  - Controls token placement in SOAP payloads.

Limitations
- Agents must not introduce new frameworks or large dependencies without strong justification.
- Do not change public API shapes of the library without documenting the impact.

Contact
- Open an issue with a concise task description. Include:
  - Problem statement
  - Desired outcome
  - Affected files/areas
  - Any configuration relevant to the change
