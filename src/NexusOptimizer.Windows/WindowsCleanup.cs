using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using NexusOptimizer.Core;

namespace NexusOptimizer.Windows;

public sealed class WindowsFileDeletion : IFileDeletion
{
    public void Delete(CleanupFile file, string allowedRoot)
    {
        if (!PathSafety.IsWithin(file.Path, allowedRoot)) throw new IOException("Caminho fora da área permitida.");
        List<SafeFileHandle> anchors = [];
        try
        {
            // Hold every ancestor without FILE_SHARE_DELETE: junction/rename swaps cannot redirect deletion.
            string parent = Path.GetDirectoryName(Path.GetFullPath(file.Path))!;
            var parents = new Stack<string>();
            for (string? p = parent; p is not null; p = Path.GetDirectoryName(p)) parents.Push(p);
            foreach (string path in parents)
            {
                var anchor = Native.CreateFile(path, 0x80, 3, IntPtr.Zero, 3, 0x02200000, IntPtr.Zero);
                anchors.Add(anchor);
                Check(anchor);
                if (!Native.GetFileInformationByHandle(anchor, out var information)) Fail();
                if ((information.Attributes & (uint)FileAttributes.ReparsePoint) != 0) throw new IOException("Junção ou link recusado.");
            }
            using var handle = Native.CreateFile(file.Path, 0x10080, 1, IntPtr.Zero, 3, 0x00200000, IntPtr.Zero);
            Check(handle);
            if (!Native.GetFileInformationByHandle(handle, out var info)) Fail();
            long size = ((long)info.SizeHigh << 32) | info.SizeLow;
            if ((info.Attributes & (uint)(FileAttributes.ReparsePoint | FileAttributes.System | FileAttributes.ReadOnly | FileAttributes.Directory)) != 0 ||
                info.Links > 1 || size != file.Length || DateTime.FromFileTimeUtc((long)info.Write.Value) != file.LastWriteUtc)
                throw new IOException("Arquivo protegido, compartilhado ou alterado após a análise.");
            int disposition = 1;
            if (!Native.SetFileInformationByHandle(handle, 4, ref disposition, 4)) Fail();
        }
        finally { foreach (var anchor in anchors) anchor.Dispose(); }
    }
    private static void Check(SafeFileHandle handle) { if (handle.IsInvalid) Fail(); }
    private static void Fail() => throw new IOException(new Win32Exception(Marshal.GetLastWin32Error()).Message);
}

public static class WindowsCleanup
{
    public static CleanupService Create() => new([
        new("user-temp", "Temporários do usuário", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Temp"), "*", "Somente arquivos sem alteração há mais de 24 horas."),
        new("windows-temp", "Temporários do Windows", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Temp"), "*", "Arquivos protegidos ou sem permissão serão ignorados. Sem elevação automática."),
        new("thumbnails", "Miniaturas do Explorer", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "Windows", "Explorer"), "thumbcache_*.db", "As miniaturas serão recriadas. Arquivos em uso serão ignorados.")
    ], new WindowsFileDeletion());

    public static (long Items, long Bytes) RecycleSize()
    {
        var info = new Native.RecycleInfo { Size = (uint)Marshal.SizeOf<Native.RecycleInfo>() };
        Marshal.ThrowExceptionForHR(Native.SHQueryRecycleBin(null, ref info)); return (info.Items, info.Bytes);
    }
    public static void EmptyRecycleBin()
    {
        // Keep the shell's own confirmation in addition to the application's explicit consent.
        Marshal.ThrowExceptionForHR(Native.SHEmptyRecycleBin(IntPtr.Zero, null, 0));
    }
}
