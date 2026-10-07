#define UNICODE
#define _UNICODE
#include <windows.h>
#include <shlobj.h>
#include <shellapi.h>
#include <string>
#include <vector>
#include <algorithm>
#include <filesystem>

static std::wstring Lower(std::wstring s) {
    std::transform(s.begin(), s.end(), s.begin(), ::towlower);
    return s;
}

static bool ContainsI(const std::wstring& a, const std::wstring& b) {
    return Lower(a).find(Lower(b)) != std::wstring::npos;
}

static std::wstring GetFolder(int csidl) {
    wchar_t buf[MAX_PATH] = {};
    if (SUCCEEDED(SHGetFolderPathW(nullptr, csidl, nullptr, SHGFP_TYPE_CURRENT, buf))) return buf;
    return L"";
}

static void StopOldProcesses(const std::wstring& oldRoot) {
    std::wstring escaped = oldRoot;
    size_t pos = 0;
    while ((pos = escaped.find(L"'", pos)) != std::wstring::npos) {
        escaped.insert(pos, L"'");
        pos += 2;
    }

    std::wstring script =
        L"$p='" + escaped + L"';"
        L"Get-CimInstance Win32_Process | "
        L"Where-Object { ($_.CommandLine -and $_.CommandLine -like ('*'+$p+'*')) -or "
        L"($_.ExecutablePath -and $_.ExecutablePath -like ('*'+$p+'*')) } | "
        L"Where-Object { $_.ProcessId -ne $PID } | "
        L"ForEach-Object { try { Stop-Process -Id $_.ProcessId -Force -ErrorAction Stop } catch {} }";

    std::wstring cmd = L"powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"" + script + L"\"";
    std::vector<wchar_t> mutableCmd(cmd.begin(), cmd.end());
    mutableCmd.push_back(L'\0');

    STARTUPINFOW si{};
    si.cb = sizeof(si);
    PROCESS_INFORMATION pi{};

    if (CreateProcessW(nullptr, mutableCmd.data(), nullptr, nullptr, FALSE, CREATE_NO_WINDOW,
                       nullptr, nullptr, &si, &pi)) {
        WaitForSingleObject(pi.hProcess, 8000);
        CloseHandle(pi.hThread);
        CloseHandle(pi.hProcess);
    }
}

static void RemoveRunEntries(const std::wstring& oldRoot) {
    HKEY key{};
    if (RegOpenKeyExW(HKEY_CURRENT_USER,
        L"Software\\Microsoft\\Windows\\CurrentVersion\\Run",
        0, KEY_READ | KEY_WRITE, &key) != ERROR_SUCCESS) return;

    std::vector<std::wstring> toDelete;
    DWORD index = 0;
    for (;;) {
        wchar_t name[512] = {};
        DWORD nameLen = 511;
        BYTE data[4096] = {};
        DWORD dataLen = sizeof(data);
        DWORD type = 0;

        LONG rc = RegEnumValueW(key, index, name, &nameLen, nullptr, &type, data, &dataLen);
        if (rc == ERROR_NO_MORE_ITEMS) break;
        if (rc == ERROR_SUCCESS) {
            std::wstring value;
            if ((type == REG_SZ || type == REG_EXPAND_SZ) && dataLen >= sizeof(wchar_t)) {
                value.assign(reinterpret_cast<wchar_t*>(data));
            }
            std::wstring n(name);
            bool oldName = ContainsI(n, L"PCAI") || ContainsI(n, L"PC AI Server");
            bool oldTarget = ContainsI(value, oldRoot) || ContainsI(value, L"PCAI Server");
            if (oldName && oldTarget) toDelete.push_back(n);
        }
        ++index;
    }

    for (const auto& n : toDelete) RegDeleteValueW(key, n.c_str());
    RegCloseKey(key);
}

static void DeleteKnownShortcuts() {
    std::vector<std::wstring> folders = {
        GetFolder(CSIDL_STARTUP),
        GetFolder(CSIDL_DESKTOPDIRECTORY),
        GetFolder(CSIDL_PROGRAMS)
    };
    const wchar_t* names[] = {
        L"PCAI Server.lnk",
        L"PC AI Server.lnk",
        L"PCAI Server Settings.lnk",
        L"PC AI Server Settings.lnk"
    };

    for (const auto& folder : folders) {
        if (folder.empty()) continue;
        for (auto name : names) {
            std::filesystem::path p = std::filesystem::path(folder) / name;
            std::error_code ec;
            std::filesystem::remove(p, ec);
        }
    }
}

static bool IsSafeOldPath(const std::wstring& oldRoot) {
    if (oldRoot.empty()) return false;
    std::wstring low = Lower(oldRoot);
    if (low.find(L"pc ai studio") != std::wstring::npos) return false;
    std::filesystem::path p(oldRoot);
    return Lower(p.filename().wstring()) == L"pcai server";
}

int WINAPI wWinMain(HINSTANCE, HINSTANCE, PWSTR, int) {
    std::wstring local = GetFolder(CSIDL_LOCAL_APPDATA);
    std::wstring oldRoot = (std::filesystem::path(local) / L"PCAI Server").wstring();

    if (!IsSafeOldPath(oldRoot)) {
        MessageBoxW(nullptr,
            L"Защитная проверка пути не пройдена. Удаление отменено.",
            L"PCAI Old Version Remover", MB_OK | MB_ICONERROR);
        return 2;
    }

    std::wstring msg =
        L"Будет полностью удалена только старая версия PCAI Server:\n\n" +
        oldRoot +
        L"\n\nPC AI Studio, ComfyUI и модели НЕ будут удалены.\n\nПродолжить?";

    if (MessageBoxW(nullptr, msg.c_str(), L"PCAI Old Version Remover",
                    MB_YESNO | MB_ICONWARNING | MB_DEFBUTTON2) != IDYES) {
        return 0;
    }

    StopOldProcesses(oldRoot);
    Sleep(500);
    RemoveRunEntries(oldRoot);
    DeleteKnownShortcuts();

    std::error_code ec;
    if (std::filesystem::exists(oldRoot, ec)) {
        for (auto it = std::filesystem::recursive_directory_iterator(
                 oldRoot, std::filesystem::directory_options::skip_permission_denied, ec);
             it != std::filesystem::recursive_directory_iterator(); it.increment(ec)) {
            if (ec) { ec.clear(); continue; }
            std::filesystem::permissions(it->path(),
                std::filesystem::perms::owner_all,
                std::filesystem::perm_options::add, ec);
            SetFileAttributesW(it->path().c_str(), FILE_ATTRIBUTE_NORMAL);
            ec.clear();
        }
        std::filesystem::remove_all(oldRoot, ec);
    }

    bool remains = std::filesystem::exists(oldRoot);
    if (!remains) {
        MessageBoxW(nullptr,
            L"Старая версия PCAI Server удалена полностью.\n\n"
            L"PC AI Studio, ComfyUI и модели не изменялись.\n"
            L"Теперь можно запускать PC AI Studio.",
            L"Готово", MB_OK | MB_ICONINFORMATION);
        return 0;
    }

    MessageBoxW(nullptr,
        L"Часть старых файлов занята другим процессом.\n\n"
        L"Перезагрузите Windows и запустите эту утилиту ещё раз.",
        L"Не всё удалено", MB_OK | MB_ICONWARNING);
    return 1;
}
