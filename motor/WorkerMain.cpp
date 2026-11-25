#include <windows.h>
#include <objbase.h>

#include <charconv>
#include <cstdint>
#include <cstdio>
#include <string_view>

#include "veyra/engine/ExportJobManager.h"

int main(int argc, char** argv) {
    if (argc == 2 && std::string_view(argv[1]) == "--version") {
        std::puts("VideoNR Render Worker (export protocol 15)");
        return 0;
    }
    if (argc != 3 || std::string_view(argv[1]) != "--export-worker") {
        return 2;
    }

    const std::string_view argument(argv[2]);
    const char* end = argument.data() + argument.size();
    std::uintptr_t mapping = 0;
    const auto parsed = std::from_chars(argument.data(), end, mapping);
    if (parsed.ec != std::errc{} || parsed.ptr != end || mapping == 0) {
        return 2;
    }

    const HRESULT initialized = CoInitializeEx(nullptr, COINIT_APARTMENTTHREADED);
    if (FAILED(initialized)) {
        return 1;
    }
    const int result = veyra::engine::runExportWorker(reinterpret_cast<HANDLE>(mapping));
    CoUninitialize();
    return result;
}
