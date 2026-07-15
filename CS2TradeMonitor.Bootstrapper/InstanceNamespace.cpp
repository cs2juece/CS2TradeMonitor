#include "InstanceNamespace.h"

#include <windows.h>
#include <bcrypt.h>

#include <algorithm>
#include <iomanip>
#include <sstream>
#include <stdexcept>
#include <vector>

namespace
{
    std::wstring RemoveExtendedPrefix(const std::wstring& path)
    {
        if (path.size() >= 8 && _wcsnicmp(path.c_str(), L"\\\\?\\UNC\\", 8) == 0)
            return L"\\\\" + path.substr(8);
        if (path.size() >= 4 && _wcsnicmp(path.c_str(), L"\\\\?\\", 4) == 0)
            return path.substr(4);
        return path;
    }

    size_t RootLength(const std::wstring& path)
    {
        if (path.size() >= 3 && path[1] == L':' && path[2] == L'\\')
            return 3;
        if (path.rfind(L"\\\\", 0) != 0)
            return 0;

        const size_t serverEnd = path.find(L'\\', 2);
        if (serverEnd == std::wstring::npos)
            return path.size();
        const size_t shareEnd = path.find(L'\\', serverEnd + 1);
        return shareEnd == std::wstring::npos ? path.size() : shareEnd + 1;
    }

    std::wstring ToInvariantLower(const std::wstring& value)
    {
        if (value.empty())
            return value;

        const int required = LCMapStringEx(
            LOCALE_NAME_INVARIANT,
            LCMAP_LOWERCASE,
            value.data(),
            static_cast<int>(value.size()),
            nullptr,
            0,
            nullptr,
            nullptr,
            0);
        if (required <= 0)
            throw std::runtime_error("LCMapStringEx failed");

        std::wstring lowered(static_cast<size_t>(required), L'\0');
        if (LCMapStringEx(
            LOCALE_NAME_INVARIANT,
            LCMAP_LOWERCASE,
            value.data(),
            static_cast<int>(value.size()),
            lowered.data(),
            required,
            nullptr,
            nullptr,
            0) <= 0)
        {
            throw std::runtime_error("LCMapStringEx failed");
        }

        return lowered;
    }

    std::vector<unsigned char> ToUtf8(const std::wstring& value)
    {
        const int required = WideCharToMultiByte(
            CP_UTF8,
            WC_ERR_INVALID_CHARS,
            value.data(),
            static_cast<int>(value.size()),
            nullptr,
            0,
            nullptr,
            nullptr);
        if (required <= 0)
            throw std::runtime_error("WideCharToMultiByte failed");

        std::vector<unsigned char> bytes(static_cast<size_t>(required));
        if (WideCharToMultiByte(
            CP_UTF8,
            WC_ERR_INVALID_CHARS,
            value.data(),
            static_cast<int>(value.size()),
            reinterpret_cast<char*>(bytes.data()),
            required,
            nullptr,
            nullptr) <= 0)
        {
            throw std::runtime_error("WideCharToMultiByte failed");
        }

        return bytes;
    }

    bool EnsureDirectory(const std::wstring& directory, instance_namespace::WritableProbeFailure& failure)
    {
        if (CreateDirectoryW(directory.c_str(), nullptr))
            return true;

        const DWORD error = GetLastError();
        const DWORD attributes = GetFileAttributesW(directory.c_str());
        if (error == ERROR_ALREADY_EXISTS && attributes != INVALID_FILE_ATTRIBUTES
            && (attributes & FILE_ATTRIBUTE_DIRECTORY) != 0)
        {
            return true;
        }

        failure = { directory, L"创建目录", error };
        return false;
    }

    bool ProbeDirectory(const std::wstring& directory, instance_namespace::WritableProbeFailure& failure)
    {
        const std::wstring suffix = std::to_wstring(GetCurrentProcessId()) + L"-"
            + std::to_wstring(GetTickCount64());
        const std::wstring source = directory + L"\\.write-probe-" + suffix + L".tmp";
        const std::wstring destination = source + L".renamed";
        HANDLE file = CreateFileW(source.c_str(), GENERIC_WRITE, 0, nullptr, CREATE_NEW,
            FILE_ATTRIBUTE_TEMPORARY, nullptr);
        if (file == INVALID_HANDLE_VALUE)
        {
            failure = { directory, L"创建临时文件", GetLastError() };
            return false;
        }

        constexpr char payload[] = "CS2TradeMonitor";
        DWORD written = 0;
        if (!WriteFile(file, payload, static_cast<DWORD>(sizeof(payload) - 1), &written, nullptr)
            || written != sizeof(payload) - 1)
        {
            const DWORD error = GetLastError();
            CloseHandle(file);
            DeleteFileW(source.c_str());
            failure = { directory, L"写入临时文件", error };
            return false;
        }
        if (!FlushFileBuffers(file))
        {
            const DWORD error = GetLastError();
            CloseHandle(file);
            DeleteFileW(source.c_str());
            failure = { directory, L"刷新临时文件", error };
            return false;
        }
        CloseHandle(file);

        if (!MoveFileExW(source.c_str(), destination.c_str(), MOVEFILE_WRITE_THROUGH))
        {
            const DWORD error = GetLastError();
            DeleteFileW(source.c_str());
            failure = { directory, L"重命名临时文件", error };
            return false;
        }
        if (!DeleteFileW(destination.c_str()))
        {
            const DWORD error = GetLastError();
            DeleteFileW(destination.c_str());
            failure = { directory, L"删除临时文件", error };
            return false;
        }

        return true;
    }
}

namespace instance_namespace
{
    std::wstring NormalizeCanonicalInstallRootForHash(const std::wstring& path)
    {
        if (path.empty())
            throw std::invalid_argument("install path is empty");

        std::wstring normalized = RemoveExtendedPrefix(path);
        std::replace(normalized.begin(), normalized.end(), L'/', L'\\');

        const DWORD required = GetFullPathNameW(normalized.c_str(), 0, nullptr, nullptr);
        if (required == 0)
            throw std::runtime_error("GetFullPathNameW failed");

        std::wstring fullPath(static_cast<size_t>(required), L'\0');
        const DWORD written = GetFullPathNameW(normalized.c_str(), required, fullPath.data(), nullptr);
        if (written == 0 || written >= required)
            throw std::runtime_error("GetFullPathNameW failed");
        fullPath.resize(written);

        const size_t rootLength = RootLength(fullPath);
        while (fullPath.size() > rootLength && fullPath.back() == L'\\')
            fullPath.pop_back();

        return ToInvariantLower(fullPath);
    }

    std::wstring ResolveCanonicalInstallRoot(const std::wstring& installRoot)
    {
        HANDLE handle = CreateFileW(
            installRoot.c_str(),
            FILE_READ_ATTRIBUTES,
            FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE,
            nullptr,
            OPEN_EXISTING,
            FILE_FLAG_BACKUP_SEMANTICS,
            nullptr);
        if (handle == INVALID_HANDLE_VALUE)
            throw std::runtime_error("CreateFileW failed for install root");

        try
        {
            std::wstring finalPath(512, L'\0');
            DWORD length = GetFinalPathNameByHandleW(
                handle,
                finalPath.data(),
                static_cast<DWORD>(finalPath.size()),
                FILE_NAME_NORMALIZED | VOLUME_NAME_DOS);
            if (length == 0)
                throw std::runtime_error("GetFinalPathNameByHandleW failed");
            if (length >= finalPath.size())
            {
                finalPath.assign(static_cast<size_t>(length) + 1, L'\0');
                length = GetFinalPathNameByHandleW(
                    handle,
                    finalPath.data(),
                    static_cast<DWORD>(finalPath.size()),
                    FILE_NAME_NORMALIZED | VOLUME_NAME_DOS);
                if (length == 0 || length >= finalPath.size())
                    throw std::runtime_error("GetFinalPathNameByHandleW failed");
            }
            finalPath.resize(length);
            CloseHandle(handle);
            return NormalizeCanonicalInstallRootForHash(finalPath);
        }
        catch (...)
        {
            CloseHandle(handle);
            throw;
        }
    }

    std::string ComputeInstanceHash(const std::wstring& canonicalInstallRoot)
    {
        std::vector<unsigned char> bytes = ToUtf8(canonicalInstallRoot);
        BCRYPT_ALG_HANDLE algorithm = nullptr;
        BCRYPT_HASH_HANDLE hash = nullptr;
        std::vector<unsigned char> hashObject;
        std::vector<unsigned char> digest;

        try
        {
            if (!BCRYPT_SUCCESS(BCryptOpenAlgorithmProvider(&algorithm, BCRYPT_SHA256_ALGORITHM, nullptr, 0)))
                throw std::runtime_error("BCryptOpenAlgorithmProvider failed");

            DWORD objectLength = 0;
            DWORD hashLength = 0;
            DWORD resultLength = 0;
            if (!BCRYPT_SUCCESS(BCryptGetProperty(
                algorithm,
                BCRYPT_OBJECT_LENGTH,
                reinterpret_cast<PUCHAR>(&objectLength),
                sizeof(objectLength),
                &resultLength,
                0)))
            {
                throw std::runtime_error("BCryptGetProperty object length failed");
            }
            if (!BCRYPT_SUCCESS(BCryptGetProperty(
                algorithm,
                BCRYPT_HASH_LENGTH,
                reinterpret_cast<PUCHAR>(&hashLength),
                sizeof(hashLength),
                &resultLength,
                0)))
            {
                throw std::runtime_error("BCryptGetProperty hash length failed");
            }

            hashObject.resize(objectLength);
            digest.resize(hashLength);
            if (!BCRYPT_SUCCESS(BCryptCreateHash(
                algorithm,
                &hash,
                hashObject.data(),
                static_cast<ULONG>(hashObject.size()),
                nullptr,
                0,
                0)))
            {
                throw std::runtime_error("BCryptCreateHash failed");
            }
            if (!BCRYPT_SUCCESS(BCryptHashData(hash, bytes.data(), static_cast<ULONG>(bytes.size()), 0)))
                throw std::runtime_error("BCryptHashData failed");
            if (!BCRYPT_SUCCESS(BCryptFinishHash(hash, digest.data(), static_cast<ULONG>(digest.size()), 0)))
                throw std::runtime_error("BCryptFinishHash failed");

            BCryptDestroyHash(hash);
            hash = nullptr;
            BCryptCloseAlgorithmProvider(algorithm, 0);
            algorithm = nullptr;
        }
        catch (...)
        {
            if (hash != nullptr)
                BCryptDestroyHash(hash);
            if (algorithm != nullptr)
                BCryptCloseAlgorithmProvider(algorithm, 0);
            throw;
        }

        std::ostringstream result;
        result << std::hex << std::setfill('0');
        for (const unsigned char value : digest)
            result << std::setw(2) << static_cast<unsigned int>(value);
        return result.str();
    }

    std::wstring BuildOsResourceName(ResourceKind kind, const std::string& instanceHash)
    {
        if (instanceHash.size() != 64)
            throw std::invalid_argument("instance hash must contain 64 hexadecimal characters");

        const std::wstring hash(instanceHash.begin(), instanceHash.end());
        switch (kind)
        {
        case ResourceKind::Bootstrap:
            return L"Global\\CS2TradeMonitor.Bootstrap." + hash;
        case ResourceKind::Application:
            return L"Global\\CS2TradeMonitor.App." + hash;
        case ResourceKind::ArgumentsPipe:
            return L"CS2TradeMonitor.Args." + hash;
        case ResourceKind::Update:
            return L"Global\\CS2TradeMonitor.Update." + hash;
        case ResourceKind::AutoStart:
            return L"CS2TradeMonitor_AutoStart_" + hash;
        default:
            throw std::invalid_argument("unknown resource kind");
        }
    }

    bool EnsureInstanceDataWritable(const std::wstring& installRoot, WritableProbeFailure& failure)
    {
        const std::wstring userDataRoot = installRoot + L"\\user-data";
        if (!EnsureDirectory(userDataRoot, failure))
            return false;

        constexpr const wchar_t* children[] =
        {
            L"data",
            L"secure",
            L"logs",
            L"cache",
            L"backup",
            L"updates"
        };
        for (const wchar_t* child : children)
        {
            const std::wstring directory = userDataRoot + L"\\" + child;
            if (!EnsureDirectory(directory, failure) || !ProbeDirectory(directory, failure))
                return false;
        }

        return true;
    }

    std::wstring FormatWindowsError(DWORD errorCode)
    {
        wchar_t* buffer = nullptr;
        const DWORD length = FormatMessageW(
            FORMAT_MESSAGE_ALLOCATE_BUFFER | FORMAT_MESSAGE_FROM_SYSTEM | FORMAT_MESSAGE_IGNORE_INSERTS,
            nullptr,
            errorCode,
            MAKELANGID(LANG_NEUTRAL, SUBLANG_DEFAULT),
            reinterpret_cast<wchar_t*>(&buffer),
            0,
            nullptr);
        if (length == 0 || buffer == nullptr)
            return L"Windows 错误 " + std::to_wstring(errorCode);

        std::wstring message(buffer, length);
        LocalFree(buffer);
        while (!message.empty() && (message.back() == L'\r' || message.back() == L'\n' || message.back() == L' '))
            message.pop_back();
        return message;
    }
}
