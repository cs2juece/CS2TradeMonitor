#include "BootstrapperCore.h"

#include <windows.h>
#include <winhttp.h>

namespace bootstrapper
{
    bool IsNet10DesktopRuntimeVersion(const std::wstring& version)
    {
        if (version.size() < 4 || version.rfind(L"10.", 0) != 0)
            return false;

        for (size_t index = 3; index < version.size(); ++index)
        {
            const wchar_t value = version[index];
            if ((value < L'0' || value > L'9') && value != L'.' && value != L'-')
                return false;
        }

        return true;
    }

    bool IsAllowedDownloadUrl(const std::wstring& url)
    {
        URL_COMPONENTS components{};
        components.dwStructSize = sizeof(components);
        components.dwSchemeLength = static_cast<DWORD>(-1);
        components.dwHostNameLength = static_cast<DWORD>(-1);
        if (!WinHttpCrackUrl(url.c_str(), 0, 0, &components))
            return false;

        if (components.nScheme != INTERNET_SCHEME_HTTPS)
            return false;

        std::wstring host(components.lpszHostName, components.dwHostNameLength);
        return _wcsicmp(host.c_str(), L"aka.ms") == 0
            || _wcsicmp(host.c_str(), L"builds.dotnet.microsoft.com") == 0;
    }

    std::wstring QuoteCommandLineArgument(const std::wstring& argument)
    {
        if (argument.empty())
            return L"\"\"";
        if (argument.find_first_of(L" \t\"") == std::wstring::npos)
            return argument;

        std::wstring result = L"\"";
        size_t backslashes = 0;
        for (const wchar_t value : argument)
        {
            if (value == L'\\')
            {
                ++backslashes;
                continue;
            }

            if (value == L'\"')
            {
                result.append(backslashes * 2 + 1, L'\\');
                result.push_back(L'\"');
                backslashes = 0;
                continue;
            }

            result.append(backslashes, L'\\');
            backslashes = 0;
            result.push_back(value);
        }

        result.append(backslashes * 2, L'\\');
        result.push_back(L'\"');
        return result;
    }

    int CalculateDownloadProgressPercent(std::uint64_t downloadedBytes, std::uint64_t totalBytes)
    {
        if (totalBytes == 0)
            return 0;
        const std::uint64_t percent = downloadedBytes >= totalBytes
            ? 100
            : downloadedBytes * 100 / totalBytes;
        return static_cast<int>(percent);
    }

    bool IsDownloadStalled(std::uint64_t nowMilliseconds, std::uint64_t lastProgressMilliseconds,
        std::uint64_t timeoutMilliseconds)
    {
        return nowMilliseconds >= lastProgressMilliseconds
            && nowMilliseconds - lastProgressMilliseconds >= timeoutMilliseconds;
    }

    bool ShouldOfferApplicationEntry(bool setupFinished, bool setupSucceeded)
    {
        return setupFinished && setupSucceeded;
    }
}
