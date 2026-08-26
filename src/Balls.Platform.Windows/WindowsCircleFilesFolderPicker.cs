using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Balls.Platform;

namespace Balls.Platform.Windows;

internal interface IWindowsCircleFilesFolderDialog
{
    string? Show(CancellationToken cancellationToken);
}

[SupportedOSPlatform("windows")]
public sealed class WindowsCircleFilesFolderPicker : ICircleFilesFolderPicker
{
    private readonly IWindowsCircleFilesFolderDialog dialog;

    public WindowsCircleFilesFolderPicker()
        : this(new WindowsCircleFilesFolderDialog())
    {
    }

    internal WindowsCircleFilesFolderPicker(IWindowsCircleFilesFolderDialog dialog)
    {
        this.dialog = dialog;
    }

    public async ValueTask<CircleFilesFolderSelection?> SelectAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var completion = new TaskCompletionSource<string?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                completion.TrySetResult(dialog.Show(cancellationToken));
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
            Name = "Balls folder picker",
        };
        if (OperatingSystem.IsWindows())
        {
            thread.SetApartmentState(ApartmentState.STA);
        }
        thread.Start();

        var path = await completion.Task.ConfigureAwait(false);
        if (path is null)
        {
            return null;
        }

        var normalized = path.TrimEnd('\\', '/');
        var separator = Math.Max(normalized.LastIndexOf('\\'), normalized.LastIndexOf('/'));
        var displayName = separator >= 0 ? normalized[(separator + 1)..] : normalized;
        return new CircleFilesFolderSelection(
            path,
            string.IsNullOrWhiteSpace(displayName) ? path : displayName);
    }
}

[SupportedOSPlatform("windows")]
internal sealed class WindowsCircleFilesFolderDialog : IWindowsCircleFilesFolderDialog
{
    private const int CancelledHResult = unchecked((int)0x800704C7);
    private const uint PickFolders = 0x00000020;
    private const uint ForceFileSystem = 0x00000040;
    private const uint PathMustExist = 0x00000800;
    private const uint NoChangeDirectory = 0x00000008;
    private const uint FileSystemPath = 0x80058000;

    public string? Show(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IFileDialog? dialog = null;
        IShellItem? result = null;
        try
        {
            dialog = (IFileDialog)(object)new FileOpenDialog();
            dialog.SetTitle("Choose an existing folder for Circle Files");
            dialog.SetOkButtonLabel("Choose folder");
            dialog.SetOptions(PickFolders | ForceFileSystem | PathMustExist | NoChangeDirectory);
            var activeDialog = dialog;
            using var registration = cancellationToken.Register(
                () => activeDialog.Close(CancelledHResult));
            var shown = dialog.Show(IntPtr.Zero);
            if (shown == CancelledHResult)
            {
                return null;
            }
            Marshal.ThrowExceptionForHR(shown);
            dialog.GetResult(out result);
            result.GetDisplayName(FileSystemPath, out var pointer);
            try
            {
                return Marshal.PtrToStringUni(pointer)
                    ?? throw new IOException("Windows did not return the selected folder path.");
            }
            finally
            {
                Marshal.FreeCoTaskMem(pointer);
            }
        }
        finally
        {
            if (result is not null)
            {
                Marshal.FinalReleaseComObject(result);
            }
            if (dialog is not null)
            {
                Marshal.FinalReleaseComObject(dialog);
            }
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
        [PreserveSig]
        int Show(IntPtr parent);

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
