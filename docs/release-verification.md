# 2.0.1-rc.1 release verification

## Scope and compatibility

This release changes the target runtime from .NET 8 to .NET 10 LTS. App and regression projects, CI, displayed version, assembly metadata, and source instructions move together. No transfer protocol, settings schema, history schema, or networking implementation changes. Existing settings and received files remain in place. Older peers retain the same protocol; mixed-version physical-peer testing remains pending.

Microsoft ended .NET 8 support on November 10, 2026. See the [official support policy](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core). Use current serviced .NET 10 patches; self-contained downloads must be rebuilt when runtime security fixes ship.

## Release gates

- Build application and run all loopback regression checks on .NET 10.
- Check direct and transitive NuGet dependencies for known vulnerabilities.
- Publish self-contained Windows x64 output from the exact released commit.
- Include the app license, dependency/runtime notices, source commit, runtime version, and SHA-256 checksum.
- Verify native startup and shutdown of the packaged executable.
- Wait for successful Windows CI at the exact released commit.

The release remains a prerelease because two physical PCs, WiFi Direct hardware, and mixed-version peer tests are unavailable. Throughput and multi-gigabyte hardware benchmarks are not claimed. No signing identity is configured; the executable is unsigned. A checksum verifies downloaded bytes, not publisher identity. Use trusted peers on trusted networks; traffic remains plaintext.

## Rollback

Keep v2.0.0 and its assets intact. Revert this maintenance commit to restore the previous source target; do not silently move release tags or replace old assets. Settings and user files need no migration. Prefer rebuilding the previous protocol implementation on a supported runtime rather than distributing an unsupported .NET 8 build.
