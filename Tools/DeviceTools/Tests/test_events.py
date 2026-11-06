"""Check the shipped C bridge's pipe protocol and subscription ownership without an iPhone."""
from pathlib import Path
import os
import shutil
import subprocess
import tempfile
import unittest


@unittest.skipUnless(shutil.which('cc'), 'C compiler is required for the native bridge fixture')
class EventBridgeChecks(unittest.TestCase):
    def test_eof_unsubscribes_and_protocol_contains_no_device_identity(self):
        source = Path(__file__).resolve().parents[1] / 'ipa-device-events.c'
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            header = root / 'libimobiledevice/libimobiledevice.h'
            header.parent.mkdir()
            header.write_text('''
typedef struct {const char *udid;} idevice_event_t;
typedef void *idevice_subscription_context_t;
#define IDEVICE_E_SUCCESS 0
int idevice_events_subscribe(idevice_subscription_context_t *, void (*)(const idevice_event_t *, void *), void *);
int idevice_events_unsubscribe(idevice_subscription_context_t);
''')
            stub = root / 'stub.c'
            stub.write_text('''
#include <stdio.h>
#include <stdlib.h>
#include <libimobiledevice/libimobiledevice.h>
int idevice_events_subscribe(idevice_subscription_context_t *ctx, void (*cb)(const idevice_event_t *, void *), void *data) {
 if (getenv("IPA_FIXTURE_FAIL")) return -1;
 *ctx=(void*)1; idevice_event_t event={"private-udid"}; cb(&event,data); return 0;
}
int idevice_events_unsubscribe(idevice_subscription_context_t ctx) { fputs("unsubscribed",stderr); return ctx ? 0 : 1; }
''')
            binary = root / 'events'
            subprocess.run(['cc', '-Wall', '-Wextra', '-Werror', '-I', str(root), str(source), str(stub), '-o', str(binary)], check=True)
            self.assertEqual(subprocess.check_output([binary, '--version'], text=True).strip(), 'IPA-DESKTOP-DEVICE-EVENTS/1')
            result = subprocess.run([binary], input='', capture_output=True, text=True, timeout=5)
            self.assertEqual(result.returncode, 0)
            self.assertEqual(result.stdout.splitlines(), ['changed', 'ready'])
            self.assertEqual(result.stderr, 'unsubscribed')
            failed = subprocess.run([binary], input='', capture_output=True, text=True, timeout=5, env=dict(os.environ, IPA_FIXTURE_FAIL='1'))
            self.assertNotEqual(failed.returncode, 0)
            self.assertEqual(failed.stdout, '')
            self.assertEqual(failed.stderr, '')
