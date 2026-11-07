#pragma once

#include <string>

std::wstring DataDir();
std::wstring LogPath();

void LogF(const wchar_t* fmt, ...);
