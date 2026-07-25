using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Hookline.NowPlaying;

internal interface ISourceProcessResolver
{
    MediaSourceIdentity Resolve(string applicationId);
}

internal sealed class EmptySourceProcessResolver : ISourceProcessResolver
{
    public MediaSourceIdentity Resolve(string applicationId) =>
        new() { ApplicationId = applicationId };
}

internal sealed class SpotifySourceProcessResolver : ISourceProcessResolver
{
    public MediaSourceIdentity Resolve(string applicationId) =>
        new()
        {
            ApplicationId = applicationId,
            ProcessId = FindSpotifyProcessTreeRoot(),
        };

    private static int? FindSpotifyProcessTreeRoot()
    {
        Process[] processes;
        try
        {
            processes = Process.GetProcessesByName("Spotify");
        }
        catch
        {
            return null;
        }

        try
        {
            if (processes.Length == 0)
            {
                return null;
            }

            var spotifyIds = processes
                .Select(process => process.Id)
                .ToHashSet();
            var parentIds = ProcessTreeSnapshot.GetParentProcessIds();
            var roots = processes
                .Where(
                    process =>
                        !parentIds.TryGetValue(
                            process.Id,
                            out var parentId
                        )
                        || !spotifyIds.Contains(parentId)
                )
                .ToArray();

            var candidates = roots.Length > 0 ? roots : processes;
            return candidates
                .OrderBy(GetStartTimeOrMaxValue)
                .ThenBy(process => process.Id)
                .First()
                .Id;
        }
        finally
        {
            foreach (var process in processes)
            {
                process.Dispose();
            }
        }
    }

    private static DateTime GetStartTimeOrMaxValue(Process process)
    {
        try
        {
            return process.StartTime;
        }
        catch
        {
            return DateTime.MaxValue;
        }
    }

    private static class ProcessTreeSnapshot
    {
        private const uint SnapshotProcesses = 0x00000002;
        private static readonly IntPtr InvalidHandleValue = new(-1);

        public static Dictionary<int, int> GetParentProcessIds()
        {
            var result = new Dictionary<int, int>();
            var snapshot = CreateToolhelp32Snapshot(SnapshotProcesses, 0);
            if (snapshot == InvalidHandleValue)
            {
                return result;
            }

            try
            {
                var entry = new ProcessEntry32
                {
                    Size = (uint)Marshal.SizeOf<ProcessEntry32>(),
                };
                if (!Process32First(snapshot, ref entry))
                {
                    return result;
                }

                do
                {
                    result[(int)entry.ProcessId] =
                        (int)entry.ParentProcessId;
                } while (Process32Next(snapshot, ref entry));
            }
            finally
            {
                CloseHandle(snapshot);
            }

            return result;
        }

        [StructLayout(
            LayoutKind.Sequential,
            CharSet = CharSet.Unicode
        )]
        private struct ProcessEntry32
        {
            public uint Size;
            public uint Usage;
            public uint ProcessId;
            public IntPtr DefaultHeapId;
            public uint ModuleId;
            public uint ThreadCount;
            public uint ParentProcessId;
            public int BasePriority;
            public uint Flags;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            public string FileName;
        }

        [DllImport(
            "kernel32.dll",
            SetLastError = true
        )]
        private static extern IntPtr CreateToolhelp32Snapshot(
            uint flags,
            uint processId
        );

        [DllImport(
            "kernel32.dll",
            CharSet = CharSet.Unicode,
            SetLastError = true
        )]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool Process32First(
            IntPtr snapshot,
            ref ProcessEntry32 entry
        );

        [DllImport(
            "kernel32.dll",
            CharSet = CharSet.Unicode,
            SetLastError = true
        )]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool Process32Next(
            IntPtr snapshot,
            ref ProcessEntry32 entry
        );

        [DllImport("kernel32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseHandle(IntPtr handle);
    }
}
