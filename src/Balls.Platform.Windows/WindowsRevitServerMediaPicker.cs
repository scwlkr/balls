using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Balls.Platform;

namespace Balls.Platform.Windows;

[SupportedOSPlatform("windows")]
public sealed class WindowsRevitServerMediaPicker : IRevitServerMediaPicker
{
    public async ValueTask<RevitServerMediaSelection?> SelectAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var completion = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                completion.TrySetResult(Show(cancellationToken));
            }
            catch (OperationCanceledException exception)
            {
                completion.TrySetCanceled(exception.CancellationToken);
            }
            catch (Exception exception)
            {
                completion.TrySetException(exception);
            }
        })
        {
            IsBackground = true,
            Name = "Balls Revit Server media picker",
        };
        if (OperatingSystem.IsWindows())
        {
            thread.SetApartmentState(ApartmentState.STA);
        }
        thread.Start();
        var path = await completion.Task.ConfigureAwait(false);
        return path is null ? null : new RevitServerMediaSelection(path, Path.GetFileName(path));
    }

    private static string? Show(CancellationToken cancellationToken)
    {
        const int cancelled = unchecked((int)0x800704C7);
        IFileDialog? dialog = null;
        IShellItem? result = null;
        try
        {
            dialog = (IFileDialog)(object)new FileOpenDialog();
            dialog.SetTitle("Choose official Autodesk Revit Server 2027 media");
            dialog.SetOkButtonLabel("Choose installer");
            dialog.SetOptions(0x00000040 | 0x00000800 | 0x00001000 | 0x00000008);
            var active = dialog;
            using var registration = cancellationToken.Register(() => active.Close(cancelled));
            var shown = dialog.Show(IntPtr.Zero);
            if (shown == cancelled)
            {
                return null;
            }
            Marshal.ThrowExceptionForHR(shown);
            dialog.GetResult(out result);
            result.GetDisplayName(0x80058000, out var pointer);
            try
            {
                return Marshal.PtrToStringUni(pointer)
                    ?? throw new IOException("Windows did not return the selected installer path.");
            }
            finally
            {
                Marshal.FreeCoTaskMem(pointer);
            }
        }
        finally
        {
            if (result is not null) Marshal.FinalReleaseComObject(result);
            if (dialog is not null) Marshal.FinalReleaseComObject(dialog);
        }
    }

    [ComImport]
    [Guid("DC1C5A9C-E88A-4DDE-A5A1-60F82A20AEF7")]
    private sealed class FileOpenDialog;

    [ComImport]
    [Guid("42F85136-DB7E-439C-85F1-E4075D135FC8")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IFileDialog
    {
        [PreserveSig] int Show(IntPtr parent);
        void SetFileTypes(uint count, IntPtr filters);
        void SetFileTypeIndex(uint index);
        void GetFileTypeIndex(out uint index);
        void Advise(IntPtr events, out uint cookie);
        void Unadvise(uint cookie);
        void SetOptions(uint options);
        void GetOptions(out uint options);
        void SetDefaultFolder(IShellItem item);
        void SetFolder(IShellItem item);
        void GetFolder(out IShellItem item);
        void GetCurrentSelection(out IShellItem item);
        void SetFileName([MarshalAs(UnmanagedType.LPWStr)] string name);
        void GetFileName([MarshalAs(UnmanagedType.LPWStr)] out string name);
        void SetTitle([MarshalAs(UnmanagedType.LPWStr)] string title);
        void SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)] string text);
        void SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)] string label);
        void GetResult(out IShellItem item);
        void AddPlace(IShellItem item, uint placement);
        void SetDefaultExtension([MarshalAs(UnmanagedType.LPWStr)] string extension);
        void Close(int result);
        void SetClientGuid(in Guid guid);
        void ClearClientData();
        void SetFilter(IntPtr filter);
    }

    [ComImport]
    [Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItem
    {
        void BindToHandler(IntPtr bindingContext, in Guid handlerId, in Guid interfaceId, out IntPtr result);
        void GetParent(out IShellItem parent);
        void GetDisplayName(uint displayName, out IntPtr name);
        void GetAttributes(uint mask, out uint attributes);
        void Compare(IShellItem item, uint hint, out int order);
    }
}
