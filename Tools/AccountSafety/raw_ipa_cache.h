#pragma once

#include <algorithm>
#include <cstdint>
#include <filesystem>
#include <string>
#include <vector>
#include "path_utf8.h"

namespace raw_ipa_cache {
namespace fs = std::filesystem;

// Set by the download command only. The cache contains original CDN bytes,
// never the personalized IPA, license data, URLs or credentials.
inline fs::path directory;
constexpr std::uintmax_t max_bytes = 3ull * 1024 * 1024 * 1024;
constexpr std::size_t max_entries = 100;

inline bool valid_hash(const std::string& hash) {
    return hash.size() == 32 && hash.find_first_not_of("0123456789abcdefABCDEF") == std::string::npos;
}

inline fs::path entry(std::int64_t app_id, std::string hash) {
    if (directory.empty() || app_id <= 0 || !valid_hash(hash)) return {};
    std::transform(hash.begin(), hash.end(), hash.begin(), [](char c) {
        return c >= 'A' && c <= 'F' ? char(c - 'A' + 'a') : c;
    });
    return directory / (std::to_string(app_id) + "-" + hash + ".raw");
}

inline bool regular(const fs::path& path) {
    std::error_code error;
    return fs::is_regular_file(fs::symlink_status(path, error));
}

template<class Verify>
bool restore(const fs::path& cached, const fs::path& target, Verify verified) {
    if (cached.empty() || !regular(cached)) return false;
    std::error_code error;
    if (!fs::is_directory(fs::symlink_status(cached.parent_path(), error))) return false;
    if (fs::symlink_status(target, error).type() != fs::file_type::not_found) return false;
    error.clear();
    if (fs::file_size(cached, error) > max_bytes || error) return false;
    if (fs::copy_file(cached, target, fs::copy_options::none, error)) {
        if (verified()) {
            fs::last_write_time(cached, fs::file_time_type::clock::now(), error);
            return true;
        }
    }
    // target was an unused path supplied by this download. Do not resume from
    // a partial local copy or from a cache entry that failed verification.
    fs::remove(target, error);
    return false;
}

inline void prune(const fs::path& parent, const fs::path& newest) {
    struct Record { fs::path path; fs::file_time_type touched; std::uintmax_t size; };
    std::vector<Record> files;
    std::error_code error;
    fs::directory_iterator it(parent, error), end;
    while (!error && it != end) {
        const auto path = it->path();
        auto name = ipt::to_utf8(path.filename());
        const auto split = name.find('-');
        if (path.extension() == ".raw" && split != std::string::npos && split > 0 &&
            name.substr(0, split).find_first_not_of("0123456789") == std::string::npos &&
            valid_hash(name.substr(split + 1, name.size() - split - 5)) && regular(path)) {
            std::error_code stat;
            auto size = it->file_size(stat);
            if (!stat) {
                auto touched = it->last_write_time(stat);
                if (!stat) files.push_back({path, path == newest ? (fs::file_time_type::max)() : touched, size});
            }
        }
        it.increment(error);
    }
    std::sort(files.begin(), files.end(), [](const Record& a, const Record& b) { return a.touched > b.touched; });
    std::uintmax_t kept = 0;
    std::size_t count = 0;
    for (const auto& file : files) {
        if (file.size <= max_bytes - kept && count < max_entries) { kept += file.size; ++count; }
        else fs::remove(file.path, error);
    }
}

inline void remember(const fs::path& source, const fs::path& cached) {
    if (cached.empty() || !regular(source)) return;
    std::error_code error;
    if (fs::file_size(source, error) > max_bytes || error) return;
    fs::create_directories(cached.parent_path(), error);
    if (error || !fs::is_directory(fs::symlink_status(cached.parent_path(), error))) return;
    // Both paths belong to the app data folder. An atomic move prevents a
    // cancelled process from leaving a half written cache entry.
    fs::rename(source, cached, error);
    if (!error) {
        fs::last_write_time(cached, fs::file_time_type::clock::now(), error);
        prune(cached.parent_path(), cached);
    }
}
}
