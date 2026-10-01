#include <windows.h>
#include <pathcch.h>
#include <strsafe.h>

#ifdef _MSC_VER
#pragma comment(lib, "pathcch.lib")
#endif

#if defined(BEHAVIOR_CONTROL_LAUNCHER)
#define LAUNCHER_TITLE L"啾糯桌宠 · 行为控制版"
#define LAUNCHER_LAYOUT_HELP L"请确认“程序文件”文件夹与这个行为控制版 EXE 放在一起，且文件夹内容完整。"
#elif defined(FEATURE_TEST_LAUNCHER)
#define LAUNCHER_TITLE L"啾糯桌宠 · 菲比啾比专属功能测试"
#define LAUNCHER_LAYOUT_HELP L"请把这个 EXE 留在“测试工具”文件夹中，并保留同一免安装目录下的“程序文件”文件夹。"
#else
#define LAUNCHER_TITLE L"啾糯桌宠"
#define LAUNCHER_LAYOUT_HELP L"请确认“程序文件”文件夹与这个 EXE 放在一起，且文件夹内容完整。"
#endif

static int ShowLaunchError(DWORD errorCode)
{
    WCHAR systemMessage[512] = L"";
    WCHAR message[1024] = L"";

    FormatMessageW(
        FORMAT_MESSAGE_FROM_SYSTEM | FORMAT_MESSAGE_IGNORE_INSERTS,
        NULL,
        errorCode,
        0,
        systemMessage,
        ARRAYSIZE(systemMessage),
        NULL);

    StringCchPrintfW(
        message,
        ARRAYSIZE(message),
        L"无法启动%s。\n\n%s\n\n系统信息：%s",
        LAUNCHER_TITLE,
        LAUNCHER_LAYOUT_HELP,
        systemMessage[0] == L'\0' ? L"未知错误" : systemMessage);

    MessageBoxW(NULL, message, LAUNCHER_TITLE, MB_OK | MB_ICONERROR);
    return 1;
}

int APIENTRY wWinMain(HINSTANCE instance, HINSTANCE previousInstance, LPWSTR commandLine, int showCommand)
{
    WCHAR rootDirectory[32768];
    WCHAR appDirectory[32768];
    WCHAR appPath[32768];
    WCHAR childCommandLine[32768];
    STARTUPINFOW startupInfo = {0};
    PROCESS_INFORMATION processInfo = {0};
    DWORD length;
    DWORD attributes;
    HRESULT result;

    UNREFERENCED_PARAMETER(instance);
    UNREFERENCED_PARAMETER(previousInstance);
    UNREFERENCED_PARAMETER(commandLine);
    UNREFERENCED_PARAMETER(showCommand);

    length = GetModuleFileNameW(NULL, rootDirectory, ARRAYSIZE(rootDirectory));
    if (length == 0 || length >= ARRAYSIZE(rootDirectory))
    {
        return ShowLaunchError(GetLastError());
    }

    result = PathCchRemoveFileSpec(rootDirectory, ARRAYSIZE(rootDirectory));
    if (FAILED(result))
    {
        return ShowLaunchError((DWORD)result);
    }

#if defined(FEATURE_TEST_LAUNCHER)
    result = PathCchRemoveFileSpec(rootDirectory, ARRAYSIZE(rootDirectory));
    if (result != S_OK)
    {
        return ShowLaunchError(ERROR_BAD_PATHNAME);
    }
#endif

    result = PathCchCombine(appDirectory, ARRAYSIZE(appDirectory), rootDirectory, L"程序文件");
    if (FAILED(result))
    {
        return ShowLaunchError((DWORD)result);
    }

    result = PathCchCombine(appPath, ARRAYSIZE(appPath), appDirectory, L"啾糯桌宠.exe");
    if (FAILED(result))
    {
        return ShowLaunchError((DWORD)result);
    }

    attributes = GetFileAttributesW(appPath);
    if (attributes == INVALID_FILE_ATTRIBUTES || (attributes & FILE_ATTRIBUTE_DIRECTORY) != 0)
    {
        return ShowLaunchError(ERROR_FILE_NOT_FOUND);
    }

#if defined(BEHAVIOR_CONTROL_LAUNCHER)
    result = StringCchPrintfW(childCommandLine, ARRAYSIZE(childCommandLine), L"\"%s\" --behavior-control", appPath);
#elif defined(FEATURE_TEST_LAUNCHER)
    result = StringCchPrintfW(childCommandLine, ARRAYSIZE(childCommandLine), L"\"%s\" --feibi-feature-test", appPath);
#else
    result = StringCchPrintfW(childCommandLine, ARRAYSIZE(childCommandLine), L"\"%s\"", appPath);
#endif
    if (FAILED(result))
    {
        return ShowLaunchError((DWORD)result);
    }

    startupInfo.cb = sizeof(startupInfo);
    if (!CreateProcessW(
            appPath,
            childCommandLine,
            NULL,
            NULL,
            FALSE,
            0,
            NULL,
            appDirectory,
            &startupInfo,
            &processInfo))
    {
        return ShowLaunchError(GetLastError());
    }

    CloseHandle(processInfo.hThread);
    CloseHandle(processInfo.hProcess);
    return 0;
}
