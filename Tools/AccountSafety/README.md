# Guarded ipatool 1.2.1.1

Based on Sorvigolova/ipatool 1.2.1 at the immutable revision in `pins.json`. MIT upstream credit/license is retained. This is a local policy patch, not a claim that Apple will accept the login or never restrict an account.

- No silent login with a stored password. New sessions do not store the Apple password, and passwords from old session files are ignored.
- A credential rejection stops the attempt immediately. Authentication transport failures are not retried.
- Apple-directed HTTPS redirects to `apple.com` subdomains remain supported (maximum four hops); a requested 2FA phase remains a separate explicit user action.
- Only HTTP status and numeric failure codes are emitted as safe auth diagnostics. Raw auth output remains private in IPA Desktop.
- The offline `account-safety-info` command lets IPA Desktop reject old or rolled-back binaries before passing credentials or running account operations.

`patch.py` checks hashes of the reviewed source files and stops on any upstream change. `test_auth.py` compiles the patched native login control flow with a fake HTTP transport and checks request counts for invalid passwords, transport failures, 2FA, redirects and account restrictions, without contacting Apple. The same tests prove silent login sends zero requests.

The existing Windows release workflow builds this engine only when the pinned engine release is absent. It publishes a separate binary ZIP, complete patched source ZIP and license files, then builds/releases the GUI. Future GUI releases reuse this engine; no extra workflow or scheduled job is added.

Version 1.2.1.1 incorporates upstream commit `3d36465d614bea25fbfb6eed53fa51ac0f16bf5f` (Windows CFG fix). Authentication source files retain their reviewed hashes; `CMakeLists.txt` is also pinned before appending the offline fixture target. The safety profile and password retry policy are unchanged.

The MinGW Unicorn build uses GCC setjmp/longjmp builtins to avoid the Windows CRT unwind/CFG termination when returning from JIT code. No Windows security setting is disabled. `cfg_smoke.cpp` links the exact same Unicorn target, launches a child with per-process CFG explicitly enabled, checks the effective policy, and exercises instruction-limit, hook-stop and invalid-instruction exits repeatedly without Apple traffic. `verify_cfg.py` checks the actual configured header and runs the fixture outside MSYS2 before packaging. Its result and header hash are included as `cfg-verification.txt`.
