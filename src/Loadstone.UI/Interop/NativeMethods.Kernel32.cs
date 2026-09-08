using System.Runtime.InteropServices;

namespace Loadstone.UI.Interop;

internal static partial class NativeMethods
{
    internal static partial class Kernel32
    {
        /// <summary>
        /// Called with a zero length and no buffer purely to read the status: ERROR_INSUFFICIENT_BUFFER
        /// when the process has a package, APPMODEL_ERROR_NO_PACKAGE when it does not. The name itself
        /// is never wanted, which is why the buffer is nullable and always null here.
        /// </summary>
        [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16)]
        internal static partial int GetCurrentPackageFullName(ref uint packageFullNameLength, [Out] char[]? packageFullName);
    }
}
