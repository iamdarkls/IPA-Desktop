"""Execute the patched native authentication control flow against a fake HTTP transport."""
from pathlib import Path
import subprocess
import sys
import tempfile
from patch import function_span

PRELUDE = r'''
#include <cassert>
#include <cstdint>
#include <cstdio>
#include <iostream>
#include <map>
#include <span>
#include <sstream>
#include <stdexcept>
#include <string>
#include <vector>
#include "account_safety.h"
using PlistDict=std::map<std::string,std::string>;
struct PlistValue { static std::string makeString(std::string s) { return s; } };
struct HttpResponse { int statusCode=200; std::string body; std::map<std::string,std::string> headers; };
struct IpaError:std::runtime_error { using std::runtime_error::runtime_error; };
struct AuthCodeRequired:std::exception {};
const std::string FAILURE_INVALID_CREDENTIALS="-5000", CUSTOMER_MSG_BAD_LOGIN="code required", CUSTOMER_MSG_ACCOUNT_DISABLED="account disabled";
const std::string CONFIGURATOR_UA="fixture",HTTP_HEADER_SAP_SIGNATURE="signature";
PlistDict decoded;
struct FakeHttp {
  std::vector<std::pair<HttpResponse,PlistDict>> replies; int calls=0;
  std::map<std::string,std::string> lastHeaders;
  HttpResponse post(const std::string&,const std::string&,const std::map<std::string,std::string>& headers) {
    lastHeaders=headers;
    if(calls>=int(replies.size())) { ++calls; throw IpaError("unexpected repeated password POST"); }
    auto reply=replies[calls++]; decoded=reply.second; return reply.first;
  }
} m_http;
std::string dict_str(const PlistDict& d,const std::string& k) { auto it=d.find(k); return it==d.end()?"":it->second; }
PlistDict decode_plist(const std::string&) { return decoded; }
std::string encode_plist_xml(const PlistDict&) { return "fixture"; }
std::string strip_spaces(std::string s) { return s; }
struct Signer { std::vector<uint8_t> Sign(std::span<const uint8_t>) { return {}; } } signer;
struct SapBase64 { static std::string Encode(const std::vector<uint8_t>&) { return "fixture"; } };
bool m_debug=false;
void debug_dump_auth_request(const std::string&,const std::map<std::string,std::string>&,const std::string&) {}
void debug_dump_auth_response(const HttpResponse&) {}
struct Account {};
'''
CASES = r'''
int main() {
  auto success=PlistDict{{"passwordToken","fixture-token"},{"dsPersonId","1"}};
  for(int status:{204,403,404,429,500,503}) {
    m_http={{{HttpResponse{status},{} }},0};
    bool rejected=false; try { login(); } catch(const IpaError&) { rejected=true; }
    assert(rejected && m_http.calls==1);
  }
  for(auto code:{std::string(""),std::string("123456")}) {
    m_http={{{HttpResponse{200},{{"failureType",FAILURE_INVALID_CREDENTIALS}}}},0};
    bool rejected=false; try { login(code); } catch(const IpaError& e) {
      rejected=std::string(e.what())==(code.empty()?"invalid credentials":"verification code rejected");
    }
    assert(rejected && m_http.calls==1);
  }
  m_http={{{HttpResponse{200},{{"customerMessage",CUSTOMER_MSG_ACCOUNT_DISABLED}}}},0};
  bool blocked=false;try {login();}catch(const IpaError&){blocked=true;}assert(blocked && m_http.calls==1);
  m_http={{{HttpResponse{200},{{"customerMessage",CUSTOMER_MSG_BAD_LOGIN}}}},0};
  bool code=false;try {login();}catch(const AuthCodeRequired&){code=true;}assert(code && m_http.calls==1);
  m_http={{{HttpResponse{200},success}},0};login("123456");assert(m_http.calls==1);
  m_http={{{HttpResponse{302,"",{{"location","https://p1-buy.itunes.apple.com/auth"}}},{}},{HttpResponse{200},success}},0};
  login();assert(m_http.calls==2);
  m_http={{{HttpResponse{302,"",{{"location","https://p1-buy.itunes.apple.com/private?token=private"}}},{}},{HttpResponse{204},{}}},0};
  std::ostringstream route;
  auto* original=std::cerr.rdbuf(route.rdbuf());
  bool empty=false;try{login();}catch(const IpaError&){empty=true;}
  std::cerr.rdbuf(original);
  assert(empty && m_http.calls==2);
  assert(route.str()=="IPA_AUTH_STATUS http=302 host=buy.itunes.apple.com redirects=0\nIPA_AUTH_STATUS http=204 host=p1-buy.itunes.apple.com redirects=1\n");
  for(auto url:{"https://evil.example/auth","https://apple.com.evil.example/auth","https://apple.com@evil.example/auth","http://buy.itunes.apple.com/auth"}) {
    m_http={{{HttpResponse{302,"",{{"location",url}}},{} }},0};
    bool rejected=false;try{login();}catch(const IpaError&){rejected=true;}assert(rejected && m_http.calls==1);
  }
  m_http={};Account account;assert(!silent_relogin(account,""));assert(m_http.calls==0);
  std::cout<<"PASS: native auth transport failures, rejected credentials, 2FA phases, account lock, redirect validation and zero silent logins\n";
}
'''

def run(root):
    root=Path(root)
    store=(root/'appstore.cpp').read_text(encoding='utf-8')
    main=(root/'main.cpp').read_text(encoding='utf-8')
    _,opening,_=function_span(store,'Account AppStore::do_login(')
    end=store.index('    std::string sf, pod;',opening)
    native='provRound' in store[opening:end]
    prelude, cases = PRELUDE, CASES
    endpoint = "https://buy.itunes.apple.com/auth"
    if native:
        endpoint = "https://auth.itunes.apple.com/auth/v1/native"
        prelude += r"""
const std::string PROV_AUTH_UA="fixture";
constexpr uint64_t kAnonAdiDSID=~uint64_t(0);
int provisionings=0;
void prov_log(const std::string&) {}
void prov_add_md(std::map<std::string,std::string>& headers, uint64_t, const char*) {
    if (provisionings) headers["X-Apple-AMD"]="proof";
}
bool prov_handle(const char*, const HttpResponse& res, uint64_t, const std::string&, const char*) {
    const auto action=res.headers.find("x-apple-amd-action");
    const auto challenge=res.headers.find("x-apple-amd-data");
    if (action==res.headers.end() || action->second!="SP" ||
        challenge==res.headers.end() || challenge->second.empty()) return false;
    ++provisionings;
    return true;
}
"""
        cases = cases.replace("https://p1-buy.itunes.apple.com/auth", endpoint + "/")
        cases = cases.replace("https://p1-buy.itunes.apple.com/private?token=private", endpoint + "?token=private")
        cases = cases.replace("host=buy.itunes.apple.com", "host=auth.itunes.apple.com")
        cases = cases.replace("host=p1-buy.itunes.apple.com", "host=auth.itunes.apple.com")
        cases = cases.replace("  m_http={};Account account;", r"""
  m_http={{{HttpResponse{200,"",{{"x-apple-amd-action","SP"},{"x-apple-amd-data","challenge"}}},{}},{HttpResponse{200},success}},0};
  login();assert(m_http.calls==2 && provisionings==1);
  assert(m_http.lastHeaders.at("X-Apple-AMD")=="proof" && m_http.lastHeaders.at(HTTP_HEADER_SAP_SIGNATURE)=="fixture");
  m_http={{{HttpResponse{200,"",{{"x-apple-amd-action","SP"}}},{} }},0};
  bool missing=false;try{login();}catch(const IpaError&){missing=true;}
  assert(missing && m_http.calls==1 && provisionings==1);
  m_http={};Account account;""")
    login='void login(std::string authCode="") {\nstd::string email="fixture", password="fixture", guid="fixture", baseEndpoint="'+endpoint+'";\n'+store[opening+1:end]+'}\n'
    start,_,end=function_span(main,'static bool silent_relogin(')
    # Exercise actual patched native control flow, not a second implementation of its retry policy.
    with tempfile.TemporaryDirectory(prefix='ipa-native-auth-') as temporary:
        source=Path(temporary)/'auth.cpp';binary=Path(temporary)/'auth.exe'
        source.write_text(prelude+login+main[start:end]+cases,encoding='utf-8')
        subprocess.run(['g++','-std=c++20','-O0','-I'+str(root),str(source),'-o',str(binary)],check=True)
        subprocess.run([str(binary)],check=True,timeout=15)
    http=(root/'http_client.cpp').read_text(encoding='utf-8')
    _,opening,end=function_span(http,'HttpResponse HttpClient::perform(')
    perform='HttpResponse perform(const std::string& method,const std::string& url,const std::string& body,const std::map<std::string,std::string>& reqHeaders) '+http[opening:end]
    transport=Path(__file__).with_name('transport_fixture.cpp').read_text().replace('// ACTUAL_PERFORM',perform)
    with tempfile.TemporaryDirectory(prefix='ipa-http-auth-') as temporary:
        source=Path(temporary)/'transport.cpp';binary=Path(temporary)/'transport.exe'
        source.write_text(transport,encoding='utf-8')
        subprocess.run(['g++','-std=c++20','-O0',str(source),'-o',str(binary)],check=True)
        subprocess.run([str(binary)],check=True,timeout=15)
    assert 'j["password"]            = "";' in main
    assert 'acc.password.set(""); // Ignore passwords' in main
    assert 'acc.password.set(""); // Never retain' in store
    print('PASS: saved session never writes or loads the Apple password')

def test_raw_ipa_cache(root):
    root = Path(root)
    store = (root/'appstore.cpp').read_text(encoding='utf-8')
    if 'raw_ipa_cache::restore' not in store:
        return
    start = store.index('    // Only an authorized, personalized App Store response')
    end = store.index('    return {dest, sinfs};', start)
    fixture = Path(__file__).with_name('raw_ipa_cache_fixture.cpp').read_text()
    with tempfile.TemporaryDirectory(prefix='ipa-raw-cache-') as folder:
        folder = Path(folder)
        source, binary = folder/'cache.cpp', folder/'cache.exe'
        source.write_text(fixture.replace('// ACTUAL_IOS_TRANSFER', store[start:end]), encoding='utf-8')
        subprocess.run(['g++', '-std=c++20', '-O0', '-I'+str(root), str(source), '-o', str(binary)], check=True)
        subprocess.run([str(binary), str(folder)], check=True, timeout=15)


if __name__ == '__main__':
    run(sys.argv[1])
    test_raw_ipa_cache(sys.argv[1])
