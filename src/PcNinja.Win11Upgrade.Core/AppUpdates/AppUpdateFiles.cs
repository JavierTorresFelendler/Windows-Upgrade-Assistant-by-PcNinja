using System.ComponentModel;
using System.Diagnostics;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Win32.SafeHandles;

namespace PcNinja.Win11Upgrade.Core;

public sealed record AppExecutableInfo(string Version, string Sha256, long SizeBytes, string? SignerThumbprint);

/// <summary>
/// Product-pinned local update staging. Keep this object alive through replacement:
/// its non-delete-sharing directory handles prevent ancestor rename/junction swaps.
/// Existing untrusted folders are rejected, never taken over or silently hardened.
/// </summary>
public sealed class AppUpdateWorkspace : IDisposable
{
    internal const string PublicDirectorySddl = "O:BAG:BAD:P(A;OICI;FA;;;SY)(A;OICI;FA;;;BA)(A;OICI;0x1200a9;;;BU)";
    internal const string PrivateDirectorySddl = "O:BAG:BAD:P(A;OICI;FA;;;SY)(A;OICI;FA;;;BA)";
    private const string PublicFileSddl = "O:BAG:BAD:P(A;;FA;;;SY)(A;;FA;;;BA)(A;;0x1200a9;;;BU)";
    private const string PrivateFileSddl = "O:BAG:BAD:P(A;;FA;;;SY)(A;;FA;;;BA)";
    private const int MaximumRequestBytes = 16 * 1024;
    private const long SpaceMarginBytes = 64L * 1024 * 1024;
    private const uint ReadControl = 0x00020000, ReadAttributes = 0x80;
    private const uint GenericRead = 0x80000000, GenericWrite = 0x40000000, DeleteAccess = 0x00010000;
    private const uint OpenReparsePoint = 0x00200000, BackupSemantics = 0x02000000;
    private const uint OwnerAndDacl = 5;
    private static readonly JsonSerializerOptions RequestJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        MaxDepth = 16,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };
    private readonly DirectoryAnchors _anchors;
    private bool _cleanupAllowed;
    private bool _disposed;

    public const string StateDirectoryName = ".pcninja-updates";
    public string SourceDirectory { get; }
    public string StateDirectory => Path.Combine(SourceDirectory, StateDirectoryName);
    public string DirectoryPath { get; }
    public string CandidatePath => Path.Combine(DirectoryPath, "candidate.exe");
    public string HelperPath => Path.Combine(DirectoryPath, "UpdateHelper.exe");
    public string RequestPath => Path.Combine(DirectoryPath, "request.json");

    private AppUpdateWorkspace(string sourceDirectory, string operationId, DirectoryAnchors anchors, bool cleanupAllowed)
    {
        SourceDirectory = sourceDirectory;
        DirectoryPath = Path.Combine(StateDirectory, operationId);
        _anchors = anchors;
        _cleanupAllowed = cleanupAllowed;
    }

    public static AppUpdateWorkspace Create(string sourceDirectory, string operationId, long candidateBytes, long helperBytes) =>
        OpenCore(operationId, candidateBytes, helperBytes, create: true, sourceDirectory);

    public static AppUpdateWorkspace Open(string sourceDirectory, string operationId) =>
        OpenCore(operationId, 0, 0, create: false, sourceDirectory);

    // Test-only API: one explicitly named random child of the current temp folder.
    // Never exposed by application arguments or by the production Create/Open path.
    internal static AppUpdateWorkspace CreateFixture(string parent, string operationId, long candidateBytes, long helperBytes) =>
        OpenCore(operationId, candidateBytes, helperBytes, true, ValidateFixtureParent(parent));
    internal static AppUpdateWorkspace OpenFixture(string parent, string operationId) =>
        OpenCore(operationId, 0, 0, false, ValidateFixtureParent(parent));

    private static AppUpdateWorkspace OpenCore(string operationId, long candidateBytes, long helperBytes, bool create, string sourceDirectory)
    {
        operationId = NormalizeOperationId(operationId);
        var required = create ? RequiredFreeBytes(candidateBytes, helperBytes) : 0;
        var parent = NormalizeLocalPath(sourceDirectory);
        var anchors = AnchorLocalChain(parent);
        try
        {
            if (create && new DriveInfo(Path.GetPathRoot(parent)!).AvailableFreeSpace < required)
                throw new IOException("There is not enough free space for the application candidate, helper and update safety margin. Windows upgrade workspace is checked separately.");
            var staging = Path.Combine(parent, StateDirectoryName);
            anchors.Add(OpenControlledDirectory(staging, true, create));
            if (create) File.SetAttributes(staging, File.GetAttributes(staging) | FileAttributes.Hidden);
            var operation = Path.Combine(staging, operationId);
            anchors.Add(OpenControlledDirectory(operation, true, create, requireNew: create));
            return new AppUpdateWorkspace(parent, operationId, anchors, cleanupAllowed: create);
        }
        catch { anchors.Dispose(); throw; }
    }

    public void WriteRequest<T>(T request)
    {
        EnsureAlive();
        ArgumentNullException.ThrowIfNull(request);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(request, RequestJson);
        if (bytes.Length is <= 0 or > MaximumRequestBytes)
            throw new InvalidDataException("The application update request exceeds its size limit.");
        using var security = new NativeSecurity(PrivateFileSddl);
        var attributes = security.Attributes;
        using var handle = CreateFileWithSecurity(RequestPath, GenericWrite | ReadControl | ReadAttributes, 0,
            ref attributes, 1, OpenReparsePoint, IntPtr.Zero); // CREATE_NEW, never overwrite a handoff.
        if (handle.IsInvalid) throw NativeFailure("Could not create the private update request.");
        VerifyRegularFile(handle, RequestPath);
        ValidateControlledDescriptor(ReadDescriptor(handle), privateScope: true, requireProtected: true);
        using var stream = new FileStream(handle, FileAccess.Write);
        stream.Write(bytes);
        stream.Flush(flushToDisk: true);
    }

    public T ReadRequest<T>()
    {
        EnsureAlive();
        using var handle = OpenRegularFile(RequestPath, GenericRead | ReadControl | ReadAttributes, 1);
        ValidateControlledDescriptor(ReadDescriptor(handle), privateScope: true, requireProtected: false);
        using var stream = new FileStream(handle, FileAccess.Read);
        if (stream.Length is <= 0 or > MaximumRequestBytes)
            throw new InvalidDataException("The private update request has an invalid size.");
        var bytes = new byte[(int)stream.Length];
        stream.ReadExactly(bytes);
        return ParseRequest<T>(bytes);
    }

    internal static T ParseRequest<T>(byte[] bytes)
    {
        if (bytes.Length is <= 0 or > MaximumRequestBytes)
            throw new InvalidDataException("The application update request exceeds its size limit.");
        using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 16 });
        RejectDuplicateProperties(document.RootElement);
        return document.RootElement.Deserialize<T>(RequestJson)
            ?? throw new InvalidDataException("The application update request is empty.");
    }

    /// <summary>Call before starting the helper. An opened helper workspace can never clean staging.</summary>
    public void MarkHandoffStarted() { EnsureAlive(); _cleanupAllowed = false; }

    /// <summary>Only before handoff, with no backup and no mapped/running helper; no recursive deletion.</summary>
    public void CleanupUncommitted()
    {
        EnsureAlive();
        if (!_cleanupAllowed)
            throw new InvalidOperationException("Update recovery or helper handoff is present; staging is retained.");
        var files = new List<SafeFileHandle>();
        try
        {
            // Acquire every file exclusively before marking any for deletion. A running
            // image cannot grant GENERIC_WRITE; do not partially clean an active helper.
            foreach (var path in new[] { HelperPath, CandidatePath, RequestPath })
            {
                if (!ExistsExactly(path)) continue;
                var handle = OpenRegularFile(path, GenericRead | GenericWrite | DeleteAccess | ReadControl | ReadAttributes, 0);
                files.Add(handle);
                ValidateControlledDescriptor(ReadDescriptor(handle), privateScope: true, requireProtected: false);
            }
            foreach (var file in files)
            {
                var disposition = new FileDisposition { DeleteFile = true };
                if (!SetFileInformationByHandle(file, 4, ref disposition, (uint)Marshal.SizeOf<FileDisposition>()))
                    throw NativeFailure("Could not remove an exact uncommitted staging file; remaining files were retained.");
            }
        }
        finally { foreach (var file in files) file.Dispose(); }
    }

    /// <summary>Final versioned files are readable; staging and request remain private.</summary>
    private static void SetFileAcl(string path, bool publicRead)
    {
        using var file = OpenRegularFile(path, ReadControl | ReadAttributes | 0x00040000 | 0x00080000, 1);
        using var security = new NativeSecurity(publicRead ? PublicFileSddl : PrivateFileSddl);
        if (!GetSecurityDescriptorOwner(security.Pointer, out var owner, out _) ||
            !GetSecurityDescriptorDacl(security.Pointer, out var present, out var dacl, out _) || !present)
            throw NativeFailure("Could not prepare the application file ACL.");
        var result = SetSecurityInfo(file, 1, OwnerAndDacl | 0x80000000, owner, IntPtr.Zero, dacl, IntPtr.Zero);
        if (result != 0) throw NativeFailure("Could not secure the installed application file.", (int)result);
        ValidateControlledDescriptor(ReadDescriptor(file), privateScope: !publicRead, requireProtected: true);
    }

    public string TargetPath(AppUpdateManifest release)
    {
        EnsureAlive();
        AppUpdateClient.ValidateManifest(release);
        return Path.Combine(SourceDirectory, release.AssetName);
    }

    public void CopyExistingCandidate(AppUpdateManifest release, string? signer)
    {
        using var guard = HoldVerifiedCandidate(TargetPath(release), release, signer);
        File.Copy(TargetPath(release), CandidatePath, overwrite: false);
        SetFileAcl(CandidatePath, publicRead: false); // CopyFile may preserve a user-writable source ACL.
    }

    public void PrepareHelper(AppUpdateManifest release, string? signer)
    {
        EnsureAlive();
        SetFileAcl(CandidatePath, publicRead: false);
        VerifyCandidate(CandidatePath, release, signer);
        File.Copy(CandidatePath, HelperPath, overwrite: false);
        SetFileAcl(HelperPath, publicRead: false);
        VerifyCandidate(HelperPath, release, signer);
    }

    /// <summary>Never overwrite. Keep exact bytes and ancestors locked through CreateProcess.</summary>
    public IDisposable PublishAndHold(AppUpdateManifest release, string? signer)
    {
        EnsureAlive();
        var target = TargetPath(release);
        if (!ExistsExactly(target))
        {
            VerifyCandidate(CandidatePath, release, signer);
            SetFileAcl(CandidatePath, publicRead: true);
            try { File.Move(CandidatePath, target, overwrite: false); }
            catch (IOException) when (ExistsExactly(target)) { /* Validate the exact raced/existing file; never overwrite it. */ }
        }
        return HoldVerifiedCandidate(target, release, signer);
    }

    public static IDisposable HoldVerifiedCandidate(string path, AppUpdateManifest release, string? signer) =>
        HoldExecutable(path, info => VerifyExecutableInfo(info, release, signer));

    public static IDisposable HoldOriginal(string path, string expectedHash) => HoldExecutable(path, info =>
    {
        if (!info.Sha256.Equals(expectedHash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The original executable changed; the new tool was not launched.");
    });

    private static IDisposable HoldExecutable(string path, Action<AppExecutableInfo> verify)
    {
        var full = NormalizeLocalPath(path);
        var anchors = AnchorLocalChain(Path.GetDirectoryName(full)!);
        SafeFileHandle? file = null;
        try
        {
            file = OpenRegularFile(full, GenericRead | ReadControl | ReadAttributes, 1);
            verify(InspectExecutable(full));
            return new ExecutableGuard(anchors, file);
        }
        catch { file?.Dispose(); anchors.Dispose(); throw; }
    }

    public void WriteResult<T>(T result)
    {
        EnsureAlive();
        var temporary = Path.Combine(DirectoryPath, "result.tmp");
        using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            JsonSerializer.Serialize(file, result, RequestJson);
        SetFileAcl(temporary, publicRead: true);
        File.Move(temporary, Path.Combine(StateDirectory, "Last-AppUpdate.json"), overwrite: true);
    }

    private sealed class ExecutableGuard(DirectoryAnchors parents, SafeFileHandle file) : IDisposable
    { public void Dispose() { file.Dispose(); parents.Dispose(); } }

    public static AppExecutableInfo InspectExecutable(string path)
    {
        var fullPath = NormalizeLocalPath(path);
        // Every parent is anchored before any pathname-based metadata/crypto API.
        // Combined with a no-write/no-delete file handle, these APIs see one file.
        using var parents = AnchorLocalChain(Path.GetDirectoryName(fullPath)!);
        using var handle = OpenRegularFile(fullPath, GenericRead | ReadControl | ReadAttributes, 1);
        using var stream = new FileStream(handle, FileAccess.Read, 64 * 1024, isAsync: false);
        if (stream.Length is <= 0 or > AppUpdateIdentity.MaximumBytes)
            throw new InvalidDataException("The application executable has an invalid byte count.");
        bool signaturePresent;
        using (var pe = new PEReader(stream, PEStreamOptions.LeaveOpen))
        {
            var header = pe.PEHeaders.PEHeader;
            if (header is null || pe.PEHeaders.CoffHeader.Machine != Machine.Amd64 ||
                header.Magic != PEMagic.PE32Plus || header.Subsystem != Subsystem.WindowsGui ||
                (pe.PEHeaders.CoffHeader.Characteristics & Characteristics.ExecutableImage) == 0 ||
                (pe.PEHeaders.CoffHeader.Characteristics & Characteristics.Dll) != 0)
                throw new InvalidDataException("The update must be an AMD64 Windows GUI executable, not a DLL or other platform.");
            var certificate = header.CertificateTableDirectory;
            signaturePresent = certificate.Size != 0 || certificate.RelativeVirtualAddress != 0;
            if (signaturePresent && (certificate.Size <= 0 || certificate.RelativeVirtualAddress <= 0 ||
                (long)certificate.RelativeVirtualAddress + certificate.Size > stream.Length))
                throw new InvalidDataException("The application executable has an invalid signature table.");
        }
        var metadata = FileVersionInfo.GetVersionInfo(fullPath);
        if (metadata.ProductName != AppUpdateIdentity.Product || metadata.CompanyName != "PcNinja" ||
            !ReleaseVersion.TryParse(metadata.ProductVersion, out var version))
            throw new InvalidDataException("The executable does not have the fixed PcNinja product/company and a valid release version.");
        stream.Position = 0;
        var hash = Convert.ToHexString(SHA256.HashData(stream));
        stream.Position = 0;
        var signer = VerifyAuthenticode(fullPath, handle, signaturePresent);
        // Validate the complete informational version; only build metadata is ignored.
        return new(version!.Value.Split('+')[0], hash, stream.Length, signer);
    }

    public static AppExecutableInfo VerifyCandidate(string path, AppUpdateManifest release, string? requiredSigner)
    {
        AppUpdateClient.ValidateManifest(release);
        var actual = InspectExecutable(path);
        VerifyExecutableInfo(actual, release, requiredSigner);
        return actual;
    }

    internal static void VerifyExecutableInfo(AppExecutableInfo actual, AppUpdateManifest release, string? requiredSigner)
    {
        AppUpdateClient.ValidateManifest(release);
        if (!ReleaseVersion.TryParse(actual.Version, out var actualVersion) ||
            !string.Equals(actualVersion!.Value.Split('+')[0], release.Version.Split('+')[0], StringComparison.Ordinal) ||
            actual.SizeBytes != release.SizeBytes || !actual.Sha256.Equals(release.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The candidate executable does not match the exact release version, size and SHA-256.");
        if (requiredSigner is not null && (requiredSigner.Length != 40 || !requiredSigner.All(Uri.IsHexDigit) ||
            actual.SignerThumbprint is null || !requiredSigner.Equals(actual.SignerThumbprint, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("The update does not preserve the installed application's trusted signing certificate.");
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _anchors.Dispose();
    }

    internal static string NormalizeOperationId(string operationId) => operationId is { Length: 32 } &&
        operationId.All(char.IsAsciiHexDigit) && Guid.TryParseExact(operationId, "N", out var id) && id != Guid.Empty
            ? id.ToString("N") : throw new ArgumentException("An update operation must be an exact 32-hex GUID.", nameof(operationId));

    internal static long RequiredFreeBytes(long candidateBytes, long helperBytes)
    {
        if (candidateBytes is <= 0 or > AppUpdateIdentity.MaximumBytes || helperBytes is <= 0 or > AppUpdateIdentity.MaximumBytes)
            throw new ArgumentOutOfRangeException(nameof(candidateBytes), "Application candidate and helper byte counts are outside their limits.");
        return checked(candidateBytes + helperBytes + SpaceMarginBytes);
    }

    internal static void ValidateControlledDescriptor(RawSecurityDescriptor descriptor, bool privateScope, bool requireProtected)
    {
        if (!IsAdministratorOrSystem(descriptor.Owner) || descriptor.DiscretionaryAcl is null ||
            (descriptor.ControlFlags & ControlFlags.DiscretionaryAclPresent) == 0 ||
            requireProtected && (descriptor.ControlFlags & ControlFlags.DiscretionaryAclProtected) == 0)
            throw new UnauthorizedAccessException("An existing update folder/file is not owned and protected by Administrators/SYSTEM. It was not changed.");
        var administrators = false;
        var system = false;
        foreach (GenericAce entry in descriptor.DiscretionaryAcl)
        {
            if (entry is not CommonAce ace || ace.AceQualifier != AceQualifier.AccessAllowed || ace.IsCallback ||
                (ace.AceFlags & AceFlags.InheritOnly) != 0)
                throw new UnauthorizedAccessException("The update ACL is not an explicit controlled allowlist.");
            if (IsAdministratorOrSystem(ace.SecurityIdentifier))
            {
                if ((uint)ace.AccessMask != 0x001f01ff) throw new UnauthorizedAccessException("The update ACL lacks its exact administrator/system full-control rule.");
                administrators |= ace.SecurityIdentifier.Value == "S-1-5-32-544";
                system |= ace.SecurityIdentifier.Value == "S-1-5-18";
            }
            else if (privateScope || ace.SecurityIdentifier.Value != "S-1-5-32-545" ||
                ((uint)ace.AccessMask & ~0x001200a9U) != 0)
                throw new UnauthorizedAccessException("Non-administrator access to update staging is not allowed.");
        }
        if (!administrators || !system) throw new UnauthorizedAccessException("The update ACL is missing Administrators or SYSTEM.");
    }

    private static bool IsAdministratorOrSystem(SecurityIdentifier? sid) => sid?.Value is "S-1-5-32-544" or "S-1-5-18";

    private static SafeFileHandle OpenControlledDirectory(string path, bool privateScope, bool create, bool requireNew = false)
    {
        if (create)
        {
            using var security = new NativeSecurity(privateScope ? PrivateDirectorySddl : PublicDirectorySddl);
            var attributes = security.Attributes;
            if (!CreateDirectoryW(path, ref attributes))
            {
                var error = Marshal.GetLastWin32Error();
                if (error != 183 || requireNew) throw NativeFailure("The protected update folder could not be created atomically.", error);
            }
        }
        var handle = OpenDirectory(path);
        try { ValidateControlledDescriptor(ReadDescriptor(handle), privateScope, requireProtected: true); return handle; }
        catch { handle.Dispose(); throw; }
    }

    private static DirectoryAnchors AnchorLocalChain(string path)
    {
        path = NormalizeLocalPath(path);
        var root = Path.GetPathRoot(path)!;
        var drive = new DriveInfo(root);
        if (drive.DriveType != DriveType.Fixed || !drive.IsReady || !drive.DriveFormat.Equals("NTFS", StringComparison.OrdinalIgnoreCase))
            throw new IOException("Download and run needs a writable ordinary fixed local NTFS folder beside this tool. Copy the tool off network/ISO/removable/reparse media first; no alternate folder is chosen.");
        var anchors = new DirectoryAnchors();
        try
        {
            anchors.Add(OpenDirectory(root));
            var current = root;
            foreach (var segment in Path.GetRelativePath(root, path).Split(Path.DirectorySeparatorChar))
            {
                if (segment == ".") continue;
                current = Path.Combine(current, segment);
                anchors.Add(OpenDirectory(current));
            }
            return anchors;
        }
        catch { anchors.Dispose(); throw; }
    }

    private static SafeFileHandle OpenDirectory(string path)
    {
        var handle = CreateFileW(path, ReadControl | ReadAttributes, 3, IntPtr.Zero, 3,
            BackupSemantics | OpenReparsePoint, IntPtr.Zero); // share read/write, never delete.
        if (handle.IsInvalid) { handle.Dispose(); throw NativeFailure("An application-update parent directory could not be anchored."); }
        try
        {
            if (!GetFileInformationByHandle(handle, out var info) ||
                (info.Attributes & (uint)FileAttributes.Directory) == 0 ||
                (info.Attributes & (uint)FileAttributes.ReparsePoint) != 0 || !MatchesHandlePath(handle, path))
                throw new IOException("Application-update paths must not contain directory reparse points or changed ancestors.");
            return handle;
        }
        catch { handle.Dispose(); throw; }
    }

    private static SafeFileHandle OpenRegularFile(string path, uint access, uint share)
    {
        var handle = CreateFileW(path, access, share, IntPtr.Zero, 3, OpenReparsePoint, IntPtr.Zero);
        if (handle.IsInvalid) { handle.Dispose(); throw NativeFailure("The exact update file could not be locked. Running, read-only or unavailable files are not replaced."); }
        try { VerifyRegularFile(handle, path); return handle; }
        catch { handle.Dispose(); throw; }
    }

    private static void VerifyRegularFile(SafeFileHandle handle, string path)
    {
        if (!GetFileInformationByHandle(handle, out var info) || info.Links != 1 ||
            (info.Attributes & (uint)(FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0 || !MatchesHandlePath(handle, path))
            throw new IOException("Update files must be ordinary single-link files at their exact anchored paths.");
    }

    private static bool MatchesHandlePath(SafeFileHandle handle, string path)
    {
        var buffer = new StringBuilder(32768);
        var length = GetFinalPathNameByHandleW(handle, buffer, (uint)buffer.Capacity, 0);
        return length > 0 && length < buffer.Capacity && string.Equals(Path.TrimEndingDirectorySeparator(buffer.ToString()),
            Path.TrimEndingDirectorySeparator(@"\\?\" + Path.GetFullPath(path)), StringComparison.OrdinalIgnoreCase);
    }

    private static RawSecurityDescriptor ReadDescriptor(SafeFileHandle handle)
    {
        var result = GetSecurityInfo(handle, 1, OwnerAndDacl, out _, out _, out _, out _, out var descriptor);
        if (result != 0) throw NativeFailure("The anchored update object's ACL could not be inspected.", (int)result);
        try
        {
            var length = GetSecurityDescriptorLength(descriptor);
            if (length is 0 or > 65536) throw new IOException("The update object has an unsupported security descriptor.");
            var bytes = new byte[(int)length];
            Marshal.Copy(descriptor, bytes, 0, bytes.Length);
            return new RawSecurityDescriptor(bytes, 0);
        }
        finally { LocalFree(descriptor); }
    }

    private static string NormalizeLocalPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path != path.Trim() || path.Length < 3 ||
            !char.IsAsciiLetter(path[0]) || path[1] != ':' || path[2] != '\\' ||
            path.AsSpan(2).Contains(':') || path.IndexOfAny(['\0', '*', '?']) >= 0)
            throw new IOException("Application-update filesystem operations require ordinary absolute local drive paths, not network/device/relative/stream paths.");
        return Path.GetFullPath(path);
    }

    private static string ValidateFixtureParent(string path)
    {
        var full = NormalizeLocalPath(path);
        const string prefix = "PcNinja-AppUpdate-Fixture-";
        var name = Path.GetFileName(Path.TrimEndingDirectorySeparator(full));
        if (!name.StartsWith(prefix, StringComparison.Ordinal) || !Guid.TryParseExact(name[prefix.Length..], "N", out _) ||
            !string.Equals(Path.TrimEndingDirectorySeparator(Path.GetDirectoryName(full)!),
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath())), StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("An updater fixture must be one uniquely named child of the current temporary directory.");
        return full;
    }

    private static bool ExistsExactly(string path)
    {
        try { File.GetAttributes(path); return true; }
        catch (FileNotFoundException) { return false; }
        catch (DirectoryNotFoundException) { return false; }
    }

    private static void RejectDuplicateProperties(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name)) throw new InvalidDataException("The private update request contains duplicate properties.");
                RejectDuplicateProperties(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var item in element.EnumerateArray()) RejectDuplicateProperties(item);
    }

    private void EnsureAlive() => ObjectDisposedException.ThrowIf(_disposed, this);
    private static IOException NativeFailure(string message, int? code = null) => new(message,
        new Win32Exception(code ?? Marshal.GetLastWin32Error()));

    private static string? VerifyAuthenticode(string path, SafeFileHandle handle, bool signaturePresent)
    {
        var action = new Guid("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");
        var file = new TrustFile { Size = (uint)Marshal.SizeOf<TrustFile>(), Path = path, Handle = handle.DangerousGetHandle() };
        var pointer = Marshal.AllocHGlobal(Marshal.SizeOf<TrustFile>());
        try
        {
            Marshal.StructureToPtr(file, pointer, false);
            var trust = new TrustData { Size = (uint)Marshal.SizeOf<TrustData>(), Ui = 2, Union = 1,
                File = pointer, StateAction = 1, ProviderFlags = 0x1080 }; // cached-only revocation, no network/UI.
            try
            {
                var status = WinVerifyTrust(new IntPtr(-1), ref action, ref trust);
                if (status == unchecked((int)0x800b0100) && !signaturePresent) return null; // Explicit unsigned policy.
                if (status != 0) throw new InvalidDataException($"The executable has an invalid/untrusted Authenticode signature (0x{unchecked((uint)status):X8}).");
                using var certificate = new X509Certificate2(X509Certificate.CreateFromSignedFile(path));
                return certificate.Thumbprint.ToUpperInvariant();
            }
            finally { trust.StateAction = 2; WinVerifyTrust(new IntPtr(-1), ref action, ref trust); }
        }
        finally { Marshal.DestroyStructure<TrustFile>(pointer); Marshal.FreeHGlobal(pointer); }
    }

    private sealed class DirectoryAnchors : IDisposable
    {
        private readonly List<SafeFileHandle> _handles = [];
        internal SafeFileHandle Last => _handles[^1];
        internal void Add(SafeFileHandle handle) => _handles.Add(handle);
        public void Dispose() { for (var i = _handles.Count - 1; i >= 0; i--) _handles[i].Dispose(); _handles.Clear(); }
    }

    private sealed class NativeSecurity : IDisposable
    {
        internal IntPtr Pointer { get; private set; }
        internal SecurityAttributes Attributes => new() { Length = Marshal.SizeOf<SecurityAttributes>(), Descriptor = Pointer };
        internal NativeSecurity(string sddl)
        {
            if (!ConvertStringSecurityDescriptorToSecurityDescriptorW(sddl, 1, out var pointer, out _))
                throw NativeFailure("Could not prepare the protected application-update ACL.");
            Pointer = pointer;
        }
        public void Dispose() { if (Pointer != IntPtr.Zero) { LocalFree(Pointer); Pointer = IntPtr.Zero; } }
    }

    [StructLayout(LayoutKind.Sequential)] private struct SecurityAttributes { public int Length; public IntPtr Descriptor; [MarshalAs(UnmanagedType.Bool)] public bool Inherit; }
    [StructLayout(LayoutKind.Sequential)] private struct FileDisposition { [MarshalAs(UnmanagedType.U1)] public bool DeleteFile; }
    [StructLayout(LayoutKind.Sequential)] private struct FileInformation
    {
        public uint Attributes; public FILETIME Created; public FILETIME Accessed; public FILETIME Written;
        public uint VolumeSerial; public uint SizeHigh; public uint SizeLow; public uint Links; public uint IndexHigh; public uint IndexLow;
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct TrustFile
    {
        public uint Size; [MarshalAs(UnmanagedType.LPWStr)] public string Path; public IntPtr Handle; public IntPtr KnownSubject;
    }
    [StructLayout(LayoutKind.Sequential)] private struct TrustData
    {
        public uint Size; public IntPtr Callback; public IntPtr Sip; public uint Ui; public uint Revocation;
        public uint Union; public IntPtr File; public uint StateAction; public IntPtr State;
        public IntPtr Url; public uint ProviderFlags; public uint UiContext;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)]
    private static extern SafeFileHandle CreateFileW(string path, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);
    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)]
    private static extern SafeFileHandle CreateFileWithSecurity(string path, uint access, uint share, ref SecurityAttributes security, uint disposition, uint flags, IntPtr template);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CreateDirectoryW(string path, ref SecurityAttributes security);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetFileInformationByHandle(SafeFileHandle file, out FileInformation information);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)]
    private static extern uint GetFinalPathNameByHandleW(SafeFileHandle file, StringBuilder path, uint length, uint flags);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetFileInformationByHandle(SafeFileHandle file, int informationClass, ref FileDisposition information, uint size);
    [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr memory);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool ConvertStringSecurityDescriptorToSecurityDescriptorW(string sddl, uint revision, out IntPtr descriptor, out uint size);
    [DllImport("advapi32.dll")] private static extern uint GetSecurityInfo(SafeFileHandle file, int type, uint information, out IntPtr owner, out IntPtr group, out IntPtr dacl, out IntPtr sacl, out IntPtr descriptor);
    [DllImport("advapi32.dll")] private static extern uint GetSecurityDescriptorLength(IntPtr descriptor);
    [DllImport("advapi32.dll")] private static extern uint SetSecurityInfo(SafeFileHandle file, int type, uint information, IntPtr owner, IntPtr group, IntPtr dacl, IntPtr sacl);
    [DllImport("advapi32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSecurityDescriptorOwner(IntPtr descriptor, out IntPtr owner, [MarshalAs(UnmanagedType.Bool)] out bool ownerDefaulted);
    [DllImport("advapi32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSecurityDescriptorDacl(IntPtr descriptor, [MarshalAs(UnmanagedType.Bool)] out bool present, out IntPtr dacl, [MarshalAs(UnmanagedType.Bool)] out bool defaulted);
    [DllImport("wintrust.dll", ExactSpelling = true)] private static extern int WinVerifyTrust(IntPtr window, ref Guid action, ref TrustData data);
}
