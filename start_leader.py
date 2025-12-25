import os
import subprocess
import platform

BASE_DIR = os.path.dirname(os.path.abspath(__file__))

system = platform.system()

if system == "Windows":
    subprocess.Popen(
        ["cmd", "/k", "dotnet", "run"],
        cwd=BASE_DIR
    )

elif system == "Darwin":  
    subprocess.Popen([
        "osascript",
        "-e",
        f'''
        tell application "Terminal"
            do script "cd {BASE_DIR} && dotnet run"
            activate
        end tell
        '''
    ])

elif system == "Linux":
    subprocess.Popen([
        "gnome-terminal",
        "--",
        "bash",
        "-c",
        f"cd {BASE_DIR} && dotnet run; exec bash"
    ])

else:
    raise Exception("Desteklenmeyen işletim sistemi")

print(" Sunucu ayrı terminalde dotnet run ile başlatıldı.")
