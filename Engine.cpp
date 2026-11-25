#ifndef NOMINMAX
#define NOMINMAX
#endif
#include <windows.h>
#include <algorithm>
#include <cmath>
#include <cstdio>
#include <filesystem>
#include <limits>
#include <locale>
#include <sstream>
#include <stdexcept>
#include <string>
#include <vector>
#include "veyra/engine/ExportFeaturePolicy.h"
#include "veyra/engine/ExportOptions.h"

namespace {
using namespace veyra::engine;
constexpr unsigned MaxParts = 4;

struct Job {
    DWORD signature = 0x56585931, version = 15, bytes = sizeof(Job);
    EnhancementSettings settings;
    EffectChain chain;
    wchar_t input[32768]{}, output[32768]{}, temporary[32768]{};
    ExportMediaOptions media;
    unsigned hevc = 0, maxFrames = 0;
    int audioStreamIndex = -1;
    double trimStartSeconds = 0, trimEndSeconds = 0;
    uint32_t rateControl = 2;
    volatile LONG64 sourceFrames = 0, generated = 0, holds = 0, encoded = 0;
    volatile LONG messageLock = 0;
    wchar_t message[1024]{};
    volatile LONG cancel = 0, pause = 0, watching = 0, state = 1, progress = 0;
    double resumeAtSeconds = 0, segmentOriginSeconds = 0;
    unsigned priorCount = 0, resumeAttempt = 0;
    double priorOrigins[MaxParts]{};
    wchar_t priorPaths[MaxParts][2048]{};
};

struct Handle {
    HANDLE value = nullptr;
    ~Handle() { if (value && value != INVALID_HANDLE_VALUE) CloseHandle(value); }
};

void print(const std::wstring& value) {
    const int size = WideCharToMultiByte(CP_UTF8, 0, value.c_str(), -1, nullptr, 0, nullptr, nullptr);
    if (size <= 0) return;
    std::string line(static_cast<size_t>(size), '\0');
    WideCharToMultiByte(CP_UTF8, 0, value.c_str(), -1, line.data(), size, nullptr, nullptr);
    std::puts(line.c_str());
    std::fflush(stdout);
}

bool cancelSignaledOnStdin(HANDLE stdinHandle) {
    if (!stdinHandle || stdinHandle == INVALID_HANDLE_VALUE) return false;
    if (GetFileType(stdinHandle) != FILE_TYPE_PIPE) return false;
    DWORD available = 0;
    if (!PeekNamedPipe(stdinHandle, nullptr, 0, nullptr, &available, nullptr))
        return true; // Parent closed stdin or exited unexpectedly.
    return available > 0;
}

int run(const wchar_t* engine, const wchar_t* input, const wchar_t* output,
        NrRuntime runtime, const std::vector<NrLayerSettings>& layers,
        bool hevc, unsigned maxFrames, double start, double end) {
    const std::wstring temporary = std::wstring(output) + L".processing-" + std::to_wstring(GetCurrentProcessId());
    if (wcslen(input) >= 32768 || temporary.size() >= 32768 || GetFileAttributesW(output) != INVALID_FILE_ATTRIBUTES) {
        print(L"Escolha um caminho de saída novo e válido.");
        return 1;
    }

    SECURITY_ATTRIBUTES security{sizeof(security), nullptr, TRUE};
    Handle mapping{CreateFileMappingW(INVALID_HANDLE_VALUE, &security, PAGE_READWRITE, 0, sizeof(Job), nullptr)};
    if (!mapping.value) return 1;
    auto* shared = static_cast<Job*>(MapViewOfFile(mapping.value, FILE_MAP_ALL_ACCESS, 0, 0, sizeof(Job)));
    if (!shared) return 1;
    new (shared) Job{};
    auto& job = *shared;
    job.settings.nr = !layers.empty();
    job.settings.nrRuntime = runtime;
    job.settings.nrLayerCount = static_cast<uint32_t>(layers.size());
    std::copy(layers.begin(), layers.end(), job.settings.nrLayers.begin());
    job.settings = freezeExportSettings(job.settings);
    job.chain = toChain(job.settings);
    fromChain(job.chain, job.settings);
    job.maxFrames = maxFrames;
    job.trimStartSeconds = start;
    job.trimEndSeconds = end;
    job.hevc = hevc ? 1u : 0u;
    job.media.audio.policy = maxFrames > 0 ? ExportTrackPolicy::None : ExportTrackPolicy::All;
    wcscpy_s(job.input, input);
    wcscpy_s(job.output, output);
    wcscpy_s(job.temporary, temporary.c_str());

    Handle partial{CreateFileW(temporary.c_str(), GENERIC_WRITE, 0, nullptr, CREATE_NEW, FILE_ATTRIBUTE_NORMAL, nullptr)};
    if (partial.value == INVALID_HANDLE_VALUE) {
        UnmapViewOfFile(shared);
        print(L"Não foi possível criar o arquivo temporário ao lado do destino.");
        return 1;
    }
    CloseHandle(partial.value);
    partial.value = nullptr;

    SIZE_T attributeBytes = 0;
    InitializeProcThreadAttributeList(nullptr, 1, 0, &attributeBytes);
    std::vector<unsigned char> attributes(attributeBytes);
    STARTUPINFOEXW startup{};
    startup.StartupInfo.cb = sizeof(startup);
    startup.lpAttributeList = reinterpret_cast<LPPROC_THREAD_ATTRIBUTE_LIST>(attributes.data());
    PROCESS_INFORMATION process{};
    std::wstring command = L"\"" + std::wstring(engine) + L"\" --export-worker "
        + std::to_wstring(reinterpret_cast<uintptr_t>(mapping.value));
    const auto directory = std::filesystem::path(engine).parent_path();
    const bool initialized = InitializeProcThreadAttributeList(startup.lpAttributeList, 1, 0, &attributeBytes) != FALSE;
    const bool launched = initialized
        && UpdateProcThreadAttribute(startup.lpAttributeList, 0, PROC_THREAD_ATTRIBUTE_HANDLE_LIST,
            &mapping.value, sizeof(HANDLE), nullptr, nullptr) != FALSE
        && CreateProcessW(engine, command.data(), nullptr, nullptr, TRUE,
            EXTENDED_STARTUPINFO_PRESENT | CREATE_NO_WINDOW, nullptr, directory.c_str(),
            &startup.StartupInfo, &process) != FALSE;
    if (initialized) DeleteProcThreadAttributeList(startup.lpAttributeList);

    DWORD code = 1;
    if (launched) {
        Handle worker{process.hProcess}, thread{process.hThread};
        const HANDLE stdinHandle = GetStdHandle(STD_INPUT_HANDLE);
        print(L"WORKER " + std::to_wstring(process.dwProcessId));
        LONG last = -1;
        ULONGLONG cancelledAt = 0;
        while (WaitForSingleObject(worker.value, 125) == WAIT_TIMEOUT) {
            const LONG progress = InterlockedCompareExchange(&job.progress, 0, 0);
            if (progress != last) {
                print(L"PROGRESS " + std::to_wstring(progress));
                last = progress;
            }
            if (!cancelledAt && cancelSignaledOnStdin(stdinHandle)) {
                InterlockedExchange(&job.cancel, 1);
                cancelledAt = GetTickCount64();
            }
            if (cancelledAt && GetTickCount64() - cancelledAt > 5000) {
                TerminateProcess(worker.value, 3);
            }
        }
        GetExitCodeProcess(worker.value, &code);
        if (code != 0 && job.message[0]) print(job.message);
        else if (code != 0 && code != 3) print(L"O motor encerrou sem concluir o vídeo. Código " + std::to_wstring(code));
    } else {
        print(L"Não foi possível iniciar o motor de processamento.");
    }
    UnmapViewOfFile(shared);
    DeleteFileW(temporary.c_str());
    return code == 0 ? 0 : code == 3 ? 3 : 1;
}
} // namespace

int wmain(int argc, wchar_t** argv) {
    int argOffset = 1;
    unsigned maxFrames = 0;
    if (argc >= 3 && std::wstring(argv[1]) == L"--max-frames") {
        std::wistringstream stream(argv[2]);
        stream.imbue(std::locale::classic());
        long long parsedFrames = 0;
        if (!(stream >> parsedFrames) || !stream.eof() || parsedFrames <= 0
            || parsedFrames > static_cast<long long>(std::numeric_limits<unsigned>::max())) {
            return 2;
        }
        maxFrames = static_cast<unsigned>(parsedFrames);
        argOffset = 3;
    }
    const int positional = argc - argOffset;
    if (positional < 7 || positional > 7 + static_cast<int>(kMaxNrInstances)) return 2;
    try {
        auto readTime = [](const wchar_t* value) {
            std::wistringstream stream(value);
            stream.imbue(std::locale::classic());
            double seconds = 0;
            if (!(stream >> seconds) || !stream.eof() || !std::isfinite(seconds) || seconds < 0)
                throw std::invalid_argument("Invalid time");
            return seconds;
        };
        const std::wstring codec = argv[argOffset + 3];
        if (codec != L"h264" && codec != L"hevc")
            throw std::invalid_argument("Invalid codec");
        const double start = readTime(argv[argOffset + 4]);
        const double end = readTime(argv[argOffset + 5]);
        if (end > 0 && end <= start)
            throw std::invalid_argument("Invalid trim range");
        const std::wstring model = argv[argOffset + 6];
        if (model != L"original" && model != L"sf-performance" && model != L"sf-legacy"
            && model != L"amd-small" && model != L"amd-original")
            throw std::invalid_argument("Invalid model");
        const auto runtime = model.starts_with(L"amd-") ? NrRuntime::LmxxfAmd
            : model == L"original" ? NrRuntime::NvidiaOriginal : NrRuntime::Ampere;
        std::vector<NrLayerSettings> layers;
        for (int index = argOffset + 7; index < argc; ++index) {
            std::wistringstream stream(argv[index]);
            stream.imbue(std::locale::classic());
            NrLayerSettings layer;
            int policy = 0;
            if (!(stream >> policy >> layer.model.intensity >> layer.model.tone >> layer.model.structure) || !stream.eof())
                throw std::invalid_argument("Invalid layer");
            layer.runtime = runtime;
            layer.model.performance = model == L"sf-performance" || model == L"amd-small";
            layer.enabled = true;
            layer.sizePolicy = static_cast<veyra::pipeline::NrSizePolicy>(policy);
            layers.push_back(layer);
        }
        return run(argv[argOffset + 0], argv[argOffset + 1], argv[argOffset + 2],
            runtime, layers, codec == L"hevc", maxFrames, start, end);
    } catch (const std::exception&) {
        print(L"Falha ao preparar o processamento.");
        return 1;
    }
}
