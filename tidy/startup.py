from pathlib import Path
import sys
import subprocess

def set_login_start(enabled: bool, entry: Path):
    import winreg
    with winreg.CreateKey(winreg.HKEY_CURRENT_USER, r'Software\Microsoft\Windows\CurrentVersion\Run') as key:
        if enabled:
            if getattr(sys, 'frozen', False):
                args = [sys.executable, '--background']
            else:
                pythonw = Path(sys.executable).with_name('pythonw.exe')
                args = [str(pythonw if pythonw.exists() else sys.executable), str(entry), '--background']
            winreg.SetValueEx(key, 'DesktopTidy', 0, winreg.REG_SZ, subprocess.list2cmdline(args))
        else:
            try:
                winreg.DeleteValue(key, 'DesktopTidy')
            except FileNotFoundError:
                pass

def login_enabled():
    try:
        import winreg
        with winreg.OpenKey(winreg.HKEY_CURRENT_USER, r'Software\Microsoft\Windows\CurrentVersion\Run') as key:
            winreg.QueryValueEx(key, 'DesktopTidy')
            return True
    except (ImportError, OSError):
        return False
