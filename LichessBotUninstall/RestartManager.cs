using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace LichessBotUninstall
{
    internal static class RestartManager
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct RM_UNIQUE_PROCESS
        {
            public uint dwProcessId;
            public System.Runtime.InteropServices.ComTypes.FILETIME ProcessStartTime;
        }

        private const int CCH_RM_MAX_APP_NAME = 255;
        private const int CCH_RM_MAX_SVC_NAME = 63;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct RM_PROCESS_INFO
        {
            public RM_UNIQUE_PROCESS Process;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = CCH_RM_MAX_APP_NAME + 1)]
            public string strAppName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = CCH_RM_MAX_SVC_NAME + 1)]
            public string strServiceShortName;
            public uint ApplicationType;
            public uint AppStatus;
            public uint TSSessionId;
            [MarshalAs(UnmanagedType.Bool)]
            public bool bRestartable;
        }

        [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
        private static extern int RmStartSession(out uint pSessionHandle, int dwSessionFlags, string strSessionKey);

        [DllImport("rstrtmgr.dll")]
        private static extern int RmEndSession(uint pSessionHandle);

        [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
        private static extern int RmRegisterResources(uint pSessionHandle,
            uint nFiles, string[] rgsFilenames,
            uint nApplications, [In] RM_UNIQUE_PROCESS[]? rgApplications,
            uint nServices, string[]? rgsServiceNames);

        [DllImport("rstrtmgr.dll")]
        private static extern int RmGetList(uint dwSessionHandle, out uint pnProcInfoNeeded,
            ref uint pnProcInfo, [In, Out] RM_PROCESS_INFO[] rgAffectedApps,
            ref uint lpdwRebootReasons);

        private const int ERROR_MORE_DATA = 234;

        public static List<(int Pid, string Name)> GetProcessesLockingFiles(IList<string> paths)
        {
            var result = new List<(int, string)>();
            if (paths.Count == 0) return result;

            string key = Guid.NewGuid().ToString();
            if (RmStartSession(out uint handle, 0, key) != 0) return result;
            try
            {
                var arr = paths.ToArray();
                if (RmRegisterResources(handle, (uint)arr.Length, arr, 0, null, 0, null) != 0)
                    return result;

                uint needed = 0, count = 0, reasons = 0;
                RM_PROCESS_INFO[] infos = Array.Empty<RM_PROCESS_INFO>();
                int rc = RmGetList(handle, out needed, ref count, infos, ref reasons);
                if (rc == ERROR_MORE_DATA)
                {
                    infos = new RM_PROCESS_INFO[needed];
                    count = needed;
                    rc = RmGetList(handle, out needed, ref count, infos, ref reasons);
                }
                if (rc != 0) return result;

                for (int i = 0; i < count; i++)
                {
                    result.Add(((int)infos[i].Process.dwProcessId, infos[i].strAppName ?? "?"));
                }
            }
            finally
            {
                RmEndSession(handle);
            }
            return result;
        }

        public static int KillLockers(string directoryPath, Action<string>? log = null)
        {
            if (!Directory.Exists(directoryPath)) return 0;

            var files = new List<string>();
            try
            {
                files.AddRange(Directory.EnumerateFiles(directoryPath, "*", SearchOption.AllDirectories));
            }
            catch { }

            if (files.Count == 0) return 0;
            if (files.Count > 800) files = files.Take(800).ToList();

            List<(int Pid, string Name)> lockers;
            try { lockers = GetProcessesLockingFiles(files); }
            catch (Exception ex)
            {
                log?.Invoke($"Restart Manager query failed: {ex.Message}");
                return 0;
            }

            int killed = 0;
            foreach (var (pid, name) in lockers)
            {
                try
                {
                    var p = Process.GetProcessById(pid);
                    log?.Invoke($"Locker: {name} (pid {pid}) — terminating");
                    p.Kill(entireProcessTree: true);
                    p.WaitForExit(3000);
                    killed++;
                }
                catch (Exception ex)
                {
                    log?.Invoke($"Could not kill {name} (pid {pid}): {ex.Message}");
                }
            }
            return killed;
        }
    }
}
