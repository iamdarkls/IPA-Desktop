# ipatool-guard 1.2.1.5 · Windows x64

Built from [Sorvigolova/ipatool PR #5, d5050b1](https://github.com/Sorvigolova/ipatool/pull/5/changes/d5050b1604b8b8964061e59c789ba975b4a68691). Store authentication uses SAP and local device proof. The engine downloads and verifies the required Apple runtime libraries during first provisioning; they are not included in the release archive. Store requests for licenses and downloads also use device proof.

The engine does not save the Apple password or silently sign in. It continues only for a validated Apple authentication redirect, an explicit device provisioning challenge, or a verification code supplied by the user. Network errors and rejected credentials do not cause password replays.

Sign in after switching from the Go engine; the session formats differ. The updater retains the previously installed engine for rollback. macOS and Linux keep their existing engine.
