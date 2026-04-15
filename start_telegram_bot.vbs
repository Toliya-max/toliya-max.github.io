Set WshShell = CreateObject("WScript.Shell")
WshShell.Run "pythonw """ & Replace(WScript.ScriptFullName, "start_telegram_bot.vbs", "telegram_bot.py") & """", 0, False
