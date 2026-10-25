"""Small, fail-closed patch for the immutable Sorvigolova/ipatool 1.2.0 source."""
from pathlib import Path
import hashlib
import sys

REVISION = 'd35bb00191f3371f579994cb016f61bf7f4a62ac'
PROFILE = 'IPA-DESKTOP-AUTH-SAFETY/1'

def replace_once(text, before, after):
    if text.count(before) != 1:
        raise ValueError('Pinned source changed; patch requires review: ' + before[:80])
    return text.replace(before, after, 1)

def function_span(text, signature):
    start = text.index(signature)
    opening = text.index('{', start)
    # Used only for these reviewed pinned functions (no braces inside string literals).
    depth = 1
    end = opening + 1
    while depth:
        depth += (text[end] == '{') - (text[end] == '}')
        end += 1
    return start, opening, end

def patch(root):
    root = Path(root)
    main = (root/'main.cpp').read_text(encoding='utf-8')
    store = (root/'appstore.cpp').read_text(encoding='utf-8')
    http = (root/'http_client.cpp').read_text(encoding='utf-8')
    pins = __import__('json').loads((Path(__file__).with_name('pins.json')).read_text())
    for name, content in [('main.cpp',main),('appstore.cpp',store),('http_client.cpp',http)]:
        if hashlib.sha256(content.encode()).hexdigest() != pins['files'][name]:
            raise ValueError('Unreviewed source: ' + name)
    start, opening, end = function_span(main, 'static bool silent_relogin(')
    main = main[:opening+1] + '\n    (void)acc; (void)passphrase;\n    return false; // Never send a saved password during a download.\n' + main[end-1:]
    main = replace_once(main, 'j["password"]            = acc.password.get();', 'j["password"]            = ""; // Session tokens only; never persist the Apple password.')
    main = replace_once(main, 'acc.password.set(     j.value("password", ""));', 'acc.password.set(""); // Ignore passwords left by older engines.')
    main = replace_once(main, 'int main(int argc, char** argv) {', 'int main(int argc, char** argv) {\n    if (argc == 2 && std::string(argv[1]) == "account-safety-info") {\n        std::cout << "' + PROFILE + '\\n"; return 0;\n    }')
    store = '#include "account_safety.h"\n#include <iostream>\n' + store
    store = replace_once(store, 'acc.password.set(       password);', 'acc.password.set(""); // Never retain the Apple password in the saved session.')
    first = store.index('        // Transport retry: up to 3 attempts')
    last = store.index('        lastData = decode_plist(lastRes.body);',first)
    store = store[:first] + '''        // Exactly one password POST per phase/Apple-directed redirect. No transport retry.
        if (!account_safety::apple_url(currentURL)) throw IpaError("unsafe authentication endpoint");
        if (m_debug) debug_dump_auth_request(currentURL, headers, body);
        lastRes = m_http.post(currentURL, body, headers);
        std::cerr << "IPA_AUTH_STATUS http=" << lastRes.statusCode << "\\n";
        if (m_debug) debug_dump_auth_response(lastRes);
        if (lastRes.statusCode == 429) throw IpaError("rate limited by Apple (HTTP 429)");
        if (lastRes.statusCode != 200 && lastRes.statusCode != 302)
            throw IpaError("Apple authentication service unavailable (HTTP " + std::to_string(lastRes.statusCode) + ")");

''' + store[last:]
    store = replace_once(store, '        std::string customerMessage = dict_str(lastData, "customerMessage");\n\n        if (lastRes.statusCode == 302)', '''        std::string customerMessage = dict_str(lastData, "customerMessage");
        if (!failureType.empty() && failureType.size() <= 16 && failureType.find_first_not_of("0123456789-") == std::string::npos)
            std::cerr << "IPA_AUTH_FAILURE code=" << failureType << "\\n";

        if (lastRes.statusCode == 302)''')
    store = replace_once(store, '            currentURL   = locIt->second;', '            if (!account_safety::apple_url(locIt->second)) throw IpaError("unsafe authentication redirect");\n            currentURL   = locIt->second;')
    store = replace_once(store, '''        } else if (attempt == 1 && failureType == FAILURE_INVALID_CREDENTIALS) {
            retry = true;''', '''        } else if (failureType == FAILURE_INVALID_CREDENTIALS) {
            throw IpaError("invalid credentials"); // Stop after the first rejection, including 2FA.''')
    # HTTP transport also retries on curl timeouts, even when AppStore itself does not.
    # No POST may be replayed after an uncertain outcome (including password/purchase POSTs).
    http = replace_once(http, 'if (!is_retryable_curl(rc) || attempt > API_MAX_RETRIES) break;',
                        'if (method == "POST" || !is_retryable_curl(rc) || attempt > API_MAX_RETRIES) break;')
    (root/'http_client.cpp').write_text(http,encoding='utf-8',newline='\n')
    (root/'main.cpp').write_text(main,encoding='utf-8',newline='\n')
    (root/'appstore.cpp').write_text(store,encoding='utf-8',newline='\n')
    (root/'account_safety.h').write_text((Path(__file__).with_name('account_safety.h')).read_text(),encoding='utf-8',newline='\n')

if __name__ == '__main__':
    patch(sys.argv[1])
