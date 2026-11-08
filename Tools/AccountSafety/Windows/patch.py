"""Adapt the pinned upstream Store engine to IPA Desktop's Windows CLI contract."""
import re
import shutil
import sys
from pathlib import Path

root = Path(sys.argv[1])

def edit(name, change):
    path = root / name
    path.write_text(change(path.read_text(encoding='utf-8')), encoding='utf-8', newline='\n')

def once(text, before, after):
    if text.count(before) != 1:
        raise ValueError(f'Unexpected upstream source: {before[:100]}')
    return text.replace(before, after, 1)

def block(text, pattern, replacement, count=1):
    text, found = re.subn(pattern, lambda _: replacement, text, flags=re.S)
    if found != count:
        raise ValueError(f'Unexpected upstream source: {pattern}')
    return text

def root_command(s):
    s = once(s, '"errors"', '"errors"\n "fmt"\n "os"')
    s = once(s, '\tcmd.AddCommand(mcpCmd())\n', '')
    s = once(s, 'cmd.PersistentFlags().BoolVar(&verbose, "verbose", false, "enables verbose logs")', '')
    s = once(s, '\t\tverbose        bool\n', '')
    return once(s, 'func Execute() int {', '''func Execute() int {
        if len(os.Args) == 2 && os.Args[1] == "account-safety-info" {
            fmt.Println(desktopSafetyProfile)
            return 0
        }''')

def state(s):
    s = once(s, 'verbose := cmd.Flag("verbose").Value.String() == "true"', 'verbose := false')
    return once(s, '''stateDirectory := util.Must(prepareStateDirectory(dependencies.OS, dependencies.Machine.HomeDirectory()))
	dependencies.CookieJar = newCookieJar(stateDirectory)
	dependencies.Keychain = newKeychain(stateDirectory, interactive)''', '''stateDirectory := filepath.Join(util.Must(os.UserCacheDir()), "IPA-Desktop", "Store-v4")
    util.Must(struct{}{}, os.MkdirAll(stateDirectory, 0700))
    dependencies.CookieJar = desktopCookieJar()
    dependencies.Keychain = desktopKeychain(stateDirectory)''').replace('interactive, _ := cmd.Context().Value(interactiveKey).(bool)\n', '')

def login_command(s):
    s = once(s, 'enter Apple ID (email address or phone number): ', 'Enter Apple ID: ')
    s = once(s, 'readPrompt("Enter Apple ID: ", false)', 'readPrompt("Enter Apple ID: ", true)')
    s = once(s, 'readPrompt("enter 2FA code: ", false)', 'readPrompt("enter 2FA code: ", true)')
    s = once(s, 'Short: "Login to the App Store",\n\t\tRunE: func(cmd *cobra.Command, args []string) error {',
        '''Short: "Login to the App Store",
        RunE: func(cmd *cobra.Command, args []string) (result error) {
            defer func() { result = desktopLoginError(result, authCode != "") }()
            if email != "" || password != "" || authCode != "" {
                return errors.New("use interactive Apple login")
            }''')
    s = once(s, '"fmt"', '"fmt"\n "strings"')
    return s + '''
// Only allowlisted messages leave the engine's login process.
func desktopLoginError(err error, verification bool) error {
    if err == nil { return nil }
    message := strings.ToLower(err.Error())
    switch {
    case strings.Contains(message, "invalid credentials") && !verification:
        return errors.New("invalid credentials")
    case strings.Contains(message, "verification code rejected"),
         verification && strings.Contains(message, "did not complete verification"):
        return errors.New("verification code rejected")
    case strings.Contains(message, "account is disabled"),
         strings.Contains(message, "account has been locked"):
        return errors.New("account is disabled")
    case strings.Contains(message, "rate limited authentication"):
        return errors.New("rate limited by Apple (HTTP 429)")
    case strings.Contains(message, "agreement required"), strings.Contains(message, "terms and conditions"):
        return errors.New("agreement required")
    case strings.Contains(message, "prepare apple authentication"),
         strings.Contains(message, "prepare store amd proof"),
         strings.Contains(message, "apple store provisioning"),
         strings.Contains(message, "initialize sap action signer"):
        return errors.New("Store runtime preparation failed")
    default:
        return errors.New("Apple authentication did not complete")
    }
}
'''

def guarded_login(s):
    s = once(s, '"fmt"', '"fmt"\n "os"')
    s = once(s, '\t\tPassword:            password,\n', '')
    s = once(s, 'if t.authBackend != nil {', '''if input.Email == "" || input.Password == "" {
        return LoginOutput{}, errors.New("invalid credentials")
    }
    if t.authBackend != nil {''')
    s = once(s, '''} else if attempt == 1 && res.Data.FailureType == FailureTypeInvalidCredentials {
		retry = true''', '''} else if res.Data.FailureType == FailureTypeInvalidCredentials {
        err = errors.New("invalid credentials")
        if authCode != "" { err = errors.New("verification code rejected") }''')
    s = once(s, 'res, err = t.sendAuthenticationRequest(request)', '''res, err = t.sendAuthenticationRequest(request)
        status := res.StatusCode
        var responseErr *http.UnexpectedResponseError
        if status == 0 && errors.As(err, &responseErr) { status = responseErr.StatusCode }
        if status >= 100 && status <= 599 {
            fmt.Fprintf(os.Stderr, "IPA_AUTH_STATUS http=%d\\n", status)
        }''')
    return block(s, r'(func \(t \*appstore\) sendAuthenticationRequest\(request http.Request\) \(http.Result\[loginResult\], error\) \{).*?\n\}', '''func (t *appstore) sendAuthenticationRequest(request http.Request) (http.Result[loginResult], error) {
        result, err := t.loginClient.Send(request)
        if err != nil { return result, authenticationRequestError(err) }
        return result, nil
}''')

def no_relogin(s):
    s = block(s, r'\n\s*if errors.Is\(lastErr, appstore.ErrPasswordTokenExpired\) \{.*?\n\s*acc = loginResult.Account\n\s*\}', '')
    return s

edit('cmd/root.go', root_command)
edit('cmd/common.go', state)
edit('cmd/auth.go', login_command)
edit('pkg/appstore/appstore_login.go', guarded_login)
for name in ['purchase', 'purchases', 'list_versions', 'get_version_metadata']:
    edit(f'cmd/{name}.go', lambda s: no_relogin(s).replace('\n\t\t\tvar lastErr error\n', '')
        .replace('\n\t\t\t\t\tlastErr = err\n', '').replace('return errors.Is(err, appstore.ErrPasswordTokenExpired)', 'return false')
        .replace('retry.RetryIf(func(err error)', 'retry.RetryIf(func(_ error)'))
edit('cmd/download.go', lambda s: block(no_relogin(s),
    r'\n\s*if errors.Is\(err, appstore.ErrPasswordTokenExpired\) \{\s*return true\s*\}', ''))

# One library fetch, then filter locally; no duplicate requests for each UI page.
edit('pkg/appstore/appstore_owned_apps.go', lambda s:
    once(once(s, 'type OwnedAppsInput struct {', 'type OwnedAppsInput struct {\n DesktopHistory bool'),
         'pageApps := ownedAppsPage(apps, input.Page, input.Limit)',
         'pageApps := apps\n if !input.DesktopHistory { pageApps = ownedAppsPage(apps, input.Page, input.Limit) }'))
def history(s):
    s = once(s, '"fmt"', '"fmt"\n "strconv"\n "strings"')
    s = once(s, 'var page, maxResults int', 'var page, maxResults int\n var all bool\n var rangeValue string')
    s = once(s, 'Platform: platform,', 'Platform: platform,\n DesktopHistory: true,')
    s = once(s, 'dependencies.Logger.Log().', """apps := output.Results
                if !all {
                    cutoff := time.Now().AddDate(0, 0, -90)
                    for i := len(apps)-1; i >= 0; i-- {
                        date := apps[i].PurchaseDate
                        include := !date.Before(cutoff)
                        if rangeValue != "" {
                            include = strconv.Itoa(date.Year()) == strings.TrimSuffix(rangeValue, "-all")
                        }
                        if !include { apps = append(apps[:i], apps[i+1:]...) }
                    }
                }
                dependencies.Logger.Log().""")
    s = once(s, 'Int("count", output.Count).', 'Int("count", len(apps)).')
    s = once(s, 'Int("totalCount", output.TotalCount).', 'Int("totalCount", len(apps)).')
    s = once(s, 'Array("apps", appstore.Apps(output.Results)).', 'Array("apps", appstore.Apps(apps)).')
    return once(s, '\treturn cmd', '    cmd.Flags().BoolVar(&all, "all", false, "Return all owned apps")\n    cmd.Flags().StringVar(&rangeValue, "range", "", "Filter by calendar year (YEAR-all)")\n    return cmd')

edit('cmd/purchases.go', history)

shutil.copyfile(Path(__file__).with_name('desktop_windows.go'), root / 'cmd/desktop_windows.go')
shutil.copyfile(Path(__file__).with_name('desktop_windows_test.go'), root / 'cmd/desktop_windows_test.go')
shutil.copyfile(Path(__file__).with_name('desktop_guard_test.go'), root / 'pkg/appstore/desktop_guard_test.go')
