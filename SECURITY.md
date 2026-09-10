# Security policy

This policy covers vulnerabilities in [Borea](https://github.com/KSAModding/Borea) and its release process.
Mod and index content problems use the separate reporting paths below.

## Report a vulnerability

Report a vulnerability in Borea or its release process through [GitHub private vulnerability reporting](https://github.com/KSAModding/Borea/security/advisories/new).
Do not open a public issue or disclose the details before the maintainers have had time to investigate and prepare a fix.

Include the affected Borea version, the impact, and clear steps to reproduce the problem when you can.
Use the [public issue tracker](https://github.com/KSAModding/Borea/issues) for an ordinary bug that does not put users, their data, or their systems at risk.

## Report content problems elsewhere

To report a problem caused by a mod, use that mod's repository or support channel.
To report a harmful mod archive or a problem with an index listing or release metadata, use the [takedown form in the KSA content index repository](https://github.com/KSAModding/content-index/issues/new?template=takedown.yml).
The [content index policy](https://github.com/KSAModding/content-index/blob/main/POLICY.md) explains what the index can remove and what remains under the control of the release host.

## What to expect

Borea is maintained by volunteers and has no on-call rota.
We aim to acknowledge a private report as soon as we can, but we cannot guarantee a response or fix time.
We will assess the report, keep the reporter informed when we have useful news, and prepare a fix before public disclosure when possible.
Please coordinate public disclosure with us so that we can prepare a fix when possible.

Only the latest published Borea release receives security fixes.
Users must update to that release to receive a fix.

## Release and dependency safeguards

Dependabot checks NuGet packages and GitHub Actions each week.
The .NET SDK also audits the resolved NuGet packages during restore.
