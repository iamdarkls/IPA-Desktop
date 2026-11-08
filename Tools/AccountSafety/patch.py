"""Small, fail-closed patch for the immutable Sorvigolova/ipatool 1.2.1 source."""
from pathlib import Path
import hashlib
import json
import sys

REVISION = '3d36465d614bea25fbfb6eed53fa51ac0f16bf5f'
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

def patch(root, pin_file=None):
    root = Path(root)
    main = (root/'main.cpp').read_text(encoding='utf-8')
    store = (root/'appstore.cpp').read_text(encoding='utf-8')
    http = (root/'http_client.cpp').read_text(encoding='utf-8')
    pins = json.loads(Path(pin_file or Path(__file__).with_name('pins.json')).read_text())
    native = 'store_proof.h' in pins['files']
    for name, expected in pins['files'].items():
        content = (root/name).read_text(encoding='utf-8')
        if hashlib.sha256(content.encode()).hexdigest() != expected:
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
    auth = '''        // Exactly one password POST per phase/Apple-directed redirect. No transport retry.
        const auto authHost = account_safety::apple_host(currentURL);
        if (authHost.empty()) throw IpaError("unsafe authentication endpoint");
        if (m_debug) debug_dump_auth_request(currentURL, headers, body);
        lastRes = m_http.post(currentURL, body, headers);
        std::cerr << "IPA_AUTH_STATUS http=" << lastRes.statusCode
                  << " host=" << authHost << " redirects=" << (attempt - 1) << "\\n";
        if (m_debug) debug_dump_auth_response(lastRes);
        if (lastRes.statusCode == 429) throw IpaError("rate limited by Apple (HTTP 429)");
        if (lastRes.statusCode != 200 && lastRes.statusCode != 302)
            throw IpaError("Apple authentication service unavailable (HTTP " + std::to_string(lastRes.statusCode) + ")");

'''
    if native:
        # Keep the one explicit Apple SP provisioning continuation, not the transport loop.
        auth = replace_once(auth, 'lastRes = m_http.post(currentURL, body, headers);',
                            'lastRes = ipt::store_proof_post(m_http, currentURL, body, headers, guid);')
        auth = auth.replace('        if (m_debug) debug_dump_auth_request(currentURL, headers, body);\n', '')
        auth = auth.replace('        if (m_debug) debug_dump_auth_response(lastRes);\n', '')
        auth = replace_once(auth, 'lastRes.statusCode != 200 && lastRes.statusCode != 302',
                            'lastRes.statusCode != 200 && lastRes.statusCode != 302 &&\n'
                            '            !(nativeAuth && (lastRes.statusCode == 301 || lastRes.statusCode == 307 || lastRes.statusCode == 308))')
    store = store[:first] + auth + store[last:]
    if native:
        store = replace_once(store, '        dsid = dict_str(lastData, "dsPersonId");', """        if (!failureType.empty() && failureType.size() <= 16 && failureType.find_first_not_of("0123456789-") == std::string::npos)
            std::cerr << "IPA_AUTH_FAILURE code=" << failureType << "\\n";
        dsid = dict_str(lastData, "dsPersonId");""")
        store = replace_once(store, '        } else if (attempt == 1 && failureType == FAILURE_INVALID_CREDENTIALS && !nativeAuth) {\n            retry = true;',
                             '        } else if (failureType == FAILURE_INVALID_CREDENTIALS) {\n'
                             '            throw IpaError(authCode.empty() ? "invalid credentials" : "verification code rejected");')
        # Use the current Configurator UA for purchase history, not the older generic UA.
        start, _, end = function_span(store, 'AppStore::ListPurchasesOutput AppStore::list_purchases(')
        history = replace_once(store[start:end], 'CONFIGURATOR_UA', 'ipt::store_user_agent')
        history = replace_once(history, '        if (loc == res.headers.end() || loc->second.empty()) break;',
                               '        if (loc == res.headers.end() || loc->second.empty()) break;\n'
                               '        if (!account_safety::apple_url(loc->second)) throw IpaError("unsafe purchase history redirect");')
        store = store[:start] + history + store[end:]
        # A non-Apple or legacy auth redirect must never receive a password.
        store = replace_once(store, '    const bool nativeAuth = ipt::store_auth_url(baseEndpoint);',
                             '    const bool nativeAuth = ipt::store_auth_url(baseEndpoint);\n'
                             '    if (!nativeAuth) throw IpaError("unsupported native Store authentication endpoint");')
    else:
        store = replace_once(store, '        std::string customerMessage = dict_str(lastData, "customerMessage");\n\n        if (lastRes.statusCode == 302)', '''        std::string customerMessage = dict_str(lastData, "customerMessage");
        if (!failureType.empty() && failureType.size() <= 16 && failureType.find_first_not_of("0123456789-") == std::string::npos)
            std::cerr << "IPA_AUTH_FAILURE code=" << failureType << "\\n";

        if (lastRes.statusCode == 302)''')
        store = replace_once(store, '            currentURL   = locIt->second;', '            if (!account_safety::apple_url(locIt->second)) throw IpaError("unsafe authentication redirect");\n            currentURL   = locIt->second;')
        store = replace_once(store, '''        } else if (attempt == 1 && failureType == FAILURE_INVALID_CREDENTIALS) {
            retry = true;''', '''        } else if (failureType == FAILURE_INVALID_CREDENTIALS) {
            throw IpaError(authCode.empty() ? "invalid credentials" : "verification code rejected");''')
    # HTTP transport also retries on curl timeouts, even when AppStore itself does not.
    # No POST may be replayed after an uncertain outcome (including password/purchase POSTs).
    http = replace_once(http, 'if (!is_retryable_curl(rc) || attempt > API_MAX_RETRIES) break;',
                        'if (method == "POST" || !is_retryable_curl(rc) || attempt > API_MAX_RETRIES) break;')
    (root/'http_client.cpp').write_text(http,encoding='utf-8',newline='\n')
    (root/'main.cpp').write_text(main,encoding='utf-8',newline='\n')
    (root/'appstore.cpp').write_text(store,encoding='utf-8',newline='\n')
    (root/'account_safety.h').write_text((Path(__file__).with_name('account_safety.h')).read_text(),encoding='utf-8',newline='\n')
    (root/'account_safety_cfg_smoke.cpp').write_text(Path(__file__).with_name('cfg_smoke.cpp').read_text(),encoding='utf-8',newline='\n')
    with (root/'CMakeLists.txt').open('a',encoding='utf-8',newline='\n') as project:
        project.write("\nif(MINGW)\n  add_executable(ipa_cfg_smoke account_safety_cfg_smoke.cpp)\n  target_include_directories(ipa_cfg_smoke PRIVATE ${UNICORN_INCLUDE_DIR})\n  target_link_libraries(ipa_cfg_smoke PRIVATE ${UNICORN_LIBRARY})\n  target_link_options(ipa_cfg_smoke PRIVATE -static -static-libgcc -static-libstdc++)\nendif()\n")
        if native:
            project.write("if(MINGW)\n  target_compile_definitions(ipa_cfg_smoke PRIVATE IPA_CFG_ARM64)\nendif()\n")

if __name__ == '__main__':
    patch(*sys.argv[1:])
