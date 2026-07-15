# Security Policy

CS2 Trade Monitor handles local Steam, YouPin, and market data credentials. Treat
all logs, packet captures, screenshots, and exported data as sensitive until they
are reviewed and redacted.

## Supported Versions

Security fixes target the latest public release and the `master` branch.

## Reporting a Vulnerability

Use GitHub's **Report a vulnerability** entry to open a private security
advisory. Do not report an unpatched vulnerability in a public issue,
discussion, pull request, commit message, or screenshot. If private reporting is
temporarily unavailable, keep the report private until the repository owner has
restored that channel.

Do not include:

- Steam cookies, `steamLoginSecure`, session IDs, access tokens, refresh tokens,
  shared secrets, identity secrets, or Web API keys.
- YouPin tokens, session IDs, device IDs, device tokens, order numbers, user IDs,
  cookies, or captured request bodies.
- Full HAR files, packet captures, logs, screenshots with account data, or local
  paths containing personal information.

## Local Data

Runtime data is stored beside the application under `user-data`. The portable
release deliberately does not read, migrate, merge, move, or delete legacy
`%LocalAppData%\CS2TradeMonitor` or `%LocalAppData%\CS2DesktopMonitor` data.
The repository `.gitignore` excludes common runtime, log, credential, and
package artifacts; maintainers are still responsible for checking changes
before commit.

## Capture Handling

Do not commit raw HAR files, packet captures, real request/response bodies,
signatures, reusable headers, or account traffic. Use synthetic, non-replayable
examples and keep real capture files outside the Git tree.
