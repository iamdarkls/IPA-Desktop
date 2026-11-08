# ipatool-guard 1.2.1.3 · Windows x64

Built from [majd/ipatool commit 001d889](https://github.com/majd/ipatool/commit/001d889896fcb118546443c5dfe0997d11b74f56), which restores local App Store authentication and updates the download protocol. No WSL or third-party authentication service is required. On first use upstream downloads and verifies runtime files from Apple and Unicorn; the account password goes only to Apple's login endpoint.

IPA Desktop sends login input via its private terminal. The engine stores session tokens and device provisioning state under the current Windows user with DPAPI. It never persists the Apple password or silently signs in after a session expires. It only continues login when Apple requests a protocol step (device provisioning, validated redirects, or 2FA confirmed by the user). Failed transport requests and password rejections are not replayed.

The Windows updater installs this engine separately. Sign in again after updating: sessions from the previous engine use a different storage format. macOS and Linux retain the previous engine.
