using System.Security.Cryptography;
using System.Text;

namespace GcmMonitor.Core;

public static class InstallationUse
{
    // O instalador usa o mesmo identificador de perfil, em UTF-16 LE.
    public static string MutexName => "Global\\GcmMonitor.InUse." + Convert.ToHexString(SHA256.HashData(
        Encoding.Unicode.GetBytes(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData).TrimEnd(Path.DirectorySeparatorChar))));

    public static Mutex OpenMarker() => new(false, MutexName);
}
