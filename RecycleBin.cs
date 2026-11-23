using System;
using System.IO;
using System.Runtime.InteropServices;

namespace PhotoDuplicateFinder
{
    internal static class RecycleBin
    {
        [Flags]
        private enum Options : uint
        {
            Silent = 0x4, NoErrorUI = 0x400, NoConnectedElements = 0x2000,
            RecycleOnDelete = 0x80000, EarlyFailure = 0x100000, AddUndoRecord = 0x20000000
        }

        // COM methods must appear in their native IFileOperation vtable order.
        [ComImport, Guid("947AAB5F-0A5C-4C13-B4D6-4BF7836FC9F8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IFileOperation
        {
            void Advise(IntPtr sink, out uint cookie);
            void Unadvise(uint cookie);
            void SetOperationFlags(Options flags);
            void SetProgressMessage([MarshalAs(UnmanagedType.LPWStr)] string message);
            void SetProgressDialog(IntPtr dialog);
            void SetProperties(IntPtr properties);
            void SetOwnerWindow(IntPtr owner);
            void ApplyPropertiesToItem(IntPtr item);
            void ApplyPropertiesToItems(IntPtr items);
            void RenameItem(IntPtr item, IntPtr name, IntPtr sink);
            void RenameItems(IntPtr items, IntPtr name);
            void MoveItem(IntPtr item, IntPtr folder, IntPtr name, IntPtr sink);
            void MoveItems(IntPtr items, IntPtr folder);
            void CopyItem(IntPtr item, IntPtr folder, IntPtr name, IntPtr sink);
            void CopyItems(IntPtr items, IntPtr folder);
            void DeleteItem(IntPtr item, IntPtr sink);
            void DeleteItems(IntPtr items);
            void NewItem(IntPtr folder, uint attributes, IntPtr name, IntPtr template, IntPtr sink);
            void PerformOperations();
            void GetAnyOperationsAborted([MarshalAs(UnmanagedType.Bool)] out bool aborted);
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
        private static extern void SHCreateItemFromParsingName(string path, IntPtr context, ref Guid iid, out IntPtr item);

        public static void Move(string path)
        {
            var clsid = new Guid("3AD05575-8857-4850-9277-11B85BDB8E09");
            var shellItem = new Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE");
            var operation = (IFileOperation)Activator.CreateInstance(Type.GetTypeFromCLSID(clsid, true));
            IntPtr item = IntPtr.Zero;
            try
            {
                SHCreateItemFromParsingName(path, IntPtr.Zero, ref shellItem, out item);
                operation.SetOperationFlags(Options.RecycleOnDelete | Options.AddUndoRecord |
                    Options.NoErrorUI | Options.NoConnectedElements | Options.Silent | Options.EarlyFailure);
                operation.DeleteItem(item, IntPtr.Zero);
                operation.PerformOperations();
                bool aborted;
                operation.GetAnyOperationsAborted(out aborted);
                if (aborted || File.Exists(path))
                    throw new IOException("Windows could not move this file to the Recycle Bin.");
            }
            finally
            {
                if (item != IntPtr.Zero) Marshal.Release(item);
                Marshal.FinalReleaseComObject(operation);
            }
        }
    }
}
