#include "raw_ipa_cache.h"
#include <cassert>
#include <fstream>
#include <iostream>
#include <iterator>

namespace fs = std::filesystem;
struct Account { std::string name; };
struct Sinf { std::string data; };
const std::string good(32, 'a'), alternate(32, 'b');
const std::int64_t songId = 123;
int requests = 0, reports = 0;
void write(const fs::path& path, const std::string& body) { std::ofstream(path, std::ios::binary) << body; }
std::string read(const fs::path& path) {
    std::ifstream in(path, std::ios::binary);
    return {std::istreambuf_iterator<char>(in), {}};
}
std::string md5_file_hex(const std::string& path) { return read(ipt::fs_path(path)) == "original" ? good : ""; }
bool ascii_iequals(const std::string& a, const std::string& b) { return a == b; }
std::int64_t file_size(const std::string&) { return 0; }
struct Http {
    void download(const std::string&, const std::string& path, std::int64_t, int) {
        ++requests;
        write(ipt::fs_path(path), "original");
    }
} m_http;
void apply_patches(int, const Account& acc, const std::string& source,
                   const std::string& output, const std::vector<Sinf>& sinfs) {
    const auto raw = read(ipt::fs_path(source));
    assert(raw == "original");
    write(ipt::fs_path(output), raw + "|" + acc.name + "|" + sinfs.front().data);
}
void download(const fs::path& output, const Account& acc, const std::vector<Sinf>& sinfs, const std::string& md5Expected) {
    const std::string dest = ipt::to_utf8(output), tmpDest = dest + ".tmp", downloadURL = "fixture";
    const int item = 0, progress = 0;
    auto verify_and_report = [&](const std::string& path) {
        assert(md5Expected.empty() || md5_file_hex(path) == md5Expected);
        ++reports;
    };
    // ACTUAL_IOS_TRANSFER
}
int main(int argc, char** argv) {
    assert(argc == 2);
    const auto root = fs::path(argv[1]);
    raw_ipa_cache::configure(ipt::to_utf8(root / "Cache"), "0");
    assert(raw_ipa_cache::entry(songId, good).empty());
    raw_ipa_cache::configure(ipt::to_utf8(root / "Cache"), "5");
    assert(raw_ipa_cache::max_bytes == 5 * raw_ipa_cache::gib);
    const auto cached = raw_ipa_cache::entry(songId, good);
    assert(cached != raw_ipa_cache::entry(songId + 1, good));
    assert(cached != raw_ipa_cache::entry(songId, alternate));
    assert(raw_ipa_cache::entry(songId, "../bad").empty());
    download(root / "a.ipa", {"first"}, {{"license-a"}}, good);
    assert(requests == 1 && reports == 1 && read(cached) == "original");
    download(root / "b.ipa", {"second"}, {{"license-b"}}, good);
    assert(requests == 1 && reports == 1);
    assert(read(root / "a.ipa") == "original|first|license-a");
    assert(read(root / "b.ipa") == "original|second|license-b");
    download(root / "missing-hash.ipa", {"second"}, {{"license-b"}}, "");
    download(root / "missing-license.ipa", {"second"}, {{""}}, good);
    assert(requests == 3 && reports == 3);
    write(cached, "corrupt");
    download(root / "fallback.ipa", {"second"}, {{"license-b"}}, good);
    assert(requests == 4 && reports == 4);
    assert(read(root / "fallback.ipa") == "original|second|license-b");
    for (std::size_t n = 0; n < raw_ipa_cache::max_entries; ++n)
        write(raw_ipa_cache::entry(1000 + n, good), "original");
    write(raw_ipa_cache::directory / "unrelated.raw", "keep");
    raw_ipa_cache::prune(raw_ipa_cache::directory, cached);
    const auto entries = std::distance(fs::directory_iterator(raw_ipa_cache::directory), fs::directory_iterator{});
    assert(entries == raw_ipa_cache::max_entries + 1);
    assert(read(cached) == "original" && read(raw_ipa_cache::directory / "unrelated.raw") == "keep");
    std::cout << "PASS: original IPA reuse, per-account licensing, and safe download fallback\n";
}
