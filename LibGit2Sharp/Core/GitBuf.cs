using System;
using System.Runtime.InteropServices;

namespace LibGit2Sharp.Core.Handles
{
    /// <summary>
    /// Blittable mirror of libgit2's <c>git_buf</c>. Passed by <c>ref</c> to native code so that
    /// native writes to <see cref="ptr"/> and <see cref="size"/> are visible to managed code.
    /// A sequential-layout class passed by value is effectively [In]-only under Native AOT:
    /// the marshaller copies it in but never copies native writes back.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct GitBufNative
    {
        public IntPtr ptr;
        public UIntPtr asize;
        public UIntPtr size;
    }

    internal class GitBuf : IDisposable
    {
        public GitBufNative Native;

        public IntPtr ptr => Native.ptr;
        public UIntPtr asize => Native.asize;
        public UIntPtr size => Native.size;

        public void Dispose()
        {
            Proxy.git_buf_dispose(this);
        }
    }
}
