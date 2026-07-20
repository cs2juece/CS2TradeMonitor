#include "BootstrapperCore.h"
#include "InstanceNamespace.h"

#include <windows.h>
#include <commctrl.h>
#include <dwmapi.h>
#include <richedit.h>
#include <shellapi.h>
#include <uxtheme.h>
#include <winhttp.h>

#include <algorithm>
#include <atomic>
#include <cstdint>
#include <functional>
#include <fstream>
#include <memory>
#include <string>
#include <thread>
#include <vector>

namespace
{
    constexpr wchar_t WindowTitle[] = L"CS2交易监控";
    constexpr wchar_t RuntimeUrl[] = L"https://aka.ms/dotnet/10.0/windowsdesktop-runtime-win-x64.exe";
    constexpr wchar_t RuntimeInstruction[] = L"首次运行需要配置必要运行环境";
    constexpr wchar_t RuntimeMessage[] =
        L"软件需要使用 Microsoft .NET 10 Desktop Runtime（x64）微软官方组件。\n"
        L"点击“确定”后，软件将自动完成环境配置，无需其他操作。";
    constexpr wchar_t SetupFailureTitle[] = L"环境配置失败";
    constexpr wchar_t SetupFailureMessage[] =
        L"如果Windows 请求管理员权限，请选择“是”\n"
        L"请重新打开软件 第二次仍然失败\n"
        L"请加qq群1057043823反馈";
    constexpr wchar_t AppRelativePath[] = L"app\\CS2TradeMonitor.exe";
    constexpr COLORREF DialogBackground = RGB(24, 27, 32);
    constexpr COLORREF DialogText = RGB(236, 239, 244);
    constexpr COLORREF DialogSubText = RGB(184, 192, 204);
    constexpr COLORREF DialogBorder = RGB(56, 63, 73);
    constexpr COLORREF DialogPrimary = RGB(0, 120, 215);
    constexpr wchar_t AuthorDivider[] = L"────────────────────────────────────────────────────────────────────────────────────────────────────────";
    constexpr wchar_t AuthorNoteUserStateSubKey[] = L"Software\\CS2TradeMonitor";
    constexpr UINT_PTR RuntimeProgressTimer = 1;
    std::wstring BootstrapperLogPath;

    enum class RuntimeSetupPhase
    {
        Preparing,
        Downloading,
        Installing,
        Complete,
        Failed
    };

    struct RuntimeSetupState
    {
        std::atomic<RuntimeSetupPhase> phase{ RuntimeSetupPhase::Preparing };
        std::atomic<int> progress{ 0 };
        std::atomic<bool> completed{ false };
        std::atomic<bool> proceedRequested{ false };
    };

    template <typename T, BOOL(WINAPI* CloseFunction)(T)>
    class UniqueWindowsHandle
    {
    public:
        UniqueWindowsHandle() = default;
        explicit UniqueWindowsHandle(T value) : value_(value) {}
        ~UniqueWindowsHandle() { Reset(); }
        UniqueWindowsHandle(const UniqueWindowsHandle&) = delete;
        UniqueWindowsHandle& operator=(const UniqueWindowsHandle&) = delete;
        UniqueWindowsHandle(UniqueWindowsHandle&& other) noexcept : value_(other.Release()) {}
        UniqueWindowsHandle& operator=(UniqueWindowsHandle&& other) noexcept
        {
            if (this != &other)
            {
                Reset();
                value_ = other.Release();
            }
            return *this;
        }
        T Get() const { return value_; }
        explicit operator bool() const { return value_ != nullptr && value_ != INVALID_HANDLE_VALUE; }
        T Release() { T value = value_; value_ = nullptr; return value; }
        void Reset(T value = nullptr)
        {
            if (*this)
                CloseFunction(value_);
            value_ = value;
        }
    private:
        T value_{};
    };

    using UniqueHandle = UniqueWindowsHandle<HANDLE, CloseHandle>;
    using UniqueInternet = UniqueWindowsHandle<HINTERNET, WinHttpCloseHandle>;

    class MutexOwnership
    {
    public:
        explicit MutexOwnership(HANDLE mutex) : mutex_(mutex) {}
        ~MutexOwnership() { Release(); }
        MutexOwnership(const MutexOwnership&) = delete;
        MutexOwnership& operator=(const MutexOwnership&) = delete;

        void Release()
        {
            if (mutex_ != nullptr)
            {
                ReleaseMutex(mutex_);
                mutex_ = nullptr;
            }
        }

    private:
        HANDLE mutex_{};
    };

    std::wstring JoinPath(const std::wstring& left, const std::wstring& right)
    {
        if (left.empty())
            return right;
        return left.back() == L'\\' ? left + right : left + L"\\" + right;
    }

    std::wstring GetExecutableDirectory()
    {
        std::vector<wchar_t> buffer(32768);
        DWORD length = GetModuleFileNameW(nullptr, buffer.data(), static_cast<DWORD>(buffer.size()));
        if (length == 0 || length >= buffer.size())
            return {};
        std::wstring path(buffer.data(), length);
        const size_t separator = path.find_last_of(L"\\/");
        return separator == std::wstring::npos ? std::wstring{} : path.substr(0, separator);
    }

    std::wstring GetEnvironmentValue(const wchar_t* name)
    {
        DWORD length = GetEnvironmentVariableW(name, nullptr, 0);
        if (length == 0)
            return {};
        std::vector<wchar_t> buffer(length);
        if (GetEnvironmentVariableW(name, buffer.data(), length) == 0)
            return {};
        return buffer.data();
    }

    bool DirectoryContainsNet10Runtime(const std::wstring& dotnetRoot)
    {
        const std::wstring search = JoinPath(dotnetRoot, L"shared\\Microsoft.WindowsDesktop.App\\*");
        WIN32_FIND_DATAW data{};
        HANDLE find = FindFirstFileW(search.c_str(), &data);
        if (find == INVALID_HANDLE_VALUE)
            return false;

        bool found = false;
        do
        {
            if ((data.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY) != 0
                && bootstrapper::IsNet10DesktopRuntimeVersion(data.cFileName))
            {
                found = true;
                break;
            }
        } while (FindNextFileW(find, &data));
        FindClose(find);
        return found;
    }

    std::vector<std::wstring> GetDotNetRoots()
    {
        std::vector<std::wstring> roots;
        for (const wchar_t* name : { L"DOTNET_ROOT_X64", L"DOTNET_ROOT" })
        {
            std::wstring value = GetEnvironmentValue(name);
            if (!value.empty())
                roots.push_back(std::move(value));
        }

        std::wstring programFiles = GetEnvironmentValue(L"ProgramW6432");
        if (programFiles.empty())
            programFiles = GetEnvironmentValue(L"ProgramFiles");
        if (!programFiles.empty())
            roots.push_back(JoinPath(programFiles, L"dotnet"));
        return roots;
    }

    bool HasNet10DesktopRuntime()
    {
        std::vector<std::wstring> roots = GetDotNetRoots();
        return std::any_of(roots.begin(), roots.end(), DirectoryContainsNet10Runtime);
    }

    int ScaleForDpi(HWND window, int value)
    {
        return MulDiv(value, static_cast<int>(GetDpiForWindow(window)), 96);
    }

    void ApplyDarkTitleBar(HWND window)
    {
        BOOL enabled = TRUE;
        DwmSetWindowAttribute(window, DWMWA_USE_IMMERSIVE_DARK_MODE, &enabled, sizeof(enabled));
        COLORREF caption = RGB(35, 38, 43);
        COLORREF text = DialogText;
        DwmSetWindowAttribute(window, DWMWA_CAPTION_COLOR, &caption, sizeof(caption));
        DwmSetWindowAttribute(window, DWMWA_TEXT_COLOR, &text, sizeof(text));
    }

    void DrawDialogButton(const DRAWITEMSTRUCT* item)
    {
        const bool primary = item->CtlID == IDOK;
        const bool pressed = (item->itemState & ODS_SELECTED) != 0;
        HBRUSH background = CreateSolidBrush(DialogBackground);
        FillRect(item->hDC, &item->rcItem, background);
        DeleteObject(background);

        COLORREF fillColor = primary
            ? (pressed ? RGB(0, 96, 176) : DialogPrimary)
            : (pressed ? RGB(48, 55, 65) : RGB(38, 44, 52));
        COLORREF borderColor = primary ? fillColor : DialogBorder;
        HBRUSH fill = CreateSolidBrush(fillColor);
        HPEN border = CreatePen(PS_SOLID, 1, borderColor);
        HGDIOBJ oldBrush = SelectObject(item->hDC, fill);
        HGDIOBJ oldPen = SelectObject(item->hDC, border);
        RoundRect(item->hDC, item->rcItem.left, item->rcItem.top,
            item->rcItem.right, item->rcItem.bottom, 6, 6);
        SelectObject(item->hDC, oldBrush);
        SelectObject(item->hDC, oldPen);
        DeleteObject(fill);
        DeleteObject(border);

        wchar_t text[64]{};
        GetWindowTextW(item->hwndItem, text, ARRAYSIZE(text));
        SetBkMode(item->hDC, TRANSPARENT);
        SetTextColor(item->hDC, DialogText);
        RECT bounds = item->rcItem;
        DrawTextW(item->hDC, text, -1, &bounds, DT_CENTER | DT_VCENTER | DT_SINGLELINE);
        if ((item->itemState & ODS_FOCUS) != 0)
        {
            InflateRect(&bounds, -2, -2);
            HPEN focusPen = CreatePen(PS_SOLID, 1, primary ? RGB(105, 190, 255) : DialogBorder);
            HGDIOBJ previousPen = SelectObject(item->hDC, focusPen);
            HGDIOBJ previousBrush = SelectObject(item->hDC, GetStockObject(NULL_BRUSH));
            RoundRect(item->hDC, bounds.left, bounds.top, bounds.right, bounds.bottom, 5, 5);
            SelectObject(item->hDC, previousBrush);
            SelectObject(item->hDC, previousPen);
            DeleteObject(focusPen);
        }
    }

    struct PromptDialogState
    {
        const wchar_t* heading{};
        const wchar_t* message{};
        bool allowCancel{};
        bool error{};
        HWND headingLabel{};
        HWND messageLabel{};
        HWND confirmButton{};
        HWND cancelButton{};
        HFONT headingFont{};
        HFONT bodyFont{};
        HFONT buttonFont{};
        HBRUSH backgroundBrush{};
        bool closed{};
        bool confirmed{};
    };

    LRESULT CALLBACK PromptWindowProc(HWND window, UINT message, WPARAM wParam, LPARAM lParam)
    {
        auto* state = reinterpret_cast<PromptDialogState*>(GetWindowLongPtrW(window, GWLP_USERDATA));
        if (message == WM_NCCREATE)
        {
            auto* create = reinterpret_cast<CREATESTRUCTW*>(lParam);
            state = static_cast<PromptDialogState*>(create->lpCreateParams);
            SetWindowLongPtrW(window, GWLP_USERDATA, reinterpret_cast<LONG_PTR>(state));
        }

        switch (message)
        {
        case WM_CREATE:
            ApplyDarkTitleBar(window);
            state->backgroundBrush = CreateSolidBrush(DialogBackground);
            state->headingLabel = CreateWindowExW(0, L"STATIC", state->heading,
                WS_CHILD | WS_VISIBLE | SS_LEFT, 0, 0, 0, 0, window,
                reinterpret_cast<HMENU>(101), GetModuleHandleW(nullptr), nullptr);
            state->messageLabel = CreateWindowExW(0, L"STATIC", state->message,
                WS_CHILD | WS_VISIBLE | SS_LEFT, 0, 0, 0, 0, window,
                reinterpret_cast<HMENU>(102), GetModuleHandleW(nullptr), nullptr);
            state->confirmButton = CreateWindowExW(0, L"BUTTON", state->allowCancel ? L"确定" : L"我知道了",
                WS_CHILD | WS_VISIBLE | WS_TABSTOP | BS_OWNERDRAW, 0, 0, 0, 0, window,
                reinterpret_cast<HMENU>(IDOK), GetModuleHandleW(nullptr), nullptr);
            if (state->allowCancel)
            {
                state->cancelButton = CreateWindowExW(0, L"BUTTON", L"取消",
                    WS_CHILD | WS_VISIBLE | WS_TABSTOP | BS_OWNERDRAW, 0, 0, 0, 0, window,
                    reinterpret_cast<HMENU>(IDCANCEL), GetModuleHandleW(nullptr), nullptr);
            }
            state->headingFont = CreateFontW(-ScaleForDpi(window, 20), 0, 0, 0, FW_BOLD,
                FALSE, FALSE, FALSE, DEFAULT_CHARSET, OUT_DEFAULT_PRECIS, CLIP_DEFAULT_PRECIS,
                CLEARTYPE_QUALITY, DEFAULT_PITCH, L"Microsoft YaHei UI");
            state->bodyFont = CreateFontW(-ScaleForDpi(window, 15), 0, 0, 0, FW_NORMAL,
                FALSE, FALSE, FALSE, DEFAULT_CHARSET, OUT_DEFAULT_PRECIS, CLIP_DEFAULT_PRECIS,
                CLEARTYPE_QUALITY, DEFAULT_PITCH, L"Microsoft YaHei UI");
            state->buttonFont = CreateFontW(-ScaleForDpi(window, 14), 0, 0, 0, FW_NORMAL,
                FALSE, FALSE, FALSE, DEFAULT_CHARSET, OUT_DEFAULT_PRECIS, CLIP_DEFAULT_PRECIS,
                CLEARTYPE_QUALITY, DEFAULT_PITCH, L"Microsoft YaHei UI");
            SendMessageW(state->headingLabel, WM_SETFONT, reinterpret_cast<WPARAM>(state->headingFont), TRUE);
            SendMessageW(state->messageLabel, WM_SETFONT, reinterpret_cast<WPARAM>(state->bodyFont), TRUE);
            SendMessageW(state->confirmButton, WM_SETFONT, reinterpret_cast<WPARAM>(state->buttonFont), TRUE);
            if (state->cancelButton)
                SendMessageW(state->cancelButton, WM_SETFONT, reinterpret_cast<WPARAM>(state->buttonFont), TRUE);
            return 0;
        case WM_SIZE:
            if (state)
            {
                RECT client{};
                GetClientRect(window, &client);
                int margin = ScaleForDpi(window, 24);
                int buttonWidth = ScaleForDpi(window, 94);
                int buttonHeight = ScaleForDpi(window, 34);
                int gap = ScaleForDpi(window, 10);
                int buttonTop = client.bottom - margin - buttonHeight;
                MoveWindow(state->headingLabel, margin, margin,
                    client.right - margin * 2, ScaleForDpi(window, 34), TRUE);
                MoveWindow(state->messageLabel, margin, margin + ScaleForDpi(window, 48),
                    client.right - margin * 2, std::max(0, buttonTop - margin - ScaleForDpi(window, 48)), TRUE);
                MoveWindow(state->confirmButton, client.right - margin - buttonWidth,
                    buttonTop, buttonWidth, buttonHeight, TRUE);
                if (state->cancelButton)
                    MoveWindow(state->cancelButton, client.right - margin - buttonWidth * 2 - gap,
                        buttonTop, buttonWidth, buttonHeight, TRUE);
            }
            return 0;
        case WM_CTLCOLORSTATIC:
            if (state)
            {
                HDC dc = reinterpret_cast<HDC>(wParam);
                HWND control = reinterpret_cast<HWND>(lParam);
                SetBkMode(dc, TRANSPARENT);
                SetTextColor(dc, control == state->headingLabel && state->error
                    ? RGB(245, 101, 101)
                    : control == state->messageLabel ? DialogSubText : DialogText);
                return reinterpret_cast<LRESULT>(state->backgroundBrush);
            }
            break;
        case WM_DRAWITEM:
            DrawDialogButton(reinterpret_cast<DRAWITEMSTRUCT*>(lParam));
            return TRUE;
        case WM_COMMAND:
            if (LOWORD(wParam) == IDOK || LOWORD(wParam) == IDCANCEL)
            {
                state->confirmed = LOWORD(wParam) == IDOK;
                DestroyWindow(window);
                return 0;
            }
            break;
        case WM_CLOSE:
            state->confirmed = !state->allowCancel;
            DestroyWindow(window);
            return 0;
        case WM_DESTROY:
            if (state)
            {
                state->closed = true;
                if (state->headingFont) DeleteObject(state->headingFont);
                if (state->bodyFont) DeleteObject(state->bodyFont);
                if (state->buttonFont) DeleteObject(state->buttonFont);
                if (state->backgroundBrush) DeleteObject(state->backgroundBrush);
            }
            return 0;
        }
        return DefWindowProcW(window, message, wParam, lParam);
    }

    bool ShowNativePrompt(const wchar_t* heading, const wchar_t* message, bool allowCancel, bool error)
    {
        static bool registered = false;
        if (!registered)
        {
            WNDCLASSEXW windowClass{};
            windowClass.cbSize = sizeof(windowClass);
            windowClass.lpfnWndProc = PromptWindowProc;
            windowClass.hInstance = GetModuleHandleW(nullptr);
            windowClass.hCursor = LoadCursorW(nullptr, IDC_ARROW);
            windowClass.hIcon = LoadIconW(GetModuleHandleW(nullptr), MAKEINTRESOURCEW(1));
            windowClass.hIconSm = windowClass.hIcon;
            windowClass.hbrBackground = CreateSolidBrush(DialogBackground);
            windowClass.lpszClassName = L"CS2TradeMonitor.Prompt";
            registered = RegisterClassExW(&windowClass) != 0 || GetLastError() == ERROR_CLASS_ALREADY_EXISTS;
        }
        if (!registered)
            return false;

        PromptDialogState state{ heading, message, allowCancel, error };
        const DWORD style = WS_OVERLAPPED | WS_CAPTION | WS_SYSMENU;
        const UINT dpi = GetDpiForSystem();
        RECT bounds{ 0, 0,
            MulDiv(620, static_cast<int>(dpi), 96),
            MulDiv(allowCancel ? 220 : error ? 230 : 210, static_cast<int>(dpi), 96) };
        AdjustWindowRectExForDpi(&bounds, style, FALSE, 0, dpi);
        int width = bounds.right - bounds.left;
        int height = bounds.bottom - bounds.top;
        RECT work{};
        SystemParametersInfoW(SPI_GETWORKAREA, 0, &work, 0);
        HWND window = CreateWindowExW(WS_EX_APPWINDOW, L"CS2TradeMonitor.Prompt", WindowTitle,
            style,
            work.left + (work.right - work.left - width) / 2,
            work.top + (work.bottom - work.top - height) / 2,
            width, height, nullptr, nullptr, GetModuleHandleW(nullptr), &state);
        if (!window)
            return false;

        ShowWindow(window, SW_SHOW);
        UpdateWindow(window);
        SetFocus(state.allowCancel ? state.confirmButton : state.confirmButton);
        MSG messageData{};
        while (!state.closed && GetMessageW(&messageData, nullptr, 0, 0) > 0)
        {
            if (!IsDialogMessageW(window, &messageData))
            {
                TranslateMessage(&messageData);
                DispatchMessageW(&messageData);
            }
        }
        return state.confirmed;
    }

    bool ConfirmRuntimeInstallation()
    {
        return ShowNativePrompt(RuntimeInstruction, RuntimeMessage, true, false);
    }

    std::wstring ReadUtf8File(const std::wstring& path)
    {
        std::ifstream input(path, std::ios::binary);
        if (!input)
            return {};
        std::string bytes((std::istreambuf_iterator<char>(input)), std::istreambuf_iterator<char>());
        if (bytes.empty())
            return {};
        int count = MultiByteToWideChar(CP_UTF8, MB_ERR_INVALID_CHARS, bytes.data(), static_cast<int>(bytes.size()), nullptr, 0);
        if (count <= 0)
            return {};
        std::wstring text(count, L'\0');
        MultiByteToWideChar(CP_UTF8, MB_ERR_INVALID_CHARS, bytes.data(), static_cast<int>(bytes.size()), text.data(), count);
        if (!text.empty() && text.front() == 0xfeff)
            text.erase(text.begin());
        return text;
    }

    struct AuthorDialogState
    {
        std::wstring text;
        RuntimeSetupState* runtimeSetup{};
        HWND richEdit{};
        HWND closeButton{};
        HWND runtimeDivider{};
        HWND runtimeHeading{};
        HWND runtimeStatus{};
        HWND runtimeProgress{};
        HWND runtimeHint{};
        HFONT bodyFont{};
        HFONT headingFont{};
        HFONT buttonFont{};
        HBRUSH backgroundBrush{};
        bool closed{};
    };

    void RefreshRuntimeProgress(HWND window, AuthorDialogState* state)
    {
        if (!state || !state->runtimeSetup)
            return;

        const RuntimeSetupPhase phase = state->runtimeSetup->phase.load();
        int progress = std::clamp(state->runtimeSetup->progress.load(), 0, 100);
        if (phase == RuntimeSetupPhase::Complete)
            progress = 100;

        std::wstring status = phase == RuntimeSetupPhase::Complete
            ? L"环境配置完成  100%"
            : phase == RuntimeSetupPhase::Failed
                ? L"环境配置未完成"
                : L"正在配置环境  " + std::to_wstring(progress) + L"%";
        SetWindowTextW(state->runtimeStatus, status.c_str());
        SendMessageW(state->runtimeProgress, PBM_SETPOS, progress, 0);

        const bool setupFinished = state->runtimeSetup->completed.load();
        const bool setupSucceeded = phase == RuntimeSetupPhase::Complete;
        if (bootstrapper::ShouldOfferApplicationEntry(setupFinished, setupSucceeded))
        {
            SetWindowTextW(state->runtimeHeading, L"首次运行环境配置完成");
            SetWindowTextW(state->runtimeHint, L"请阅读上方内容，准备好后进入软件");
            SetWindowTextW(state->closeButton, L"我知道了，进入软件");
            if (!IsWindowVisible(state->closeButton))
            {
                ShowWindow(state->closeButton, SW_SHOW);
                SetFocus(state->closeButton);
            }
        }
        else if (setupFinished && phase == RuntimeSetupPhase::Failed)
            DestroyWindow(window);
    }

    void FormatAuthorRange(AuthorDialogState* state, size_t start, size_t length,
        LONG height, bool bold, COLORREF textColor, COLORREF backgroundColor = DialogBackground)
    {
        CHARFORMAT2W format{};
        format.cbSize = sizeof(format);
        format.dwMask = CFM_FACE | CFM_SIZE | CFM_BOLD | CFM_COLOR | CFM_BACKCOLOR;
        format.dwEffects = bold ? CFE_BOLD : 0;
        format.yHeight = height;
        format.crTextColor = textColor;
        format.crBackColor = backgroundColor;
        wcscpy_s(format.szFaceName, L"Microsoft YaHei UI");
        SendMessageW(state->richEdit, EM_SETSEL, static_cast<WPARAM>(start), static_cast<LPARAM>(start + length));
        SendMessageW(state->richEdit, EM_SETCHARFORMAT, SCF_SELECTION, reinterpret_cast<LPARAM>(&format));
    }

    void FormatAuthorPhrase(AuthorDialogState* state, const wchar_t* phrase,
        LONG height, bool bold, COLORREF textColor, COLORREF backgroundColor = DialogBackground)
    {
        size_t start = 0;
        const size_t length = wcslen(phrase);
        while ((start = state->text.find(phrase, start)) != std::wstring::npos)
        {
            FormatAuthorRange(state, start, length, height, bold, textColor, backgroundColor);
            start += length;
        }
    }

    void FormatAuthorText(AuthorDialogState* state)
    {
        CHARFORMAT2W normal{};
        normal.cbSize = sizeof(normal);
        normal.dwMask = CFM_FACE | CFM_SIZE | CFM_COLOR | CFM_BACKCOLOR;
        normal.yHeight = 180;
        normal.crTextColor = DialogText;
        normal.crBackColor = DialogBackground;
        wcscpy_s(normal.szFaceName, L"Microsoft YaHei UI");
        SendMessageW(state->richEdit, EM_SETSEL, 0, -1);
        SendMessageW(state->richEdit, EM_SETCHARFORMAT, SCF_SELECTION, reinterpret_cast<LPARAM>(&normal));

        PARAFORMAT2 paragraph{};
        paragraph.cbSize = sizeof(paragraph);
        paragraph.dwMask = PFM_SPACEAFTER;
        paragraph.dySpaceAfter = 45;
        SendMessageW(state->richEdit, EM_SETPARAFORMAT, 0, reinterpret_cast<LPARAM>(&paragraph));

        for (const wchar_t* heading : {
            L"关于作者", L"为什么做这个工具", L"关于开发", L"关于自动化", L"关于免费和开源", L"最后" })
            FormatAuthorPhrase(state, heading, 210, true, DialogText);
        FormatAuthorPhrase(state, AuthorDivider, 100, false, DialogBorder);

        for (const wchar_t* strong : {
            L"一开始只是为了方便自己交易使用，后来慢慢增加了一些自己觉得有价值的功能。",
            L"如果它能够帮助更多交易玩家减少重复操作，把更多时间放在真正重要的事情上，那就是它存在的意义。",
            L"工具只能提高效率。",
            L"最终决定结果的，永远是交易者自己的判断和执行。",
            L"CS2TradeMonitor 是一个免费开源项目。",
            L"这个软件最开始只是我自己交易过程中使用的小工具。",
            L"如果它能在某个时候，帮你省下一点时间，少错过一次报价，少做一次重复操作，那就是它最大的价值。" })
            FormatAuthorPhrase(state, strong, 190, true, DialogText);

        for (const wchar_t* phrase : {
            L"CS2 饰品交易", L"真正适合自己的工具", L"AI 辅助下一步一步完成",
            L"真实交易过程中的需求", L"官方渠道" })
            FormatAuthorPhrase(state, phrase, 180, true, DialogText);

        SendMessageW(state->richEdit, EM_SETSEL, 0, 0);
    }

    std::wstring CompactAuthorText(const std::wstring& text)
    {
        std::wstring result;
        result.reserve(text.size());
        bool previousWasLineBreak = false;
        for (size_t index = 0; index < text.size(); ++index)
        {
            wchar_t value = text[index];
            if (value == L'\r')
                continue;
            if (value == L'\n')
            {
                if (!previousWasLineBreak)
                    result.push_back(L'\r');
                previousWasLineBreak = true;
                continue;
            }
            previousWasLineBreak = false;
            result.push_back(value);
        }

        for (const wchar_t* heading : {
            L"为什么做这个工具", L"关于开发", L"关于自动化", L"关于免费和开源", L"最后" })
        {
            std::wstring marker = std::wstring(L"\r") + heading;
            size_t position = result.find(marker);
            if (position != std::wstring::npos)
                result.replace(position, marker.size(), std::wstring(L"\r") + AuthorDivider + L"\r" + heading);
        }
        return result;
    }

    LRESULT CALLBACK AuthorWindowProc(HWND window, UINT message, WPARAM wParam, LPARAM lParam)
    {
        auto* state = reinterpret_cast<AuthorDialogState*>(GetWindowLongPtrW(window, GWLP_USERDATA));
        if (message == WM_NCCREATE)
        {
            auto* create = reinterpret_cast<CREATESTRUCTW*>(lParam);
            state = static_cast<AuthorDialogState*>(create->lpCreateParams);
            SetWindowLongPtrW(window, GWLP_USERDATA, reinterpret_cast<LONG_PTR>(state));
        }

        switch (message)
        {
        case WM_CREATE:
            ApplyDarkTitleBar(window);
            state->backgroundBrush = CreateSolidBrush(DialogBackground);
            state->richEdit = CreateWindowExW(
                0, MSFTEDIT_CLASS, state->text.c_str(),
                WS_CHILD | WS_VISIBLE | WS_VSCROLL | ES_MULTILINE | ES_READONLY | ES_AUTOVSCROLL,
                0, 0, 0, 0, window, reinterpret_cast<HMENU>(100), GetModuleHandleW(nullptr), nullptr);
            state->closeButton = CreateWindowExW(
                0, L"BUTTON", L"我知道了", WS_CHILD | WS_VISIBLE | WS_TABSTOP | BS_OWNERDRAW,
                0, 0, 0, 0, window, reinterpret_cast<HMENU>(IDOK), GetModuleHandleW(nullptr), nullptr);
            if (state->runtimeSetup)
            {
                state->runtimeDivider = CreateWindowExW(0, L"STATIC", AuthorDivider,
                    WS_CHILD | WS_VISIBLE | SS_LEFT, 0, 0, 0, 0, window,
                    reinterpret_cast<HMENU>(103), GetModuleHandleW(nullptr), nullptr);
                state->runtimeHeading = CreateWindowExW(0, L"STATIC", L"正在配置首次运行环境",
                    WS_CHILD | WS_VISIBLE | SS_LEFT, 0, 0, 0, 0, window,
                    reinterpret_cast<HMENU>(104), GetModuleHandleW(nullptr), nullptr);
                state->runtimeStatus = CreateWindowExW(0, L"STATIC", L"正在配置环境  0%",
                    WS_CHILD | WS_VISIBLE | SS_LEFT, 0, 0, 0, 0, window,
                    reinterpret_cast<HMENU>(105), GetModuleHandleW(nullptr), nullptr);
                state->runtimeProgress = CreateWindowExW(0, PROGRESS_CLASSW, nullptr,
                    WS_CHILD | WS_VISIBLE | PBS_SMOOTH, 0, 0, 0, 0, window,
                    reinterpret_cast<HMENU>(106), GetModuleHandleW(nullptr), nullptr);
                state->runtimeHint = CreateWindowExW(0, L"STATIC",
                    L"请保持窗口开启，完成后软件将自动启动",
                    WS_CHILD | WS_VISIBLE | SS_LEFT, 0, 0, 0, 0, window,
                    reinterpret_cast<HMENU>(107), GetModuleHandleW(nullptr), nullptr);
                ShowWindow(state->closeButton, SW_HIDE);
                SendMessageW(state->runtimeProgress, PBM_SETRANGE32, 0, 100);
                SendMessageW(state->runtimeProgress, PBM_SETBARCOLOR, 0, DialogPrimary);
                SetTimer(window, RuntimeProgressTimer, 200, nullptr);
            }
            state->bodyFont = CreateFontW(-16, 0, 0, 0, FW_NORMAL, FALSE, FALSE, FALSE, DEFAULT_CHARSET,
                OUT_DEFAULT_PRECIS, CLIP_DEFAULT_PRECIS, CLEARTYPE_QUALITY, DEFAULT_PITCH, L"Microsoft YaHei UI");
            state->headingFont = CreateFontW(-18, 0, 0, 0, FW_BOLD, FALSE, FALSE, FALSE, DEFAULT_CHARSET,
                OUT_DEFAULT_PRECIS, CLIP_DEFAULT_PRECIS, CLEARTYPE_QUALITY, DEFAULT_PITCH, L"Microsoft YaHei UI");
            state->buttonFont = CreateFontW(-15, 0, 0, 0, FW_NORMAL, FALSE, FALSE, FALSE, DEFAULT_CHARSET,
                OUT_DEFAULT_PRECIS, CLIP_DEFAULT_PRECIS, CLEARTYPE_QUALITY, DEFAULT_PITCH, L"Microsoft YaHei UI");
            SendMessageW(state->richEdit, WM_SETFONT, reinterpret_cast<WPARAM>(state->bodyFont), TRUE);
            SendMessageW(state->closeButton, WM_SETFONT, reinterpret_cast<WPARAM>(state->buttonFont), TRUE);
            if (state->runtimeSetup)
            {
                SendMessageW(state->runtimeDivider, WM_SETFONT, reinterpret_cast<WPARAM>(state->bodyFont), TRUE);
                SendMessageW(state->runtimeHeading, WM_SETFONT, reinterpret_cast<WPARAM>(state->headingFont), TRUE);
                SendMessageW(state->runtimeStatus, WM_SETFONT, reinterpret_cast<WPARAM>(state->bodyFont), TRUE);
                SendMessageW(state->runtimeHint, WM_SETFONT, reinterpret_cast<WPARAM>(state->bodyFont), TRUE);
            }
            SendMessageW(state->richEdit, EM_SETBKGNDCOLOR, 0, DialogBackground);
            SendMessageW(state->richEdit, EM_SETMARGINS, EC_LEFTMARGIN | EC_RIGHTMARGIN,
                MAKELPARAM(ScaleForDpi(window, 10), ScaleForDpi(window, 10)));
            SetWindowTheme(state->richEdit, L"DarkMode_Explorer", nullptr);
            FormatAuthorText(state);
            return 0;
        case WM_SIZE:
            if (state)
            {
                RECT client{};
                GetClientRect(window, &client);
                int margin = ScaleForDpi(window, 24);
                int footerHeight = ScaleForDpi(window, 58);
                int buttonWidth = ScaleForDpi(window, state->runtimeSetup ? 176 : 104);
                int buttonHeight = ScaleForDpi(window, 34);
                int contentWidth = std::max(0L, client.right - margin * 2);
                int runtimeHeight = state->runtimeSetup ? ScaleForDpi(window, 178) : 0;
                MoveWindow(state->richEdit, margin, margin, contentWidth,
                    std::max(0L, client.bottom - footerHeight - margin - runtimeHeight), TRUE);
                if (state->runtimeSetup)
                {
                    int top = client.bottom - margin - runtimeHeight;
                    MoveWindow(state->runtimeDivider, margin, top, contentWidth, ScaleForDpi(window, 18), TRUE);
                    MoveWindow(state->runtimeHeading, margin, top + ScaleForDpi(window, 24),
                        contentWidth, ScaleForDpi(window, 28), TRUE);
                    MoveWindow(state->runtimeStatus, margin, top + ScaleForDpi(window, 60),
                        contentWidth, ScaleForDpi(window, 24), TRUE);
                    MoveWindow(state->runtimeProgress, margin, top + ScaleForDpi(window, 90),
                        contentWidth, ScaleForDpi(window, 18), TRUE);
                    MoveWindow(state->runtimeHint, margin, top + ScaleForDpi(window, 120),
                        contentWidth, ScaleForDpi(window, 24), TRUE);
                }
                MoveWindow(state->closeButton,
                    std::max(0L, client.right - margin - buttonWidth),
                    std::max(0L, client.bottom - ScaleForDpi(window, 46)), buttonWidth, buttonHeight, TRUE);
            }
            return 0;
        case WM_TIMER:
            if (wParam == RuntimeProgressTimer)
            {
                RefreshRuntimeProgress(window, state);
                return 0;
            }
            break;
        case WM_DRAWITEM:
            DrawDialogButton(reinterpret_cast<DRAWITEMSTRUCT*>(lParam));
            return TRUE;
        case WM_CTLCOLORSTATIC:
            if (state && state->runtimeSetup)
            {
                HDC dc = reinterpret_cast<HDC>(wParam);
                HWND control = reinterpret_cast<HWND>(lParam);
                SetBkMode(dc, OPAQUE);
                SetBkColor(dc, DialogBackground);
                SetTextColor(dc, control == state->runtimeHeading ? DialogText : DialogSubText);
                return reinterpret_cast<LRESULT>(state->backgroundBrush);
            }
            break;
        case WM_COMMAND:
            if (LOWORD(wParam) == IDOK)
            {
                if (state && state->runtimeSetup)
                {
                    const bool setupFinished = state->runtimeSetup->completed.load();
                    const bool setupSucceeded = state->runtimeSetup->phase.load() == RuntimeSetupPhase::Complete;
                    if (!bootstrapper::ShouldOfferApplicationEntry(setupFinished, setupSucceeded))
                        return 0;
                    state->runtimeSetup->proceedRequested.store(true);
                }
                DestroyWindow(window);
                return 0;
            }
            break;
        case WM_CLOSE:
            if (state && state->runtimeSetup && !state->runtimeSetup->completed.load())
                return 0;
            DestroyWindow(window);
            return 0;
        case WM_DESTROY:
            if (state)
            {
                state->closed = true;
                KillTimer(window, RuntimeProgressTimer);
                if (state->bodyFont) DeleteObject(state->bodyFont);
                if (state->headingFont) DeleteObject(state->headingFont);
                if (state->buttonFont) DeleteObject(state->buttonFont);
                if (state->backgroundBrush) DeleteObject(state->backgroundBrush);
            }
            return 0;
        }
        return DefWindowProcW(window, message, wParam, lParam);
    }

    bool ShowAuthorDialog(const std::wstring& text, RuntimeSetupState* runtimeSetup = nullptr)
    {
        static bool registered = false;
        if (!registered)
        {
            WNDCLASSEXW windowClass{};
            windowClass.cbSize = sizeof(windowClass);
            windowClass.lpfnWndProc = AuthorWindowProc;
            windowClass.hInstance = GetModuleHandleW(nullptr);
            windowClass.hCursor = LoadCursorW(nullptr, IDC_ARROW);
            windowClass.hIcon = LoadIconW(GetModuleHandleW(nullptr), MAKEINTRESOURCEW(1));
            windowClass.hIconSm = windowClass.hIcon;
            windowClass.hbrBackground = CreateSolidBrush(DialogBackground);
            windowClass.lpszClassName = L"CS2TradeMonitor.AuthorNote";
            registered = RegisterClassExW(&windowClass) != 0 || GetLastError() == ERROR_CLASS_ALREADY_EXISTS;
        }
        if (!registered || !LoadLibraryW(L"Msftedit.dll"))
            return false;

        INITCOMMONCONTROLSEX controls{ sizeof(controls), ICC_PROGRESS_CLASS };
        InitCommonControlsEx(&controls);
        AuthorDialogState state{ CompactAuthorText(text), runtimeSetup };
        int width = 860;
        int height = 720;
        RECT work{};
        SystemParametersInfoW(SPI_GETWORKAREA, 0, &work, 0);
        width = std::min(width, static_cast<int>(work.right - work.left - 40));
        height = std::min(height, static_cast<int>(work.bottom - work.top - 40));
        HWND window = CreateWindowExW(
            WS_EX_APPWINDOW | WS_EX_TOPMOST, L"CS2TradeMonitor.AuthorNote", WindowTitle,
            WS_OVERLAPPEDWINDOW & ~(WS_MINIMIZEBOX),
            work.left + (work.right - work.left - width) / 2,
            work.top + (work.bottom - work.top - height) / 2,
            width, height, nullptr, nullptr, GetModuleHandleW(nullptr), &state);
        if (!window)
            return false;

        ShowWindow(window, SW_SHOW);
        SetWindowPos(window, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_SHOWWINDOW);
        UpdateWindow(window);
        MSG message{};
        while (!state.closed && GetMessageW(&message, nullptr, 0, 0) > 0)
        {
            if (!IsDialogMessageW(window, &message))
            {
                TranslateMessage(&message);
                DispatchMessageW(&message);
            }
        }
        return state.closed;
    }

    void ShowAuthorNoteOnce(const std::wstring& installRoot)
    {
        if (bootstrapper::IsAuthorNoteShown(installRoot, AuthorNoteUserStateSubKey))
            return;
        std::wstring text = ReadUtf8File(JoinPath(installRoot, L"resources\\author-note.txt"));
        if (!text.empty() && ShowAuthorDialog(text))
            bootstrapper::MarkAuthorNoteShown(installRoot, AuthorNoteUserStateSubKey);
    }

    void AppendFailureLog(const std::wstring& detail);

    void RemoveStaleRuntimeInstallers(const std::wstring& directory)
    {
        WIN32_FIND_DATAW data{};
        const std::wstring search = JoinPath(directory, L"windowsdesktop-runtime-10.0-*.exe");
        HANDLE find = FindFirstFileW(search.c_str(), &data);
        if (find == INVALID_HANDLE_VALUE)
            return;
        do
        {
            if ((data.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY) == 0)
                DeleteFileW(JoinPath(directory, data.cFileName).c_str());
        } while (FindNextFileW(find, &data));
        FindClose(find);
    }

    std::wstring CreateInstallerPath(const std::string& instanceHash)
    {
        wchar_t temp[MAX_PATH]{};
        if (GetTempPathW(ARRAYSIZE(temp), temp) == 0)
            return {};
        std::wstring directory = JoinPath(temp, L"CS2TradeMonitor");
        CreateDirectoryW(directory.c_str(), nullptr);
        directory = JoinPath(directory, std::wstring(instanceHash.begin(), instanceHash.end()));
        CreateDirectoryW(directory.c_str(), nullptr);
        directory = JoinPath(directory, L"runtime");
        CreateDirectoryW(directory.c_str(), nullptr);
        RemoveStaleRuntimeInstallers(directory);
        return JoinPath(directory, L"windowsdesktop-runtime-10.0-" + std::to_wstring(GetCurrentProcessId())
            + L"-" + std::to_wstring(GetTickCount64()) + L".exe");
    }

    bool DownloadRuntime(const std::wstring& destination, std::wstring& error,
        const std::function<void(std::uint64_t, std::uint64_t)>& reportProgress)
    {
        URL_COMPONENTS components{};
        components.dwStructSize = sizeof(components);
        components.dwSchemeLength = static_cast<DWORD>(-1);
        components.dwHostNameLength = static_cast<DWORD>(-1);
        components.dwUrlPathLength = static_cast<DWORD>(-1);
        if (!WinHttpCrackUrl(RuntimeUrl, 0, 0, &components))
        {
            error = L"WinHttpCrackUrl failed";
            return false;
        }

        std::wstring host(components.lpszHostName, components.dwHostNameLength);
        std::wstring path(components.lpszUrlPath, components.dwUrlPathLength);
        UniqueInternet session(WinHttpOpen(L"CS2TradeMonitor/1.0", WINHTTP_ACCESS_TYPE_AUTOMATIC_PROXY,
            WINHTTP_NO_PROXY_NAME, WINHTTP_NO_PROXY_BYPASS, 0));
        if (session)
            WinHttpSetTimeouts(session.Get(), 10'000, 10'000, 30'000, 30'000);
        UniqueInternet connection(session ? WinHttpConnect(session.Get(), host.c_str(), components.nPort, 0) : nullptr);
        UniqueInternet request(connection ? WinHttpOpenRequest(connection.Get(), L"GET", path.c_str(), nullptr,
            WINHTTP_NO_REFERER, WINHTTP_DEFAULT_ACCEPT_TYPES, WINHTTP_FLAG_SECURE) : nullptr);
        if (!session || !connection || !request
            || !WinHttpSendRequest(request.Get(), WINHTTP_NO_ADDITIONAL_HEADERS, 0,
                WINHTTP_NO_REQUEST_DATA, 0, 0, 0)
            || !WinHttpReceiveResponse(request.Get(), nullptr))
        {
            error = L"WinHTTP request failed: " + std::to_wstring(GetLastError());
            return false;
        }

        DWORD contentLength = 0;
        DWORD contentLengthSize = sizeof(contentLength);
        WinHttpQueryHeaders(request.Get(), WINHTTP_QUERY_CONTENT_LENGTH | WINHTTP_QUERY_FLAG_NUMBER,
            WINHTTP_HEADER_NAME_BY_INDEX, &contentLength, &contentLengthSize, WINHTTP_NO_HEADER_INDEX);

        DWORD status = 0;
        DWORD statusSize = sizeof(status);
        if (!WinHttpQueryHeaders(request.Get(), WINHTTP_QUERY_STATUS_CODE | WINHTTP_QUERY_FLAG_NUMBER,
            WINHTTP_HEADER_NAME_BY_INDEX, &status, &statusSize, WINHTTP_NO_HEADER_INDEX) || status != 200)
        {
            error = L"Runtime download HTTP status: " + std::to_wstring(status);
            return false;
        }

        DWORD finalUrlBytes = 0;
        WinHttpQueryOption(request.Get(), WINHTTP_OPTION_URL, nullptr, &finalUrlBytes);
        std::vector<wchar_t> finalUrl(finalUrlBytes / sizeof(wchar_t) + 1);
        if (finalUrlBytes == 0 || !WinHttpQueryOption(request.Get(), WINHTTP_OPTION_URL, finalUrl.data(), &finalUrlBytes)
            || !bootstrapper::IsAllowedDownloadUrl(finalUrl.data()))
        {
            error = L"Unexpected runtime download host";
            return false;
        }

        UniqueHandle file(CreateFileW(destination.c_str(), GENERIC_WRITE, 0, nullptr, CREATE_ALWAYS,
            FILE_ATTRIBUTE_TEMPORARY | FILE_FLAG_SEQUENTIAL_SCAN, nullptr));
        if (!file)
        {
            error = L"Cannot create runtime installer: " + std::to_wstring(GetLastError());
            return false;
        }

        std::vector<BYTE> buffer(128 * 1024);
        std::uint64_t downloaded = 0;
        std::uint64_t lastProgress = GetTickCount64();
        reportProgress(0, contentLength);
        while (true)
        {
            DWORD read = 0;
            if (!WinHttpReadData(request.Get(), buffer.data(), static_cast<DWORD>(buffer.size()), &read))
            {
                error = L"Runtime download read failed: " + std::to_wstring(GetLastError());
                return false;
            }
            if (read == 0)
                break;
            DWORD written = 0;
            if (!WriteFile(file.Get(), buffer.data(), read, &written, nullptr) || written != read)
            {
                error = L"Runtime installer write failed: " + std::to_wstring(GetLastError());
                return false;
            }
            downloaded += read;
            const std::uint64_t now = GetTickCount64();
            if (bootstrapper::IsDownloadStalled(now, lastProgress, 30'000))
            {
                error = L"Runtime download stalled";
                return false;
            }
            lastProgress = now;
            reportProgress(downloaded, contentLength);
        }
        if (downloaded == 0 || (contentLength > 0 && downloaded != contentLength))
        {
            error = L"Runtime download was incomplete";
            return false;
        }
        reportProgress(downloaded, contentLength == 0 ? downloaded : contentLength);
        return true;
    }

    bool InstallRuntime(const std::wstring& installer, DWORD& exitCode, std::wstring& error)
    {
        SHELLEXECUTEINFOW execute{};
        execute.cbSize = sizeof(execute);
        execute.fMask = SEE_MASK_NOCLOSEPROCESS | SEE_MASK_FLAG_NO_UI;
        execute.lpVerb = L"runas";
        execute.lpFile = installer.c_str();
        execute.lpParameters = L"/install /quiet /norestart";
        execute.nShow = SW_HIDE;
        if (!ShellExecuteExW(&execute) || !execute.hProcess)
        {
            error = L"Runtime installer launch failed: " + std::to_wstring(GetLastError());
            return false;
        }
        UniqueHandle process(execute.hProcess);
        if (WaitForSingleObject(process.Get(), INFINITE) != WAIT_OBJECT_0
            || !GetExitCodeProcess(process.Get(), &exitCode))
        {
            error = L"Runtime installer wait failed: " + std::to_wstring(GetLastError());
            return false;
        }
        return exitCode == 0 || exitCode == 1641 || exitCode == 3010;
    }

    void AppendFailureLog(const std::wstring& detail)
    {
        if (BootstrapperLogPath.empty())
            return;
        std::wofstream output(BootstrapperLogPath, std::ios::app);
        if (!output)
            return;
        SYSTEMTIME now{};
        GetLocalTime(&now);
        output << now.wYear << L'-' << now.wMonth << L'-' << now.wDay << L' '
            << now.wHour << L':' << now.wMinute << L':' << now.wSecond << L' ' << detail << L'\n';
    }

    void ShowSetupFailure(const std::wstring& detail)
    {
        AppendFailureLog(detail);
        ShowNativePrompt(SetupFailureTitle, SetupFailureMessage, false, true);
    }

    bool LaunchApplication(const std::wstring& installRoot, int argumentCount, wchar_t** arguments,
        bool openSettings = false, bool showErrors = true)
    {
        const std::wstring appPath = JoinPath(installRoot, AppRelativePath);
        if (GetFileAttributesW(appPath.c_str()) == INVALID_FILE_ATTRIBUTES)
        {
            if (showErrors)
            {
                ShowNativePrompt(L"软件启动失败",
                    L"找不到主程序文件：app\\CS2TradeMonitor.exe\n请重新下载完整安装包。", false, true);
            }
            return false;
        }
        std::wstring commandLine = bootstrapper::QuoteCommandLineArgument(appPath);
        for (int index = 1; index < argumentCount; ++index)
            commandLine += L" " + bootstrapper::QuoteCommandLineArgument(arguments[index]);
        if (openSettings)
            commandLine += L" --open-settings";
        std::vector<wchar_t> mutableCommand(commandLine.begin(), commandLine.end());
        mutableCommand.push_back(L'\0');

        SetEnvironmentVariableW(L"DOTNET_DISABLE_GUI_ERRORS", L"1");
        STARTUPINFOW startup{};
        startup.cb = sizeof(startup);
        PROCESS_INFORMATION process{};
        if (!CreateProcessW(appPath.c_str(), mutableCommand.data(), nullptr, nullptr, FALSE,
            CREATE_NO_WINDOW, nullptr,
            installRoot.c_str(), &startup, &process))
        {
            if (showErrors)
                ShowNativePrompt(L"软件启动失败", L"软件启动失败，请重新下载完整安装包。", false, true);
            return false;
        }
        CloseHandle(process.hThread);
        CloseHandle(process.hProcess);
        return true;
    }
}

int WINAPI wWinMain(HINSTANCE, HINSTANCE, PWSTR, int)
{
    SetProcessDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
    const std::wstring installRoot = GetExecutableDirectory();
    if (installRoot.empty())
        return 1;
    std::string instanceHash;
    try
    {
        const std::wstring canonicalRoot = instance_namespace::ResolveCanonicalInstallRoot(installRoot);
        instanceHash = instance_namespace::ComputeInstanceHash(canonicalRoot);
    }
    catch (const std::exception&)
    {
        ShowNativePrompt(L"软件启动失败",
            L"无法确定当前软件目录的实例身份，软件已拒绝启动。\n请将完整软件目录复制到本地磁盘后重试。",
            false,
            true);
        return 1;
    }

    instance_namespace::WritableProbeFailure probeFailure;
    if (!instance_namespace::EnsureInstanceDataWritable(installRoot, probeFailure))
    {
        const std::wstring message = L"程序目录无法保存用户数据，软件已拒绝启动。\n\n路径："
            + probeFailure.path + L"\n操作：" + probeFailure.operation + L"\n原因："
            + instance_namespace::FormatWindowsError(probeFailure.errorCode)
            + L"\n\n请将完整软件目录复制到当前用户拥有写入权限的位置后重新启动。"
            + L"\n软件不会提权，也不会回退使用 LocalAppData。";
        ShowNativePrompt(L"软件目录不可写", message.c_str(), false, true);
        return 1;
    }
    BootstrapperLogPath = JoinPath(installRoot, L"user-data\\logs\\bootstrapper.log");

    const std::wstring mutexName = instance_namespace::BuildOsResourceName(
        instance_namespace::ResourceKind::Bootstrap,
        instanceHash);
    UniqueHandle mutex(CreateMutexW(nullptr, FALSE, mutexName.c_str()));
    if (!mutex)
    {
        ShowNativePrompt(L"软件启动失败", L"无法创建当前目录的启动协调锁，软件已拒绝启动。", false, true);
        return 1;
    }
    const DWORD mutexWait = WaitForSingleObject(mutex.Get(), INFINITE);
    if (mutexWait != WAIT_OBJECT_0 && mutexWait != WAIT_ABANDONED)
    {
        ShowNativePrompt(L"软件启动失败", L"等待当前目录的启动准备失败，请稍后重试。", false, true);
        return 1;
    }
    MutexOwnership mutexOwnership(mutex.Get());
    int argumentCount = 0;
    std::unique_ptr<wchar_t*, decltype(&LocalFree)> arguments(
        CommandLineToArgvW(GetCommandLineW(), &argumentCount), LocalFree);
    if (!arguments)
        return 1;

    if (HasNet10DesktopRuntime())
    {
        ShowAuthorNoteOnce(installRoot);
        mutexOwnership.Release();
        return LaunchApplication(installRoot, argumentCount, arguments.get()) ? 0 : 1;
    }

    bool confirmed = ConfirmRuntimeInstallation();
    if (!confirmed)
    {
        ShowAuthorNoteOnce(installRoot);
        return 0;
    }

    std::wstring installer = CreateInstallerPath(instanceHash);
    std::wstring error;
    DWORD exitCode = 0;
    bool configured = false;
    RuntimeSetupState setupState;
    AppendFailureLog(L"Runtime setup started");
    std::thread runtimeSetup([&]()
    {
        HRESULT comResult = CoInitializeEx(nullptr, COINIT_APARTMENTTHREADED);
        setupState.phase.store(RuntimeSetupPhase::Downloading);
        const bool downloaded = !installer.empty()
            && DownloadRuntime(installer, error, [&](std::uint64_t downloadedBytes, std::uint64_t totalBytes)
            {
                setupState.progress.store(
                    bootstrapper::CalculateDownloadProgressPercent(downloadedBytes, totalBytes));
            });
        bool installed = false;
        if (downloaded)
        {
            setupState.progress.store(100);
            setupState.phase.store(RuntimeSetupPhase::Installing);
            installed = InstallRuntime(installer, exitCode, error);
        }
        configured = installed && HasNet10DesktopRuntime();
        if (configured)
        {
            setupState.phase.store(RuntimeSetupPhase::Complete);
        }
        else
        {
            setupState.phase.store(RuntimeSetupPhase::Failed);
            if (error.empty())
                error = L"Runtime was not detected after installer exit code " + std::to_wstring(exitCode);
            AppendFailureLog(error);
        }
        setupState.completed.store(true);
        if (SUCCEEDED(comResult))
            CoUninitialize();
    });
    const std::wstring authorText = ReadUtf8File(JoinPath(installRoot, L"resources\\author-note.txt"));
    ShowAuthorDialog(authorText, &setupState);
    runtimeSetup.join();
    if (!installer.empty())
        DeleteFileW(installer.c_str());
    if (!configured)
    {
        ShowSetupFailure(error);
        return 1;
    }
    if (!setupState.proceedRequested.load())
        return 0;

    mutexOwnership.Release();
    const bool applicationLaunched = LaunchApplication(
        installRoot, argumentCount, arguments.get(), true, false);
    if (!applicationLaunched)
    {
        ShowNativePrompt(L"软件启动失败", L"软件启动失败，请重新下载完整安装包。", false, true);
        return 1;
    }
    bootstrapper::MarkAuthorNoteShown(installRoot, AuthorNoteUserStateSubKey);
    return 0;
}
