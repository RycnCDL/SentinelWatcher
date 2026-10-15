# Contributing

Thanks for helping with SentinelWatcher. Bug reports, ideas and pull requests are welcome.

## Before you start

- For bugs and feature ideas, open an issue first, so we can agree on the approach before you write code.
- Security problems go through private reporting, see [SECURITY.md](SECURITY.md).
- Never put real tenant IDs, workspace resource IDs, tokens or log data in issues, commits or screenshots.

## Development setup

Follow [docs/local-development.md](docs/local-development.md). In short: .NET 10 SDK, Node.js 22, Azure CLI, an app registration created with `scripts/setup-app-registration.sh`, and `az login` with read access to a test workspace.

## Checks before a pull request

Run these from the repository root. CI runs the same checks.

```bash
dotnet test SentinelWatcher.sln -c Release
cd src/client && npm ci && npm run lint && npm run build
```

If you changed `infra/main.bicep` or `infra/modules/*.bicep`, regenerate the ARM template and commit it:

```bash
az bicep build --file infra/main.bicep --outfile infra/azuredeploy.json
```

## Rules for changes

- **Security model:** the API must never run a query, ARM path or Resource Graph query taken from the request. The only inputs are the workspace resource ID (matched against the allow-list) and the Sentinel Health `days` value. `EndpointInputSurfaceTests` fails when a new parameter appears; adding one needs a review of [docs/security-model.md](docs/security-model.md).
- **Data sent to the browser:** keep free text that can contain names (health descriptions, incident titles, resource IDs of other workspaces) on the server, or document why it is needed.
- **Permissions:** a new Azure call needs the matching permission in `infra/` and in [docs/permissions.md](docs/permissions.md). Prefer permissions already granted by Microsoft Sentinel Reader.
- **Tests:** add or update tests for new behavior. The test project fakes Log Analytics and ARM (`tests/SentinelWatcher.Api.Tests/Fakes`), so no Azure access is needed.
- **Writing style:** English, short and direct. Do not use em dashes or en dashes anywhere (code, comments, UI text, docs, commit messages); use commas, colons or separate sentences.

## Commits and pull requests

- Small commits with messages in the style `type(scope): summary`, for example `fix(api): ...`, `feat: ...`, `docs: ...`.
- Describe what changed and why in the pull request, and how you tested it.
- Update [CHANGELOG.md](CHANGELOG.md) under **Unreleased** for changes users will notice.

By contributing you agree that your contribution is licensed under the [MIT License](LICENSE).
