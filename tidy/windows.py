from __future__ import annotations
import ctypes as C
from ctypes import wintypes as W
import os
from pathlib import Path
import uuid

class DesktopError(RuntimeError):
    pass

class POINT(C.Structure):
    _fields_ = [('x', W.LONG), ('y', W.LONG)]

class LVITEM(C.Structure):
    _fields_ = [
        ('mask', W.UINT), ('iItem', C.c_int), ('iSubItem', C.c_int),
        ('state', W.UINT), ('stateMask', W.UINT), ('pszText', C.c_void_p),
        ('cchTextMax', C.c_int), ('iImage', C.c_int), ('lParam', C.c_ssize_t),
        ('iIndent', C.c_int), ('iGroupId', C.c_int), ('cColumns', W.UINT),
        ('puColumns', C.c_void_p), ('piColFmt', C.c_void_p), ('iGroup', C.c_int),
    ]

class GUID(C.Structure):
    _fields_ = [('d1', W.DWORD), ('d2', W.WORD), ('d3', W.WORD), ('d4', C.c_ubyte * 8)]

def desktop_paths():
    if os.name != 'nt':
        raise DesktopError('此功能只支持 Windows 10/11')
    shell = C.WinDLL('shell32', use_last_error=True)
    ole = C.WinDLL('ole32', use_last_error=True)
    shell.SHGetKnownFolderPath.argtypes = [C.POINTER(GUID), W.DWORD, W.HANDLE, C.POINTER(C.c_void_p)]
    shell.SHGetKnownFolderPath.restype = C.c_long
    ole.CoTaskMemFree.argtypes = [C.c_void_p]
    paths = []
    for key in ['B4BFCC3A-DB2C-424C-B029-7FE99A87C641', 'C4AA340D-F20F-4863-AFEF-F87EF2E6BA25']:
        guid = GUID.from_buffer_copy(uuid.UUID(key).bytes_le)
        ptr = C.c_void_p()
        hr = shell.SHGetKnownFolderPath(C.byref(guid), 0, None, C.byref(ptr))
        if hr < 0:
            raise DesktopError('无法取得桌面路径：HRESULT ' + hex(hr & 0xffffffff))
        try:
            path = Path(C.wstring_at(ptr))
            if path not in paths:
                paths.append(path)
        finally:
            ole.CoTaskMemFree(ptr)
    return paths

def display_name(path: Path):
    class SHFILEINFO(C.Structure):
        _fields_ = [('hIcon', W.HANDLE), ('iIcon', C.c_int), ('attrs', W.DWORD),
                    ('display', W.WCHAR * 260), ('type', W.WCHAR * 80)]
    shell = C.WinDLL('shell32', use_last_error=True)
    shell.SHGetFileInfoW.argtypes = [W.LPCWSTR, W.DWORD, C.POINTER(SHFILEINFO), W.UINT, W.UINT]
    shell.SHGetFileInfoW.restype = C.c_size_t
    info = SHFILEINFO()
    if not shell.SHGetFileInfoW(str(path), 0, C.byref(info), C.sizeof(info), 0x200):
        raise OSError('Shell 无法解析显示名称')
    return info.display

class Desktop:
    # Pointer messages use memory allocated inside Explorer, not Python.
    def __init__(self):
        if os.name != 'nt':
            raise DesktopError('仅支持 Windows')
        self.user = C.WinDLL('user32', use_last_error=True)
        self.kernel = C.WinDLL('kernel32', use_last_error=True)
        self.process = None
        self.remote = None
        self._bind()
        self.hwnd = self._find()
        if not self.hwnd:
            raise DesktopError('未找到 Explorer 桌面；请显示桌面图标或重启资源管理器。可改用分类快捷方式')
        pid = W.DWORD()
        self.user.GetWindowThreadProcessId(self.hwnd, C.byref(pid))
        self.process = self.kernel.OpenProcess(0x1000 | 0x8 | 0x10 | 0x20, False, pid.value)
        if not self.process:
            raise DesktopError('无法访问 Explorer；请以普通用户运行。可改用分类快捷方式')
        try:
            a, b = W.BOOL(), W.BOOL()
            if not self.kernel.IsWow64Process(self.process, C.byref(a)) or not self.kernel.IsWow64Process(self.kernel.GetCurrentProcess(), C.byref(b)):
                raise DesktopError('无法验证 Explorer 进程位数')
            if a.value != b.value:
                raise DesktopError('Python 与 Explorer 位数不匹配；请使用 64 位 Python')
            self.remote = self.kernel.VirtualAllocEx(self.process, None, 8192, 0x3000, 0x04)
            if not self.remote:
                raise DesktopError('无法分配 Explorer 消息缓冲区')
        except Exception:
            self.close()
            raise

    def _bind(self):
        u, k = self.user, self.kernel
        self.callback_type = C.WINFUNCTYPE(W.BOOL, W.HWND, C.c_ssize_t)
        u.EnumWindows.argtypes = [self.callback_type, C.c_ssize_t]
        u.EnumChildWindows.argtypes = [W.HWND, self.callback_type, C.c_ssize_t]
        u.GetClassNameW.argtypes = [W.HWND, W.LPWSTR, C.c_int]
        u.GetParent.argtypes = [W.HWND]
        u.GetParent.restype = W.HWND
        u.GetWindowThreadProcessId.argtypes = [W.HWND, C.POINTER(W.DWORD)]
        u.GetClientRect.argtypes = [W.HWND, C.POINTER(W.RECT)]
        u.GetWindowLongW.argtypes = [W.HWND, C.c_int]
        u.GetWindowLongW.restype = W.LONG
        u.SendMessageTimeoutW.argtypes = [W.HWND, W.UINT, C.c_size_t, C.c_ssize_t, W.UINT, W.UINT, C.POINTER(C.c_size_t)]
        u.SendMessageTimeoutW.restype = C.c_size_t
        k.OpenProcess.argtypes = [W.DWORD, W.BOOL, W.DWORD]
        k.OpenProcess.restype = W.HANDLE
        k.GetCurrentProcess.restype = W.HANDLE
        k.IsWow64Process.argtypes = [W.HANDLE, C.POINTER(W.BOOL)]
        k.VirtualAllocEx.argtypes = [W.HANDLE, C.c_void_p, C.c_size_t, W.DWORD, W.DWORD]
        k.VirtualAllocEx.restype = C.c_void_p
        k.VirtualFreeEx.argtypes = [W.HANDLE, C.c_void_p, C.c_size_t, W.DWORD]
        k.ReadProcessMemory.argtypes = [W.HANDLE, C.c_void_p, C.c_void_p, C.c_size_t, C.POINTER(C.c_size_t)]
        k.WriteProcessMemory.argtypes = k.ReadProcessMemory.argtypes
        k.CloseHandle.argtypes = [W.HANDLE]

    def _class(self, hwnd):
        text = C.create_unicode_buffer(256)
        self.user.GetClassNameW(hwnd, text, 256)
        return text.value

    def _find(self):
        found = []
        @self.callback_type
        def child(hwnd, _):
            if self._class(hwnd) == 'SysListView32' and self._class(self.user.GetParent(hwnd)) == 'SHELLDLL_DefView':
                found.append(hwnd)
            return True
        @self.callback_type
        def top(hwnd, _):
            if self._class(hwnd) in ('Progman', 'WorkerW'):
                self.user.EnumChildWindows(hwnd, child, 0)
            return True
        self.user.EnumWindows(top, 0)
        return found[0] if found else None

    def send(self, message, index=0, pointer=0):
        result = C.c_size_t()
        if not self.user.SendMessageTimeoutW(self.hwnd, message, index, pointer, 0x2, 2000, C.byref(result)):
            raise DesktopError('Explorer 无响应或拒绝消息，整理已停止')
        return result.value

    def write(self, data, offset=0):
        done = C.c_size_t()
        if not self.kernel.WriteProcessMemory(self.process, self.remote + offset, C.byref(data), C.sizeof(data), C.byref(done)) or done.value != C.sizeof(data):
            raise DesktopError('无法写入 Explorer 消息缓冲区')

    def read(self, data, offset=0):
        done = C.c_size_t()
        if not self.kernel.ReadProcessMemory(self.process, self.remote + offset, C.byref(data), C.sizeof(data), C.byref(done)) or done.value != C.sizeof(data):
            raise DesktopError('无法读取 Explorer 消息缓冲区')
        return data

    def label(self, index):
        text = C.create_unicode_buffer(1024)
        self.write(text, 512)
        item = LVITEM(mask=1, iItem=index, pszText=self.remote + 512, cchTextMax=1024)
        self.write(item)
        self.send(0x1073, index, self.remote)
        return self.read(text, 512).value

    def position(self, index):
        if not self.send(0x1010, index, self.remote):
            raise DesktopError('图标坐标读取失败')
        point = self.read(POINT())
        return [point.x, point.y]

    def snapshot(self):
        count = self.send(0x1004)
        if count > 3000:
            raise DesktopError('图标数量超过安全上限 3000')
        icons = [{'index': i, 'name': self.label(i), 'xy': self.position(i)} for i in range(count)]
        if self.send(0x1004) != count:
            raise DesktopError('桌面内容正在变化，请稍后重试')
        rect = W.RECT()
        if not self.user.GetClientRect(self.hwnd, C.byref(rect)):
            raise DesktopError('桌面区域读取失败')
        spacing = self.send(0x1033, 0)
        sx, sy = spacing & 0xffff, (spacing >> 16) & 0xffff
        return {'kind': 'layout', 'icons': icons, 'size': [rect.right, rect.bottom], 'spacing': [max(80, sx), max(88, sy)]}

    def assert_manual(self):
        if self.user.GetWindowLongW(self.hwnd, -16) & 0x100:
            raise DesktopError('请在桌面右键 → 查看，取消「自动排列图标」；本程序不会擅自修改系统设置')

    def move(self, index, name, xy):
        if self.label(index) != name:
            raise DesktopError('图标身份发生变化，整理已停止；请撤销后重试')
        point = POINT(*xy)
        self.write(point)
        self.send(0x1031, index, self.remote)
        if self.position(index) != list(xy):
            raise DesktopError('Windows 未接受图标位置；请关闭「对齐到网格」后重试，或使用分类快捷方式')

    def close(self):
        if self.remote and self.process:
            self.kernel.VirtualFreeEx(self.process, self.remote, 0, 0x8000)
            self.remote = None
        if self.process:
            self.kernel.CloseHandle(self.process)
            self.process = None

    def __enter__(self):
        return self

    def __exit__(self, *_):
        self.close()
