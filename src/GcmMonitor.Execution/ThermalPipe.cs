using System.IO.Pipes;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;

namespace GcmMonitor.Execution;

/// <summary>Canal térmico local entre o mesmo usuário em níveis diferentes de elevação.</summary>
[SupportedOSPlatform("windows")]
public static class ThermalPipe
{
    public static NamedPipeServerStream CreateServer(string name)
    {
        using var identity = WindowsIdentity.GetCurrent();
        var user = identity.User ?? throw new UnauthorizedAccessException("Identidade da conta não disponível.");
        var security = new PipeSecurity();
        security.SetOwner(user);
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.AddAccessRule(new PipeAccessRule(user, PipeAccessRights.FullControl, AccessControlType.Allow));
        return NamedPipeServerStreamAcl.Create(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.FirstPipeInstance, 4096, 4096, security, HandleInheritability.None);
    }

    // CurrentUserOnly no cliente também exige o mesmo nível de elevação no Windows.
    // A ACL usa SID de conta e Identification impede o servidor de assumir o token elevado.
    public static NamedPipeClientStream CreateClient(string name) => new(".", name, PipeDirection.InOut,
        PipeOptions.Asynchronous, TokenImpersonationLevel.Identification, HandleInheritability.None);

    public static void ValidateServer(NamedPipeClientStream pipe, int pid, DateTimeOffset startedAt)
    {
        LocalPipePeer.ValidateServer(pipe.SafePipeHandle, pid, startedAt);
        using var identity = WindowsIdentity.GetCurrent();
        if (identity.User is null || !identity.User.Equals(pipe.GetAccessControl().GetOwner(typeof(SecurityIdentifier))))
            throw new InvalidDataException("Canal térmico pertence a outra conta.");
    }
}
