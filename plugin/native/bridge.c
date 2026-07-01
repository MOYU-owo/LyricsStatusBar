#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <stddef.h>
#include <string.h>

typedef enum NativeAPIType {
    NativeInt,
    NativeBoolean,
    NativeDouble,
    NativeString,
    NativeV8Value
} NativeAPIType;

typedef enum NCMProcessType {
    ProcessUndetected = 0x0,
    ProcessMain = 0x0001,
    ProcessRenderer = 0x10,
    ProcessGPU = 0x100,
    ProcessUtility = 0x1000
} NCMProcessType;

typedef const char *(__cdecl *NativeFunction)(void **);
typedef int (__cdecl *AddNativeAPI)(NativeAPIType arguments[], int argument_count, const char *identifier, NativeFunction function);

typedef struct PluginAPI {
    AddNativeAPI addNativeAPI;
    const char *betterncmVersion;
    NCMProcessType processType;
    const unsigned short (*ncmVersion)[3];
} PluginAPI;

static SRWLOCK pipe_lock = SRWLOCK_INIT;
static HANDLE pipe_handle = INVALID_HANDLE_VALUE;
static const wchar_t *pipe_name = L"\\\\.\\pipe\\LyricsStatusBar.Bridge.v1";

static void close_pipe(void) {
    if (pipe_handle != INVALID_HANDLE_VALUE) {
        CloseHandle(pipe_handle);
        pipe_handle = INVALID_HANDLE_VALUE;
    }
}

static int ensure_pipe(void) {
    if (pipe_handle != INVALID_HANDLE_VALUE) {
        return 1;
    }
    if (!WaitNamedPipeW(pipe_name, 25) && GetLastError() != ERROR_SEM_TIMEOUT) {
        return 0;
    }
    pipe_handle = CreateFileW(pipe_name, GENERIC_WRITE, 0, NULL, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, NULL);
    return pipe_handle != INVALID_HANDLE_VALUE;
}

static int write_all(const char *data, DWORD length) {
    DWORD offset = 0;
    while (offset < length) {
        DWORD written = 0;
        if (!WriteFile(pipe_handle, data + offset, length - offset, &written, NULL) || written == 0) {
            close_pipe();
            return 0;
        }
        offset += written;
    }
    return 1;
}

static const char *__cdecl send_message(void **arguments) {
    if (arguments == NULL || arguments[0] == NULL) {
        return NULL;
    }
    const char *message = (const char *)arguments[0];
    const size_t length = strlen(message);
    if (length == 0 || length > 2u * 1024u * 1024u) {
        return NULL;
    }
    AcquireSRWLockExclusive(&pipe_lock);
    if (ensure_pipe() && write_all(message, (DWORD)length)) {
        (void)write_all("\n", 1);
    }
    ReleaseSRWLockExclusive(&pipe_lock);
    return NULL;
}

__declspec(dllexport) int __cdecl BetterNCMPluginMain(const PluginAPI *api) {
    if (api != NULL && api->addNativeAPI != NULL) {
        NativeAPIType arguments[] = { NativeString };
        api->addNativeAPI(arguments, 1, "lyrics_statusbar.send", send_message);
    }
    return 0;
}
