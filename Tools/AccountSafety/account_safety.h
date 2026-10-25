#pragma once
#include <string>
#include <algorithm>
#include <cctype>
namespace account_safety {
inline bool apple_url(const std::string& url) {
    if (url.compare(0, 8, "https://") != 0) return false;
    auto host = url.substr(8, url.find_first_of("/?#",8) - 8);
    std::transform(host.begin(),host.end(),host.begin(),[](unsigned char c){return std::tolower(c);});
    if (host.size()>4 && host.substr(host.size()-4)==":443") host.resize(host.size()-4);
    if (host.empty() || host.find_first_not_of("abcdefghijklmnopqrstuvwxyz0123456789.-") != std::string::npos) return false;
    return host=="apple.com" || (host.size()>10 && host.substr(host.size()-10)==".apple.com");
}
}
