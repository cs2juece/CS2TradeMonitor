#pragma once

#include <string>
#include <cstdint>

namespace bootstrapper
{
    bool IsNet10DesktopRuntimeVersion(const std::wstring& version);
    bool IsAllowedDownloadUrl(const std::wstring& url);
    std::wstring QuoteCommandLineArgument(const std::wstring& argument);
    int CalculateDownloadProgressPercent(std::uint64_t downloadedBytes, std::uint64_t totalBytes);
    bool IsDownloadStalled(std::uint64_t nowMilliseconds, std::uint64_t lastProgressMilliseconds,
        std::uint64_t timeoutMilliseconds);
    bool ShouldOfferApplicationEntry(bool setupFinished, bool setupSucceeded);
}
