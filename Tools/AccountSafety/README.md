# Guarded ipatool 1.2.0.2

Based on Sorvigolova/ipatool 1.2.0 at the immutable revision in `pins.json`. MIT upstream credit/license is retained. This is a local policy patch, not a claim that Apple will accept the login or never restrict an account.

- No silent login with a stored password. New sessions do not store the Apple password, and passwords from old session files are ignored.
- A credential rejection stops the attempt immediately. Authentication transport failures are not retried.
- Apple-directed HTTPS redirects to `apple.com` subdomains remain supported (maximum four hops); a requested 2FA phase remains a separate explicit user action.
- Only HTTP status and numeric failure codes are emitted as safe auth diagnostics. Raw auth output remains private in IPA Desktop.
- The offline `account-safety-info` command lets IPA Desktop reject old or rolled-back binaries before passing credentials or running account operations.

`patch.py` checks hashes of the reviewed source files and stops on any upstream change. `test_auth.py` compiles the patched native login control flow with a fake HTTP transport and checks request counts for invalid passwords, transport failures, 2FA, redirects and account restrictions, without contacting Apple. The same tests prove silent login sends zero requests.

The existing Windows release workflow builds this engine only when the pinned engine release is absent. It publishes a separate binary ZIP, complete patched source ZIP and license files, then builds/releases the GUI. Future GUI releases reuse this engine; no extra workflow or scheduled job is added.

Version 1.2.0.2 rebuilds the same reviewed authentication patch with updated MinGW-w64 CRT/headers/pthreads (r569). `msys2-packages.txt` records the exact build environment. The safety profile and password retry policy are unchanged.
