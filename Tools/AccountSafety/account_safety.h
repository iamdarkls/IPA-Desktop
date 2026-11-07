#pragma once
#include <string>
#include <algorithm>
#include <cctype>
namespace account_safety {
inline std::string apple_host(const std::string& url) {
    if (url.compare(0, 8, "https://") != 0) return {};
    auto host = url.substr(8, url.find_first_of("/?#",8) - 8);
    std::transform(host.begin(),host.end(),host.begin(),[](unsigned char c){return std::tolower(c);});
    if (host.size()>4 && host.substr(host.size()-4)==":443") host.resize(host.size()-4);
    if (host.empty() || host.size()>253 || host.find_first_not_of("abcdefghijklmnopqrstuvwxyz0123456789.-") != std::string::npos) return {};
    if (host=="apple.com" || (host.size()>10 && host.substr(host.size()-10)==".apple.com")) return host;
    return {};
}
inline bool apple_url(const std::string& url) { return !apple_host(url).empty(); }
}
