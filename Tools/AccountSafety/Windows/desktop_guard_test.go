package appstore

import (
	"errors"
	"fmt"
	"testing"

	"github.com/majd/ipatool/v2/pkg/http"
	"go.uber.org/mock/gomock"
)

func TestDesktopDoesNotReplayLogin(t *testing.T) {
	for _, status := range []int{204, 403, 404, 429, 503} {
		t.Run(fmt.Sprint(status), func(t *testing.T) {
			client := http.NewMockClient[loginResult](gomock.NewController(t))
			response := &http.UnexpectedResponseError{StatusCode: status}
			client.EXPECT().Send(gomock.Any()).Return(http.Result[loginResult]{}, response).Times(1)
			store := appstore{loginClient: client}
			if _, err := store.sendAuthenticationRequest(http.Request{}); !errors.Is(err, response) {
				t.Fatalf("expected Apple error, got %v", err)
			}
		})
	}

	store := appstore{}
	for code, expected := range map[string]string{"": "invalid credentials", "012345": "verification code rejected"} {
		result := http.Result[loginResult]{Data: loginResult{FailureType: FailureTypeInvalidCredentials}}
		retry, _, err := store.parseLoginResponse(&result, 1, code, "")
		if retry || err == nil || err.Error() != expected {
			t.Fatalf("incorrect rejection handling: retry=%v err=%v", retry, err)
		}
	}
}
