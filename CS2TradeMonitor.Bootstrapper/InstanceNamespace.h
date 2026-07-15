#pragma once

#include <string>
#include <windows.h>

namespace instance_namespace
{
    enum class ResourceKind
    {
        Bootstrap,
        Application,
        ArgumentsPipe,
        Update,
        AutoStart
    };

    struct WritableProbeFailure
    {
        std::wstring path;
        std::wstring operation;
        DWORD errorCode = ERROR_SUCCESS;
    };

    std::wstring NormalizeCanonicalInstallRootForHash(const std::wstring& path);
    std::wstring ResolveCanonicalInstallRoot(const std::wstring& installRoot);
    std::string ComputeInstanceHash(const std::wstring& canonicalInstallRoot);
    std::wstring BuildOsResourceName(ResourceKind kind, const std::string& instanceHash);
    bool EnsureInstanceDataWritable(const std::wstring& installRoot, WritableProbeFailure& failure);
    std::wstring FormatWindowsError(DWORD errorCode);
}
