# Security policy

## Supported versions

| Version | Supported |
|---------|-----------|
| 0.1.x   | Yes       |

Only the latest release gets security fixes.

## Reporting a vulnerability

Please do not report security problems in public issues, discussions or pull requests.

Report them privately through GitHub instead:

1. Open the [Security tab](https://github.com/RycnCDL/SentinelWatcher/security) of the repository.
2. Select **Report a vulnerability**.
3. Describe the problem, the affected version, and the steps to reproduce it.

Do not include real tenant IDs, workspace resource IDs, tokens, log data or screenshots with customer data. Placeholders are enough.

SentinelWatcher is a personal open-source project without a support agreement. Reports are answered through the private advisory as soon as possible. When a fix is released, the advisory is published and credits the reporter unless they ask not to be named.

## What counts as a vulnerability

SentinelWatcher shows Microsoft Sentinel data to people who have no Azure role, using its own managed identity. Its security model is described in [docs/security-model.md](docs/security-model.md). Problems that break that model are in scope, for example:

- reaching any `/api` endpoint without a signed-in user that has the `SentinelWatcher.Viewer` app role
- reading a workspace that is not on the configured allow-list
- getting the API to run a query, ARM request or Resource Graph query that is not fixed in the code
- data reaching the browser that the documentation says stays on the server, such as raw health event descriptions or resource IDs of other workspaces
- the managed identity getting more Azure permissions from the provided templates than documented

Out of scope:

- what a person with the Viewer role can see in the dashboards: that is the purpose of the app
- what an administrator with Owner rights on the app or workspace resource group can do
- findings in dependencies without a way to exploit them in SentinelWatcher (please report those upstream)
