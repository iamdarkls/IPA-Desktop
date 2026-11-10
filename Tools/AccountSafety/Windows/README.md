# ipatool-guard 1.3.1.1 · Windows x64

Built from the complete [Sorvigolova/ipatool 1.3.1 release](https://github.com/Sorvigolova/ipatool/releases/tag/1.3.1). Uses upstream's bundled CoreADI implementation, including provisioning and machine sync. No patch from PR #5, Android package download, or previous helper state is needed.

IPA Desktop's safeguards remain: no saved Apple password, no silent sign-in, and no password replay after transport failures or rejection. Explicit Store device actions, validated redirects and user-entered 2FA remain supported.

The updater retains the previously installed engine for rollback. macOS and Linux keep their existing engine.
