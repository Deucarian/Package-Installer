# Security policy

## Supported versions

Security fixes are considered for the current `main` package channel. `develop` is a preview channel and may receive a fix before promotion. Older commits and locally modified copies are not guaranteed to receive backports.

## Report a vulnerability privately

Use [GitHub private vulnerability reporting](https://github.com/Deucarian/Package-Installer/security/advisories/new). Do not open a public issue for a suspected vulnerability.

Include the affected installer version or commit, Unity version and operating system, selected channel, impact, a minimal reproduction or proof of concept, and known mitigations. Redact tokens, local paths, private repository URLs, registry credentials, and personal data.

The maintainers will triage the report in GitHub's private advisory, may ask for additional evidence, and will coordinate disclosure after a fix or mitigation is available. No response or remediation deadline is guaranteed.

Security scope includes registry retrieval and parsing, Git/package URL handling, dependency resolution, package operations, update checks, and editor UI supplied by this package. Unity Package Manager and downstream packages retain their own security processes.
