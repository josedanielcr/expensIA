# AGENTS.md

## Scope
GitHub workflow files live here.

## Deployment guardrails
- `main` deploys production to `email-processor-ai`.
- `staging` deploys staging to `email-processor-ai-staging`.
- Do not change branch triggers, app names, or secret names unless explicitly requested.

## Backend build
- Workflows build from `./backend`.
- Keep the build command aligned with the solution file:
  `dotnet build EmailParserService.slnx --configuration Release --output ./output`
