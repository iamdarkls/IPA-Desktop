package cmd

import (
	"errors"
	"fmt"
	"io/fs"
	"net/http/cookiejar"
	"os"
	"path/filepath"
	"unsafe"

	"github.com/majd/ipatool/v2/pkg/http"
	"golang.org/x/sys/windows"
)

const desktopSafetyProfile = "IPA-DESKTOP-AUTH-SAFETY/1"

type desktopKeychain string

func (k desktopKeychain) path(key string) string {
	return filepath.Join(string(k), fmt.Sprintf("%x.dpapi", key))
}

func (k desktopKeychain) Get(key string) ([]byte, error) {
	encrypted, err := os.ReadFile(k.path(key))
	if err != nil {
		return nil, err
	}
	return desktopDPAPI(encrypted, false)
}

func (k desktopKeychain) Set(key string, data []byte) error {
	encrypted, err := desktopDPAPI(data, true)
	if err != nil {
		return err
	}
	f, err := os.CreateTemp(string(k), "session-*")
	if err != nil {
		return err
	}
	defer os.Remove(f.Name())
	_, writeErr := f.Write(encrypted)
	if err := errors.Join(writeErr, f.Close()); err != nil {
		return err
	}
	return os.Rename(f.Name(), k.path(key))
}

func (k desktopKeychain) Remove(key string) error {
	err := os.Remove(k.path(key))
	if errors.Is(err, fs.ErrNotExist) {
		return nil
	}
	return err
}

func desktopDPAPI(data []byte, protect bool) ([]byte, error) {
	if len(data) == 0 || len(data) > 16<<20 {
		return nil, errors.New("invalid Windows session data size")
	}
	in := windows.DataBlob{Size: uint32(len(data)), Data: &data[0]}
	var out windows.DataBlob
	var err error
	if protect {
		err = windows.CryptProtectData(&in, nil, nil, 0, nil, windows.CRYPTPROTECT_UI_FORBIDDEN, &out)
	} else {
		err = windows.CryptUnprotectData(&in, nil, nil, 0, nil, windows.CRYPTPROTECT_UI_FORBIDDEN, &out)
	}
	if err != nil {
		return nil, err
	}
	defer windows.LocalFree(windows.Handle(unsafe.Pointer(out.Data)))
	view := unsafe.Slice(out.Data, out.Size)
	defer clear(view)
	return append([]byte(nil), view...), nil
}

// Authentication cookies are available during the command (including 2FA).
// The following commands use the DPAPI-protected Store account and AMD state.
type desktopCookies struct{ *cookiejar.Jar }

func (desktopCookies) Save() error { return nil }

func desktopCookieJar() http.CookieJar {
	jar, err := cookiejar.New(nil)
	if err != nil {
		panic(err)
	}
	return desktopCookies{jar}
}
