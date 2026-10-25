// Offline curl stand-in for executing the actual patched HttpClient::perform body.
#include <cassert>
#include <chrono>
#include <cstdio>
#include <iostream>
#include <map>
#include <stdexcept>
#include <string>
#include <thread>
using CURLcode=int;
constexpr int CURLE_OK=0,CURLE_COULDNT_RESOLVE_HOST=6,CURLE_COULDNT_RESOLVE_PROXY=5,CURLE_OPERATION_TIMEDOUT=28;
enum { CURLOPT_URL,CURLOPT_USERAGENT,CURLOPT_SSL_VERIFYPEER,CURLOPT_CONNECTTIMEOUT,CURLOPT_TIMEOUT,
  CURLOPT_WRITEFUNCTION,CURLOPT_WRITEDATA,CURLOPT_HEADERFUNCTION,CURLOPT_HEADERDATA,CURLOPT_FOLLOWLOCATION,
  CURLOPT_HTTPHEADER,CURLOPT_POST,CURLOPT_POSTFIELDS,CURLOPT_POSTFIELDSIZE,CURLOPT_HTTPGET,CURLINFO_RESPONSE_CODE };
struct CURL {};
struct curl_slist {};
struct HttpResponse { int statusCode=0;std::string body;std::map<std::string,std::string> headers; };
struct DnsResolveError:std::runtime_error { using std::runtime_error::runtime_error; };
int calls=0,result=CURLE_OK,body_write_cb=0,header_cb=0;
long status=200;
bool m_debug=false;
std::string m_cookieFile,USER_AGENT="fixture";
constexpr int API_MAX_RETRIES=3,API_BACKOFF_S[]={0,0,0};
CURL* curl_easy_init() { static CURL handle;return &handle; }
template<class... T> void curl_easy_setopt(CURL*,int,T...) {}
void curl_easy_getinfo(CURL*,int,long* code) { *code=status; }
void curl_easy_cleanup(CURL*) {}
int curl_easy_perform(CURL*) { calls++;return result; }
const char* curl_easy_strerror(CURLcode) { return "fixture transport failure"; }
curl_slist* curl_slist_append(curl_slist*,const char*) {static curl_slist list;return &list;}
void curl_slist_free_all(curl_slist*) {}
void load_cookies(CURL*,const std::string&) {}
void save_cookies(CURL*,const std::string&) {}
bool is_retryable_curl(CURLcode code) { return code==CURLE_OPERATION_TIMEDOUT; }
// ACTUAL_PERFORM
int main() {
  for(int code:{CURLE_OPERATION_TIMEDOUT,CURLE_COULDNT_RESOLVE_HOST,CURLE_COULDNT_RESOLVE_PROXY,55,56}) {
    calls=0;result=code;bool failed=false;
    try { perform("POST","https://buy.itunes.apple.com/auth","fixture-password",{}); } catch(const std::runtime_error&) {failed=true;}
    assert(failed && calls==1);
  }
  calls=0;result=CURLE_OK;
  assert(perform("POST","https://buy.itunes.apple.com/auth","fixture",{}).statusCode==200 && calls==1);
  std::cout<<"PASS: actual native HTTP transport never replays password/purchase POSTs after timeout or connection errors\n";
}
