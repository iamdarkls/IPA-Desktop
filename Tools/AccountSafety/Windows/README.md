# ipatool-guard 1.2.1.4 · Windows x64

Built from [majd/ipatool commit 72c47d5](https://github.com/majd/ipatool/commit/72c47d538b726df397fbf17bb3af809d69ef51b7). It retains local App Store authentication and uses the current Store User-Agent for purchase history and license acquisition. No WSL or third-party authentication service is required. On first use upstream downloads and verifies runtime files from Apple and Unicorn; the account password goes only to Apple's login endpoint.

IPA Desktop sends login input via its private terminal. The engine stores session tokens and device provisioning state under the current Windows user with DPAPI. It never persists the Apple password or silently signs in after a session expires. It only continues login when Apple requests a protocol step (device provisioning, validated redirects, or 2FA confirmed by the user). Failed transport requests and password rejections are not replayed.

The Windows updater installs this engine separately. Sessions from engine 1.2.1.3 are compatible. Users upgrading from the older C++ engine need to sign in again. macOS and Linux retain the previous engine.
