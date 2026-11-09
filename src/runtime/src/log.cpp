#include "log.h"

#include <windows.h>
#include <cstdarg>
#include <cstdio>

namespace {

constexpr unsigned long long kMaxLogBytes = 2ull * 1024 * 1024;

bool EnsureDir(const std::wstring& dir) {
    if (dir.empty()) return false;
    DWORD attr = GetFileAttributesW(dir.c_str());
    if (attr != INVALID_FILE_ATTRIBUTES && (attr & FILE_ATTRIBUTE_DIRECTORY)) return true;
    if (CreateDirectoryW(dir.c_str(), nullptr)) return true;
    return GetLastError() == ERROR_ALREADY_EXISTS;
}

std::wstring ReadEnv(const wchar_t* name) {
    DWORD len = GetEnvironmentVariableW(name, nullptr, 0);
    if (len == 0) return {};
    std::wstring value(len, L'\0');
    DWORD written = GetEnvironmentVariableW(name, value.data(), len);
    value.resize(written);
    return value;
}

void RotateIfHuge(const std::wstring& path) {
    WIN32_FILE_ATTRIBUTE_DATA data{};
    if (!GetFileAttributesExW(path.c_str(), GetFileExInfoStandard, &data)) return;
    unsigned long long size = (static_cast<unsigned long long>(data.nFileSizeHigh) << 32) | data.nFileSizeLow;
    if (size < kMaxLogBytes) return;
    std::wstring old = path + L".old";
    DeleteFileW(old.c_str());
    MoveFileW(path.c_str(), old.c_str());
}

}  // namespace

std::wstring DataDir() {
    std::wstring local = ReadEnv(L"LOCALAPPDATA");
    if (local.empty()) return L".";
    std::wstring dir = local + L"\\MW3Mod";
    EnsureDir(dir);
    return dir;
}

std::wstring LogPath() {
    static const std::wstring path = [] {
        std::wstring root = DataDir();
        std::wstring legacy = root + L"\\runtime.log";
        std::wstring dir = root + L"\\Log";
        if (!EnsureDir(dir)) return legacy;

        std::wstring target = dir + L"\\runtime.log";
        std::wstring targetOld = target + L".old";
        if (GetFileAttributesW(target.c_str()) == INVALID_FILE_ATTRIBUTES) {
            MoveFileW(legacy.c_str(), target.c_str());
        }
        if (GetFileAttributesW(targetOld.c_str()) == INVALID_FILE_ATTRIBUTES) {
            MoveFileW((legacy + L".old").c_str(), targetOld.c_str());
        }
        return target;
    }();
    return path;
}

void LogF(const wchar_t* fmt, ...) {
    std::wstring path = LogPath();
    RotateIfHuge(path);

    SYSTEMTIME st{};
    GetLocalTime(&st);

    wchar_t message[2048];
    va_list args;
    va_start(args, fmt);
    _vsnwprintf_s(message, _countof(message), _TRUNCATE, fmt, args);
    va_end(args);

    wchar_t line[4096];
    _snwprintf_s(line, _countof(line), _TRUNCATE, L"[%04u-%02u-%02u %02u:%02u:%02u.%03u] %s\r\n",
                 st.wYear, st.wMonth, st.wDay, st.wHour, st.wMinute, st.wSecond, st.wMilliseconds, message);

    FILE* file = nullptr;
    if (_wfopen_s(&file, path.c_str(), L"ab") == 0 && file) {
        fwrite(line, sizeof(wchar_t), wcslen(line), file);
        fclose(file);
    }
}
