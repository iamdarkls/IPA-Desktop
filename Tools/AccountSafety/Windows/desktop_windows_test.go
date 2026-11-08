package cmd

import (
	"bytes"
	"os"
	"testing"
)

func TestDesktopSessionStorage(t *testing.T) {
	store := desktopKeychain(t.TempDir())
	for _, secret := range [][]byte{[]byte("session-one"), bytes.Repeat([]byte("session-two"), 4096)} {
		if err := store.Set("account", secret); err != nil {
			t.Fatal(err)
		}
		encrypted, err := os.ReadFile(store.path("account"))
		if err != nil || bytes.Contains(encrypted, secret[:11]) {
			t.Fatal("session was not encrypted")
		}
		if got, err := store.Get("account"); err != nil || !bytes.Equal(got, secret) {
			t.Fatalf("session read failed: %v", err)
		}
	}
	if err := store.Remove("account"); err != nil {
		t.Fatal(err)
	}
	if _, err := store.Get("account"); !os.IsNotExist(err) {
		t.Fatalf("session still exists: %v", err)
	}
}
